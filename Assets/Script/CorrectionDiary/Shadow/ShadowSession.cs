using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// YouTube シャドーイング: タイトルで検索して動画を選ぶ → Start → (初めての動画だけ) Gemini でお手本を作る →
/// (イヤホンが無ければ確認) → アプリの中で動画を流し、再生が始まったら録音 → 最後まで / Stop → 180 秒ずつ書き起こし →
/// お手本と照合 → Gemini のアドバイス → まとめ (My voice)。Web には点数だけ、Discord には文とアドバイスも送る。
/// 画面の部品は RepeatPracticeManager のものを使う (Speak ボタン = Stop、Again = My voice)。
/// </summary>
public class ShadowSession
{
    public enum State { Idle, Searching, Preparing, WaitingToPlay, Shadowing, Checking, Summary }

    public const string NotHeardMessage = "I couldn't hear you. Tap Start to try again.";
    public const string CannotPlayMessage = "Could not play the video in the app.";
    private const int SplitSeconds = 180;
    private const int ExtraRecordSeconds = 30;
    private const float EndTailSeconds = 2f;
    private const float PlayWaitSeconds = 60f;   // ▶ を押してもらうのを待つ
    private const int MissedThreshold = 80;
    private const string MatchColor = "#40c057";
    private const string MissColor = "#fa5252";

    /// <summary>RepeatPracticeManager の画面の部品</summary>
    public class View
    {
        public Button MainButton; public TMP_Text MainLabel;
        public Button SpeakButton; public TMP_Text SpeakLabel;
        public Button AgainButton; public Button SlowButton;
        public TMP_Text Header, Sentence, Result, Status, Credit;
        public GameObject Loading;
        public ShadowUi Ui;
    }

    public State Current { get; private set; } = State.Idle;
    public bool CanSwitchMode => Current == State.Idle || Current == State.Summary;
    public bool IsRunning => Current != State.Idle && Current != State.Summary;
    public event Action StateChanged;

    private readonly View _v;
    private readonly RepeatAudioPlayer _audio;
    private readonly YouTubePlayerView _video;
    private readonly YouTubeSearchClient _youtube;
    private readonly AudioRecorder _recorder;
    private readonly GeminiApiClient _gemini;
    private readonly Func<List<RepeatResult>, DateTime, Task<string>> _save;
    private readonly Func<YouTubeSearchClient.Video, int, string, List<ShadowScorer.SentenceScore>, Task<string>> _sendDiscord;
    private readonly Action _countStudyDay;

    private CancellationTokenSource _cts;
    private CancellationTokenSource _replayCts;
    private AudioClip _myVoice;
    private bool _playing, _ended, _stopRequested;
    private string _playError;

    public ShadowSession(View view, RepeatAudioPlayer audio, YouTubePlayerView video, YouTubeSearchClient youtube,
                         AudioRecorder recorder, GeminiApiClient gemini,
                         Func<List<RepeatResult>, DateTime, Task<string>> save,
                         Func<YouTubeSearchClient.Video, int, string, List<ShadowScorer.SentenceScore>, Task<string>> sendDiscord,
                         Action countStudyDay)
    {
        _v = view; _audio = audio; _video = video; _youtube = youtube; _recorder = recorder; _gemini = gemini;
        _save = save; _sendDiscord = sendDiscord; _countStudyDay = countStudyDay;
        _video.OnPlaying += () => _playing = true;
        _video.OnEnded += () => _ended = true;
        _video.OnError += msg => _playError = msg;
        _v.Ui.SearchRequested += q => _ = SearchAsync(q);
        _v.Ui.VideoChosen += _ => ShowIdle();
    }

    /// <summary>シャドーイングに切り替えた・画面を開いたときの表示</summary>
    public void ShowIdle(string status = null)
    {
        var video = _v.Ui.Video;
        SetText(_v.Header, video == null ? "Shadowing" : Escape(video.title));
        SetText(_v.Result, "");
        SetText(_v.Credit, video == null ? "" : "Video: " + Escape(video.channel) + " on YouTube. Personal study only.");
        if (_v.Sentence != null) _v.Sentence.text = "";
        _v.Ui.ShowVideo(false);
        SetState(State.Idle);
        SetStatus(status ?? (video == null
            ? "Search for the video you shadow on YouTube, then choose it."
            : "Tap \"Start\" and shadow the whole video. Use earphones."));
    }

    // ------------------------------------------------------------
    // Buttons (RepeatPracticeManager から呼ぶ)
    // ------------------------------------------------------------
    public async void OnMain()
    {
        if ((Current == State.Idle || Current == State.Summary) && _v.Ui.Video != null) await RunAsync(_v.Ui.Video);
    }

    public void OnSpeak()   // Stop
    {
        if (Current == State.Shadowing) _stopRequested = true;                     // そこまでで判定する
        else if (Current == State.WaitingToPlay) Cancel("Stopped. Tap \"Start\" to try again.");
    }

    public async void OnAgain()   // My voice
    {
        if (Current != State.Summary || _myVoice == null) return;
        _replayCts?.Cancel();
        _audio.Stop();
        var cts = _replayCts = new CancellationTokenSource();
        try { await _audio.PlayClipAsync(_myVoice, cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogWarning("[Shadow] could not play: " + e.Message); }
    }

    /// <summary>止める (タブの移動・電話など)。録音は捨てて、保存しない</summary>
    public void Cancel(string status = "Cancelled. Tap \"Start\" to try again.")
    {
        _replayCts?.Cancel();
        _audio.Stop();
        if (!IsRunning) return;
        _cts?.Cancel();
        _v.Ui.CloseEarphonePopup(false);
        _video.Hide();
        if (_recorder.IsRecording) _recorder.StopRecording();
        _recorder.MaxSeconds = 60;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        SetLoading(false);
        ShowIdle(status);
    }

    // ------------------------------------------------------------
    // 検索
    // ------------------------------------------------------------
    private async Task SearchAsync(string query)
    {
        if (!CanSwitchMode) return;
        if (_youtube == null || !_youtube.IsConfigured) { SetStatus("YouTube API key is not set."); return; }
        SetState(State.Searching);
        SetLoading(true);
        SetStatus("Searching...");
        try
        {
            var videos = await _youtube.SearchVideosByTitleAsync(query, 60, 900, 5);
            if (Current != State.Searching) return;
            _v.Ui.ShowCandidates(videos);
            SetLoading(false);
            SetState(State.Idle);
            SetStatus(videos.Count == 0 ? "No videos found (1-15 min). Try other words." : "Choose the video.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] search failed: " + e.Message);
            if (Current != State.Searching) return;
            SetLoading(false);
            SetState(State.Idle);
            SetStatus("Could not search (" + Escape(FirstLine(e.Message)) + ").");
        }
    }

    // ------------------------------------------------------------
    // 流れ
    // ------------------------------------------------------------
    private async Task RunAsync(YouTubeSearchClient.Video video)
    {
        _replayCts?.Cancel();
        _audio.Stop();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _v.Ui.HideCandidates();
        SetText(_v.Result, "");
        SetText(_v.Header, Escape(video.title));
        SetState(State.Preparing);
        SetLoading(true);
        SetStatus("Preparing the script (first time only)...");

        // 1. お手本
        List<string> script;
        try
        {
            script = await ShadowScriptStore.GetAsync(_gemini, video, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] could not prepare the script: " + e);
            if (ct.IsCancellationRequested) return;
            SetLoading(false);
            ShowIdle("Could not prepare the script (" + Escape(FirstLine(e.Message)) + "). Tap \"Start\" to try again.");
            return;
        }
        if (ct.IsCancellationRequested) return;
        SetLoading(false);

        // 2. イヤホン・マイク
        if (!EarphoneCheck.HasEarphones())
        {
            bool go = await _v.Ui.ConfirmEarphonesAsync();
            if (ct.IsCancellationRequested) return;
            if (!go) { ShowIdle(); return; }
        }
        if (!await _recorder.RequestPermissionAsync())
        {
            if (!ct.IsCancellationRequested) ShowIdle("Microphone permission denied. Please allow it in Settings.");
            return;
        }
        if (ct.IsCancellationRequested) return;
        if (!YouTubePlayerView.IsEmbeddedSupported) { ShowIdle(CannotPlayMessage); return; }

        // 3. 動画を出して、再生が始まったら録音
        byte[] wav;
        double recorded;
        int maxSeconds = video.durationSeconds + ExtraRecordSeconds;
        try
        {
            _playing = _ended = _stopRequested = false;
            _playError = null;
            _v.Ui.ShowVideo(true);
            await Task.Yield();   // 枠の大きさが決まってから WebView を置く
            _video.SetVideoArea(_v.Ui.VideoArea);
            _video.Show(video.id);
            SetState(State.WaitingToPlay);
            SetStatus("Tap ▶ on the video. Recording starts with the video.");
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            float waitEnd = Time.realtimeSinceStartup + PlayWaitSeconds;
            while (!_playing)
            {
                ct.ThrowIfCancellationRequested();
                if (_playError != null || Time.realtimeSinceStartup > waitEnd) throw new ShadowPlayException();
                await Task.Yield();
            }

            _recorder.MaxSeconds = maxSeconds;
            if (!_recorder.StartRecording()) throw new Exception("could not start the microphone");
            float start = Time.realtimeSinceStartup;
            SetState(State.Shadowing);
            float endAt = float.MaxValue;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                float elapsed = Time.realtimeSinceStartup - start;
                if (_ended && endAt == float.MaxValue) endAt = elapsed + EndTailSeconds;
                if (_stopRequested || elapsed >= endAt || elapsed >= maxSeconds - 0.5f) break;
                SetStatus("Shadowing... " + YouTubeSearchClient.FormatDuration((int)elapsed) + " / " +
                          YouTubeSearchClient.FormatDuration(video.durationSeconds) + "\n<size=70%>Tap \"Stop\" to finish early.</size>");
                await Task.Yield();
            }
            recorded = Time.realtimeSinceStartup - start;
            wav = _recorder.IsRecording ? _recorder.StopRecording() : null;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            if (ct.IsCancellationRequested) return;
            Debug.LogWarning("[Shadow] " + e.Message);
            _video.Hide();
            if (_recorder.IsRecording) _recorder.StopRecording();
            ShowIdle(e is ShadowPlayException ? CannotPlayMessage : "Could not record (" + Escape(FirstLine(e.Message)) + ").");
            return;
        }
        finally
        {
            _recorder.MaxSeconds = 60;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }
        _video.Hide();
        _v.Ui.ShowVideo(false);

        // 4. 書き起こし (180 秒ずつ)
        SetState(State.Checking);
        SetLoading(true);
        SetText(_v.Header, "Checking...");
        var heard = new StringBuilder();
        var notes = new List<string>();
        try
        {
            var parts = WavSplitter.Split(wav, SplitSeconds);
            string reference = string.Join(" ", script);
            for (int i = 0; i < parts.Count; i++)
            {
                SetStatus("Checking... (" + (i + 1) + " / " + parts.Count + ")");
                var r = await _gemini.TranscribeShadowingAsync(parts[i], reference);
                if (ct.IsCancellationRequested) return;
                heard.Append(r.transcript).Append(' ');
                notes.AddRange(r.notes);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] transcription failed: " + e.Message);
            if (ct.IsCancellationRequested) return;
            SetLoading(false);
            ShowIdle("Could not check your voice (" + Escape(FirstLine(e.Message)) + "). Tap \"Start\" to try again.");
            return;
        }

        double fraction = video.durationSeconds > 0 ? recorded / video.durationSeconds : 1;
        var sentences = ShadowScript.SentencesUpTo(script, fraction);
        var score = ShadowScorer.Score(sentences, heard.ToString());
        if (score.empty)
        {
            SetLoading(false);
            ShowIdle(NotHeardMessage);
            return;
        }

        // 5. アドバイス
        SetStatus("Getting advice...");
        var missed = score.scores.SelectMany(s => s.Words).Where(w => !w.Matched)
            .Select(w => new string(w.Text.Where(c => char.IsLetter(c) || c == '\'').ToArray()).ToLowerInvariant())
            .Where(w => w.Length > 0).GroupBy(w => w).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        string advice = null;
        try { advice = await _gemini.ShadowAdviceAsync(notes, missed); }
        catch (Exception e) { Debug.LogWarning("[Shadow] advice failed: " + e.Message); }
        if (ct.IsCancellationRequested) return;
        SetLoading(false);
        SetMyVoice(wav);
        await FinishAsync(video, score.scores, score.average, advice);
    }

    private async Task FinishAsync(YouTubeSearchClient.Video video, List<ShadowScorer.SentenceScore> scores, int average, string advice)
    {
        var sb = new StringBuilder();
        sb.Append("<b>Average: ").Append(average).Append("%</b>  <size=75%>(").Append(scores.Count).Append(" sentences)</size>\n\n")
          .Append("<b>Advice</b>\n").Append(Escape(string.IsNullOrWhiteSpace(advice) ? "Advice: not available" : advice)).Append("\n\n");
        foreach (var s in scores)
        {
            foreach (var w in s.Words)
                sb.Append("<color=").Append(w.Matched ? MatchColor : MissColor).Append('>').Append(Escape(w.Text)).Append("</color> ");
            sb.Append(" <b>").Append(s.Accuracy).Append("%</b>\n\n");
        }
        SetText(_v.Header, "Summary - " + Escape(video.title));
        SetText(_v.Result, sb.ToString().TrimEnd());
        SetState(State.Summary);
        SetStatus("Saving...");

        // Web には点数だけ (1 回 = 1 行)
        var results = new List<RepeatResult>
        {
            new RepeatResult { sentence = "", heard = "", accuracy = average, replays = 0, source = "youtube",
                               voice = RepeatAudioPlayer.VoiceHuman, mode = "shadow" },
        };
        _countStudyDay?.Invoke();
        string saveNote = await _save(results, DateTime.UtcNow);
        string discordError = await _sendDiscord(video, average, advice, scores);
        if (Current != State.Summary || !_v.Ui.ShadowOn) return;   // もう次の練習・リピーティングに移った
        SetStatus((saveNote ?? "Saved.") + (discordError == null ? "" : "  Discord failed: " + discordError) +
                  "  \"My voice\" to listen to yourself.");
    }

    // ------------------------------------------------------------
    private void SetMyVoice(byte[] wav)
    {
        if (_myVoice != null) UnityEngine.Object.Destroy(_myVoice);
        _myVoice = null;
        // AudioRecorder の WAV: 44 バイトのヘッダー + 16bit PCM
        if (wav == null || wav.Length <= 44) return;
        int rate = BitConverter.ToInt32(wav, 24);
        int channels = BitConverter.ToInt16(wav, 22);
        var pcm = new byte[wav.Length - 44];
        Buffer.BlockCopy(wav, 44, pcm, 0, pcm.Length);
        float[] samples = PcmAudio.Pcm16ToFloats(pcm);
        if (samples.Length == 0 || rate <= 0 || channels <= 0) return;
        _myVoice = AudioClip.Create("my_voice", samples.Length / channels, channels, rate, false);
        _myVoice.SetData(samples, 0);
    }

    private void SetState(State s)
    {
        Current = s;
        bool idle = s == State.Idle || s == State.Summary;
        _v.MainButton.interactable = idle && _v.Ui.Video != null;
        if (_v.MainLabel != null) _v.MainLabel.text = idle ? "Start" : "...";

        bool stop = s == State.WaitingToPlay || s == State.Shadowing;
        if (_v.SpeakButton != null) { _v.SpeakButton.gameObject.SetActive(stop); _v.SpeakButton.interactable = true; }
        if (_v.SpeakLabel != null) _v.SpeakLabel.text = "Stop";

        SetButton(_v.AgainButton, s == State.Summary && _myVoice != null, "My voice");
        if (_v.SlowButton != null) _v.SlowButton.gameObject.SetActive(false);
        _v.Ui.SetInteractable(idle);
        StateChanged?.Invoke();
    }

    private static void SetButton(Button b, bool active, string label)
    {
        if (b == null) return;
        b.gameObject.SetActive(active);
        b.interactable = true;
        var t = b.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = label;
    }

    private void SetStatus(string s) => SetText(_v.Status, s);
    private static void SetText(TMP_Text t, string s) { if (t != null) t.text = s ?? ""; }
    private void SetLoading(bool on) { if (_v.Loading != null) _v.Loading.SetActive(on); }
    private static string Escape(string s) => (s ?? "").Replace("<", "<​");

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf('\n');
        return i >= 0 ? s.Substring(0, i) : s;
    }

    private class ShadowPlayException : Exception { }
}
