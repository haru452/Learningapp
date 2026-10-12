using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// 端末内蔵の音声認識ラッパー。
///  - Android: android.speech.SpeechRecognizer を AndroidJavaObject 経由で利用
///  - iOS    : Plugins/iOS/SpeechRecognizerBridge.mm (SFSpeechRecognizer) を利用
///  - その他 (Editor 等): IsAvailable = false
///
/// 認識セッションは「最終結果 or エラー」で必ず1回だけ OnSessionEnded が発火する。
/// (ユーザーが停止した場合も、無音で自動終了した場合も同じ)
/// iOS の UnitySendMessage で呼ばれるため、このコンポーネントの GameObject 名は変えないこと。
/// </summary>
public class NativeSpeechRecognizer : MonoBehaviour
{
    public bool IsAvailable { get; private set; }
    public bool IsListening => _sessionOpen;

    /// <summary>認識途中のテキスト (リアルタイム表示用)</summary>
    public event Action<string> OnPartialResult;

    /// <summary>セッション終了。引数: (文字起こし, エラーメッセージ)。成功時 error は null。</summary>
    public event Action<string, string> OnSessionEnded;

    private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
    private bool _sessionOpen;
    private string _lastPartial = "";
    private TaskCompletionSource<bool> _permissionTcs;

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------
    private void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                _activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            _srClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
            IsAvailable = _srClass.CallStatic<bool>("isRecognitionAvailable", _activity);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NativeSTT] Android init failed: " + e.Message);
            IsAvailable = false;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _SR_Init(gameObject.name);
        IsAvailable = _SR_IsAvailable("en-US") == 1;
#else
        IsAvailable = false;
#endif
        if (!IsAvailable)
            Debug.Log("[NativeSTT] Native speech recognition is not available on this platform/device.");

        if (FindObjectsOfType<NativeSpeechRecognizer>().Length > 1)
            Debug.LogWarning("[NativeSTT] Multiple NativeSpeechRecognizer components found. " +
                             "Share ONE instance between all panels (iOS sends native callbacks to a single object).");
    }

    private void Update()
    {
        // Android のコールバックは Unity のメインスレッドではないため、ここで実行する
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    private void OnDestroy()
    {
        Cancel();
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            var rec = _recognizer;
            _recognizer = null;
            if (rec != null && _activity != null)
                _activity.Call("runOnUiThread", new AndroidJavaRunnable(() => { rec.Call("destroy"); }));
        }
        catch (Exception) { /* アプリ終了時は無視 */ }
#endif
    }

    // ------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------

    /// <summary>
    /// 音声認識の権限をリクエストする。
    /// iOS: 音声認識の許可ダイアログ。Android: 何もしない (マイク権限は AudioRecorder 側で取得)。
    /// </summary>
    public Task<bool> RequestSpeechPermissionAsync()
    {
#if UNITY_IOS && !UNITY_EDITOR
        _permissionTcs = new TaskCompletionSource<bool>();
        _SR_RequestPermission();
        return _permissionTcs.Task;
#else
        return Task.FromResult(true);
#endif
    }

    public bool StartListening(string languageTag = "en-US")
    {
        if (!IsAvailable || _sessionOpen) return false;

        _sessionOpen = true;
        _lastPartial = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        StartAndroid(languageTag);
#elif UNITY_IOS && !UNITY_EDITOR
        _SR_Start(languageTag);
#endif
        return true;
    }

    /// <summary>話し終わりを通知する。結果は OnSessionEnded で届く。</summary>
    public void StopListening()
    {
        if (!_sessionOpen) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        RunOnAndroidUiThread(() => _recognizer?.Call("stopListening"));
#elif UNITY_IOS && !UNITY_EDITOR
        _SR_Stop();
#endif
    }

    /// <summary>認識を破棄する (OnSessionEnded は発火しない)。</summary>
    public void Cancel()
    {
        if (!_sessionOpen) return;
        _sessionOpen = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        RunOnAndroidUiThread(() => _recognizer?.Call("cancel"));
#elif UNITY_IOS && !UNITY_EDITOR
        _SR_Cancel();
#endif
    }

    // ------------------------------------------------------------
    // Common event handling (Unity main thread)
    // ------------------------------------------------------------
    private void HandlePartial(string text)
    {
        if (!_sessionOpen || string.IsNullOrEmpty(text)) return;
        _lastPartial = text;
        OnPartialResult?.Invoke(text);
    }

    private void HandleFinal(string text)
    {
        if (!_sessionOpen) return;
        _sessionOpen = false;
        string result = string.IsNullOrWhiteSpace(text) ? _lastPartial : text;
        OnSessionEnded?.Invoke(result ?? "", null);
    }

    private void HandleError(string message, bool noSpeech)
    {
        if (!_sessionOpen) return;
        _sessionOpen = false;

        // 途中までの認識結果があればそれを採用する
        if (!string.IsNullOrWhiteSpace(_lastPartial))
            OnSessionEnded?.Invoke(_lastPartial, null);
        else if (noSpeech)
            OnSessionEnded?.Invoke("", null);
        else
            OnSessionEnded?.Invoke("", message);
    }

    // ============================================================
    // Android
    // ============================================================
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject _activity;
    private AndroidJavaClass _srClass;
    private AndroidJavaObject _recognizer;
    private RecognitionListenerProxy _listener;

    private void RunOnAndroidUiThread(Action action)
    {
        _activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            try { action(); }
            catch (Exception e)
            {
                string msg = e.Message;
                _mainThreadQueue.Enqueue(() => HandleError(msg, false));
            }
        }));
    }

    private void StartAndroid(string languageTag)
    {
        RunOnAndroidUiThread(() =>
        {
            // SpeechRecognizer は UI スレッドで生成・操作する必要がある
            if (_recognizer == null)
            {
                _recognizer = _srClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", _activity);
                _listener = new RecognitionListenerProxy(this);
                _recognizer.Call("setRecognitionListener", _listener);
            }

            using (var intent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH"))
            {
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form")?.Dispose();
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE", languageTag)?.Dispose();
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PARTIAL_RESULTS", true)?.Dispose();
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", 1)?.Dispose();
                // 少し間が空いても自動終了しにくくする (端末によっては無視される)
                intent.Call<AndroidJavaObject>("putExtra",
                    "android.speech.extra.SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS", 2500L)?.Dispose();
                intent.Call<AndroidJavaObject>("putExtra",
                    "android.speech.extra.SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS", 2500L)?.Dispose();

                _recognizer.Call("startListening", intent);
            }
        });
    }

    private static string AndroidErrorMessage(int code)
    {
        switch (code)
        {
            case 1: return "Network timeout";
            case 2: return "Network error (speech recognition may need an internet connection)";
            case 3: return "Audio recording error";
            case 4: return "Speech server error";
            case 5: return "Speech client error";
            case 8: return "Speech recognizer is busy";
            case 9: return "Microphone permission is missing";
            case 10: return "Too many speech requests";
            case 11: return "Speech server disconnected";
            case 12: return "Language is not supported";
            case 13: return "Language data is not available";
            default: return "Speech recognition error (" + code + ")";
        }
    }

    private static string ExtractFirstResult(AndroidJavaObject bundle)
    {
        if (bundle == null) return "";
        using (var list = bundle.Call<AndroidJavaObject>("getStringArrayList", "results_recognition"))
        {
            if (list == null || list.Call<int>("size") == 0) return "";
            using (var first = list.Call<AndroidJavaObject>("get", 0))
            {
                return first != null ? first.Call<string>("toString") : "";
            }
        }
    }

    // android.speech.RecognitionListener の実装。
    // 呼び出しは Android の UI スレッドから来るので、Unity API には触れず Queue 経由で戻す。
    private class RecognitionListenerProxy : AndroidJavaProxy
    {
        private readonly NativeSpeechRecognizer _owner;

        public RecognitionListenerProxy(NativeSpeechRecognizer owner)
            : base("android.speech.RecognitionListener")
        {
            _owner = owner;
        }

        public void onReadyForSpeech(AndroidJavaObject @params) { }
        public void onBeginningOfSpeech() { }
        public void onRmsChanged(float rmsdB) { }
        public void onBufferReceived(byte[] buffer) { }
        public void onEndOfSpeech() { }
        public void onEvent(int eventType, AndroidJavaObject @params) { }

        public void onError(int error)
        {
            // 6: SPEECH_TIMEOUT, 7: NO_MATCH → 「何も聞き取れなかった」扱い
            bool noSpeech = error == 6 || error == 7;
            string msg = AndroidErrorMessage(error);
            _owner._mainThreadQueue.Enqueue(() => _owner.HandleError(msg, noSpeech));
        }

        public void onResults(AndroidJavaObject results)
        {
            string text = ExtractFirstResult(results);
            _owner._mainThreadQueue.Enqueue(() => _owner.HandleFinal(text));
        }

        public void onPartialResults(AndroidJavaObject partialResults)
        {
            string text = ExtractFirstResult(partialResults);
            _owner._mainThreadQueue.Enqueue(() => _owner.HandlePartial(text));
        }
    }
#endif

    // ============================================================
    // iOS (Plugins/iOS/SpeechRecognizerBridge.mm)
    // ============================================================
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void _SR_Init(string objectName);
    [DllImport("__Internal")] private static extern int _SR_IsAvailable(string languageTag);
    [DllImport("__Internal")] private static extern void _SR_RequestPermission();
    [DllImport("__Internal")] private static extern void _SR_Start(string languageTag);
    [DllImport("__Internal")] private static extern void _SR_Stop();
    [DllImport("__Internal")] private static extern void _SR_Cancel();
#endif

    // 以下は iOS ネイティブから UnitySendMessage で呼ばれる (メインスレッド)
    public void OnNativePermissionResult(string result) { _permissionTcs?.TrySetResult(result == "1"); }
    public void OnNativePartial(string text) { HandlePartial(text); }
    public void OnNativeFinal(string text) { HandleFinal(text); }
    public void OnNativeError(string message) { HandleError(message, false); }
}