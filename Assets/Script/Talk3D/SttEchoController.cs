using System;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// ステップ3用: 話しかけた内容を認識して、そのまま読み上げる(オウム返し)。
///
/// 状態: Stopped → Starting(準備/待機) → Listening → Speaking → Listening → ...
///
/// ・読み上げ中はマイクを止める(自分の声を拾うループを防ぐ)
/// ・無音/聞き取り失敗のときは自動で聞き取りをやり直す
/// ・Canvas 不要(OnGUI で描画)
/// </summary>
public class SttEchoController : MonoBehaviour
{
    [SerializeField] PseudoLipSync lipSync;

    [Header("音声認識(話しかける言語) 例: en-US / ja-JP")]
    [SerializeField] string sttLocale = "en-US";

    [Header("読み上げ(オウム返しの言語。STT と合わせる) 例: en/US, ja/JP")]
    [SerializeField] string ttsLanguage = "en";
    [SerializeField] string ttsCountry = "US";

    enum State { Stopped, Starting, Listening, Speaking }

    State state = State.Stopped;
    ISpeechToText stt;
    ITextToSpeech tts;

    int sessionId;               // 古い非同期処理を無効化するための番号
    bool resumeOnFocus;          // アプリ復帰時に再開するか

    string status = "停止中";
    string partialText = "";
    string finalText = "";

    // ------------------------------------------------------------------ ライフサイクル

    void Awake()
    {
        if (lipSync == null) lipSync = FindFirstObjectByType<PseudoLipSync>();

#if UNITY_ANDROID && !UNITY_EDITOR
        tts = new AndroidTts(ttsLanguage, ttsCountry);
#else
        tts = new EditorDummyTts();
#endif
        tts.Init();
    }

    void Update()
    {
        stt?.Pump();
        if (lipSync != null) lipSync.IsSpeaking = tts.IsSpeaking;
    }

    void OnDestroy()
    {
        sessionId++;
        stt?.Dispose();
        tts?.Dispose();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            resumeOnFocus = state != State.Stopped;
            if (resumeOnFocus) StopConversation();
        }
        else if (resumeOnFocus)
        {
            resumeOnFocus = false;
            StartConversation();
        }
    }

    // ------------------------------------------------------------------ 会話の開始/停止

    async void StartConversation()
    {
        if (state != State.Stopped) return;

        state = State.Starting;
        int id = ++sessionId;
        status = "準備中…";

        bool granted = await EnsureMicPermission();
        if (id != sessionId) return;

        if (!granted)
        {
            state = State.Stopped;
            status = "マイク権限が許可されていません。設定アプリから許可してください。";
            return;
        }

        if (stt == null) CreateStt();
        BeginListening();
    }

    void StopConversation()
    {
        sessionId++;
        state = State.Stopped;
        stt?.Cancel();
        tts.Stop();
        partialText = "";
        status = "停止中";
    }

    void CreateStt()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        stt = new AndroidSpeechToText();
#else
        stt = new EditorDummySpeechToText();
#endif
        stt.OnPartialResult += text => { if (state == State.Listening) partialText = text; };
        stt.OnFinalResult += HandleFinalResult;
        stt.OnError += HandleError;
        stt.Init(sttLocale);
    }

    void BeginListening()
    {
        if (state == State.Stopped) return;
        state = State.Listening;
        partialText = "";
        status = "聞いています…";
        stt.StartListening();
    }

    // ------------------------------------------------------------------ 認識結果の処理

    async void HandleFinalResult(string text)
    {
        if (state != State.Listening) return;

        finalText = text;
        partialText = "";
        state = State.Speaking;
        status = "オウム返し中…";
        int id = sessionId;

        // 念のため認識を止めてから読み上げる(自分の声を拾わない)
        stt.Cancel();

        try
        {
            await tts.SpeakAsync(text, destroyCancellationToken);
            await Task.Delay(600, destroyCancellationToken); // 余韻を拾わないための間
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogException(e); }

        if (id != sessionId || state != State.Speaking) return;
        BeginListening();
    }

    void HandleError(int code, string message)
    {
        if (state != State.Listening) return;

        switch (code)
        {
            // 何も話さなかった/聞き取れなかった: 静かにやり直す
            case SttErrorCode.SpeechTimeout:
            case SttErrorCode.NoMatch:
                RestartAfter(0.3f);
                break;

            // ビジー: 一度キャンセルして少し待つ
            case SttErrorCode.RecognizerBusy:
                stt.Cancel();
                RestartAfter(1.0f);
                break;

            // 続けられないエラー: 停止して理由を表示
            case SttErrorCode.Unavailable:
            case SttErrorCode.InsufficientPermissions:
            case SttErrorCode.LanguageNotSupported:
            case SttErrorCode.LanguageUnavailable:
                StopConversation();
                status = "エラー: " + message;
                Debug.LogError("[STT] " + code + " " + message);
                break;

            // それ以外(ネットワーク等): 表示して少し待ってやり直す
            default:
                status = "エラー: " + message + "(再試行します)";
                Debug.LogWarning("[STT] " + code + " " + message);
                RestartAfter(1.5f);
                break;
        }
    }

    async void RestartAfter(float seconds)
    {
        state = State.Starting;
        int id = sessionId;
        try { await Task.Delay((int)(seconds * 1000f), destroyCancellationToken); }
        catch (OperationCanceledException) { return; }

        if (id != sessionId || state != State.Starting) return;
        BeginListening();
    }

    // ------------------------------------------------------------------ マイク権限

    Task<bool> EnsureMicPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            return Task.FromResult(true);

        var tcs = new TaskCompletionSource<bool>();
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => tcs.TrySetResult(true);
        callbacks.PermissionDenied += _ => tcs.TrySetResult(false);
        callbacks.PermissionDeniedAndDontAskAgain += _ => tcs.TrySetResult(false);
        Permission.RequestUserPermission(Permission.Microphone, callbacks);
        return tcs.Task;
#else
        return Task.FromResult(true);
#endif
    }

    // ------------------------------------------------------------------ 画面

    void OnGUI()
    {
        int fontSize = Mathf.Max(24, Screen.height / 38);
        var label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };
        var button = new GUIStyle(GUI.skin.button) { fontSize = fontSize };

        float margin = Screen.width * 0.05f;
        float width = Screen.width - margin * 2f;
        float y = Screen.height * 0.03f;
        float lineH = Screen.height * 0.07f;

        GUI.Label(new Rect(margin, y, width, lineH * 1.5f), "状態: " + status, label);
        y += lineH * 1.5f;

        GUI.Label(new Rect(margin, y, width, lineH * 2f), "認識中: " + partialText, label);
        y += lineH * 2f;

        GUI.Label(new Rect(margin, y, width, lineH * 3f), "認識結果: " + finalText, label);

        float btnH = Screen.height * 0.09f;
        float btnY = Screen.height * 0.85f;
        bool running = state != State.Stopped;
        if (GUI.Button(new Rect(margin, btnY, width, btnH), running ? "会話を止める" : "会話を始める", button))
        {
            if (running) StopConversation();
            else StartConversation();
        }
    }
}
