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
/// リピーティング練習の画面。1 回 = 5 文。
/// Start → (聞く) → Again / Slow で聞き直し → Speak で言い返す → Stop → 英文と色分け・一致率 → Next … → まとめ。
/// 英文は言い返すまで出さない。言えた単語は緑、抜けた単語は赤。一致率 80% 未満の文は次回に優先して出る (サーバー)。
/// </summary>
public partial class RepeatPracticeManager : MonoBehaviour
{
    private enum State { Idle, Preparing, Listening, Ready, Speaking, Result, Summary }

    public const int SentencesPerSession = 5;
    public const int MissedThreshold = 80;
    private const string MatchColor = "#40c057";
    private const string MissColor = "#fa5252";
    private const int StopTimeoutMs = 2000;

    [Header("API Keys (空なら ApiSecrets.json の値)")]
    [SerializeField] private string geminiApiKey;
    [SerializeField] private string geminiModel = "gemini-3.5-flash-lite";
    [SerializeField] private string geminiFallbackModel = "gemini-3.8-flash";

    [Header("Voice")]
    [Tooltip("自分の添削文を読む Gemini の音声合成モデル")]
    [SerializeField] private string ttsModel = "gemini-3.8-flash-tts";
    [Tooltip("Gemini の声 (Leda = 女性の若い声)")]
    [SerializeField] private string ttsVoice = "Leda";
    [SerializeField] private string speechLanguage = "en-US";

    [Header("Discord (リピーティング用のチャンネル。空なら ApiSecrets.json の値)")]
    [SerializeField] private bool sendToDiscord = true;
    [SerializeField] private string discordWebhookUrl;
    [SerializeField] private string discordChannelId;

    [Header("Cloud (空欄ならシーンから探す)")]
    [SerializeField] private SupabaseService supabase;

    [Header("UI")]
    [SerializeField] private Button mainButton;           // Start / Next / Finish
    [SerializeField] private TMP_Text mainButtonLabel;
    [SerializeField] private Button againButton;
    [SerializeField] private Button slowButton;
    [SerializeField] private Button speakButton;          // Speak / Stop / Try again
    [SerializeField] private TMP_Text speakButtonLabel;
    [SerializeField] private TMP_Text headerText;         // "Listen (1 / 5)"
    [SerializeField] private TMP_Text sentenceText;       // 英文 (言い返した後だけ出す)
    [SerializeField] private TMP_Text resultText;         // 色分け・一致率 / まとめ
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text creditText;         // Tatoeba の表示
    [SerializeField] private GameObject loadingIndicator;

    [Header("Components (空欄なら自動で追加)")]
    [SerializeField] private RepeatAudioPlayer player;
    [SerializeField] private AudioRecorder recorder;
    [SerializeField] private NativeSpeechRecognizer nativeStt;

    private State _state = State.Idle;
    private GeminiApiClient _gemini;
    private DiscordClient _discord;
    private DiaryManager _diary;
    private readonly System.Random _rng = new System.Random();
    private static List<RepeatSentence> _tatoeba;
    private string _note = "";   // 画面の下に出す案内 (Tatoeba の文が無いときなど)

    private List<RepeatSentence> _set = new List<RepeatSentence>();
    private int _index;
    private readonly Dictionary<int, RepeatResult> _results = new Dictionary<int, RepeatResult>();
    private int _replays;
    private CancellationTokenSource _cts;

    // 聞き取り
    private bool _useNative;
    private bool _listening;
    private string _heard = "";
    private string _partial = "";
    private bool _stopping;

    // シャドーイング (Shadowing スイッチがオンのとき)
    private ShadowUi _shadowUi;
    private ShadowSession _shadow;
    private bool _shadowShown;
    private bool ShadowMode => _shadowUi != null && _shadowUi.ShadowOn;

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------
    private void Awake()
    {
        if (player == null) player = GetComponent<RepeatAudioPlayer>();
        if (player == null) player = gameObject.AddComponent<RepeatAudioPlayer>();
        if (recorder == null) recorder = GetComponent<AudioRecorder>();
        if (recorder == null) recorder = gameObject.AddComponent<AudioRecorder>();
        if (nativeStt == null) nativeStt = GetComponent<NativeSpeechRecognizer>();
        if (nativeStt == null) nativeStt = gameObject.AddComponent<NativeSpeechRecognizer>();
        if (supabase == null) supabase = FindObjectOfType<SupabaseService>(true);

        var secrets = ApiSecrets.Data;
        geminiApiKey = ApiSecrets.Pick(secrets.geminiApiKey, geminiApiKey);
        // 読み上げ・書き起こしはリピーティング用のキー (無ければ日記のキー)。上限ならほかのキーを借りる
        string ttsKey = ApiSecrets.Pick(secrets.geminiTtsApiKey, geminiApiKey);
        _gemini = new GeminiApiClient(ttsKey, geminiModel, geminiFallbackModel,
            backupApiKeys: new[] { geminiApiKey, secrets.geminiSummaryApiKey, secrets.geminiTopicApiKey, secrets.geminiShadowApiKey });
        player.Configure(_gemini, ttsModel, ttsVoice);

        discordWebhookUrl = ApiSecrets.Pick(secrets.repeatDiscordWebhookUrl, discordWebhookUrl);
        discordChannelId = ApiSecrets.Pick(secrets.repeatDiscordChannelId, discordChannelId);
        string botToken = string.IsNullOrWhiteSpace(discordChannelId) ? "" : secrets.discordBotToken;
        _discord = new DiscordClient(discordWebhookUrl, botToken, discordChannelId);
    }

    private void Start()
    {
        mainButton.onClick.AddListener(OnMainClicked);
        if (againButton != null) againButton.onClick.AddListener(() => { if (ShadowMode) _shadow.OnAgain(); else _ = ReplayAsync(false); });
        if (slowButton != null) slowButton.onClick.AddListener(() => { if (!ShadowMode) _ = ReplayAsync(true); });
        if (speakButton != null) speakButton.onClick.AddListener(OnSpeakClicked);
        nativeStt.OnPartialResult += OnNativePartial;
        nativeStt.OnSessionEnded += OnNativeSessionEnded;
        recorder.OnMaxDurationReached += OnMaxDuration;

        ShowSentence(null);
        SetText(resultText, "");
        SetText(headerText, "Repeat");
        SetText(creditText, "");
        SetLoading(false);
        SetState(State.Idle);
        SetStatus("Tap \"Start\". Listen to a sentence, then say it back.");

        ShadowScriptStore.DeleteOldBbcData();   // 前の版 (BBC) の音声などを消す
        _shadowUi = new ShadowUi(mainButton, statusText);
        var video = GetComponent<YouTubePlayerView>();
        if (video == null) video = gameObject.AddComponent<YouTubePlayerView>();
        // シャドーイングは専用のキー (空ならリピーティングのキー)。上限ならほかのキーを借りる
        var secrets = ApiSecrets.Data;
        string ttsKey = ApiSecrets.Pick(secrets.geminiTtsApiKey, geminiApiKey);
        var shadowGemini = new GeminiApiClient(ApiSecrets.Pick(secrets.geminiShadowApiKey, ttsKey), geminiModel, geminiFallbackModel,
            backupApiKeys: new[] { ttsKey, geminiApiKey, secrets.geminiSummaryApiKey, secrets.geminiTopicApiKey });
        var youtube = new YouTubeSearchClient(ApiSecrets.Pick(secrets.youtubeShadowApiKey, secrets.youtubeApiKey));
        _shadow = new ShadowSession(new ShadowSession.View
            {
                MainButton = mainButton, MainLabel = mainButtonLabel, SpeakButton = speakButton, SpeakLabel = speakButtonLabel,
                AgainButton = againButton, SlowButton = slowButton, Header = headerText, Sentence = sentenceText,
                Result = resultText, Status = statusText, Credit = creditText, Loading = loadingIndicator, Ui = _shadowUi,
            },
            player, video, youtube, recorder, shadowGemini, SaveAsync, SendShadowToDiscordAsync,
            () => { var d = FindDiary(); if (d != null) d.CountOtherPracticeAsStudyDay(); });
        _shadowUi.Changed += ApplyMode;
        _shadow.StateChanged += RefreshModeButtons;
        ApplyMode();
    }

    // Shadowing のオン・オフ (切り替えられるのは練習していないときだけ)
    private void ApplyMode()
    {
        if (ShadowMode == _shadowShown && _shadowShown) return;
        if (ShadowMode)
        {
            _shadowShown = true;
            ShowSentence(null);
            _shadow.ShowIdle();
        }
        else
        {
            bool wasShadow = _shadowShown;
            _shadowShown = false;
            if (!wasShadow && _state != State.Idle) return;   // 画面を開いたとき (リピーティングのまま)
            if (sentenceText != null) sentenceText.gameObject.SetActive(true);
            SetButtonLabel(againButton, "Again");
            SetButtonLabel(slowButton, "Slow");
            ShowSentence(null);
            SetText(resultText, "");
            SetText(headerText, "Repeat");
            SetText(creditText, "");
            SetLoading(false);
            SetState(State.Idle);
            SetStatus("Tap \"Start\". Listen to a sentence, then say it back.");
        }
        RefreshModeButtons();
    }

    private void RefreshModeButtons()
    {
        if (_shadowUi == null) return;
        _shadowUi.SetInteractable(ShadowMode ? _shadow.CanSwitchMode : _state == State.Idle || _state == State.Summary);
    }

    private static void SetButtonLabel(Button b, string text)
    {
        var t = b != null ? b.GetComponentInChildren<TMP_Text>(true) : null;
        if (t != null) t.text = text;
    }

    private void OnDestroy()
    {
        if (nativeStt != null)
        {
            nativeStt.OnPartialResult -= OnNativePartial;
            nativeStt.OnSessionEnded -= OnNativeSessionEnded;
        }
        if (recorder != null) recorder.OnMaxDurationReached -= OnMaxDuration;
        _cts?.Cancel();
    }

    // ほかのタブへ移ったら止める (まとめまで行った結果の保存は続ける)
    private void OnDisable()
    {
        if (ShadowMode) { _shadow.Cancel(); return; }
        if (_state == State.Idle || _state == State.Summary) return;
        _cts?.Cancel();
        player?.Stop();
        CancelListening();
        SetLoading(false);
        SetState(State.Idle);
        SetStatus("Cancelled. Tap \"Start\" to try again.");
    }

    // 電話・ほかのアプリへの切り替えでシャドーイングが止まったら、録音を捨てて最初から (保存しない)
    private void OnApplicationPause(bool paused)
    {
        // 動画を流している間だけ (書き起こしの待ち時間・マイクの許可の画面では止めない)
        if (paused && ShadowMode && _shadow.IsPlayingVideo) _shadow.Cancel("Interrupted. Tap \"Start\" to try again.");
    }

    // ------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------
    private async void OnMainClicked()
    {
        if (ShadowMode) { _shadow.OnMain(); return; }
        switch (_state)
        {
            case State.Idle:
            case State.Summary:
                await StartSessionAsync();
                break;
            case State.Result:
                if (_index + 1 < _set.Count) { _index++; await ListenAsync(); }
                else await FinishSessionAsync();
                break;
        }
    }

    private async void OnSpeakClicked()
    {
        if (ShadowMode) { _shadow.OnSpeak(); return; }
        if (_state == State.Ready || _state == State.Result) await StartSpeakingAsync();
        else if (_state == State.Speaking) await StopSpeakingAsync();
    }

    // ------------------------------------------------------------
    // 1. 文を集める
    // ------------------------------------------------------------
    private async Task StartSessionAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _results.Clear();
        _index = 0;
        ShowSentence(null);
        SetText(resultText, "");
        SetState(State.Preparing);
        SetLoading(true);
        SetStatus("Preparing sentences...");

        var missed = new List<RepeatSentence>();
        var own = new List<RepeatSentence>();
        if (supabase != null && supabase.IsConfigured)
        {
            foreach (var s in await supabase.GetRepeatSentencesAsync(RepeatSetBuilder.MaxMissed, RepeatSetBuilder.MaxOwn))
                (s.Missed ? missed : own).Add(s);
            if (this == null) return;
        }

        // 言えなかった Tatoeba の文には、作者とライセンスを一覧から補う
        var catalog = TatoebaSentences();
        foreach (var m in missed.Where(m => m.HasHumanAudio))
        {
            var t = catalog.FirstOrDefault(c => c.AudioId == m.AudioId);
            if (t != null) { m.Author = t.Author; m.License = t.License; }
        }

        _set = RepeatSetBuilder.Build(missed, own, catalog, RecentRepeatLog.Keys(), _rng, SentencesPerSession);
        if (_set.Count == 0)
        {
            SetLoading(false);
            SetState(State.Idle);
            SetStatus("No sentences yet. Add Tatoeba sentences (see README) or practice Diary / Topic first.");
            return;
        }
        _note = "";
        if (catalog.Count == 0)
        {
            Debug.LogWarning("[Repeat] Resources/RepeatSentences.json is missing or empty. See README (Tatoeba).");
            _note = "Only " + _set.Count + " sentence" + (_set.Count == 1 ? "" : "s") +
                    ": add Tatoeba sentences (see README) for 5 per session.";
        }

        await player.PrepareAsync(_set[0]);
        if (this == null || _state != State.Preparing) return;
        SetLoading(false);
        await ListenAsync();
    }

    private static List<RepeatSentence> TatoebaSentences()
    {
        if (_tatoeba != null) return _tatoeba;
        var asset = Resources.Load<TextAsset>("RepeatSentences");
        _tatoeba = asset != null ? TatoebaCatalog.Parse(asset.text) : new List<RepeatSentence>();
        return _tatoeba;
    }

    // ------------------------------------------------------------
    // 2. 聞く
    // ------------------------------------------------------------
    private RepeatSentence Current => _index < _set.Count ? _set[_index] : null;

    private async Task ListenAsync()
    {
        _state = State.Ready;   // 新しい文: 聞き終わったら Ready (前の文の Result に戻らないように)
        _replays = 0;
        ShowSentence(null);
        SetText(resultText, "");
        await PlayCurrentAsync(false);
        // 次の文の音声を先に用意しておく
        if (_index + 1 < _set.Count) _ = player.PrepareAsync(_set[_index + 1]);
    }

    private async Task ReplayAsync(bool slow)
    {
        if (_state != State.Ready && _state != State.Result) return;
        _replays = Math.Min(99, _replays + 1);
        await PlayCurrentAsync(slow);
    }

    private async Task PlayCurrentAsync(bool slow)
    {
        var s = Current;
        if (s == null) return;
        var back = _state == State.Result ? State.Result : State.Ready;
        SetState(State.Listening);
        SetText(headerText, "Listen (" + (_index + 1) + " / " + _set.Count + ")" + (slow ? "  - slow" : ""));
        SetStatus(slow ? "Listening slowly..." : "Listen carefully...");
        ShowCredit(s);
        try
        {
            await player.PlayAsync(s, slow, _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            // 流せなかった: 聞いていない文を言い返させない。練習をやめて Idle に戻る
            Debug.LogException(e);
            if (this == null) return;
            _cts?.Cancel();
            player.Stop();
            SetLoading(false);
            ShowSentence(null);
            SetText(headerText, "Repeat");
            SetState(State.Idle);
            SetStatus("Could not play the sentence (" + FirstLine(e.Message) + "). Tap \"Start\" to try again.");
            return;
        }
        if (this == null || _state != State.Listening) return;
        SetState(back);
        string voiceNote = player.VoiceOf(s) == RepeatAudioPlayer.VoiceDevice ? "  <size=70%>(device voice)</size>" : "";
        SetText(headerText, "Say it back (" + (_index + 1) + " / " + _set.Count + ")" + voiceNote);
        if (back == State.Ready) SetStatus("Tap \"Speak\" and say the sentence. \"Again\" / \"Slow\" to listen again.");
    }

    // ------------------------------------------------------------
    // 3. 言い返す
    // ------------------------------------------------------------
    private async Task StartSpeakingAsync()
    {
        if (!await recorder.RequestPermissionAsync())
        {
            SetStatus("Microphone permission denied. Please allow it in Settings.");
            return;
        }
        _useNative = nativeStt.IsAvailable;
        if (_useNative && !await nativeStt.RequestSpeechPermissionAsync())
        {
            SetStatus("Speech recognition permission denied. Please allow it in Settings.");
            return;
        }
        if (this == null) return;

        _heard = "";
        _partial = "";
        _stopping = false;
        bool started = _useNative ? nativeStt.StartListening(speechLanguage) : recorder.StartRecording();
        if (!started)
        {
            SetStatus("Could not start listening.");
            return;
        }
        _listening = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        SetState(State.Speaking);
        SetStatus("Say the sentence, then tap \"Stop\".");
    }

    private void OnNativePartial(string text)
    {
        if (_listening) _partial = text ?? "";
    }

    private async void OnNativeSessionEnded(string text, string error)
    {
        if (_state != State.Speaking) return;
        if (!string.IsNullOrWhiteSpace(text)) _heard = text.Trim();
        // 1 文なので、聞き取りが切れたらそこで終わり
        if (_listening && !_stopping) await StopSpeakingAsync();
    }

    private async void OnMaxDuration()
    {
        if (_state == State.Speaking && !_useNative) await StopSpeakingAsync();
    }

    private async Task StopSpeakingAsync()
    {
        if (_state != State.Speaking || _stopping) return;
        _stopping = true;
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
            _stopping = false;
            SetLoading(false);
        }
        if (this == null || _state != State.Speaking) return;

        if (string.IsNullOrWhiteSpace(heard))
        {
            // 0% にしない。もう一度言ってもらう
            SetState(_results.ContainsKey(_index) ? State.Result : State.Ready);
            SetStatus("I couldn't hear you. Tap \"Speak\" and try again.");
            return;
        }
        ShowResult(heard);
    }

    private async Task<string> StopListeningAndGetTextAsync()
    {
        _listening = false;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        if (_useNative)
        {
            if (nativeStt.IsListening)
            {
                nativeStt.StopListening();
                float deadline = Time.realtimeSinceStartup + StopTimeoutMs / 1000f;
                while (nativeStt.IsListening && Time.realtimeSinceStartup < deadline) await Task.Yield();
                if (nativeStt.IsListening) nativeStt.Cancel();
            }
            return (string.IsNullOrWhiteSpace(_heard) ? _partial : _heard).Trim();
        }

        byte[] wav = recorder.IsRecording ? recorder.StopRecording() : null;
        if (wav == null) return "";
        SetLoading(true);
        SetStatus("Transcribing...");
        return ((await _gemini.TranscribeAsync(wav)) ?? "").Trim();
    }

    private void CancelListening()
    {
        _listening = false;
        _stopping = false;
        if (nativeStt != null && nativeStt.IsListening) nativeStt.Cancel();
        if (recorder != null && recorder.IsRecording) recorder.StopRecording();
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

}
