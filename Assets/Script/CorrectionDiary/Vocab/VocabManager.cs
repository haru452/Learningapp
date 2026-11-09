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
    public static readonly Color SelectedColor = new Color(0.31f, 0.27f, 0.90f, 1f);

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

    [Header("Components (空欄なら自動で追加)")]
    [SerializeField] private VocabAudio audioPlayer;
    [SerializeField] private AudioRecorder recorder;
    [SerializeField] private NativeSpeechRecognizer nativeStt;

    private VocabContext _ctx;
    private VocabTodayView _today;
    private VocabWordsView _words;
    private VocabSettingsView _settings;
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
    }

    private void Start()
    {
        _today = new VocabTodayView(_ctx, new VocabTodayView.View
        {
            LevelPickRow = levelPickRow, LevelPickButtons = levelPickButtons, StartButton = startButton, StartLabel = startLabel,
            ProgressText = progressText, ProgressFill = progressFill, CardArea = cardArea, CardButton = cardButton, CardText = cardText, SoundButton = soundButton,
            SayButton = sayButton, SayLabel = sayLabel, RatingRow = ratingRow, RatingButtons = ratingButtons, RatingLabels = ratingLabels,
            RatingHint = ratingHint, SummaryScroll = summaryScroll, SummaryContent = summaryContent, SummaryRowTemplate = summaryRowTemplate,
            RescoreButton = rescoreButton, WeakButton = weakButton, LevelUpButton = levelUpButton, LevelUpLabel = levelUpLabel,
        });
        _words = new VocabWordsView(_ctx, new VocabWordsView.View
        {
            LearningToggle = learningToggle, KnownToggle = knownToggle, FilterButtons = filterButtons, Content = wordsContent,
            RowTemplate = wordRowTemplate, DialogPanel = dialogPanel, DialogText = dialogText, DialogButtons = dialogButtons,
            DialogLabels = dialogLabels,
        });
        _settings = new VocabSettingsView(_ctx, new VocabSettingsView.View
        {
            LevelButtons = settingsLevelButtons, SizeButtons = sizeButtons, RetentionButtons = retentionButtons, CreditText = creditText,
        });

        if (todayTab != null) todayTab.onClick.AddListener(() => ShowTab(0));
        if (wordsTab != null) wordsTab.onClick.AddListener(() => ShowTab(1));
        if (settingsTab != null) settingsTab.onClick.AddListener(() => ShowTab(2));
        ShowTab(0);
    }

    private void OnDisable()
    {
        // ほかの画面に切り替えた: 回は終わり (評価した単語はもう保存してある。録音は捨てる)
        _today?.Leave();
        if (_tab == 0) _tab = -1;
    }

    private void OnEnable()
    {
        if (_today != null && _tab == -1) ShowTab(0);
    }

    private void OnDestroy()
    {
        _ctx?.Capture?.Dispose();
    }

    private void ShowTab(int tab)
    {
        if (tab == _tab) return;
        if (_tab == 0) _today.Leave();
        _tab = tab;
        SetStatus("");
        if (todayPanel != null) todayPanel.SetActive(tab == 0);
        if (wordsPanel != null) wordsPanel.SetActive(tab == 1);
        if (settingsPanel != null) settingsPanel.SetActive(tab == 2);
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
