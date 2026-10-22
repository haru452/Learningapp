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
public class TopicPracticeManager : MonoBehaviour
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

    // ------------------------------------------------------------
    // 3. 終了 → 採点
    // ------------------------------------------------------------
    private async Task FinishSpeakingAsync()
    {
        if (_state != State.Speaking || _finishing) return;
        _finishing = true;
        _busy = true;
        if (timerText != null) timerText.text = "";
        SetLoading(true);
        SetStatus("Finishing...");
        RefreshButtons();

        // 録音した答えは、文字起こしせずにそのまま採点に送る (Gemini が聞いて文字起こしもする)
        if (!_useNative)
        {
            _listening = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            byte[] wav = recorder.IsRecording ? recorder.StopRecording() : null;
            _busy = false;
            if (wav == null || wav.Length == 0)
            {
                SetLoading(false);
                SetState(State.Idle);
                SetStatus("Could not record your answer. Tap \"New Topic\" to try again.");
                return;
            }
            await GradeAsync(null, wav);
            return;
        }

        string text;
        try
        {
            text = await StopListeningAndGetTextAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            text = "";
        }
        if (this == null) return;
        if (transcriptText != null) transcriptText.text = text;

        _busy = false;
        if (CountWords(text) < 2)
        {
            SetLoading(false);
            SetState(State.Idle);
            SetStatus("Please speak a little more. Tap \"New Topic\" to try again.");
            return;
        }

        await GradeAsync(text);
    }

    /// <summary>答えを採点する。wav があれば録音を送り、文字起こしも Gemini に任せる (text は使わない)</summary>
    private async Task GradeAsync(string text, byte[] wav = null)
    {
        _busy = true;
        SetState(State.Grading);
        SetLoading(true);
        SetStatus(wav != null ? "Listening to your answer and grading it... (this can take up to a minute)" : "Grading your answer...");
        if (retryButton != null) retryButton.gameObject.SetActive(false);

        try
        {
            int seconds = TopicCatalog.SecondsFor(_topic.kind);
            var grade = wav != null
                ? await _gemini.GradeTopicAudioAsync(_topic.kind, _topic.topic, seconds, wav, _focusTypes)
                : await _gemini.GradeTopicAsync(_topic.kind, _topic.topic, seconds, text, _focusTypes);
            if (this == null) return;
            _retryText = null;
            _retryWav = null;

            if (wav != null)
            {
                text = grade.transcript;
                if (transcriptText != null) transcriptText.text = text;
                if (CountWords(text) < 2)
                {
                    SetState(State.Idle);
                    SetStatus("Please speak a little more. Tap \"New Topic\" to try again.");
                    return;
                }
            }

            var counts = MistakeTypes.Count(grade.mistakes);
            ShowGrade(grade, text, counts);
            SaveLastCounts(counts);
            SetUpReadAloud(grade.readAloud);

            _ticket = new SaveTicket();
            SaveResultToCloudAsync(_ticket, _topic, seconds, grade, text);   // 待たない
            CountAsStudyDay();

            string discordError = await SendToDiscordAsync(grade);
            SetState(State.Idle);
            SetStatus(discordError == null
                ? "Done! Read the corrected sentences aloud, or tap \"New Topic\"."
                : "Graded, but Discord failed: " + discordError);
            NotifyIfHidden(grade.score);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (wav != null) _retryWav = wav;   // 録音は残しておく (答えを失わないように)
            else _retryText = text;
            SetState(State.Idle);
            SetStatus("Error: " + FirstLine(e.Message) + "  (Tap Retry to grade the same answer again)");
            NotifyIfHidden(null);
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            RefreshButtons();
        }
    }

    private void ShowGrade(GeminiApiClient.TopicGrade g, string spoken, Dictionary<string, int> counts)
    {
        if (resultText == null) return;
        var sb = new StringBuilder();
        sb.Append("<b>Score: ").Append(g.score).Append(" / 100</b>\n")
          .Append("<size=70%>Content ").Append(g.contentScore).Append('/').Append(GeminiApiClient.TopicContentMax)
          .Append("  ·  Organization ").Append(g.organizationScore).Append('/').Append(GeminiApiClient.TopicOrganizationMax)
          .Append("  ·  Grammar & vocabulary ").Append(g.languageScore).Append('/').Append(GeminiApiClient.TopicLanguageMax)
          .Append("</size>\n\n");

        // 苦手のチェック: 今回と前回の回数
        if (_focusTypes.Count > 0)
        {
            var last = LoadLastCounts();
            sb.Append("<b>Focus check</b>\n");
            foreach (string t in _focusTypes)
            {
                counts.TryGetValue(t, out int now);
                string before = last.TryGetValue(t, out int prev) ? prev.ToString() : "-";
                sb.Append(MistakeTypes.Label(t)).Append(": this time ").Append(now)
                  .Append(" / last time ").Append(before).Append('\n');
            }
            sb.Append('\n');
        }

        AppendSection(sb, "Good points", g.goodPoints);
        AppendSection(sb, "To improve", g.fixPoints);
        string corrected = string.IsNullOrWhiteSpace(g.corrected) ? "" : TextDiff.UnderlineChanges(spoken, g.corrected, out _);
        AppendSection(sb, "Corrected", corrected);
        AppendSection(sb, "More natural", g.natural);
        if (g.phrases != null && g.phrases.Length > 0)
            AppendSection(sb, "Useful phrases", "- " + string.Join("\n- ", g.phrases));
        resultText.text = sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------
    // 4. 言い直し
    // ------------------------------------------------------------
    private void SetUpReadAloud(string[] sentences)
    {
        _readSentences = sentences ?? new string[0];
        _readIndex = 0;
        _readScores.Clear();
        _lastPronComment = "";
        _lastReadResult = "";
        RefreshReadAloud();
    }

    private bool ReadAloudFinished => _readIndex >= _readSentences.Length;

    private async void OnReadAloudClicked()
    {
        if (_busy || _state != State.Idle || _recordingPron || _readSentences.Length == 0) return;

        if (_reading) { await StopReadingAsync(); return; }

        // 1 文読み終わった後のボタンは「次の文へ」
        if (!string.IsNullOrEmpty(_lastReadResult))
        {
            _readIndex++;
            _lastReadResult = "";
            _lastPronComment = "";
            RefreshReadAloud();
            if (ReadAloudFinished) FinishReadAloud();
            return;
        }

        if (ReadAloudFinished) return;
        if (!await StartListeningAsync()) return;
        _reading = true;
        SetStatus("Read the sentence aloud, then tap Stop.");
        RefreshReadAloud();
    }

    private async Task StopReadingAsync()
    {
        if (!_reading || _finishing) return;
        _finishing = true;
        string heard;
        try
        {
            heard = await StopListeningAndGetTextAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            heard = "";
        }
        finally
        {
            _finishing = false;
            _reading = false;
        }
        if (this == null || ReadAloudFinished) return;

        // 何も聞き取れなかった (通信エラー・無音など) ときは 0% にせず、もう一度読んでもらう
        if (string.IsNullOrWhiteSpace(heard))
        {
            SetStatus("I couldn't hear you. Tap \"Read aloud\" to try again.");
            RefreshReadAloud();
            return;
        }

        var match = ReadAloudMatcher.Match(_readSentences[_readIndex], heard);
        _readScores.Add(match.AccuracyPercent);
        var sb = new StringBuilder();
        foreach (var w in match.Words)
            sb.Append("<color=").Append(w.Matched ? MatchColor : MissColor).Append('>').Append(w.Text).Append("</color> ");
        sb.Append("\n<b>").Append(match.AccuracyPercent).Append("%</b>");
        if (!string.IsNullOrWhiteSpace(heard)) sb.Append("  <size=70%>(heard: ").Append(heard).Append(")</size>");
        _lastReadResult = sb.ToString();
        SetStatus("Tap \"Next\" for the next sentence, or check your pronunciation.");
        RefreshReadAloud();
    }

    private void FinishReadAloud()
    {
        if (_readScores.Count == 0) return;
        int average = (int)Math.Round(_readScores.Average(), MidpointRounding.AwayFromZero);
        if (readAloudText != null) readAloudText.text = "<b>Read-aloud accuracy: " + average + "%</b>";
        SetStatus("Read-aloud finished! Tap \"New Topic\" for another topic.");

        var ticket = _ticket;
        if (ticket == null) return;
        ticket.ReadAloudAccuracy = average;
        if (ticket.Id > 0) _ = SaveReadAloudAsync(ticket.Id, average);
        else if (ticket.PendingLocalId != null) PendingTopicStore.SetReadAloud(ticket.PendingLocalId, average);
        // どちらでもない (保存中) ときは、保存が終わったところで入れる
    }

    private async Task SaveReadAloudAsync(long id, int accuracy)
    {
        try { await supabase.SetTopicReadAloudAsync(id, accuracy); }
        catch (Exception e) { Debug.LogWarning("[TopicPractice] could not save read-aloud accuracy: " + e.Message); }
    }

    private async void OnPronunciationClicked()
    {
        if (_busy || _state != State.Idle || _reading || ReadAloudFinished) return;

        if (_recordingPron) { await StopPronunciationAsync(); return; }

        if (!await recorder.RequestPermissionAsync())
        {
            SetStatus("Microphone permission denied. Please allow it in Settings.");
            return;
        }
        if (!recorder.StartRecording())
        {
            SetStatus("Could not start recording.");
            return;
        }
        _recordingPron = true;
        _pronEnd = Time.realtimeSinceStartup + MaxPronunciationSeconds;
        SetStatus("Read the sentence aloud, then tap Stop.");
        RefreshReadAloud();
    }

    private async Task StopPronunciationAsync()
    {
        if (!_recordingPron) return;
        _recordingPron = false;
        byte[] wav = recorder.IsRecording ? recorder.StopRecording() : null;
        RefreshReadAloud();
        if (wav == null || ReadAloudFinished) return;

        _busy = true;
        SetLoading(true);
        SetStatus("Checking your pronunciation...");
        RefreshButtons();
        try
        {
            string sentence = _readSentences[_readIndex];
            _lastPronComment = await _gemini.CheckPronunciationAsync(sentence, wav);
            SetStatus("Pronunciation checked.");
            SendPronunciationToDiscord(sentence, _lastPronComment);   // 待たない
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Error: " + FirstLine(e.Message));
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            RefreshButtons();
            RefreshReadAloud();
        }
    }

    // 言い直しの間は、お題 (質問) の場所に読む文を出す (どこを読めばいいか分かるように)
    private bool _showingSentenceAtTop;

    // 読む文を囲む枠 (お題の文字の上に、実行時に作る)
    private GameObject _sentenceBox;
    private Vector4 _topicMargin;
    private static readonly Color SentenceBoxColor = new Color(1f, 1f, 1f, 0.75f);
    private const float SentenceBoxLine = 3f;

    private void SetSentenceBox(bool on)
    {
        if (topicText == null) return;
        if (_sentenceBox == null)
        {
            if (!on) return;
            _topicMargin = topicText.margin;
            _sentenceBox = new GameObject("ReadAloudBox", typeof(RectTransform));
            var box = (RectTransform)_sentenceBox.transform;
            box.SetParent(topicText.transform, false);
            box.anchorMin = Vector2.zero;
            box.anchorMax = Vector2.one;
            box.offsetMin = Vector2.zero;
            box.offsetMax = Vector2.zero;

            var fill = _sentenceBox.AddComponent<Image>();      // うっすら背景
            fill.color = new Color(1f, 1f, 1f, 0.06f);
            fill.raycastTarget = false;

            // 上下左右の線
            AddEdge(box, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, SentenceBoxLine));
            AddEdge(box, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, SentenceBoxLine));
            AddEdge(box, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(SentenceBoxLine, 0));
            AddEdge(box, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(SentenceBoxLine, 0));
        }
        _sentenceBox.SetActive(on);
        topicText.margin = on ? new Vector4(24, 16, 24, 16) : _topicMargin;   // 文字が枠に付かないように
    }

    private static void AddEdge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)edge.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = edge.GetComponent<Image>();
        img.color = SentenceBoxColor;
        img.raycastTarget = false;
    }

    private void ShowSentenceAtTop()
    {
        _showingSentenceAtTop = true;
        SetSentenceBox(true);
        if (kindText != null) kindText.text = "Read aloud  ·  " + (_readIndex + 1) + " / " + _readSentences.Length;
        if (topicText != null) topicText.text = _readSentences[_readIndex];
        if (hintText != null)
            hintText.text = "Read this sentence aloud. \"Read aloud\" checks the words, " +
                            "\"Pronunciation check\" gives pronunciation tips.";
    }

    private void RestoreTopicAtTop()
    {
        if (!_showingSentenceAtTop) return;
        _showingSentenceAtTop = false;
        SetSentenceBox(false);
        if (_topic != null) ShowTopic();
        else ClearTopic();
    }

    private void RefreshReadAloud()
    {
        bool any = _readSentences.Length > 0;
        if (readAloudGroup != null) readAloudGroup.SetActive(any);
        if (!any || ReadAloudFinished) RestoreTopicAtTop();
        if (!any) return;
        if (ReadAloudFinished)
        {
            // 全部読み終わった (readAloudText には FinishReadAloud が平均を出している)
            if (readAloudButtonLabel != null) readAloudButtonLabel.text = "Done";
            if (readAloudButton != null) readAloudButton.interactable = false;
            if (pronunciationButton != null) pronunciationButton.interactable = false;
            return;
        }

        if (readAloudText != null)
        {
            // 読む文は上 (お題の場所) に出すので、ここには結果だけ
            var sb = new StringBuilder();
            sb.Append("<b>Read aloud (").Append(_readIndex + 1).Append('/').Append(_readSentences.Length).Append(")</b>");
            if (string.IsNullOrEmpty(_lastReadResult) && string.IsNullOrEmpty(_lastPronComment))
                sb.Append("\nRead the sentence shown at the top.");
            if (!string.IsNullOrEmpty(_lastReadResult)) sb.Append("\n").Append(_lastReadResult);
            if (!string.IsNullOrEmpty(_lastPronComment)) sb.Append("\n\n<i>").Append(_lastPronComment).Append("</i>");
            readAloudText.text = sb.ToString();
        }
        ShowSentenceAtTop();

        if (readAloudButtonLabel != null)
            readAloudButtonLabel.text = _reading ? "Stop"
                : !string.IsNullOrEmpty(_lastReadResult) ? (_readIndex + 1 < _readSentences.Length ? "Next" : "Finish")
                : "Read aloud";
        if (readAloudButton != null) readAloudButton.interactable = !_busy && !_recordingPron;
        if (pronunciationButtonLabel != null)
            pronunciationButtonLabel.text = _recordingPron ? "Stop" : "Pronunciation check";
        if (pronunciationButton != null) pronunciationButton.interactable = !_busy && !_reading;
    }

    // ------------------------------------------------------------
    // 結果の保存・送信・連続記録
    // ------------------------------------------------------------
    // 失敗しても採点結果は画面に残る。ログインしていない・送れないときは端末に一時保存し、ログイン時にまとめて送る
    private async void SaveResultToCloudAsync(SaveTicket ticket, GeminiApiClient.TopicPrompt topic, int seconds,
                                              GeminiApiClient.TopicGrade g, string transcript)
    {
        if (supabase == null || !supabase.IsConfigured) return;

        DateTime takenAt = DateTime.UtcNow;
        if (!await supabase.EnsureSignedInAsync())
        {
            ticket.PendingLocalId = PendingTopicStore.Add(topic.kind, topic.topic, seconds, g, transcript, takenAt,
                                                          ticket.ReadAloudAccuracy);
            return;
        }

        try
        {
            long id = await supabase.SaveTopicResultAsync(topic.kind, topic.topic, seconds, g, transcript,
                                                          ticket.ReadAloudAccuracy, takenAt);
            int? sent = ticket.ReadAloudAccuracy;
            ticket.Id = id;
            // 保存している間に言い直しが終わっていたら、その一致率を入れる
            if (id > 0 && ticket.ReadAloudAccuracy.HasValue && ticket.ReadAloudAccuracy != sent)
                await SaveReadAloudAsync(id, ticket.ReadAloudAccuracy.Value);
            if (this == null) return;
            if (PendingTopicStore.Count > 0) _ = supabase.UploadPendingTopicResultsAsync();
            var diary = FindDiary();
            if (diary != null) diary.RefreshStreakFromCloud();   // サーバーが数えた連続記録を、カードに反映する
        }
        catch (SupabaseService.SupabaseException e) when (e.StatusCode == 400)
        {
            Debug.LogWarning("[TopicPractice] the result could not be saved (invalid data): " + e.Message);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TopicPractice] could not save the result. Keeping it to upload later: " + e.Message);
            ticket.PendingLocalId = PendingTopicStore.Add(topic.kind, topic.topic, seconds, g, transcript, takenAt,
                                                          ticket.ReadAloudAccuracy);
        }
    }

    // 発音チェックのアドバイスを、発音用のチャンネルに送る (失敗しても画面の結果はそのまま)
    private async void SendPronunciationToDiscord(string sentence, string advice)
    {
        if (!sendPronunciationToDiscord || _pronDiscord == null || !_pronDiscord.IsConfigured) return;
        try
        {
            await _pronDiscord.SendPronunciationAsync(DateTime.Now, _topic?.topic, sentence, advice);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TopicPractice] could not send pronunciation advice to Discord: " + e.Message);
            if (this != null && !_busy) SetStatus("Pronunciation checked, but Discord failed: " + FirstLine(e.Message));
        }
    }

    private async Task<string> SendToDiscordAsync(GeminiApiClient.TopicGrade g)
    {
        if (!sendToDiscord || !_discord.IsConfigured) return null;
        SetStatus("Sending to Discord...");
        try
        {
            await _discord.SendTopicResultAsync(DateTime.Now, _topic.kind, _topic.topic, g);
            return null;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return FirstLine(e.Message);
        }
    }

    private DiaryManager FindDiary()
    {
        if (_diary == null) _diary = FindObjectOfType<DiaryManager>(true);
        return _diary;
    }

    // お題スピーキングをした日も、連続記録に数える (端末内の記録 + 夜のリマインダー。クラウドはサーバーが数える)
    private void CountAsStudyDay()
    {
        var diary = FindDiary();
        if (diary != null) diary.CountOtherPracticeAsStudyDay();
    }

    // ------------------------------------------------------------
    // 端末に覚えておくもの
    // ------------------------------------------------------------
    private static List<string> LoadRecentTopics()
    {
        string saved = PlayerPrefs.GetString(RecentTopicsKey, "");
        return string.IsNullOrEmpty(saved) ? new List<string>() : saved.Split('\n').ToList();
    }

    private static void RememberTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;
        var list = LoadRecentTopics();
        list.Remove(topic);
        list.Add(topic.Replace('\n', ' '));
        if (list.Count > RecentTopicsToKeep) list.RemoveRange(0, list.Count - RecentTopicsToKeep);
        PlayerPrefs.SetString(RecentTopicsKey, string.Join("\n", list));
        PlayerPrefs.Save();
    }

    // 前回のお題スピーキングの、種類ごとのミスの回数 ("tense:2,article:1")
    private static Dictionary<string, int> LoadLastCounts()
    {
        var counts = new Dictionary<string, int>();
        string saved = PlayerPrefs.GetString(LastCountsKey, "");
        foreach (string pair in saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1], out int n)) counts[MistakeTypes.Normalize(kv[0])] = n;
        }
        return counts;
    }

    private static void SaveLastCounts(Dictionary<string, int> counts)
    {
        PlayerPrefs.SetString(LastCountsKey, string.Join(",", counts.Select(kv => kv.Key + ":" + kv.Value)));
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private void ShowTopic()
    {
        if (kindText != null)
        {
            string label = _topic.kind == "exam" ? "Exam" : _topic.kind == "work" ? "Work" : "Daily";
            kindText.text = label + "  ·  " + TopicCatalog.SecondsFor(_topic.kind) + " sec";
        }
        if (topicText != null) topicText.text = _topic.topic;
        if (hintText != null) hintText.text = string.IsNullOrWhiteSpace(_topic.hint) ? "" : "Hint: " + _topic.hint;
    }

    private void ClearTopic()
    {
        _showingSentenceAtTop = false;
        SetSentenceBox(false);
        if (kindText != null) kindText.text = "";
        if (topicText != null) topicText.text = "";
        if (hintText != null) hintText.text = "";
        if (weakPointText != null) weakPointText.text = "";
    }

    private void ClearResults()
    {
        if (timerText != null) timerText.text = "";
        if (transcriptText != null) transcriptText.text = "";
        if (resultText != null) resultText.text = "";
        _ticket = null;
        SetUpReadAloud(null);
    }

    private void CancelListening()
    {
        _listening = false;
        _sessionId++;
        if (nativeStt != null && nativeStt.IsListening) nativeStt.Cancel();
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
        if (mainButtonLabel != null)
        {
            switch (_state)
            {
                case State.Idle: mainButtonLabel.text = "New Topic"; break;
                case State.Generating: mainButtonLabel.text = "Making a topic..."; break;
                case State.Thinking: mainButtonLabel.text = "Start now"; break;
                case State.Speaking: mainButtonLabel.text = "Finish"; break;
                case State.Grading: mainButtonLabel.text = "Grading..."; break;
            }
        }
        mainButton.interactable = !_busy && !_reading && !_recordingPron &&
            (_state == State.Idle || _state == State.Thinking || (_state == State.Speaking && !_finishing));

        if (skipThinkButton != null)
        {
            // 考える時間はメインのボタンが「Start now」になるので、同じ働きのこのボタンは出さない (2 つ並ぶと紛らわしい)
            skipThinkButton.gameObject.SetActive(false);
            skipThinkButton.interactable = !_busy;
        }
        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(_state == State.Idle && (!string.IsNullOrEmpty(_retryText) || _retryWav != null));
            retryButton.interactable = !_busy;
        }
        RefreshReadAloud();
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

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static void AppendSection(StringBuilder sb, string heading, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.Append("<b>").Append(heading).Append("</b>\n").Append(body.Trim()).Append("\n\n");
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = "Status: " + message;
    }

    private void SetLoading(bool on)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(on);
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf('\n');
        return i >= 0 ? s.Substring(0, i) : s;
    }
}
