using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// UIイベントを受け取り、録音 → STT → 添削 → 表示 の全体フローを制御するメインスクリプト。
///
/// 処理の流れ:
/// [実機] どちらのエンジンでも、端末内蔵の音声認識で文字起こしする
///    Gemini モード    : 内蔵の音声認識 → Gemini(テキストを添削 + 解説)
///    DeepL Write モード: 内蔵の音声認識 → DeepL Write
/// [Editor / 内蔵認識が使えない端末] 録音(WAV) を使うフォールバック
///    Gemini モード    : 録音(WAV) → Gemini(文字起こし + 添削)
///    DeepL Write モード: 録音(WAV) → Gemini(文字起こしのみ) → DeepL Write
/// </summary>
public class AppManager : MonoBehaviour
{
    private enum Engine
    {
        Gemini = 0,
        DeepLWrite = 1
    }

    [Header("API Keys (※ 本番ではアプリに埋め込まず、バックエンド経由にすること)")]
    [SerializeField] private string geminiApiKey;
    [Tooltip("モデルが廃止された場合はここを書き換える")]
    [SerializeField] private string geminiModel = "gemini-3.8-flash";
    [Tooltip("メインモデルが混雑(503等)のときに切り替える予備モデル。空なら使わない")]
    [SerializeField] private string geminiFallbackModel = "gemini-3.7-flash";
    [SerializeField] private string deeplApiKey;
    [SerializeField] private bool deeplUseFreeApi = true;
    [SerializeField] private string deeplTargetLang = "en-US";

    [Header("Speech-to-Text")]
    [Tooltip("ON: 実機では、どちらのエンジンでも端末内蔵の音声認識を使う。OFF や Editor では録音して Gemini で処理する")]
    [FormerlySerializedAs("useNativeSttForDeepL")]
    [SerializeField] private bool useNativeStt = true;
    [Tooltip("音声認識の言語")]
    [SerializeField] private string speechLanguage = "en-US";

    [Header("UI")]
    [SerializeField] private TMP_Dropdown engineDropdown;
    [SerializeField] private Button recordButton;
    [SerializeField] private TMP_Text recordButtonLabel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text transcriptText;
    [SerializeField] private TMP_Text correctedText;
    [Tooltip("任意: Gemini使用時の解説表示用")]
    [SerializeField] private TMP_Text explanationText;
    [SerializeField] private GameObject loadingIndicator;

    [Header("Components (空欄なら自動で取得/追加)")]
    [SerializeField] private AudioRecorder recorder;
    [SerializeField] private NativeSpeechRecognizer nativeStt;

    private GeminiApiClient _gemini;
    private DeepLApiClient _deepl;

    private bool _busy;

    // 現在の録音セッションの情報 (開始時に確定する)
    private Engine _sessionEngine;
    private bool _sessionNative;

    private Engine CurrentEngine =>
        engineDropdown != null ? (Engine)engineDropdown.value : Engine.DeepLWrite;

    private bool IsSessionActive => _sessionNative || recorder.IsRecording;

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
        
        _gemini = new GeminiApiClient(geminiApiKey, geminiModel, geminiFallbackModel);
        _deepl = new DeepLApiClient(deeplApiKey, deeplUseFreeApi, deeplTargetLang);
    }

    private void Start()
    {
        if (engineDropdown != null)
        {
            engineDropdown.ClearOptions();
            engineDropdown.AddOptions(new List<string> { "Gemini (Direct Audio)", "DeepL Write" });
            engineDropdown.value = (int)Engine.DeepLWrite;   // 初期選択
        }

        recordButton.onClick.AddListener(OnRecordButtonClicked);
        recorder.OnMaxDurationReached += OnMaxDurationReached;
        nativeStt.OnPartialResult += OnNativePartial;
        nativeStt.OnSessionEnded += OnNativeSessionEnded;

        ClearResults();
        SetRecordingUi(false);
        SetLoading(false);
        SetStatus("Idle");
    }

    // パネルが非表示にされたとき、録音中・聞き取り中なら中断する
    private void OnDisable()
    {
        if (_busy) return;   // 送信・添削待ちの最中は、そのまま完了させる

        if (_sessionNative)
        {
            _sessionNative = false;
            if (nativeStt != null) nativeStt.Cancel();
        }
        else if (recorder != null && recorder.IsRecording)
        {
            recorder.StopRecording();
        }
        else
        {
            return;
        }

        SetRecordingUi(false);
        SetLoading(false);
        SetStatus("Idle");
        ClearResults();
    }

    private void OnDestroy()
    {
        if (recordButton != null) recordButton.onClick.RemoveListener(OnRecordButtonClicked);
        if (recorder != null) recorder.OnMaxDurationReached -= OnMaxDurationReached;
        if (nativeStt != null)
        {
            nativeStt.OnPartialResult -= OnNativePartial;
            nativeStt.OnSessionEnded -= OnNativeSessionEnded;
        }
    }

    // ------------------------------------------------------------
    // UI Events
    // ------------------------------------------------------------
    private async void OnRecordButtonClicked()
    {
        if (_busy) return;

        if (!IsSessionActive)
            await BeginRecordingAsync();
        else
            await StopAsync();
    }

    private async void OnMaxDurationReached()
    {
        if (!_sessionNative && recorder.IsRecording && !_busy)
            await StopAsync();
    }

    // ------------------------------------------------------------
    // Begin
    // ------------------------------------------------------------
    private async Task BeginRecordingAsync()
    {
        _busy = true;
        recordButton.interactable = false;
        try
        {
            // 録音方式をこのセッションの開始時に確定する
            Engine engine = CurrentEngine;
            bool native = useNativeStt && nativeStt.IsAvailable;

            // マイク権限 (Gemini/内蔵STTどちらでも必要)
            if (!await recorder.RequestPermissionAsync())
            {
                SetStatus("Microphone permission denied. Please allow it in Settings.");
                return;
            }

            if (native)
            {
                // iOS: 音声認識の許可ダイアログ
                if (!await nativeStt.RequestSpeechPermissionAsync())
                {
                    SetStatus("Speech recognition permission denied. Please allow it in Settings.");
                    return;
                }

                if (!nativeStt.StartListening(speechLanguage))
                {
                    SetStatus("Could not start speech recognition.");
                    return;
                }
            }
            else
            {
                if (!recorder.StartRecording())
                {
                    SetStatus("Could not start recording.");
                    return;
                }
            }

            _sessionEngine = engine;
            _sessionNative = native;

            ClearResults();
            SetRecordingUi(true);
            SetStatus("Recording...");
        }
        finally
        {
            _busy = false;
            recordButton.interactable = true;
        }
    }

    // ------------------------------------------------------------
    // Stop
    // ------------------------------------------------------------
    private async Task StopAsync()
    {
        if (_sessionNative)
        {
            // 内蔵STT: 停止を通知し、最終結果は OnNativeSessionEnded で受け取る
            _busy = true;
            recordButton.interactable = false;
            SetRecordingUi(false);
            SetLoading(true);
            SetStatus("Finishing...");
            nativeStt.StopListening();
            return;
        }

        await StopAndProcessWavAsync();
    }

    // ---- 内蔵STT: セッション終了 (ユーザー停止 / 無音による自動終了 どちらも来る) ----
    private void OnNativePartial(string text)
    {
        if (_sessionNative && transcriptText != null)
            transcriptText.text = text;
    }

    private async void OnNativeSessionEnded(string transcript, string error)
    {
        if (!_sessionNative) return;
        _sessionNative = false;

        _busy = true;
        recordButton.interactable = false;
        SetRecordingUi(false);
        SetLoading(true);

        try
        {
            if (!string.IsNullOrEmpty(error))
            {
                SetStatus("Error: " + error);
                return;
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                SetStatus("Could not recognize speech. Please try again.");
                return;
            }

            transcriptText.text = transcript;
            await CorrectTextAsync(transcript, _sessionEngine);
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
            recordButton.interactable = true;
        }
    }

    // 文字起こし済みのテキストを、選択中のエンジンで添削して表示する
    private async Task CorrectTextAsync(string text, Engine engine)
    {
        if (engine == Engine.Gemini)
        {
            SetStatus("Correcting with Gemini...");
            var result = await _gemini.CorrectSpokenTextAsync(text);
            correctedText.text = result.corrected;
            if (explanationText != null) explanationText.text = result.explanation;
        }
        else
        {
            SetStatus("Polishing with DeepL Write...");
            correctedText.text = await _deepl.RephraseAsync(text);
        }

        SetStatus("Done");
    }

    // ---- WAV 録音 → Gemini / (Gemini STT → DeepL) ----
    private async Task StopAndProcessWavAsync()
    {
        _busy = true;
        recordButton.interactable = false;
        SetRecordingUi(false);

        try
        {
            byte[] wav = recorder.StopRecording();
            if (wav == null)
            {
                SetStatus("No audio was recorded.");
                return;
            }

            SetLoading(true);
            SetStatus("Processing...");

            switch (_sessionEngine)
            {
                case Engine.Gemini:
                    await RunGeminiAsync(wav);
                    break;
                case Engine.DeepLWrite:
                    await RunDeepLWithGeminiSttAsync(wav);
                    break;
            }
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
            recordButton.interactable = true;
        }
    }

    private async Task RunGeminiAsync(byte[] wav)
    {
        var result = await _gemini.TranscribeAndCorrectAsync(wav);

        if (string.IsNullOrWhiteSpace(result.transcript))
        {
            SetStatus("Could not recognize speech. Please try again.");
            return;
        }

        transcriptText.text = result.transcript;
        correctedText.text = result.corrected;
        if (explanationText != null) explanationText.text = result.explanation;
        SetStatus("Done");
    }

    // Editor や内蔵STTが使えない端末用のフォールバック
    private async Task RunDeepLWithGeminiSttAsync(byte[] wav)
    {
        string transcript = await _gemini.TranscribeAsync(wav);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            SetStatus("Could not recognize speech. Please try again.");
            return;
        }

        transcriptText.text = transcript;
        SetStatus("Polishing with DeepL Write...");

        correctedText.text = await _deepl.RephraseAsync(transcript);
        SetStatus("Done");
    }

    // ------------------------------------------------------------
    // UI helpers
    // ------------------------------------------------------------
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
            recordButtonLabel.text = recording ? "Stop Recording" : "Start Recording";
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