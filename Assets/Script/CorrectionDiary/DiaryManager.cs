using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 日記モード: 1回目の録音ボタンで文字起こしを開始し、2回目に押されるまで続ける。
/// 2回目に押された時点で、溜まった全文を添削する。
///
/// 端末内蔵の音声認識は「無音」や「時間切れ」で自動終了するため、
/// 終了するたびに自動で再開し、確定した文章を繋げていく。
/// (Editor など内蔵認識が使えない環境では、録音 → 停止後に Gemini で文字起こしするフォールバック)
/// </summary>
public class DiaryManager : MonoBehaviour
{
    private enum Engine
    {
        Gemini = 0,      // 添削 + 解説
        DeepLWrite = 1   // 言い換えのみ
    }

    [Header("API Keys (※ 本番ではアプリに埋め込まず、バックエンド経由にすること)")]
    [SerializeField] private string geminiApiKey;
    [SerializeField] private string geminiModel = "gemini-3.8-flash";
    [SerializeField] private string geminiFallbackModel = "gemini-3.7-flash";
    [SerializeField] private string deeplApiKey;
    [SerializeField] private bool deeplUseFreeApi = true;
    [SerializeField] private string deeplTargetLang = "en-US";

    [Header("Discord (※ Webhook URL / Botトークンは第三者に見せないこと)")]
    [Tooltip("ON: 添削が終わったら Discord に送信する")]
    [SerializeField] private bool sendToDiscord = true;
    [Tooltip("Webhook URL (推奨)。設定されていれば、こちらを使う")]
    [SerializeField] private string discordWebhookUrl;
    [Tooltip("Webhook URL が空のときだけ使う: Bot トークン")]
    [SerializeField] private string discordBotToken;
    [Tooltip("Bot で送る場合の送信先チャンネルID")]
    [SerializeField] private string discordChannelId;
    [Tooltip("ON: 添削結果に加えて、文字起こしの原文も送る")]
    [SerializeField] private bool includeOriginalInDiscord = true;
    [Tooltip("ON: 添削文のうち、自分が話した内容と違う単語に下線を付ける")]
    [SerializeField] private bool underlineChangesInDiscord = true;

    [Header("Reminder (夜までに日記が無ければ Discord に通知する Worker。任意)")]
    [Tooltip("ON: 日記の添削に成功したら、Worker に「今日は書いた」と連絡する")]
    [SerializeField] private bool sendReminderPing = true;
    [Tooltip("デプロイした Worker の URL (例: https://diary-reminder.xxxx.workers.dev)")]
    [SerializeField] private string reminderWorkerUrl;
    [Tooltip("Worker に登録した APP_SECRET と同じ値")]
    [SerializeField] private string reminderSecret;

    [Header("Speech")]
    [SerializeField] private string speechLanguage = "en-US";

    [Header("UI")]
    [Tooltip("任意: 添削エンジンの選択。未設定なら Gemini")]
    [SerializeField] private TMP_Dropdown engineDropdown;
    [SerializeField] private Button recordButton;
    [SerializeField] private TMP_Text recordButtonLabel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text transcriptText;
    [SerializeField] private TMP_Text correctedText;
    [SerializeField] private TMP_Text explanationText;
    [SerializeField] private GameObject loadingIndicator;
    [Tooltip("任意: 添削に失敗したとき、文字起こし済みの文章で再試行するボタン (普段は非表示)")]
    [SerializeField] private Button retryButton;
    [Header("Cloud (任意。ログイン中だけ、添削した日記を直近7日分クラウドに保存する)")]
    [SerializeField] private SupabaseService supabase;

    [Tooltip("任意: 日記を書いた日の記録(連続日数)。添削に成功すると、今日を達成として記録する")]
    [SerializeField] private DiaryStreakView streakView;

    [Header("Components (空欄なら自動で取得/追加)")]
    [SerializeField] private AudioRecorder recorder;
    [SerializeField] private NativeSpeechRecognizer nativeStt;

    private const int MaxConsecutiveErrors = 3;   // 連続エラーでこの回数に達したら諦める
    private const int RestartDelayMs = 300;       // 自動再開までの待ち時間
    private const int StopTimeoutMs = 6000;       // 停止後、最終結果が来ない場合の保険

    private GeminiApiClient _gemini;
    private DeepLApiClient _deepl;
    private DiscordClient _discord;
    private ReminderClient _reminder;

    private readonly List<string> _segments = new List<string>();   // 確定済みの文章
    private string _currentPartial = "";                            // 認識途中の文章

    private bool _busy;          // 権限確認中・添削中など、ボタン操作を受け付けない間
    private bool _sessionLive;   // 日記セッションが開始されてから、添削に入るまで
    private bool _active;        // ユーザーが「まだ話している」状態 (停止ボタンが押されるまで true)
    private bool _finalizing;
    private bool _useNative;
    private Engine _engine;
    private int _sessionId;
    private int _consecutiveErrors;
    private string _pendingText;  // 添削待ちの文章 (失敗時の再試行用)

    // 直近の添削結果 (Discord 送信に失敗したとき、再送するために保持する)
    private string _lastOriginal;
    private string _lastCorrected;
    private string _lastExplanation;
    private bool _discordPending;

    private Engine CurrentEngine =>
        engineDropdown != null ? (Engine)engineDropdown.value : Engine.DeepLWrite;

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------
    private void Awake()
    {
        // 複数のパネルで同じ AudioRecorder / NativeSpeechRecognizer を共有する。
        // Inspector で未設定ならシーン内から探し、無ければ自分の GameObject に追加する。
        if (recorder == null) recorder = FindObjectOfType<AudioRecorder>();
        if (recorder == null) recorder = gameObject.AddComponent<AudioRecorder>();

        if (nativeStt == null) nativeStt = FindObjectOfType<NativeSpeechRecognizer>();
        if (nativeStt == null) nativeStt = gameObject.AddComponent<NativeSpeechRecognizer>();

        // API キーは Assets/Resources/ApiSecrets.json から読み込む (GitHub には上げない)
        var secrets = ApiSecrets.Data;
        geminiApiKey = ApiSecrets.Pick(secrets.geminiApiKey, geminiApiKey);
        deeplApiKey = ApiSecrets.Pick(secrets.deeplApiKey, deeplApiKey);
        discordWebhookUrl = ApiSecrets.Pick(secrets.discordWebhookUrl, discordWebhookUrl);
        discordBotToken = ApiSecrets.Pick(secrets.discordBotToken, discordBotToken);
        discordChannelId = ApiSecrets.Pick(secrets.discordChannelId, discordChannelId);
        reminderWorkerUrl = ApiSecrets.Pick(secrets.reminderWorkerUrl, reminderWorkerUrl);
        reminderSecret = ApiSecrets.Pick(secrets.reminderSecret, reminderSecret);
        
        _gemini = new GeminiApiClient(geminiApiKey, geminiModel, geminiFallbackModel);
        _deepl = new DeepLApiClient(deeplApiKey, deeplUseFreeApi, deeplTargetLang);
        _discord = new DiscordClient(discordWebhookUrl, discordBotToken, discordChannelId);
        _reminder = new ReminderClient(reminderWorkerUrl, reminderSecret);
    }

    private void Start()
    {
        if (engineDropdown != null)
        {
            engineDropdown.ClearOptions();
            engineDropdown.AddOptions(new List<string> { "Gemini (with explanations)", "DeepL Write" });
            engineDropdown.value = (int)Engine.DeepLWrite;   // 初期選択
        }

        recordButton.onClick.AddListener(OnRecordButtonClicked);
        if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);

        recorder.OnMaxDurationReached += OnMaxDurationReached;
        nativeStt.OnPartialResult += OnNativePartial;
        nativeStt.OnSessionEnded += OnNativeSessionEnded;

        ClearResults();
        SetRecordingUi(false);
        SetLoading(false);
        UpdateRetryButton();
        SetStatus("Idle");
    }

    // 話している最中にパネルが非表示にされたら、その日記は破棄して中断する
    // (停止後の添削中は、そのまま完了させる)
    private void OnDisable()
    {
        if (!_sessionLive || !_active || _finalizing) return;

        _active = false;
        _sessionLive = false;
        _sessionId++;   // 自動再開やタイムアウト待ちを無効化
        _currentPartial = "";
        _segments.Clear();

        if (nativeStt != null) nativeStt.Cancel();
        if (recorder != null && recorder.IsRecording) recorder.StopRecording();
        Screen.sleepTimeout = SleepTimeout.SystemSetting;

        SetRecordingUi(false);
        SetLoading(false);
        SetStatus("Idle");
        ClearResults();
    }

    private void OnDestroy()
    {
        if (recordButton != null) recordButton.onClick.RemoveListener(OnRecordButtonClicked);
        if (retryButton != null) retryButton.onClick.RemoveListener(OnRetryClicked);

        if (recorder != null)
        {
            recorder.OnMaxDurationReached -= OnMaxDurationReached;
            if (recorder.IsRecording) recorder.StopRecording();
        }

        if (nativeStt != null)
        {
            nativeStt.OnPartialResult -= OnNativePartial;
            nativeStt.OnSessionEnded -= OnNativeSessionEnded;
            nativeStt.Cancel();
        }

        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    // ------------------------------------------------------------
    // Button
    // ------------------------------------------------------------
    private async void OnRecordButtonClicked()
    {
        if (_busy) return;

        if (!_active)
            await BeginAsync();
        else
            await StopAsync();
    }

    // ------------------------------------------------------------
    // Begin (1回目のタップ)
    // ------------------------------------------------------------
    private async Task BeginAsync()
    {
        _busy = true;
        recordButton.interactable = false;
        try
        {
            _engine = CurrentEngine;
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

            // 新しい日記セッションを初期化
            _segments.Clear();
            _currentPartial = "";
            _pendingText = null;
            _discordPending = false;
            _consecutiveErrors = 0;
            _finalizing = false;
            _sessionId++;
            ClearResults();
            UpdateRetryButton();

            bool started = _useNative
                ? nativeStt.StartListening(speechLanguage)
                : recorder.StartRecording();

            if (!started)
            {
                SetStatus("Could not start listening.");
                return;
            }

            _sessionLive = true;
            _active = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;   // 話している間は画面を消さない

            SetRecordingUi(true);
            SetStatus(_useNative
                ? "Listening... Tap again to finish and correct."
                : "Recording... Tap again to finish. (No live text in this mode)");
        }
        finally
        {
            _busy = false;
            recordButton.interactable = true;
        }
    }

    // ------------------------------------------------------------
    // Stop (2回目のタップ)
    // ------------------------------------------------------------
    private async Task StopAsync()
    {
        _active = false;   // 以降は自動再開しない
        _busy = true;
        recordButton.interactable = false;
        SetRecordingUi(false);
        SetLoading(true);

        if (!_useNative)
        {
            await FinalizeAsync();
            return;
        }

        SetStatus("Finishing...");
        int id = _sessionId;

        if (nativeStt.IsListening)
        {
            // 最終結果は OnNativeSessionEnded に届く
            nativeStt.StopListening();

            // 保険: 一定時間待っても結果が来なければ、認識途中のテキストで先に進む
            await Task.Delay(StopTimeoutMs);
            if (this == null || id != _sessionId || _finalizing) return;

            nativeStt.Cancel();
            CommitPartial();
            await FinalizeAsync();
        }
        else
        {
            // 自動再開の待ち時間中に押された場合など、認識セッションが開いていない
            CommitPartial();
            await FinalizeAsync();
        }
    }

    private void OnMaxDurationReached()
    {
        // フォールバック(録音)モードで最大録音時間に達した
        if (!_useNative && _active && !_busy)
            OnRecordButtonClicked();
    }

    // ------------------------------------------------------------
    // Native speech events
    // ------------------------------------------------------------
    private void OnNativePartial(string text)
    {
        if (!_sessionLive) return;
        _currentPartial = text;
        transcriptText.text = Compose(text);
    }

    private async void OnNativeSessionEnded(string text, string error)
    {
        if (!_sessionLive || _finalizing) return;

        _currentPartial = "";

        if (!string.IsNullOrWhiteSpace(text))
        {
            _segments.Add(text.Trim());
            _consecutiveErrors = 0;
            transcriptText.text = Compose(null);
        }
        else if (!string.IsNullOrEmpty(error))
        {
            _consecutiveErrors++;
        }
        // (エラーも無く空の場合は「無音だった」だけなので、失敗には数えない)

        if (!_active)
        {
            // ユーザーが停止済み → 添削へ
            await FinalizeAsync(string.IsNullOrEmpty(error) ? null : "Error: " + error);
            return;
        }

        // まだ話している最中に認識が自動終了した (無音 / 時間切れ) → 続けて聞き取る
        if (_consecutiveErrors >= MaxConsecutiveErrors)
        {
            await AbortAsync(string.IsNullOrEmpty(error) ? "Speech recognition keeps failing." : error);
            return;
        }

        await RestartAsync(_sessionId);
    }

    private async Task RestartAsync(int id)
    {
        await Task.Delay(RestartDelayMs);
        if (this == null || id != _sessionId || !_active || !_sessionLive) return;

        if (!nativeStt.StartListening(speechLanguage))
            await AbortAsync("Could not restart speech recognition.");
    }

    // 続行できないエラー。認識済みの文章があれば、それを添削する
    private async Task AbortAsync(string reason)
    {
        _active = false;
        _busy = true;
        recordButton.interactable = false;
        SetRecordingUi(false);
        SetLoading(true);
        await FinalizeAsync("Error: " + reason);
    }

    // ------------------------------------------------------------
    // Finalize → Correct
    // ------------------------------------------------------------
    private async Task FinalizeAsync(string emptyMessage = null)
    {
        if (_finalizing) return;
        _finalizing = true;
        _sessionLive = false;
        _active = false;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;

        _busy = true;
        recordButton.interactable = false;
        SetRecordingUi(false);
        SetLoading(true);

        try
        {
            string text = _useNative
                ? Compose(null).Trim()
                : await TranscribeRecordedWavAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus(emptyMessage ?? "No speech was recognized. Please try again.");
                return;
            }

            transcriptText.text = text;
            _pendingText = text;
            await CorrectAsync(text);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Error: " + FirstLine(e.Message) +
                      (string.IsNullOrEmpty(_pendingText) ? "" : "  (You can retry the correction)"));
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            recordButton.interactable = true;
            UpdateRetryButton();
        }
    }

    // フォールバック: 録音した音声全体を Gemini で文字起こし
    private async Task<string> TranscribeRecordedWavAsync()
    {
        SetStatus("Transcribing...");
        byte[] wav = recorder.StopRecording();
        if (wav == null) return "";
        string text = await _gemini.TranscribeAsync(wav);
        return (text ?? "").Trim();
    }

    private async Task CorrectAsync(string text)
    {
        string corrected;
        string explanation = "";

        if (_engine == Engine.Gemini)
        {
            SetStatus("Correcting with Gemini...");
            var result = await _gemini.CorrectDiaryAsync(text);
            corrected = result.corrected;
            explanation = result.explanation;
            correctedText.text = corrected;
            if (explanationText != null) explanationText.text = explanation;
        }
        else
        {
            SetStatus("Polishing with DeepL Write...");
            corrected = await _deepl.RephraseAsync(text);
            correctedText.text = corrected;
        }

        _pendingText = null;   // 添削は成功したので、添削の再試行は不要
        _lastOriginal = text;
        _lastCorrected = corrected;
        _lastExplanation = explanation;
        if (streakView != null) _ = streakView.MarkTodayDoneAsync();   // 今日を「日記を書いた日」として記録 (ログイン中はクラウドにも保存)
        NotifyReminderServerAsync();                          // 「今日は書いた」と Worker に連絡 (待たない)
        SaveDiaryToCloudAsync(text, corrected, explanation);  // ログイン中なら、日記をクラウドに保存 (待たない)
        SetStatus("Done");

        await SendToDiscordAsync();
    }

    // ------------------------------------------------------------
    // Cloud (添削した日記を、直近7日分だけ保存する。古いものはサーバーが自動で消す)
    // ------------------------------------------------------------
    // 失敗しても、日記の処理には影響しない (画面の処理を待たせない)
    private async void SaveDiaryToCloudAsync(string original, string corrected, string explanation)
    {
        if (supabase == null || !supabase.IsSignedIn) return;

        try
        {
            string engineName = _engine == Engine.Gemini ? "Gemini" : "DeepL Write";
            await supabase.SaveDiaryAsync(original, corrected, explanation, engineName);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DiaryManager] could not save the diary to the cloud: " + e.Message);
        }
    }

    // ------------------------------------------------------------
    // Reminder (Worker に「今日は書いた」と連絡する)
    // ------------------------------------------------------------
    // 画面の処理を待たせないよう、結果は待たない。失敗しても日記の処理には影響しない
    private async void NotifyReminderServerAsync()
    {
        if (!sendReminderPing || _reminder == null || !_reminder.IsConfigured) return;

        try
        {
            DateTime date = streakView != null ? streakView.TodayDate : DateTime.Now.Date;
            int streak = streakView != null ? streakView.GetCurrentStreak() : 0;
            await _reminder.NotifyDiaryDoneAsync(date, streak);
        }
        catch (Exception e)
        {
            // 連絡できなかった場合、夜に「まだ書いていない」という通知が来てしまうことがある
            Debug.LogWarning("[Reminder] Could not notify the reminder server: " + e.Message);
        }
    }

    // ------------------------------------------------------------
    // Discord
    // ------------------------------------------------------------
    // 送信に失敗しても例外は投げない (添削結果は画面に残し、Retry で再送できる)
    private async Task SendToDiscordAsync()
    {
        if (!sendToDiscord || !_discord.IsConfigured) return;

        _discordPending = true;
        try
        {
            SetStatus("Sending to Discord...");
            await _discord.SendDiaryAsync(
                DateTime.Now,
                _lastCorrected,
                _lastExplanation,
                _lastOriginal,
                includeOriginalInDiscord,
                underlineChangesInDiscord);

            _discordPending = false;
            SetStatus("Done (sent to Discord)");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Corrected, but Discord failed: " + FirstLine(e.Message) + "  (You can retry)");
        }
    }

    // ------------------------------------------------------------
    // Retry (添削だけやり直す)
    // ------------------------------------------------------------
    private async void OnRetryClicked()
    {
        if (_busy || _active) return;
        if (string.IsNullOrEmpty(_pendingText) && !_discordPending) return;

        _busy = true;
        recordButton.interactable = false;
        SetLoading(true);
        _engine = CurrentEngine;   // 失敗したエンジンから切り替えて再試行できる

        try
        {
            if (!string.IsNullOrEmpty(_pendingText))
                await CorrectAsync(_pendingText);   // 成功すれば、そのまま Discord にも送られる
            else
                await SendToDiscordAsync();         // 添削は済んでいて、Discord だけ失敗していた場合
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Error: " + FirstLine(e.Message) + "  (You can retry the correction)");
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            recordButton.interactable = true;
            UpdateRetryButton();
        }
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
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

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = "Status: " + message;
    }

    private void SetLoading(bool on)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(on);
    }

    private void SetRecordingUi(bool recording)
    {
        if (recordButtonLabel != null)
            recordButtonLabel.text = recording ? "Finish & Correct" : "Start Diary";
    }

    private void UpdateRetryButton()
    {
        if (retryButton != null)
            retryButton.gameObject.SetActive(!string.IsNullOrEmpty(_pendingText) || _discordPending);
    }

    private void ClearResults()
    {
        if (transcriptText != null) transcriptText.text = "";
        if (correctedText != null) correctedText.text = "";
        if (explanationText != null) explanationText.text = "";
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf('\n');
        return i >= 0 ? s.Substring(0, i) : s;
    }
}