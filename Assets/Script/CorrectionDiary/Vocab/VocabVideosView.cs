using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 単語帳のカードの「Videos」: その単語が出てくる YouTube の場面を流す画面 (Replay / Next / Close)。
/// 保存にあればすぐ流す。無ければ Supabase の一覧 → YouTube の検索 (VocabClipSource) の順に探す。
/// カードと評価の行を隠して表示するのは VocabTodayView の役目
/// </summary>
public class VocabVideosView
{
    public struct View
    {
        public GameObject Panel;
        public RectTransform VideoArea;     // 動画 (16:9) を出す範囲
        public TMP_Text SentenceText;       // 字幕の文 (単語は太字)。待ち・エラーの文もここ
        public TMP_Text ChannelText;
        public TMP_Text CountText;          // 「3 / 7」
        public Button Replay, Next, Close;
    }

    public const string FindingText = "Finding videos...";
    public const string SearchingText = "Searching YouTube...";
    public const string NoneText = "No videos for this word yet.";
    public const string OfflineText = "Can't reach the internet.";
    public const string LimitText = "Video search limit reached. Try again tomorrow.";
    public const string NoKeyText = "No YouTube key. Add youtubeApiKey to ApiSecrets.json.";

    private readonly View _v;
    private readonly VocabClipSource _source;
    private readonly IVocabClipStorage _storage;
    private readonly YouTubePlayerView _player;
    private readonly Func<DateTime> _nowUtc;

    private int _token;                     // 開くたび・閉じるたびに増やす (古い探索の結果を捨てる)
    private bool _open;
    private bool _paused;                   // ほかのタブへ移った (プレーヤーは隠してある)
    private string _word = "";
    private List<VocabClip> _clips = new List<VocabClip>();
    private readonly HashSet<int> _failed = new HashSet<int>();   // 再生できなかった場面
    private int _index = -1;                // 表示中の場面。-1 なら文 (待ち・エラー) を表示中
    private int _played = -1;               // プレーヤーに渡した場面 (Replay で流し直せるもの)
    private string _message = "";
    private Action _onClose;

    public bool IsOpen => _open;

    public VocabVideosView(View v, VocabClipSource source, IVocabClipStorage storage, YouTubePlayerView player, Func<DateTime> nowUtc)
    {
        _v = v;
        _source = source;
        _storage = storage;
        _player = player;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        if (_player != null)
        {
            if (_v.VideoArea != null) _player.SetVideoArea(_v.VideoArea);
            _player.OnError += OnPlayerError;   // 1 回だけ登録する (閉じている間は無視)
        }
        if (_v.Replay != null) _v.Replay.onClick.AddListener(Replay);
        if (_v.Next != null) _v.Next.onClick.AddListener(Next);
        if (_v.Close != null) _v.Close.onClick.AddListener(Close);
        VocabContext.SetLabel(_v.Replay, "Replay");
        VocabContext.SetLabel(_v.Next, "Next");
        VocabContext.SetLabel(_v.Close, "Close");
        SetActive(_v.Panel, false);
    }

    // ------------------------------------------------------------
    // 開く・閉じる
    // ------------------------------------------------------------
    /// <summary>単語の場面を探して流す。onClose は Close ボタンで閉じたときだけ呼ぶ</summary>
    public void Open(VocabCard card, Action onClose)
    {
        if (card == null) return;
        int token = ++_token;
        _open = true;
        _paused = false;
        _onClose = onClose;
        _word = VocabDeck.Key(card.Word);
        _clips = new List<VocabClip>();
        _failed.Clear();
        _index = -1;
        _played = -1;
        _message = "";
        SetActive(_v.Panel, true);
        ShowMessage("");
        _ = LoadAsync(token, _word);
    }

    /// <summary>Close ボタン: プレーヤーを隠してカードへ戻る</summary>
    public void Close()
    {
        if (!_open) return;
        var cb = _onClose;
        Dismiss();
        cb?.Invoke();
    }

    /// <summary>onClose を呼ばずに閉じる (評価した・回を離れたとき)</summary>
    public void Dismiss()
    {
        if (!_open) return;
        _token++;
        _open = false;
        _paused = false;
        _onClose = null;
        _player?.Hide();
        SetActive(_v.Panel, false);
    }

    /// <summary>ほかのタブ・画面へ移った: プレーヤーを隠す。場面の一覧と番号は残す</summary>
    public void Pause()
    {
        if (!_open) return;
        _paused = true;
        _player?.Hide();
    }

    /// <summary>戻ってきた: 同じ場面の文を表示するだけで流さない (Replay で流す)</summary>
    public void ResumeWithoutPlaying()
    {
        if (!_open) return;
        _paused = false;
        if (_index >= 0) ShowClip(_index, play: false);
        else ShowMessage(_message);
    }

    // ------------------------------------------------------------
    // 探す: 保存 → Supabase の一覧 → YouTube の検索
    // ------------------------------------------------------------
    private async Task LoadAsync(int token, string word)
    {
        try
        {
            var entry = _storage?.Load(word);
            if (VocabClipStore.IsReady(entry, _nowUtc())) { UseClips(entry.Clips); return; }   // 待たない
            if (_source == null) { ShowMessage(NoneText); return; }

            ShowMessage(FindingText);
            await _source.FillFromCloudAsync(new[] { word });
            if (token != _token) return;
            entry = _storage?.Load(word);
            if (VocabClipStore.IsReady(entry, _nowUtc())) { UseClips(entry.Clips); return; }

            ShowMessage(SearchingText);
            var (status, clips) = await _source.GetAsync(word, allowSearch: true);
            if (token != _token) return;
            if (status == ClipStatus.Ok) UseClips(clips);
            else ShowMessage(MessageFor(status));
        }
        catch (Exception e)
        {
            if (token != _token) return;
            Debug.LogWarning("[VocabClips] could not get videos for " + word + ": " + e.Message);
            ShowMessage(NoneText);
        }
    }

    public static string MessageFor(ClipStatus status) =>
        status == ClipStatus.Offline ? OfflineText
        : status == ClipStatus.Limit ? LimitText
        : status == ClipStatus.NoKey ? NoKeyText
        : NoneText;

    private void UseClips(List<VocabClip> clips)
    {
        _clips = clips ?? new List<VocabClip>();
        _clips.RemoveAll(c => c == null);
        _failed.Clear();
        if (_clips.Count == 0) { ShowMessage(NoneText); return; }
        ShowClip(0, play: true);
    }

    // ------------------------------------------------------------
    // 表示と再生
    // ------------------------------------------------------------
    private void ShowClip(int i, bool play)
    {
        _index = i;
        _message = "";
        var clip = _clips[i];
        if (_v.SentenceText != null) _v.SentenceText.text = Highlight(clip.Sentence, _word);
        if (_v.ChannelText != null) _v.ChannelText.text = VocabContext.Esc(clip.Channel ?? "");
        if (_v.CountText != null) _v.CountText.text = (i + 1) + " / " + _clips.Count;
        VocabContext.Show(_v.Replay, true);
        VocabContext.Show(_v.Next, true);
        if (play && !_paused) PlayCurrent();   // 裏で見つかった場面は、画面に戻ってから Replay で流す
    }

    private void PlayCurrent()
    {
        if (_index < 0 || _player == null) return;
        var clip = _clips[_index];
        _player.Show(clip.VideoId, clip.StartMs / 1000.0, clip.EndMs / 1000.0);
        _played = _index;
    }

    /// <summary>待ち・エラーの文を出す (動画は出さず、Close だけ押せる)</summary>
    private void ShowMessage(string text)
    {
        _index = -1;
        _message = text ?? "";
        _player?.Hide();
        if (_v.SentenceText != null) _v.SentenceText.text = _message;
        if (_v.ChannelText != null) _v.ChannelText.text = "";
        if (_v.CountText != null) _v.CountText.text = "";
        VocabContext.Show(_v.Replay, false);
        VocabContext.Show(_v.Next, false);
    }

    private void Replay()
    {
        if (!_open || _index < 0) return;
        _paused = false;
        if (_played == _index && _player != null) _player.Replay();
        else PlayCurrent();   // まだプレーヤーに渡していない (裏で見つかった) 場面
    }

    private void Next()
    {
        if (!_open || _index < 0) return;
        _paused = false;
        int next = NextIndex(_index);
        if (next < 0) { ShowMessage(NoneText); return; }
        ShowClip(next, play: true);
    }

    /// <summary>from の次の、再生できなかった場面を除いた番号 (最後なら最初へ)。全部だめなら -1</summary>
    private int NextIndex(int from)
    {
        int n = _clips.Count;
        for (int k = 1; k <= n; k++)
        {
            int i = (from + k) % n;
            if (!_failed.Contains(i)) return i;
        }
        return -1;
    }

    // 再生できない動画 (削除・埋め込み禁止など): 自動で次へ。全部だめなら「No videos」
    private void OnPlayerError(string error)
    {
        if (!_open || _index < 0) return;
        Debug.LogWarning("[VocabClips] could not play " + _clips[_index].VideoId + " (" + error + ")");
        _failed.Add(_index);
        int next = NextIndex(_index);
        if (next < 0)
        {
            Debug.LogWarning("[VocabClips] no playable videos for " + _word);
            ShowMessage(NoneText);
            return;
        }
        ShowClip(next, play: true);
    }

    // ------------------------------------------------------------
    // 文の中の単語を太字にする
    // ------------------------------------------------------------
    /// <summary>
    /// 文の中で word (空白を含めば連続した単語) に一致した所を &lt;b&gt; で囲む。区切りは VocabClipFinder.Tokens と同じ
    /// ([a-z'] の並び・前後の ' は除く・大文字小文字は無視)。元の文字はそのまま残し、文は VocabContext.Esc でエスケープする
    /// </summary>
    public static string Highlight(string sentence, string word)
    {
        if (string.IsNullOrEmpty(sentence)) return "";
        var target = VocabClipFinder.Tokens(word);
        var spans = TokenSpans(sentence);
        var sb = new StringBuilder();
        int pos = 0;
        if (target.Count > 0)
        {
            int i = 0;
            while (i + target.Count <= spans.Count)
            {
                bool hit = true;
                for (int k = 0; k < target.Count && hit; k++) hit = spans[i + k].Text == target[k];
                if (!hit) { i++; continue; }
                int s = spans[i].Start, e = spans[i + target.Count - 1].End;
                sb.Append(VocabContext.Esc(sentence.Substring(pos, s - pos)))
                  .Append("<b>").Append(VocabContext.Esc(sentence.Substring(s, e - s))).Append("</b>");
                pos = e;
                i += target.Count;
            }
        }
        sb.Append(VocabContext.Esc(sentence.Substring(pos)));
        return sb.ToString();
    }

    private struct Span { public int Start, End; public string Text; }

    // 文の中の単語の位置 (End は含まない) と、小文字にした単語
    private static List<Span> TokenSpans(string text)
    {
        var list = new List<Span>();
        int runStart = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool tok = false;
            if (i < text.Length)
            {
                char c = char.ToLowerInvariant(text[i]);
                tok = (c >= 'a' && c <= 'z') || c == '\'';
            }
            if (tok) { if (runStart < 0) runStart = i; continue; }
            if (runStart < 0) continue;
            int s = runStart, e = i;
            while (s < e && text[s] == '\'') s++;
            while (e > s && text[e - 1] == '\'') e--;
            if (e > s) list.Add(new Span { Start = s, End = e, Text = text.Substring(s, e - s).ToLowerInvariant() });
            runStart = -1;
        }
        return list;
    }

    private static void SetActive(GameObject go, bool on)
    {
        if (go != null) go.SetActive(on);
    }
}
