using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// カードの「Didn't sound right?」: Say it の最後の録音 (単語ごと) を Gemini に送り、どの音ができていないか・直し方を
/// カードの表に出す。練習する音があれば YouTube で練習動画を探し、ボタンを「Watch: 〇〇 video」に変える (押すとアプリの中の動画の画面で流す)。
/// 録音が無い単語・裏を見ているときはボタンを隠す。言い直したら、その単語の結果は消して、もう一度聞けるようにする
/// </summary>
public class VocabSoundHelp
{
    public const string AskLabel = "Didn't sound right?";
    public const string CheckingLabel = "Checking...";
    public const string RecordingLabel = "Say it now...";
    public const int ClipMilliseconds = 3500;   // 録音が無いときに、その場で録る長さ (単語 1 つ)
    public const string FindingLabel = "Finding a video...";
    private string _recordingWord;              // その場で録っている単語
    private string _findingWord;                // 練習動画を探している単語

    private readonly VocabContext _ctx;
    private readonly Button _button;
    private readonly TMP_Text _label;
    private readonly YouTubeSearchClient _youtube;   // キーが無ければ null (動画は探さない)

    private readonly Dictionary<string, byte[]> _wavs = new Dictionary<string, byte[]>();
    private readonly Dictionary<string, GeminiApiClient.WordHelp> _helps = new Dictionary<string, GeminiApiClient.WordHelp>();
    private readonly Dictionary<string, YouTubeSearchClient.Video> _videos = new Dictionary<string, YouTubeSearchClient.Video>();
    private readonly HashSet<string> _busy = new HashSet<string>();
    private int _generation;   // Clear() で増やす (前の回の返事を捨てる)

    /// <summary>今カードに出ている単語 (表でなければ null)。VocabTodayView がつなぐ</summary>
    public Func<string> CurrentWord;
    /// <summary>単語の発音記号 (無ければ空)</summary>
    public Func<string, string> IpaFor;
    /// <summary>カードを描き直す</summary>
    public Action Changed;
    /// <summary>練習動画を流す (アプリの中の動画の画面)。無ければ YouTube で開く</summary>
    public Action<YouTubeSearchClient.Video> PlayVideo;

    public VocabSoundHelp(VocabContext ctx, Button button, string youtubeApiKey)
    {
        _ctx = ctx;
        _button = button;
        _label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        _youtube = string.IsNullOrWhiteSpace(youtubeApiKey) ? null : new YouTubeSearchClient(youtubeApiKey);
        if (_button != null) _button.onClick.AddListener(OnClicked);
        SetRowActive(false);
    }

    /// <summary>Say it の録音が取れた: その単語の最後の録音にする (前の結果は消す)</summary>
    public void SetRecording(string word, byte[] wav)
    {
        if (string.IsNullOrEmpty(word) || wav == null || wav.Length == 0) return;
        _wavs[word] = wav;
        _helps.Remove(word);
        _videos.Remove(word);
    }

    /// <summary>新しい回: 録音と結果をすべて捨てる</summary>
    public void Clear()
    {
        _generation++;
        _wavs.Clear(); _helps.Clear(); _videos.Clear(); _busy.Clear();
        SetRowActive(false);
    }

    /// <summary>ボタンの行を今のカードに合わせる (front: カードの表を見ている)</summary>
    public void Render(string word, bool front)
    {
        bool hasHelp = word != null && _helps.ContainsKey(word);
        bool hasVideo = word != null && _videos.ContainsKey(word);
        bool busy = word != null && _busy.Contains(word);
        // 結果が出て動画が無いときは、ボタンは要らない (説明はカードに出ている)
        // 録音が無くても出す (押したらその場で録る。録音と認識を同時にできない端末では Say it の録音が無い)
        bool show = word != null && front && _ctx.Gemini != null && (!hasHelp || hasVideo || busy);
        SetRowActive(show);
        if (!show || _button == null) return;
        _button.interactable = !busy;
        if (_label != null)
            _label.text = busy ? (_recordingWord == word ? RecordingLabel : _findingWord == word ? FindingLabel : CheckingLabel) : hasVideo ? "Watch: " + SoundName(_helps[word].Sounds[0]) + " video" : AskLabel;
    }

    /// <summary>カードの表に足す文 (結果が無ければ空)</summary>
    public string Describe(string word)
    {
        if (word == null || !_helps.TryGetValue(word, out var h)) return "";
        var sb = new StringBuilder("\n\n<size=70%><align=left>");
        if (h.Problem.Length > 0) sb.Append(VocabContext.Esc(h.Problem)).Append('\n');
        if (h.HowTo.Length > 0) sb.Append("<color=#22D3EE>").Append(VocabContext.Esc(h.HowTo)).Append("</color>\n");
        if (h.Sounds.Count > 0)
            sb.Append("<color=#9CA3AF>Practice: ").Append(VocabContext.Esc(string.Join(", ", h.Sounds.Select(SoundName)))).Append("</color>");
        return sb.ToString().TrimEnd('\n') + "</align></size>";
    }

    private static string SoundName(SoundHit s) =>
        s.Code == SoundTypes.Other ? s.Label : SoundTypes.Label(s.Code, s.Label);

    private void OnClicked()
    {
        string word = CurrentWord?.Invoke();
        if (word == null) return;
        if (_videos.TryGetValue(word, out var video))
        {
            if (PlayVideo != null) PlayVideo(video);
            else Application.OpenURL(video.WatchUrl);
            return;
        }
        if (_busy.Contains(word) || _helps.ContainsKey(word)) return;
        if (_ctx.Capture.IsActive || _ctx.Capture.IsClipping) return;   // Say it の途中
        _ = AskAsync(word);
    }

    private async Task AskAsync(string word)
    {
        var gemini = _ctx.Gemini;
        if (gemini == null) { _ctx.SetStatus("No Gemini key, so no pronunciation advice."); return; }
        int gen = _generation;
        _busy.Add(word);
        try
        {
            if (!_wavs.TryGetValue(word, out byte[] wav))
            {
                // Say it の録音が無い: その場で録る
                _ctx.Audio.Stop();
                _recordingWord = word;
                Refresh(word, "Say \"" + word + "\" now.");
                try { wav = await _ctx.Capture.RecordClipAsync(ClipMilliseconds); }
                finally { _recordingWord = null; }
                if (gen != _generation) return;
                if (wav == null)
                {
                    _busy.Remove(word);
                    Refresh(word, "Could not record. Tap \"" + AskLabel + "\" again.");
                    return;
                }
                _wavs[word] = wav;
            }
            Refresh(word, "Listening to your recording...");
            var help = await gemini.GetWordHelpAsync(word, IpaFor?.Invoke(word) ?? "", wav);
            if (gen != _generation || !SameRecording(word, wav)) return;
            _helps[word] = help;
            YouTubeSearchClient.Video video = null;
            if (help.Sounds.Count > 0 && _youtube != null)
            {
                _findingWord = word;
                Refresh(word, "Finding a video for the " + SoundName(help.Sounds[0]) + " sound...");
                try { video = await FindVideoAsync(help.Sounds[0]); }
                finally { _findingWord = null; }
                if (gen != _generation || !SameRecording(word, wav)) return;
            }
            if (video != null) _videos[word] = video;
            _busy.Remove(word);
            Refresh(word, video != null ? "Watch the video, then tap \"Say it\" and try again."
                         : help.Sounds.Count > 0 ? "Read the tip, then tap \"Say it\" and try again."
                         : "Tap \"Say it\" to try again.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] word pronunciation help failed: " + e.Message);
            if (gen != _generation) return;
            _busy.Remove(word);
            Refresh(word, e is TimeoutException ? "Gemini is slow now. Tap \"" + AskLabel + "\" to try again."
                         : GeminiLater.IsBusy(e) ? "Gemini is busy now. Try again in a little while." : "Could not check it. Try again.");
        }
        finally
        {
            // 途中で言い直して結果を捨てたとき: 「Checking...」のままにしない
            if (gen == _generation && _busy.Remove(word) && CurrentWord?.Invoke() == word) Changed?.Invoke();
        }
    }

    // 待っている間に言い直していたら、古い録音の結果は使わない
    private bool SameRecording(string word, byte[] wav) => _wavs.TryGetValue(word, out byte[] now) && ReferenceEquals(now, wav);

    // 今のカードがその単語のときだけ、案内とカードを更新する
    private void Refresh(string word, string status)
    {
        if (CurrentWord?.Invoke() != word) return;
        _ctx.SetStatus(status);
        Changed?.Invoke();
    }

    private void SetRowActive(bool on)
    {
        if (_button == null) return;
        var row = _button.transform.parent != null ? _button.transform.parent.gameObject : _button.gameObject;
        if (row.activeSelf != on) row.SetActive(on);
    }

    // ------------------------------------------------------------
    // 音ごとの練習動画: 端末に 30 日覚えておく (YouTube の検索の上限を使いすぎない)
    // ------------------------------------------------------------
    public const int VideoCacheDays = 30;
    private const string CachePrefix = "vocab_sound_video_";

    private async Task<YouTubeSearchClient.Video> FindVideoAsync(SoundHit sound)
    {
        string key = CachePrefix + sound.Code + (sound.Code == SoundTypes.Other ? "_" + sound.Label : "");
        var cached = DecodeVideo(PlayerPrefs.GetString(key, ""), DateTime.UtcNow);
        if (cached != null) return cached;
        try
        {
            var found = await _youtube.SearchVideosByTitleAsync(SoundTypes.SearchQuery(sound.Code, sound.Label), 60, 900, 5);
            var video = found.FirstOrDefault();
            if (video == null) return null;
            PlayerPrefs.SetString(key, EncodeVideo(video, DateTime.UtcNow));
            PlayerPrefs.Save();
            return video;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] could not find a sound video: " + e.Message);
            return null;
        }
    }

    /// <summary>"ticks\tid\tchannel\ttitle" (UTC)</summary>
    public static string EncodeVideo(YouTubeSearchClient.Video v, DateTime savedUtc) =>
        savedUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t" + Clean(v.id) + "\t" + Clean(v.channel) + "\t" + Clean(v.title);

    /// <summary>保存した動画を読む。壊れている・30 日より古いときは null</summary>
    public static YouTubeSearchClient.Video DecodeVideo(string s, DateTime nowUtc)
    {
        var p = (s ?? "").Split('\t');
        if (p.Length < 4 || p[1].Length == 0) return null;
        if (!long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) || ticks <= 0) return null;
        if (nowUtc - new DateTime(ticks, DateTimeKind.Utc) > TimeSpan.FromDays(VideoCacheDays)) return null;
        return new YouTubeSearchClient.Video { id = p[1], channel = p[2], title = p[3] };
    }

    private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');
}
