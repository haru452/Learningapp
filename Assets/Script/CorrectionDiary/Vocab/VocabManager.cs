using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 単語帳の画面 (タブ「Vocab」)。上の 4 つの切り替え Today / Words / Add / Settings と、共有の部品
/// (単語帳・CEFR-J・辞書・音声) を持つ。画面ごとの動きは VocabTodayView などに分けている。
/// 画面は Tools → Vocabulary → Create Vocabulary Screen (VocabScreenBuilder) で組み立てる
/// </summary>
public class VocabManager : MonoBehaviour
{
    public static readonly Color SelectedColor = UiTheme.SurfaceRaised;

    [Header("API Keys (空なら ApiSecrets.json の値)")]
    [SerializeField] private string geminiModel = "gemini-3.8-flash";
    [SerializeField] private string geminiFallbackModel = "gemini-3.5-flash-lite";

    [Header("Cloud (空欄ならシーンから探す)")]
    [SerializeField] private SupabaseService supabase;

    [Header("Tabs")]
    [SerializeField] private Button todayTab;
    [SerializeField] private Button wordsTab;
    [SerializeField] private Button settingsTab;
    [SerializeField] private GameObject todayPanel;
    [SerializeField] private GameObject wordsPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject loadingIndicator;

    [Header("Today")]
    [SerializeField] private GameObject levelPickRow;
    [SerializeField] private Button[] levelPickButtons;      // A1 A2 B1 B2
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text startLabel;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Image progressFill;
    [SerializeField] private GameObject cardArea;
    [SerializeField] private Button cardButton;
    [SerializeField] private TMP_Text cardText;
    [SerializeField] private Button soundButton;
    [SerializeField] private Button sayButton;
    [SerializeField] private TMP_Text sayLabel;
    [SerializeField] private GameObject ratingRow;
    [SerializeField] private Button[] ratingButtons;         // Didn't know / Hard / Knew it / Easy
    [SerializeField] private TMP_Text[] ratingLabels;
    [SerializeField] private TMP_Text ratingHint;
    [SerializeField] private GameObject summaryScroll;
    [SerializeField] private Transform summaryContent;
    [SerializeField] private GameObject summaryRowTemplate;  // 子: Text (TMP)、Buttons/MyVoice、Buttons/Model (名前は VocabTodayView の定数)
    [SerializeField] private Button rescoreButton;
    [SerializeField] private Button weakButton;
    [SerializeField] private Button levelUpButton;
    [SerializeField] private TMP_Text levelUpLabel;

    [Header("Words")]
    [SerializeField] private Button learningToggle;
    [SerializeField] private Button knownToggle;
    [SerializeField] private Button[] filterButtons;          // All A1 A2 B1 B2 —
    [SerializeField] private Transform wordsContent;
    [SerializeField] private GameObject wordRowTemplate;      // Button + 子の TMP
    [SerializeField] private GameObject dialogPanel;
    [SerializeField] private TMP_Text dialogText;
    [SerializeField] private Button[] dialogButtons;          // 3 つ
    [SerializeField] private TMP_Text[] dialogLabels;

    [Header("Settings")]
    [SerializeField] private Button[] settingsLevelButtons;   // A1 A2 B1 B2
    [SerializeField] private Button[] sizeButtons;            // 5 10 15 20
    [SerializeField] private Button[] retentionButtons;       // 80 85 90 95
    [SerializeField] private TMP_Text creditText;

    [Header("Videos (空欄なら動画の場面の機能は無し)")]
    [SerializeField] private Button videosButton;             // カードの Sound / Say it の行
    [SerializeField] private GameObject videosPanel;
    [SerializeField] private RectTransform videoArea;         // 動画 (16:9) を出す範囲
    [SerializeField] private TMP_Text videoSentence;
    [SerializeField] private TMP_Text videoChannel;
    [SerializeField] private TMP_Text videoCount;
    [SerializeField] private Button videoReplay;
    [SerializeField] private Button videoNext;
    [SerializeField] private Button videoClose;
    [SerializeField] private YouTubePlayerView videoPlayer;   // 空欄なら自動で追加

    [Header("Components (空欄なら自動で追加)")]
    [SerializeField] private VocabAudio audioPlayer;
    [SerializeField] private AudioRecorder recorder;
    [SerializeField] private NativeSpeechRecognizer nativeStt;

    private VocabContext _ctx;
    private VocabTodayView _today;
    private VocabWordsView _words;
    private VocabSettingsView _settings;
    private IVocabClipStorage _clipStorage;
    private VocabClipSource _clipSource;
    private VocabClipPrefetcher _prefetcher;     // 動画の機能が無ければ null
    private readonly System.Random _rng = new System.Random();
    private int _tab = -1;
    private readonly Dictionary<Button, Color> _normalColors = new Dictionary<Button, Color>();

    // ------------------------------------------------------------
    private void Awake()
    {
        if (audioPlayer == null) audioPlayer = GetComponent<VocabAudio>();
        if (audioPlayer == null) { if (GetComponent<AudioSource>() == null) gameObject.AddComponent<AudioSource>(); audioPlayer = gameObject.AddComponent<VocabAudio>(); }
        if (recorder == null) recorder = GetComponent<AudioRecorder>();
        if (recorder == null) recorder = gameObject.AddComponent<AudioRecorder>();
        if (nativeStt == null) nativeStt = GetComponent<NativeSpeechRecognizer>();
        if (nativeStt == null) nativeStt = gameObject.AddComponent<NativeSpeechRecognizer>();
        if (supabase == null) supabase = FindObjectOfType<SupabaseService>(true);

        VocabStore.DeleteAllRecordings();   // 前に開いたときの録音 (採点しないまま閉じたもの)

        var secrets = ApiSecrets.Data;
        string diaryKey = secrets.geminiApiKey;
        var gemini = new GeminiApiClient(ApiSecrets.Pick(secrets.geminiVocabApiKey, diaryKey), geminiModel, geminiFallbackModel,
            backupApiKeys: new[] { diaryKey, secrets.geminiSummaryApiKey, secrets.geminiTopicApiKey, secrets.geminiTtsApiKey, secrets.geminiShadowApiKey });
        bool geminiReady = !string.IsNullOrWhiteSpace(secrets.geminiVocabApiKey) || !string.IsNullOrWhiteSpace(diaryKey);

        var cefrAsset = Resources.Load<TextAsset>("CefrjWords");
        var cefr = CefrWordList.Parse(cefrAsset != null ? cefrAsset.text : "[]");
        if (cefr.Count == 0) Debug.LogWarning("[Vocab] Assets/Resources/CefrjWords.json is missing or empty.");

        _ctx = new VocabContext
        {
            Deck = VocabStore.LoadDeck(),
            Cefr = cefr,
            Dictionary = new MerriamWebsterClient(secrets.merriamWebsterApiKey),
            Gemini = geminiReady ? gemini : null,
            Audio = audioPlayer,
            Supabase = supabase,
            SetStatus = SetStatus,
            SetLoading = on => { if (loadingIndicator != null) loadingIndicator.SetActive(on); },
            Highlight = Highlight,
            ShowTabs = on => { if (todayTab != null && todayTab.transform.parent != null) todayTab.transform.parent.gameObject.SetActive(on); },
        };
        _ctx.Capture = new VocabPronCapture(recorder, nativeStt, _ctx.Gemini);
        if (VideosEnabled) SetUpClips(secrets);
        _pixabay = new PixabayClient(secrets.pixabayApiKey);
    }

    private PixabayClient _pixabay;
    private VocabSearchLimitRow _limitRow;   // Settings の「今日の動画の検索」と Reset   // 単語カードの裏の画像 (キーが無ければ画像は出さない)

    // シーンを組み立て直す前 (Videos の部品が無い) は動画の機能を使わない
    private bool VideosEnabled => videosButton != null && videosPanel != null;

    // 動画の場面: 端末の保存 → Supabase の一覧 → YouTube の検索。次の 20 単語を裏で準備する
    private void SetUpClips(ApiSecrets secrets)
    {
        _clipStorage = new VocabClipFileStorage();
        string ytKey = ApiSecrets.Pick(secrets.youtubeVocabApiKey, secrets.youtubeApiKey);
        Func<string, Task<List<YouTubeSearchClient.Video>>> search = null;
        if (!string.IsNullOrWhiteSpace(ytKey)) search = new YouTubeSearchClient(ytKey).SearchWordVideosAsync;
        var transcript = new YouTubeTranscriptClient();
        _clipSource = new VocabClipSource(
            async words => await CloudReadyAsync() ? await supabase.GetVocabClipsAsync(words) : new List<VocabClip>(),
            async clips => { if (await CloudReadyAsync()) await supabase.AddVocabClipsAsync(clips); },   // ログインしていなければ保存しない
            search,
            transcript.GetEnglishAsync,
            _clipStorage,
            () => DateTime.UtcNow,
            Debug.LogWarning);
        _prefetcher = new VocabClipPrefetcher(
            _clipSource,
            _clipStorage,
            () => _today != null ? _today.RemainingWords() : new List<string>(),
            () => _ctx.Deck.Cards,
            VocabStore.LoadNextNew,
            VocabStore.SaveNextNew,
            () => VocabSettings.Level.Length == 0
                ? new List<string>()
                : VocabQueue.NewCandidates(_ctx.Cefr, VocabSettings.Level, _ctx.Deck.Cards.Select(c => c.Word), _rng),
            () => DateTime.UtcNow,
            Debug.Log);
        _ctx.KickPrefetch = () => _prefetcher?.Kick();
    }

    // 呼ぶたびに確かめる (途中でログイン・ログアウトしてもよい)
    /// <summary>「Word 3 / 10」のすぐ下に「Previous word」のボタンの行を作る (学習中で、戻れるときだけ出る)</summary>
    private Button CreateBackButton()
    {
        if (progressText == null) return null;
        var row = UiStyle.NewUi("BackRow", progressText.transform.parent);
        row.transform.SetSiblingIndex(progressText.transform.GetSiblingIndex() + 1);
        UiStyle.Size(row, UiTheme.MinTouch);
        UiStyle.Row(row, UiTheme.Gap);
        var go = UiStyle.NewUi("BackButton", row.transform);
        var button = go.AddComponent<Button>();
        UiStyle.Size(go, -1, 0, 360, 0);
        var label = UiStyle.NewText("Label", go.transform, progressText.font, "Previous word", UiTheme.BodySize, UiTheme.Text, FontStyles.Bold);
        UiStyle.Stretch(label.transform);
        UiStyle.Secondary(button);
        UiStyle.Size(UiStyle.NewUi("Spacer", row.transform), -1, 0, -1, 1);
        row.SetActive(false);
        return button;
    }

    /// <summary>Sound / Say it の行のすぐ下に「Didn't sound right?」の行を作る (録音がある単語の表でだけ出る)</summary>
    private Button CreateSoundHelpButton()
    {
        if (soundButton == null || soundButton.transform.parent == null) return null;
        var voiceRow = soundButton.transform.parent;
        var row = UiStyle.NewUi("SoundHelpRow", voiceRow.parent);
        row.transform.SetSiblingIndex(voiceRow.GetSiblingIndex() + 1);
        UiStyle.Size(row, UiTheme.MinTouch);
        UiStyle.Row(row, UiTheme.Gap);
        var go = UiStyle.NewUi("SoundHelpButton", row.transform);
        var button = go.AddComponent<Button>();
        UiStyle.Size(go, -1, 0, -1, 1);
        var font = cardText != null ? cardText.font : progressText != null ? progressText.font : null;
        var label = UiStyle.NewText("Label", go.transform, font, VocabSoundHelp.AskLabel, UiTheme.BodySize, UiTheme.Text, FontStyles.Bold);
        UiStyle.Stretch(label.transform);
        UiStyle.Secondary(button);
        row.SetActive(false);
        return button;
    }

    /// <summary>
    /// タブの行・Sound / Say it の行などを、決めた高さのままにする。
    /// 横に並べる行は「余りの高さがほしい」と伝えるので、何もしないとカードや下のパネルと高さを半分ずつ取り合い、
    /// タブやボタンがとても大きくなる。シーンを組み立て直していなくても直るように、起動時にここでも 0 にする
    /// </summary>
    private void KeepRowsCompact()
    {
        var rows = new[]
        {
            todayTab != null ? todayTab.transform.parent : null,     // Today / Words / Settings
            soundButton != null ? soundButton.transform.parent : null, // Sound / Say it / Videos
            levelPickRow != null ? levelPickRow.transform : null,
            ratingRow != null ? ratingRow.transform : null,
            startButton != null ? startButton.transform : null,
        };
        foreach (var row in rows)
        {
            if (row == null) continue;
            var le = row.GetComponent<LayoutElement>();
            if (le == null) le = row.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 0;
            if (le.preferredHeight < UiTheme.MinTouch) le.preferredHeight = UiTheme.MinTouch;   // 48dp より小さくしない
        }
    }

    /// <summary>ほかの機能と同じく、前回のログインがまだ戻っていなければ戻してから答える</summary>
    private async Task<bool> CloudReadyAsync() =>
        supabase != null && supabase.IsConfigured && await supabase.EnsureSignedInAsync();

    private void Start()
    {
        KeepRowsCompact();
        VocabVideosView videos = null;
        if (VideosEnabled)
        {
            if (videoPlayer == null) videoPlayer = GetComponent<YouTubePlayerView>();
            if (videoPlayer == null) videoPlayer = gameObject.AddComponent<YouTubePlayerView>();
            videos = new VocabVideosView(new VocabVideosView.View
            {
                Panel = videosPanel, VideoArea = videoArea, SentenceText = videoSentence, ChannelText = videoChannel,
                CountText = videoCount, Replay = videoReplay, Next = videoNext, Close = videoClose,
            }, _clipSource, _clipStorage, videoPlayer, () => DateTime.UtcNow);
        }
        else if (videosButton != null) videosButton.gameObject.SetActive(false);

        _today = new VocabTodayView(_ctx, new VocabTodayView.View
        {
            LevelPickRow = levelPickRow, LevelPickButtons = levelPickButtons, StartButton = startButton, StartLabel = startLabel,
            ProgressText = progressText, ProgressFill = progressFill, CardArea = cardArea, CardButton = cardButton, CardText = cardText, SoundButton = soundButton,
            SayButton = sayButton, SayLabel = sayLabel, RatingRow = ratingRow, RatingButtons = ratingButtons, RatingLabels = ratingLabels,
            RatingHint = ratingHint, SummaryScroll = summaryScroll, SummaryContent = summaryContent, SummaryRowTemplate = summaryRowTemplate,
            RescoreButton = rescoreButton, WeakButton = weakButton, LevelUpButton = levelUpButton, LevelUpLabel = levelUpLabel,
            VideosButton = videosButton, BackButton = CreateBackButton(),
        }, videos);
        if (_pixabay != null && _pixabay.IsConfigured && cardArea != null && soundButton != null)
            _today.SetCardImage(new VocabCardImageView(cardArea, soundButton.transform.parent, _pixabay, cardText != null ? cardText.font : null,
                () => { if (cardButton != null) cardButton.onClick.Invoke(); }));
        var helpButton = CreateSoundHelpButton();
        if (helpButton != null)
        {
            var secrets = ApiSecrets.Data;
            _today.SetSoundHelp(new VocabSoundHelp(_ctx, helpButton, ApiSecrets.Pick(secrets.youtubeVocabApiKey, secrets.youtubeApiKey)));
        }
        _words =new VocabWordsView(_ctx, new VocabWordsView.View
        {
            LearningToggle = learningToggle, KnownToggle = knownToggle, FilterButtons = filterButtons, Content = wordsContent,
            RowTemplate = wordRowTemplate, DialogPanel = dialogPanel, DialogText = dialogText, DialogButtons = dialogButtons,
            DialogLabels = dialogLabels,
        });
        _settings = new VocabSettingsView(_ctx, new VocabSettingsView.View
        {
            LevelButtons = settingsLevelButtons, SizeButtons = sizeButtons, RetentionButtons = retentionButtons, CreditText = creditText,
        });
        if (VideosEnabled && _clipStorage != null && settingsPanel != null)
            _limitRow = new VocabSearchLimitRow(settingsPanel, creditText != null ? creditText.transform : null,
                creditText != null ? creditText.font : null, _clipStorage, () => _prefetcher?.Kick());

        if (todayTab != null) todayTab.onClick.AddListener(() => ShowTab(0));
        if (wordsTab != null) wordsTab.onClick.AddListener(() => ShowTab(1));
        if (settingsTab != null) settingsTab.onClick.AddListener(() => ShowTab(2));
        ShowTab(0);
        _ctx.KickPrefetch?.Invoke();
    }

    private void OnDisable()
    {
        // ほかの画面に切り替えた: マイクと音声だけ止める。戻ったら同じタブ・同じカードから続ける
        _today?.Pause();
    }

    private void OnEnable()
    {
        if (_today != null && _tab == 0) _today.Show();   // 状況に合った案内文に戻す
        if (_today != null) _ctx.KickPrefetch?.Invoke();  // 初回は Start で準備を始める
    }

    private void OnDestroy()
    {
        _ctx?.Capture?.Dispose();
        _prefetcher?.Stop();
    }

    private void ShowTab(int tab)
    {
        if (tab == _tab) return;
        if (_tab == 0) _today.Pause();
        _tab = tab;
        SetStatus("");
        if (todayPanel != null) todayPanel.SetActive(tab == 0);
        if (wordsPanel != null) wordsPanel.SetActive(tab == 1);
        if (settingsPanel != null) settingsPanel.SetActive(tab == 2);
        if (tab == 2) _limitRow?.Refresh();
        Highlight(todayTab, tab == 0);
        Highlight(wordsTab, tab == 1);
        Highlight(settingsTab, tab == 2);
        if (tab == 0) _today.Show();
        else if (tab == 1) _words.Show();
        else _settings.Show();
    }

    private void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text ?? "";
    }

    private void Highlight(Button b, bool on)
    {
        if (b == null || b.image == null) return;
        if (!_normalColors.ContainsKey(b)) _normalColors[b] = b.image.color;
        b.image.color = on ? SelectedColor : _normalColors[b];
        var label = b.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = on ? UiTheme.Text : UiTheme.SubText;
    }
}

/// <summary>4 つの画面が共有するもの</summary>
public class VocabContext
{
    public VocabDeck Deck;
    public CefrWordList Cefr;
    public IDictionaryProvider Dictionary;
    public GeminiApiClient Gemini;          // キーが無ければ null
    public VocabAudio Audio;
    public VocabPronCapture Capture;
    public SupabaseService Supabase;
    public Action<string> SetStatus;
    public Action<bool> SetLoading;
    public Action<Button, bool> Highlight;
    public Action<bool> ShowTabs;           // 上の 4 つの切り替えを出す・隠す
    public Action KickPrefetch;             // 動画の場面の準備を始める (動画の機能が無ければ null)

    public const string NoDictionaryKey = "No dictionary key. Add merriamWebsterApiKey to ApiSecrets.json.";

    /// <summary>保存した辞書の結果、無ければ辞書で引いて保存する。キーが無いときは null。通信できないときは例外</summary>
    public async Task<DictionaryResult> LookupAsync(string word)
    {
        var cached = VocabStore.LoadDict(word);
        if (cached != null && cached.Version >= DictionaryResult.CurrentVersion) return cached;
        if (Dictionary == null || !Dictionary.IsConfigured) return cached;   // 引けないときは古い保存データのまま
        var r = await Dictionary.LookupAsync(word);
        VocabStore.SaveDict(word, r);
        return r;
    }

    /// <summary>辞書の品詞と照合した CEFR レベル</summary>
    public string LevelFor(string word, DictionaryResult dict) =>
        Cefr.LevelFor(word, dict != null && dict.Found ? dict.Entries.Select(e => e.Pos) : Enumerable.Empty<string>());

    public void SaveDeck()
    {
        try { VocabStore.SaveDeck(Deck); }
        catch (Exception e) { Debug.LogWarning("[Vocab] could not save the deck: " + e.Message); }
    }

    /// <summary>TMP のタグとして読まれないようにする</summary>
    /// <summary>辞書の結果を画面の文にする (見出し・品詞・CEFR・発音記号・意味)。カードの裏で使う</summary>
    public string DescribeEntries(DictionaryResult r, string word, int maxDefs = 3)
    {
        var sb = new StringBuilder();
        foreach (var e in r.Entries.Take(4))
        {
            string level = Cefr.LevelForPos(word, e.Pos);
            sb.Append("<b>").Append(Esc(e.Headword)).Append("</b>");
            if (e.Pos.Length > 0) sb.Append("  <i>").Append(Esc(e.Pos)).Append("</i>");
            if (level != null) sb.Append("  <color=#aaaaaa>").Append(level).Append("</color>");
            if (e.Ipa.Length > 0) sb.Append("  /").Append(Esc(e.Ipa)).Append('/');
            sb.Append('\n');
            foreach (var d in e.Definitions.Take(maxDefs)) sb.Append("<size=85%>- ").Append(Esc(d)).Append("</size>\n");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 品詞ごとの例文 (名詞と動詞がある語なら、両方の例文)。辞書の見出しの順で、同じ品詞の見出しはまとめる。
    /// 例文の無い品詞は出さない。品詞は maxPos まで、例文は品詞ごとに perPos まで
    /// </summary>
    public static List<(string pos, List<string> examples)> ExamplesByPos(DictionaryResult r, int perPos = 2, int maxPos = 4)
    {
        var result = new List<(string pos, List<string> examples)>();
        if (r == null || !r.Found) return result;
        foreach (var e in r.Entries)
        {
            if (e == null || e.Examples == null) continue;
            string pos = (e.Pos ?? "").Trim();
            int i = result.FindIndex(g => g.pos == pos);
            if (i < 0)
            {
                if (result.Count >= maxPos) continue;
                result.Add((pos, new List<string>()));
                i = result.Count - 1;
            }
            foreach (var ex in e.Examples)
                if (result[i].examples.Count < perPos && !string.IsNullOrWhiteSpace(ex) && !result[i].examples.Contains(ex))
                    result[i].examples.Add(ex);
        }
        result.RemoveAll(g => g.examples.Count == 0);
        return result;
    }

    public static string Esc(string s) => (s ?? "").Replace("<", "<​");

    public static string PronLabel(string code) =>
        code == VocabPronJudge.Clear ? "<color=#40c057>Clear</color>"
        : code == VocabPronJudge.Close ? "<color=#fab005>Close</color>"
        : code == VocabPronJudge.Again ? "<color=#fa5252>Try again</color>" : "";

    public static string GeminiLabel(string code) =>
        code == "good" ? "<color=#40c057>Good</color>"
        : code == "close" ? "<color=#fab005>Close</color>"
        : code == "practice" ? "<color=#fa5252>Practice</color>" : "";

    /// <summary>ボタンの子の文字を変える</summary>
    public static void SetLabel(Button b, string text)
    {
        var t = b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
        if (t != null) t.text = text;
    }

    public static void Show(Component c, bool on)
    {
        if (c != null) c.gameObject.SetActive(on);
    }

    /// <summary>テンプレート以外の子を消す</summary>
    public static void ClearChildren(Transform parent, GameObject template)
    {
        if (parent == null) return;
        foreach (Transform child in parent.Cast<Transform>().ToList())
            if (template == null || child.gameObject != template) UnityEngine.Object.Destroy(child.gameObject);
    }

    public static GameObject CloneRow(GameObject template, Transform parent)
    {
        var go = UnityEngine.Object.Instantiate(template, parent);
        go.SetActive(true);
        return go;
    }
}
