using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// お題スピーキング:
///   New Topic → (Gemini がお題を作る) → 考える 15 秒 → 話す (daily 45 秒 / exam・work 60 秒)
///   → 採点・添削 → 言い直し (直された文を読み上げて、単語ごとに照合)。
/// ・苦手なミスの種類 (直近 30 日で 2 回以上) を「Today's focus」に出し、お題と採点にも使う
/// ・結果はクラウドに保存し (ログインしていなければ端末に一時保存)、Discord にも送る
/// ・この練習をした日も、連続記録に数える
/// 聞き取りは要約練習と同じく、内蔵の音声認識 (無音で切れても自動で再開)。使えない端末では録音して Gemini で文字起こし。
/// </summary>
public partial class TopicPracticeManager : MonoBehaviour
{
    private enum State
    {
        Idle,        // お題がまだ無い / 結果表示中
        Generating,  // お題を作っている
        Thinking,    // 考える時間
        Speaking,    // 話している
        Grading,     // 採点中
    }

    [Header("API Keys (空なら ApiSecrets.json の値)")]
    [SerializeField] private string geminiApiKey;
    [SerializeField] private string geminiModel = "gemini-3.5-flash-lite";
    [SerializeField] private string geminiFallbackModel = "gemini-3.8-flash";

    [Header("Practice")]
    [Tooltip("考える時間 (秒)。「Start now」で早めに話し始められる")]
    [SerializeField] private int thinkSeconds = 60;
    [Tooltip("答えは録音し、採点のときに Gemini が録音を聞いて文字起こしもする (採点と同じ 1 回の通信なので Gemini の使用回数は増えない。言葉が抜けない。話している途中の文字は出ない)。" +
             "オフにすると端末の音声認識 (途中の文字が出るが、数秒黙ると区切られて言葉が抜けやすい)")]
    [SerializeField] private bool recordAnswerForGrading = true;
    [SerializeField] private string speechLanguage = "en-US";

    [Header("Discord (要約練習と同じチャンネル。空なら ApiSecrets.json の値)")]
    [SerializeField] private bool sendToDiscord = true;
    [SerializeField] private string discordWebhookUrl;
    [SerializeField] private string discordChannelId;
    [Tooltip("発音チェックのアドバイスを Discord に送る (採点結果とは別のチャンネル)")]
    [SerializeField] private bool sendPronunciationToDiscord = true;
    [Tooltip("発音チェック用の Webhook URL。ApiSecrets.json の pronunciationDiscordWebhookUrl があればそちらを使う")]
    [SerializeField] private string pronunciationWebhookUrl;
    [Tooltip("Webhook の代わりに Bot で送る場合のチャンネルID。ApiSecrets.json の pronunciationDiscordChannelId があればそちらを使う")]
    [SerializeField] private string pronunciationChannelId;

    [Header("Cloud (空欄ならシーンから探す)")]
    [SerializeField] private SupabaseService supabase;

    [Header("UI")]
    [Tooltip("New Topic → Start now → Finish と、状態によって役割が変わるボタン")]
    [SerializeField] private Button mainButton;
    [SerializeField] private TMP_Text mainButtonLabel;
    [Tooltip("考える時間を飛ばすボタン (任意。無ければメインボタンの Start now だけ)")]
    [SerializeField] private Button skipThinkButton;
    [SerializeField] private TMP_Text kindText;
    [SerializeField] private TMP_Text topicText;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private TMP_Text weakPointText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text statusText;
    [Tooltip("話している内容 (任意)")]
    [SerializeField] private TMP_Text transcriptText;
    [Tooltip("点数・良かった点・直す点・添削・言い換え・表現・苦手のチェック (リッチテキスト)")]
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private GameObject loadingIndicator;
    [Tooltip("Topic タブの文字。空なら「Topic」という名前のボタンから探す。ほかの画面にいる間に採点が終わったら ● を付ける")]
    [SerializeField] private TMP_Text tabLabel;

    [Header("Read aloud (言い直し)")]
    [SerializeField] private GameObject readAloudGroup;
    [SerializeField] private TMP_Text readAloudText;
    [SerializeField] private Button readAloudButton;
    [SerializeField] private TMP_Text readAloudButtonLabel;
    [SerializeField] private Button pronunciationButton;
    [SerializeField] private TMP_Text pronunciationButtonLabel;
    [Tooltip("採点に失敗したとき、同じ答えで採点をやり直すボタン")]
    [SerializeField] private Button retryButton;

    [Header("Components (空欄なら自動で追加)")]
    [SerializeField] private AudioRecorder recorder;
    [SerializeField] private NativeSpeechRecognizer nativeStt;

    private const string RecentTopicsKey = "topic_recent_v1";
    private const string LastCountsKey = "topic_last_counts_v1";
    private const int RecentTopicsToKeep = 10;
    private const int MinFocusCount = 2;          // 「今日の注意」に出すのは、直近 30 日で 2 回以上の種類
    private const int MaxConsecutiveErrors = 3;
    private const int RestartDelayMs = 300;
    private const int StopTimeoutMs = 2000;
    private const int MaxPronunciationSeconds = 15;

    private const string MatchColor = "#40c057";
    private const string MissColor = "#fa5252";

    private GeminiApiClient _gemini;
    private DiscordClient _discord;
    private DiscordClient _pronDiscord;   // 発音チェックのアドバイス用 (別のチャンネル)
    private readonly System.Random _rng = new System.Random();

    private State _state = State.Idle;
    private bool _busy;
    private GeminiApiClient.TopicPrompt _topic;
    private List<string> _focusTypes = new List<string>();
    private float _thinkEnd;
    private float _endTime;

    // 聞き取り (話す・言い直しで共用)
    private readonly List<string> _segments = new List<string>();
    private string _currentPartial = "";
    private bool _useNative;
    private bool _listening;
    private bool _finishing;
    private int _sessionId;
    private int _consecutiveErrors;

    // 採点・文字起こしに失敗したときの再試行用
    private string _retryText;
    private byte[] _retryWav;        // 録音を採点するときに失敗したら、録音を取っておいて Retry で送り直す

    // 採点結果 1 回分の保存先。言い直しの一致率は、その結果に入れる (次のお題の結果に入らないように)
    private class SaveTicket
    {
        public long Id;                // クラウドに保存できたときの ID
        public string PendingLocalId;  // 端末に一時保存したときの ID
        public int? ReadAloudAccuracy; // 言い直しが終わったときの一致率
    }
    private SaveTicket _ticket;

    // 言い直し
    private string[] _readSentences = new string[0];
    private int _readIndex;
    private readonly List<int> _readScores = new List<int>();
    private bool _reading;           // 言い直しの聞き取り中
    private bool _recordingPron;     // 発音チェックの録音中
    private float _pronEnd;
    private string _lastPronComment = "";
    private string _lastReadResult = "";

    private DiaryManager _diary;

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------
    private void Awake()
    {
        // ほかの画面のものは共有しない (非表示の画面に置かれたものは結果が届かない。要約練習と同じ理由)
        if (recorder == null) recorder = GetComponent<AudioRecorder>();
        if (recorder == null) recorder = gameObject.AddComponent<AudioRecorder>();
        if (nativeStt == null) nativeStt = GetComponent<NativeSpeechRecognizer>();
        if (nativeStt == null) nativeStt = gameObject.AddComponent<NativeSpeechRecognizer>();

        var secrets = ApiSecrets.Data;
        geminiApiKey = ApiSecrets.Pick(secrets.geminiApiKey, geminiApiKey);
        discordWebhookUrl = ApiSecrets.Pick(secrets.summaryDiscordWebhookUrl, discordWebhookUrl);
        discordChannelId = ApiSecrets.Pick(secrets.summaryDiscordChannelId, discordChannelId);
        string discordBotToken = string.IsNullOrWhiteSpace(discordChannelId) ? "" : secrets.discordBotToken;

        if (supabase == null) supabase = FindObjectOfType<SupabaseService>(true);

        // お題スピーキングのキー (無ければ日記のキー) を使い、上限 (429) になったら日記・要約練習のキーを借りる
        string topicKey = ApiSecrets.Pick(secrets.geminiTopicApiKey, geminiApiKey);
        _gemini = new GeminiApiClient(topicKey, geminiModel, geminiFallbackModel,
            backupApiKeys: new[] { geminiApiKey, secrets.geminiSummaryApiKey, secrets.geminiTtsApiKey });
        _discord = new DiscordClient(discordWebhookUrl, discordBotToken, discordChannelId);

        pronunciationWebhookUrl = ApiSecrets.Pick(secrets.pronunciationDiscordWebhookUrl, pronunciationWebhookUrl);
        pronunciationChannelId = ApiSecrets.Pick(secrets.pronunciationDiscordChannelId, pronunciationChannelId);
        string pronBotToken = string.IsNullOrWhiteSpace(pronunciationChannelId) ? "" : secrets.discordBotToken;
        _pronDiscord = new DiscordClient(pronunciationWebhookUrl, pronBotToken, pronunciationChannelId);
    }

    private void Start()
    {
        mainButton.onClick.AddListener(OnMainButtonClicked);
        if (skipThinkButton != null) skipThinkButton.onClick.AddListener(OnSkipThinkClicked);
        if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
        if (readAloudButton != null) readAloudButton.onClick.AddListener(OnReadAloudClicked);
        if (pronunciationButton != null) pronunciationButton.onClick.AddListener(OnPronunciationClicked);

        recorder.OnMaxDurationReached += OnMaxDurationReached;
        nativeStt.OnPartialResult += OnNativePartial;
        nativeStt.OnSessionEnded += OnNativeSessionEnded;

        ClearTopic();
        ClearResults();
        SetLoading(false);
        SetState(State.Idle);
        SetStatus("Tap \"New Topic\" to start.");
    }

    // 画面が非表示にされたら、考える時間・話している途中・言い直しの途中なら中断する (採点中はそのまま完了させる)
    // この画面を開いたら、タブの「採点が終わった」印を消す
    private void OnEnable()
    {
        var label = FindTabLabel();
        if (label != null && TabBadge.Has(label.text)) label.text = TabBadge.Remove(label.text);
    }

    private TMP_Text FindTabLabel()
    {
        if (tabLabel != null) return tabLabel;
        var tab = FindObjectsOfType<Button>(true)
            .FirstOrDefault(b => b.name == "Topic" && b.gameObject.scene == gameObject.scene);
        tabLabel = tab != null ? tab.GetComponentInChildren<TMP_Text>(true) : null;
        return tabLabel;
    }

    /// <summary>
    /// 採点を待っている間にほかの画面 (日記・要約練習など) へ移っていたら、終わったことを知らせる:
    /// Topic タブに ● を付け、Android ではトーストも出す。score が null なら失敗したとき
    /// </summary>
    private void NotifyIfHidden(int? score)
    {
        if (this == null || gameObject.activeInHierarchy) return;
        var label = FindTabLabel();
        if (label != null) label.text = TabBadge.Add(label.text);
        ShowToast(TabBadge.Message(score));
    }

    private static void ShowToast(string message)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    using (var toast = new AndroidJavaClass("android.widget.Toast"))
                    using (var t = toast.CallStatic<AndroidJavaObject>("makeText", activity, message, 1))   // 1 = LENGTH_LONG
                        t.Call("show");
                }));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TopicPractice] could not show a toast: " + e.Message);
        }
#else
        Debug.Log("[TopicPractice] " + message);
#endif
    }

    private void OnDisable()
    {
        if (_state == State.Thinking || (_state == State.Speaking && !_finishing))
        {
            CancelListening();
            SetState(State.Idle);
            SetStatus("Cancelled. Tap \"New Topic\" to try again.");
        }
        if (_reading) { CancelListening(); _reading = false; RefreshReadAloud(); }
        if (_recordingPron) { if (recorder.IsRecording) recorder.StopRecording(); _recordingPron = false; RefreshReadAloud(); }
    }

    private void OnDestroy()
    {
        if (mainButton != null) mainButton.onClick.RemoveListener(OnMainButtonClicked);
        if (skipThinkButton != null) skipThinkButton.onClick.RemoveListener(OnSkipThinkClicked);
        if (retryButton != null) retryButton.onClick.RemoveListener(OnRetryClicked);
        if (readAloudButton != null) readAloudButton.onClick.RemoveListener(OnReadAloudClicked);
        if (pronunciationButton != null) pronunciationButton.onClick.RemoveListener(OnPronunciationClicked);

        if (recorder != null)
        {
            recorder.OnMaxDurationReached -= OnMaxDurationReached;
            if (recorder.IsRecording) recorder.StopRecording();
        }
        if (nativeStt != null)
        {
            nativeStt.OnPartialResult -= OnNativePartial;
            nativeStt.OnSessionEnded -= OnNativeSessionEnded;
            if (nativeStt.IsListening) nativeStt.Cancel();
        }
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    private void Update()
    {
        if (_state == State.Thinking)
        {
            float remaining = Mathf.Max(0f, _thinkEnd - Time.realtimeSinceStartup);
            if (timerText != null) timerText.text = "Think: " + Mathf.CeilToInt(remaining);
            if (remaining <= 0f && !_busy) _ = BeginSpeakingAsync();
        }
        else if (_state == State.Speaking && !_finishing)
        {
            float remaining = Mathf.Max(0f, _endTime - Time.realtimeSinceStartup);
            if (timerText != null) timerText.text = Mathf.CeilToInt(remaining).ToString();
            if (remaining <= 0f) _ = FinishSpeakingAsync();
        }

        if (_recordingPron && Time.realtimeSinceStartup >= _pronEnd) _ = StopPronunciationAsync();
    }

    // ------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------
    private async void OnMainButtonClicked()
    {
        if (_busy) return;
        switch (_state)
        {
            case State.Idle: await NewTopicAsync(); break;
            case State.Thinking: await BeginSpeakingAsync(); break;
            case State.Speaking: await FinishSpeakingAsync(); break;
        }
    }

    private async void OnSkipThinkClicked()
    {
        if (_busy || _state != State.Thinking) return;
        await BeginSpeakingAsync();
    }

    private async void OnRetryClicked()
    {
        if (_busy || _state != State.Idle) return;
        if (_retryWav != null) { await GradeAsync(null, _retryWav); return; }
        if (string.IsNullOrEmpty(_retryText)) return;
        await GradeAsync(_retryText);
    }

    // ------------------------------------------------------------
    // 1. お題を作る → 考える
    // ------------------------------------------------------------
    private async Task NewTopicAsync()
    {
        _busy = true;
        _retryText = null;
        _retryWav = null;
        ClearTopic();
        ClearResults();
        SetState(State.Generating);
        SetLoading(true);
        SetStatus("Making a topic...");

        try
        {
            // 苦手 (直近 30 日で 2 回以上) を最大 3 つ
            var weak = supabase != null ? await supabase.GetWeakPointsAsync() : new SupabaseService.WeakPoint[0];
            if (this == null) return;
            _focusTypes = weak.Where(w => w.Count >= MinFocusCount).Select(w => w.Type).Take(3).ToList();
            if (weakPointText != null)
                weakPointText.text = _focusTypes.Count > 0 ? "Today's focus: " + MistakeTypes.Label(_focusTypes[0]) : "";

            string kind = TopicCatalog.RandomKind(_rng);
            var recent = LoadRecentTopics();
            bool offline = false;
            try
            {
                _topic = await _gemini.GenerateTopicAsync(kind, _focusTypes, recent);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TopicPractice] could not make a topic with Gemini, using an offline one: " + e.Message);
                _topic = TopicCatalog.Offline(kind, _rng, recent);
                offline = true;
            }
            if (this == null) return;

            RememberTopic(_topic.topic);
            ShowTopic();
            _thinkEnd = Time.realtimeSinceStartup + Mathf.Max(0, thinkSeconds);
            SetState(State.Thinking);
            SetStatus((offline ? "Offline topic. " : "") +
                      $"Think about your answer. You will speak for {TopicCatalog.SecondsFor(_topic.kind)} seconds.");
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

    // ------------------------------------------------------------
    // 2. 話す
    // ------------------------------------------------------------
    private async Task BeginSpeakingAsync()
    {
        if (_state != State.Thinking) return;
        _busy = true;
        try
        {
            if (!await StartListeningAsync(forAnswer: true)) { SetState(State.Idle); return; }
            _endTime = Time.realtimeSinceStartup + TopicCatalog.SecondsFor(_topic.kind);
            SetState(State.Speaking);
            SetStatus(_useNative ? "Speak now!" : "Speak now! Your answer is recorded and written out after grading.");
        }
        finally
        {
            _busy = false;
            RefreshButtons();
        }
    }

    /// <summary>
    /// 聞き取りを始める。始められたら true。
    /// 答え (forAnswer) は録音して、採点のときに Gemini に聞いてもらう: 端末の音声認識は数秒の無音で切れ、
    /// 再開するまでの間の言葉が抜けるため、長く話すと文が欠けて点数が下がる。
    /// 言い直し (1 文) は端末の音声認識ですぐ結果を出す。使えなければどちらも録音。
    /// </summary>
    private async Task<bool> StartListeningAsync(bool forAnswer = false)
    {
        _useNative = nativeStt.IsAvailable && !(forAnswer && recordAnswerForGrading);
        if (!await recorder.RequestPermissionAsync())
        {
            SetStatus("Microphone permission denied. Please allow it in Settings.");
            return false;
        }
        if (_useNative && !await nativeStt.RequestSpeechPermissionAsync())
        {
            SetStatus("Speech recognition permission denied. Please allow it in Settings.");
            return false;
        }

        _segments.Clear();
        _currentPartial = "";
        _consecutiveErrors = 0;
        _finishing = false;
        _sessionId++;
        if (transcriptText != null) transcriptText.text = "";

        bool started = _useNative ? nativeStt.StartListening(speechLanguage) : recorder.StartRecording();
        if (!started)
        {
            SetStatus("Could not start listening.");
            return false;
        }
        _listening = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        return true;
    }

    private void OnMaxDurationReached()
    {
        if (_useNative) return;
        if (_state == State.Speaking) _ = FinishSpeakingAsync();
        else if (_reading) _ = StopReadingAsync();
    }

    private void OnNativePartial(string text)
    {
        if (!_listening) return;
        _currentPartial = text;
        if (_state == State.Speaking && transcriptText != null) transcriptText.text = Compose(text);
    }

    private async void OnNativeSessionEnded(string text, string error)
    {
        if (_state != State.Speaking && !_reading) return;

        _currentPartial = "";
        if (!string.IsNullOrWhiteSpace(text))
        {
            _segments.Add(text.Trim());
            _consecutiveErrors = 0;
            if (_state == State.Speaking && transcriptText != null) transcriptText.text = Compose(null);
        }
        else if (!string.IsNullOrEmpty(error))
        {
            _consecutiveErrors++;
        }

        if (!_listening) return;   // 終了処理の途中 (結果を待っている)

        // 言い直しは1文なので、聞き取りが切れたらそこで終わり
        if (_reading) { await StopReadingAsync(); return; }

        if (_consecutiveErrors >= MaxConsecutiveErrors)
        {
            Debug.LogWarning("[TopicPractice] speech recognition keeps failing: " + error);
            await FinishSpeakingAsync();
            return;
        }

        int id = _sessionId;
        await Task.Delay(RestartDelayMs);
        if (this == null || id != _sessionId || !_listening) return;
        if (!nativeStt.StartListening(speechLanguage)) await FinishSpeakingAsync();
    }

    /// <summary>聞き取りを止めて、聞き取れた文を返す (録音モードでは Gemini で文字起こし)</summary>
    private async Task<string> StopListeningAndGetTextAsync()
    {
        _listening = false;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;

        if (_useNative)
        {
            if (nativeStt.IsListening)
            {
                int before = _segments.Count;
                nativeStt.StopListening();
                float deadline = Time.realtimeSinceStartup + StopTimeoutMs / 1000f;
                while (nativeStt.IsListening && Time.realtimeSinceStartup < deadline)
                    await Task.Yield();
                if (nativeStt.IsListening) nativeStt.Cancel();
                if (_segments.Count == before) CommitPartial();
            }
            else
            {
                CommitPartial();
            }
            return Compose(null).Trim();
        }

        byte[] wav = recorder.IsRecording ? recorder.StopRecording() : null;
        if (wav == null) return "";
        SetStatus("Transcribing...");
        return ((await _gemini.TranscribeAsync(wav)) ?? "").Trim();
    }

}
