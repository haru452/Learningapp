using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Versant 対策: YouTube の短い英語動画を見て、制限時間内に話して要約する練習。
///
/// 流れ (メインボタン1つで進める):
///  1. [New Video]     YouTube Data API で短い英語の動画を探して表示する
///  2. [Start Summary] 動画を隠し、制限時間のカウントダウンと聞き取りを始める
///                     (動画が最後まで再生されたら、自動で始めることもできる)
///  3. [Finish]        時間切れ、またはボタンで終了 → Gemini が動画を見て採点する
///  4. 結果: スコア / 押さえた点・抜けた点 / 文法 / お手本の要約 / 改善例
///     → Discord に送信し、ログイン中ならクラウドにも保存する (点数の推移は Web 版で見られる)
///
/// 聞き取りは DiaryManager と同じく、内蔵の音声認識が無音などで切れても自動で再開する。
/// (Editor など内蔵認識が使えない環境では、録音した音声をそのまま Gemini に送る)
/// </summary>
public partial class SummaryPracticeManager : MonoBehaviour
{
    private enum State
    {
        Idle,       // 動画がまだ無い / 結果表示中
        Searching,
        Watching,
        Speaking,
        Grading
    }

    [Header("API Keys (※ 本番ではアプリに埋め込まず、バックエンド経由にすること)")]
    [SerializeField] private string geminiApiKey;
    [Tooltip("採点に使うモデル。動画を送るのでトークンを多く使う → 1日の回数に余裕がある Flash Lite を先に使う")]
    [SerializeField] private string geminiModel = "gemini-3.5-flash-lite";
    [Tooltip("上のモデルが上限 (429) や名前違い (404) のときに使う予備モデル")]
    [SerializeField] private string geminiFallbackModel = "gemini-3.8-flash";
    [Tooltip("YouTube Data API v3 のキー")]
    [SerializeField] private string youtubeApiKey;

    [Header("Practice")]
    [Tooltip("要約を話せる秒数")]
    [SerializeField] private int timeLimitSeconds = 60;
    [Tooltip("出題する動画の長さ (秒)")]
    [SerializeField] private int minVideoSeconds = 40;
    [SerializeField] private int maxVideoSeconds = 150;
    [Tooltip("ON: 動画が最後まで再生されたら、自動で要約のカウントダウンを始める")]
    [SerializeField] private bool autoStartWhenVideoEnds = true;
    [Tooltip("検索ワード。毎回この中からランダムに選ぶ")]
    [SerializeField] private string[] searchTopics =
    {
        "explained in 2 minutes", "science explained", "history short documentary",
        "how it works", "business news", "technology news", "health tips explained",
        "environment news", "psychology explained", "travel guide short", "space news",
        "economics explained simply"
    };

    [Header("Speech")]
    [SerializeField] private string speechLanguage = "en-US";

    [Header("Discord (※ Webhook URL / Botトークンは第三者に見せないこと。空なら ApiSecrets.json の値)")]
    [Tooltip("ON: 採点が終わったら Discord に送信する")]
    [SerializeField] private bool sendToDiscord = true;
    [Tooltip("要約練習用の Webhook URL (日記とは別のチャンネル)。ApiSecrets.json の summaryDiscordWebhookUrl")]
    [SerializeField] private string discordWebhookUrl;
    [Tooltip("Webhook URL が空のときだけ使う: 送信先チャンネルID (ApiSecrets.json の summaryDiscordChannelId)。" +
             "Bot トークンは日記と同じもの (discordBotToken) を使う")]
    [SerializeField] private string discordChannelId;

    [Header("Cloud (任意。ログイン中だけ、採点結果をクラウドに保存する。空欄ならシーンから探す)")]
    [SerializeField] private SupabaseService supabase;

    [Header("UI")]
    [Tooltip("New Video → Start Summary → Finish と、状態によって役割が変わるボタン")]
    [SerializeField] private Button mainButton;
    [SerializeField] private TMP_Text mainButtonLabel;
    [Tooltip("任意: 動画を飛ばして別の動画にするボタン")]
    [SerializeField] private Button skipButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text videoTitleText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text transcriptText;
    [SerializeField] private TMP_Text scoreText;
    [Tooltip("押さえた点 / 抜けた点 / 文法の指摘")]
    [SerializeField] private TMP_Text feedbackText;
    [Tooltip("お手本の要約 / 自分の要約の改善例")]
    [SerializeField] private TMP_Text referenceText;
    [SerializeField] private GameObject loadingIndicator;

    [Header("Components (空欄なら自動で取得/追加)")]
    [SerializeField] private YouTubePlayerView player;
    [SerializeField] private AudioRecorder recorder;
    [SerializeField] private NativeSpeechRecognizer nativeStt;

    private const string RecentIdsKey = "SummaryPractice.RecentVideoIds";
    private const int RecentIdsToKeep = 50;       // 同じ動画が続けて出ないよう、最近の動画を覚えておく
    private const int MaxSearchAttempts = 3;
    private const int MaxConsecutiveErrors = 3;
    private const int RestartDelayMs = 300;
    private const int StopTimeoutMs = 2000;       // 終了後、最終結果を待つ時間 (来なければ途中結果を使う)

    private GeminiApiClient _gemini;
    private YouTubeSearchClient _youtube;
    private DiscordClient _discord;

    private State _state = State.Idle;
    private YouTubeSearchClient.Video _video;
    private bool _busy;

    // 聞き取り
    private readonly List<string> _segments = new List<string>();
    private string _currentPartial = "";
    private bool _useNative;
    private bool _listening;      // 制限時間内で、まだ聞き取りを続ける
    private bool _finishing;
    private int _sessionId;
    private int _consecutiveErrors;
    private float _endTime;

    // 採点に失敗したときの再試行用
    private string _pendingText;
    private byte[] _pendingWav;

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------
    private void Awake()
    {
        if (player == null) player = GetComponentInChildren<YouTubePlayerView>(true);
        if (player == null) player = gameObject.AddComponent<YouTubePlayerView>();

        // ほかの画面のものは共有しない: NativeSpeechRecognizer は自分の Update() で認識結果を処理するので、
        // 置かれている画面が非表示になると結果が届かなくなる (AudioRecorder の録音上限の検知も同じ)。
        // Summary タブは「この画面を表示 → ほかの画面を非表示」の順に動くため、探すと非表示になる画面のものを拾ってしまう。
        if (recorder == null) recorder = GetComponent<AudioRecorder>();
        if (recorder == null) recorder = gameObject.AddComponent<AudioRecorder>();

        if (nativeStt == null) nativeStt = GetComponent<NativeSpeechRecognizer>();
        if (nativeStt == null) nativeStt = gameObject.AddComponent<NativeSpeechRecognizer>();

        // API キーは Assets/Resources/ApiSecrets.json から読み込む (GitHub には上げない)
        var secrets = ApiSecrets.Data;
        geminiApiKey = ApiSecrets.Pick(secrets.geminiApiKey, geminiApiKey);
        youtubeApiKey = ApiSecrets.Pick(secrets.youtubeApiKey, youtubeApiKey);
        // 日記とは別のチャンネルに送る。要約用の送り先が無ければ送らない (日記のチャンネルには流さない)
        discordWebhookUrl = ApiSecrets.Pick(secrets.summaryDiscordWebhookUrl, discordWebhookUrl);
        discordChannelId = ApiSecrets.Pick(secrets.summaryDiscordChannelId, discordChannelId);
        string discordBotToken = string.IsNullOrWhiteSpace(discordChannelId) ? "" : secrets.discordBotToken;

        if (supabase == null) supabase = FindObjectOfType<SupabaseService>(true);

        // 要約練習のキー (無ければ日記のキー) を使い、上限 (429) になったら日記・お題スピーキングのキーを借りる
        string summaryKey = ApiSecrets.Pick(secrets.geminiSummaryApiKey, geminiApiKey);
        _gemini = new GeminiApiClient(summaryKey, geminiModel, geminiFallbackModel,
            backupApiKeys: new[] { geminiApiKey, secrets.geminiTopicApiKey, secrets.geminiTtsApiKey });
        _youtube = new YouTubeSearchClient(youtubeApiKey);
        _discord = new DiscordClient(discordWebhookUrl, discordBotToken, discordChannelId);
    }

    private void Start()
    {
        mainButton.onClick.AddListener(OnMainButtonClicked);
        if (skipButton != null) skipButton.onClick.AddListener(OnSkipClicked);

        player.OnEnded += OnVideoEnded;
        recorder.OnMaxDurationReached += OnMaxDurationReached;
        nativeStt.OnPartialResult += OnNativePartial;
        nativeStt.OnSessionEnded += OnNativeSessionEnded;

        ClearResults();
        SetLoading(false);
        SetState(State.Idle);
        SetStatus("Tap \"New Video\" to start.");
    }

    // パネルが非表示にされたら、視聴・要約の途中なら中断する (採点中はそのまま完了させる)
    private void OnDisable()
    {
        if (_state == State.Watching)
        {
            player.Hide();
            SetState(State.Idle);
            SetStatus("Tap \"New Video\" to start.");
        }
        else if (_state == State.Speaking && !_finishing)
        {
            CancelListening();
            SetState(State.Idle);
            SetStatus("Cancelled. Tap \"New Video\" to try again.");
        }
    }

    private void OnDestroy()
    {
        if (mainButton != null) mainButton.onClick.RemoveListener(OnMainButtonClicked);
        if (skipButton != null) skipButton.onClick.RemoveListener(OnSkipClicked);
        if (player != null) player.OnEnded -= OnVideoEnded;

        if (recorder != null)
        {
            recorder.OnMaxDurationReached -= OnMaxDurationReached;
            if (_state == State.Speaking && recorder.IsRecording) recorder.StopRecording();
        }

        if (nativeStt != null)
        {
            nativeStt.OnPartialResult -= OnNativePartial;
            nativeStt.OnSessionEnded -= OnNativeSessionEnded;
            if (_state == State.Speaking) nativeStt.Cancel();
        }

        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    private void Update()
    {
        if (_state != State.Speaking || _finishing) return;

        float remaining = Mathf.Max(0f, _endTime - Time.realtimeSinceStartup);
        if (timerText != null) timerText.text = Mathf.CeilToInt(remaining).ToString();

        if (remaining <= 0f) _ = FinishSpeakingAsync();
    }

    // ------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------
    private async void OnMainButtonClicked()
    {
        if (_busy) return;

        switch (_state)
        {
            case State.Idle:
                if (_pendingText != null || _pendingWav != null) await GradeAsync();   // 採点の再試行
                else await LoadNewVideoAsync();
                break;
            case State.Watching:
                await BeginSpeakingAsync();
                break;
            case State.Speaking:
                await FinishSpeakingAsync();
                break;
        }
    }

    private async void OnSkipClicked()
    {
        if (_busy) return;
        if (_state != State.Idle && _state != State.Watching) return;

        _pendingText = null;
        _pendingWav = null;
        await LoadNewVideoAsync();
    }

    private async void OnVideoEnded()
    {
        if (_state != State.Watching || _busy) return;

        if (autoStartWhenVideoEnds) await BeginSpeakingAsync();
        else SetStatus("The video has ended. Tap \"Start Summary\" when you are ready.");
    }

    // ------------------------------------------------------------
    // 1. 動画を探す
    // ------------------------------------------------------------
    private async Task LoadNewVideoAsync()
    {
        _busy = true;
        player.Hide();
        ClearResults();
        SetState(State.Searching);
        SetLoading(true);
        SetStatus("Finding a video...");

        try
        {
            _video = await FindVideoAsync();
            if (_video == null)
            {
                SetState(State.Idle);
                SetStatus("Could not find a suitable video. Please try again.");
                return;
            }

            RememberVideo(_video.id);
            if (videoTitleText != null) videoTitleText.text = _video.title + "\n<size=70%>" + _video.channel + "</size>";

            player.Show(_video.id);
            SetState(State.Watching);
            SetStatus(YouTubePlayerView.IsEmbeddedSupported
                ? $"Watch the video ({FormatDuration(_video.durationSeconds)}). You will have {timeLimitSeconds} seconds to summarize it."
                : "The video opened in your browser. Come back and tap \"Start Summary\" when it ends.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetState(State.Idle);
            SetStatus("Error: " + FirstLine(e.Message));
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            RefreshButtons();
        }
    }

    private async Task<YouTubeSearchClient.Video> FindVideoAsync()
    {
        var recent = new HashSet<string>(LoadRecentIds());
        YouTubeSearchClient.Video fallback = null;

        for (int i = 0; i < MaxSearchAttempts; i++)
        {
            string topic = searchTopics[UnityEngine.Random.Range(0, searchTopics.Length)];
            var videos = await _youtube.SearchShortVideosAsync(topic, minVideoSeconds, maxVideoSeconds);

            foreach (var v in videos)
            {
                if (!recent.Contains(v.id)) return v;
                if (fallback == null) fallback = v;
            }
        }

        return fallback;   // 最近見た動画しか無ければ、それでも出題する
    }

    // ------------------------------------------------------------
    // 2. 要約を話す
    // ------------------------------------------------------------
    private async Task BeginSpeakingAsync()
    {
        _busy = true;
        try
        {
            _useNative = nativeStt.IsAvailable;

            if (!await recorder.RequestPermissionAsync())
            {
                SetStatus("Microphone permission denied. Please allow it in Settings.");
                return;
            }

            if (_useNative && !await nativeStt.RequestSpeechPermissionAsync())
            {
                SetStatus("Speech recognition permission denied. Please allow it in Settings.");
                return;
            }

            _segments.Clear();
            _currentPartial = "";
            _consecutiveErrors = 0;
            _finishing = false;
            _pendingText = null;
            _pendingWav = null;
            _sessionId++;
            ClearResults();

            bool started = _useNative
                ? nativeStt.StartListening(speechLanguage)
                : recorder.StartRecording();

            if (!started)
            {
                SetStatus("Could not start listening.");
                return;
            }

            player.Hide();   // 本番と同じく、話している間は動画を見られない
            _listening = true;
            _endTime = Time.realtimeSinceStartup + timeLimitSeconds;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            SetState(State.Speaking);
            SetStatus(_useNative
                ? "Summarize the video in English!"
                : "Summarize the video in English! (No live text in this mode)");
        }
        finally
        {
            _busy = false;
            RefreshButtons();
        }
    }

    private void OnMaxDurationReached()
    {
        // フォールバック(録音)モードで、録音の上限に先に達した
        if (_state == State.Speaking && !_useNative) _ = FinishSpeakingAsync();
    }

    private void OnNativePartial(string text)
    {
        if (_state != State.Speaking) return;
        _currentPartial = text;
        if (transcriptText != null) transcriptText.text = Compose(text);
    }

    private async void OnNativeSessionEnded(string text, string error)
    {
        if (_state != State.Speaking) return;

        _currentPartial = "";
        if (!string.IsNullOrWhiteSpace(text))
        {
            _segments.Add(text.Trim());
            _consecutiveErrors = 0;
            if (transcriptText != null) transcriptText.text = Compose(null);
        }
        else if (!string.IsNullOrEmpty(error))
        {
            _consecutiveErrors++;
        }

        if (!_listening) return;   // 終了処理の途中 (FinishSpeakingAsync が結果を待っている)

        if (_consecutiveErrors >= MaxConsecutiveErrors)
        {
            Debug.LogWarning("[SummaryPractice] speech recognition keeps failing: " + error);
            await FinishSpeakingAsync();
            return;
        }

        // 無音などで自動終了した → 制限時間内なので続けて聞き取る
        int id = _sessionId;
        await Task.Delay(RestartDelayMs);
        if (this == null || id != _sessionId || !_listening) return;

        if (!nativeStt.StartListening(speechLanguage))
            await FinishSpeakingAsync();
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private void CancelListening()
    {
        _listening = false;
        _sessionId++;   // 自動再開を無効化
        if (nativeStt != null) nativeStt.Cancel();
        if (recorder != null && recorder.IsRecording) recorder.StopRecording();
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        if (timerText != null) timerText.text = "";
    }

    private void SetState(State state)
    {
        _state = state;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool hasPending = _pendingText != null || _pendingWav != null;

        if (mainButtonLabel != null)
        {
            switch (_state)
            {
                case State.Idle:
                    mainButtonLabel.text = hasPending ? "Retry Grading" : _video == null ? "New Video" : "Next Video";
                    break;
                case State.Searching: mainButtonLabel.text = "Searching..."; break;
                case State.Watching:  mainButtonLabel.text = "Start Summary"; break;
                case State.Speaking:  mainButtonLabel.text = "Finish"; break;
                case State.Grading:   mainButtonLabel.text = "Grading..."; break;
            }
        }

        mainButton.interactable = !_busy &&
            (_state == State.Idle || _state == State.Watching || (_state == State.Speaking && !_finishing));

        if (skipButton != null)
        {
            skipButton.gameObject.SetActive(_state == State.Watching || (_state == State.Idle && hasPending));
            skipButton.interactable = !_busy;
        }
    }

    private string Compose(string current)
    {
        var sb = new StringBuilder();
        foreach (string s in _segments)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(s);
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(current.Trim());
        }

        return sb.ToString();
    }

    private void CommitPartial()
    {
        if (string.IsNullOrWhiteSpace(_currentPartial)) return;
        _segments.Add(_currentPartial.Trim());
        _currentPartial = "";
    }

    private static List<string> LoadRecentIds()
    {
        string saved = PlayerPrefs.GetString(RecentIdsKey, "");
        return string.IsNullOrEmpty(saved)
            ? new List<string>()
            : new List<string>(saved.Split(','));
    }

    private static void RememberVideo(string id)
    {
        var ids = LoadRecentIds();
        ids.Remove(id);
        ids.Add(id);
        if (ids.Count > RecentIdsToKeep) ids.RemoveRange(0, ids.Count - RecentIdsToKeep);
        PlayerPrefs.SetString(RecentIdsKey, string.Join(",", ids));
        PlayerPrefs.Save();
    }

    private static string FormatDuration(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = "Status: " + message;
    }

    private void SetLoading(bool on)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(on);
    }

    private void ClearResults()
    {
        if (timerText != null) timerText.text = "";
        if (transcriptText != null) transcriptText.text = "";
        if (scoreText != null) scoreText.text = "";
        if (feedbackText != null) feedbackText.text = "";
        if (referenceText != null) referenceText.text = "";
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf('\n');
        return i >= 0 ? s.Substring(0, i) : s;
    }
}
