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
/// シャドーイング 1 回 = BBC 6 Minute English の続いた 5 文。
/// Start → 用意 → (イヤホンが無ければポップアップ) → 1 回目: 聞くだけ (Skip あり) → 3-2-1 →
/// 2 回目: 録音しながら流す → 最後の文の 1.5 秒後に録音を止める → Gemini で書き起こし → 照合 → まとめ (My voice / Original)。
/// 画面の部品は RepeatPracticeManager のものを使う (Speak ボタン = Skip / Stop、Again = My voice、Slow = Original)。
/// </summary>
public class ShadowSession
{
    public enum State { Idle, Preparing, ListenFirst, Countdown, Shadowing, Checking, Summary }

    public const string PrepareFailedMessage = "Could not prepare the BBC audio. Please try again later.";
    public const string NotHeardMessage = "I couldn't hear you. Tap Start to try again.";
    private const float GapSeconds = (float)ShadowChunker.Gap;
    private const float TailSeconds = (float)ShadowChunker.Tail;
    private const float CountSeconds = 0.8f;
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
    public event Action StateChanged;

    private readonly View _v;
    private readonly RepeatAudioPlayer _player;
    private readonly AudioRecorder _recorder;
    private readonly GeminiApiClient _gemini;
    private readonly Func<List<RepeatResult>, DateTime, Task<string>> _save;
    private readonly Func<string, List<RepeatResult>, Task<string>> _sendDiscord;
    private readonly Action _countStudyDay;

    private CancellationTokenSource _cts;         // 1 回の練習
    private CancellationTokenSource _skipCts;     // 1 回目を Skip
    private CancellationTokenSource _replayCts;   // まとめの My voice / Original
    private ShadowEpisodeStore.Chunk _chunk;
    private List<AudioClip> _segments = new List<AudioClip>();
    private AudioClip _myVoice;

    public ShadowSession(View view, RepeatAudioPlayer player, AudioRecorder recorder, GeminiApiClient gemini,
                         Func<List<RepeatResult>, DateTime, Task<string>> save,
                         Func<string, List<RepeatResult>, Task<string>> sendDiscord, Action countStudyDay)
    {
        _v = view; _player = player; _recorder = recorder; _gemini = gemini;
        _save = save; _sendDiscord = sendDiscord; _countStudyDay = countStudyDay;
    }

    /// <summary>シャドーイングに切り替えた・画面を開いたときの表示</summary>
    public void ShowIdle(string status = null)
    {
        SetText(_v.Header, "Shadowing");
        SetText(_v.Result, "");
        SetText(_v.Credit, "Audio: BBC Learning English — 6 Minute English. Personal study only.");
        if (_v.Sentence != null) _v.Sentence.text = "";
        SetState(State.Idle);
        SetStatus(status ?? "Tap \"Start\". Listen to 5 sentences first, then shadow them (speak along).");
    }

    // ------------------------------------------------------------
    // Buttons (RepeatPracticeManager から呼ぶ)
    // ------------------------------------------------------------
    public async void OnMain()
    {
        if (Current == State.Idle || Current == State.Summary) await RunAsync();
    }

    public void OnSpeak()
    {
        if (Current == State.ListenFirst) _skipCts?.Cancel();                     // Skip
        else if (Current == State.Countdown || Current == State.Shadowing)        // Stop: 保存しない
            Cancel("Stopped. Tap \"Start\" to try again.");
    }

    public async void OnAgain()   // My voice
    {
        if (Current != State.Summary || _myVoice == null) return;
        await ReplayAsync(new List<AudioClip> { _myVoice });
    }

    public async void OnSlow()    // Original
    {
        if (Current != State.Summary || _segments.Count == 0) return;
        await ReplayAsync(_segments);
    }

    /// <summary>止める (タブの移動・Stop)。録音は捨てて、保存しない</summary>
    public void Cancel(string status = "Cancelled. Tap \"Start\" to try again.")
    {
        _replayCts?.Cancel();
        if (Current == State.Idle || Current == State.Summary) return;
        _cts?.Cancel();
        _skipCts?.Cancel();
        _v.Ui?.CloseEarphonePopup(false);
        _player.Stop();
        if (_recorder.IsRecording) _recorder.StopRecording();
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        SetLoading(false);
        ShowIdle(status);
    }

    // ------------------------------------------------------------
    // 流れ
    // ------------------------------------------------------------
    private async Task RunAsync()
    {
        _replayCts?.Cancel();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetText(_v.Result, "");
        SetState(State.Preparing);
        SetLoading(true);
        SetText(_v.Header, "Shadowing");
        SetStatus("Preparing...");

        // 1. 次の 5 文
        try
        {
            _chunk = await ShadowEpisodeStore.NextChunkAsync(_gemini, SetStatus, ct);
            if (ct.IsCancellationRequested) return;
            ClearSegments();
            bool slow = _v.Ui != null && _v.Ui.SlowOn;
            foreach (var line in _chunk.Lines)
            {
                var w = ShadowChunker.Window(line, _chunk.Full.length);
                var clip = RepeatAudioPlayer.MakeSegment(_chunk.Full, w.start, w.end, slow);
                if (clip == null) throw new Exception("could not cut the audio");
                _segments.Add(clip);
                await Task.Yield();   // 画面を止めない
                if (ct.IsCancellationRequested) return;
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] could not prepare: " + e);
            if (ct.IsCancellationRequested) return;
            SetLoading(false);
            ShowIdle(PrepareFailedMessage);
            return;
        }
        SetLoading(false);
        SetText(_v.Credit, "Audio: BBC Learning English — 6 Minute English (" + Escape(_chunk.Episode.Title) + "). Personal study only.");

        // 2. イヤホン (Android でイヤホンが無いときだけ)
        if (!EarphoneCheck.HasEarphones() && _v.Ui != null)
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
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        byte[] wav;
        try
        {
            // 3. 1 回目: 聞くだけ (Skip で 2 回目へ)
            SetState(State.ListenFirst);
            SetStatus("Just listen. Tap \"Skip\" to start shadowing.");
            _skipCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                await PlayAllAsync(i => "Listen first (" + (i + 1) + " / " + _segments.Count + ")", _skipCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { _player.Stop(); }

            // 4. 3-2-1
            SetState(State.Countdown);
            SetText(_v.Header, "Get ready");
            for (int n = 3; n >= 1; n--)
            {
                SetStatus("<size=160%><b>" + n + "</b></size>");
                await Delay(CountSeconds, ct);
            }

            // 5. 2 回目: 録音しながら流す
            if (!_recorder.StartRecording()) throw new Exception("could not start the microphone");
            SetState(State.Shadowing);
            SetStatus("Speak along with the voice. Tap \"Stop\" to quit.");
            await PlayAllAsync(i => "Shadow (" + (i + 1) + " / " + _segments.Count + ")", ct);
            await Delay(TailSeconds, ct);
            wav = _recorder.IsRecording ? _recorder.StopRecording() : null;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (ct.IsCancellationRequested) return;
            _player.Stop();
            if (_recorder.IsRecording) _recorder.StopRecording();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            ShowIdle("Could not play or record (" + FirstLine(e.Message) + "). Tap \"Start\" to try again.");
            return;
        }
        finally
        {
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }

        // 6. 判定
        SetState(State.Checking);
        SetLoading(true);
        SetText(_v.Header, "Checking...");
        SetStatus("Checking...");
        string heard;
        try
        {
            heard = wav == null ? "" : ((await _gemini.TranscribeAsync(wav)) ?? "").Trim();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] transcription failed: " + e.Message);
            if (ct.IsCancellationRequested) return;
            SetLoading(false);
            ShowIdle("Could not check your voice (" + FirstLine(e.Message) + "). Tap \"Start\" to try again.");
            return;
        }
        if (ct.IsCancellationRequested) return;
        SetLoading(false);

        var sentences = _chunk.Lines.Select(l => l.Text).ToList();
        var score = ShadowScorer.Score(sentences, heard);
        if (score.empty)
        {
            ShowIdle(NotHeardMessage);   // 0% にしない。同じ 5 文をもう一度
            return;
        }
        SetMyVoice(wav);
        await FinishAsync(score.scores, score.average);
    }

    private async Task FinishAsync(List<ShadowScorer.SentenceScore> scores, int average)
    {
        ShadowEpisodeStore.MarkDone(_chunk);
        var results = scores.Select(s => new RepeatResult
        {
            sentence = s.Text,
            heard = string.Join(" ", s.Words.Where(w => w.Matched).Select(w => w.Text)),
            accuracy = s.Accuracy,
            replays = 0,
            source = "bbc",
            voice = RepeatAudioPlayer.VoiceHuman,
            mode = "shadow",
        }).ToList();

        var sb = new StringBuilder();
        sb.Append("<b>Average: ").Append(average).Append("%</b>\n")
          .Append("<size=75%>").Append(Escape(_chunk.Episode.Title)).Append(" (")
          .Append(_chunk.Index + 1).Append(" / ").Append(_chunk.Total).Append(")</size>\n\n");
        foreach (var s in scores)
        {
            foreach (var w in s.Words)
                sb.Append("<color=").Append(w.Matched ? MatchColor : MissColor).Append('>').Append(Escape(w.Text)).Append("</color> ");
            sb.Append(" <b>").Append(s.Accuracy).Append("%</b>\n\n");
        }
        SetText(_v.Header, "Summary");
        SetText(_v.Result, sb.ToString().TrimEnd());
        SetState(State.Summary);
        SetStatus("Saving...");

        _countStudyDay?.Invoke();
        string saveNote = await _save(results, DateTime.UtcNow);
        string discordError = await _sendDiscord(_chunk.Episode.Title, results);
        if (Current != State.Summary) return;
        SetStatus((saveNote ?? "Saved.") + (discordError == null ? "" : "  Discord failed: " + discordError) +
                  "  \"My voice\" / \"Original\" to compare. \"Start\" for the next 5.");
    }

    // ------------------------------------------------------------
    private async Task PlayAllAsync(Func<int, string> header, CancellationToken ct)
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            SetText(_v.Header, header(i));
            await _player.PlayClipAsync(_segments[i], ct);
            if (i < _segments.Count - 1) await Delay(GapSeconds, ct);
        }
    }

    private async Task ReplayAsync(List<AudioClip> clips)
    {
        _replayCts?.Cancel();
        _player.Stop();
        var cts = _replayCts = new CancellationTokenSource();
        try
        {
            for (int i = 0; i < clips.Count; i++)
            {
                await _player.PlayClipAsync(clips[i], cts.Token);
                if (i < clips.Count - 1) await Delay(GapSeconds, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogWarning("[Shadow] could not play: " + e.Message); }
    }

    private void SetMyVoice(byte[] wav)
    {
        if (_myVoice != null) UnityEngine.Object.Destroy(_myVoice);
        _myVoice = null;
        // AudioRecorder の WAV: 44 バイトのヘッダー + 16bit PCM (モノラル)
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

    private void ClearSegments()
    {
        foreach (var c in _segments) if (c != null) UnityEngine.Object.Destroy(c);
        _segments.Clear();
    }

    private static async Task Delay(float seconds, CancellationToken ct)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        ct.ThrowIfCancellationRequested();
    }

    private void SetState(State s)
    {
        Current = s;
        _v.MainButton.interactable = s == State.Idle || s == State.Summary;
        if (_v.MainLabel != null) _v.MainLabel.text = s == State.Idle || s == State.Summary ? "Start" : "...";

        bool speak = s == State.ListenFirst || s == State.Countdown || s == State.Shadowing;
        if (_v.SpeakButton != null) { _v.SpeakButton.gameObject.SetActive(speak); _v.SpeakButton.interactable = true; }
        if (_v.SpeakLabel != null) _v.SpeakLabel.text = s == State.ListenFirst ? "Skip" : "Stop";

        bool summary = s == State.Summary;
        SetButton(_v.AgainButton, summary, "My voice");
        SetButton(_v.SlowButton, summary, "Original");
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
}
