#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Android 標準の SpeechRecognizer を C# から直接呼ぶ実装。
/// ・SpeechRecognizer の生成/操作は UI スレッドで行う
/// ・コールバックは UI スレッドから来るので、キュー経由で Unity のメインスレッドに戻す
/// ・StartListening / Cancel / Pump などは Unity のメインスレッドから呼ぶこと
/// </summary>
public class AndroidSpeechToText : ISpeechToText
{
    public event Action<string> OnPartialResult;
    public event Action<string> OnFinalResult;
    public event Action<int, string> OnError;

    public bool IsListening { get; private set; }

    readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

    AndroidJavaObject activity;
    AndroidJavaObject recognizer;
    AndroidJavaObject intent;

    // ---------------------------------------------------------------- 初期化

    public void Init(string locale)
    {
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        using (var cls = new AndroidJavaClass("android.speech.SpeechRecognizer"))
        {
            if (!cls.CallStatic<bool>("isRecognitionAvailable", activity))
            {
                mainThreadQueue.Enqueue(() => OnError?.Invoke(
                    SttErrorCode.Unavailable,
                    "この端末では音声認識サービスが利用できません(Google アプリが無効、または Manifest の <queries> が不足)"));
                return;
            }
        }

        BuildIntent(locale);

        RunOnUiThread(() =>
        {
            using (var cls = new AndroidJavaClass("android.speech.SpeechRecognizer"))
            {
                recognizer = cls.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);
            }
            recognizer.Call("setRecognitionListener", new Listener(this));
        });
    }

    void BuildIntent(string locale)
    {
        intent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH");
        intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form");
        intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE", locale);
        intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.CALLING_PACKAGE",
            activity.Call<string>("getPackageName"));
        intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PARTIAL_RESULTS", true);
        intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PREFER_OFFLINE", true);
        intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", 1);
        // 無音がこの時間続いたら発話終了とみなす(端末によっては無視される)
        intent.Call<AndroidJavaObject>("putExtra",
            "android.speech.extra.SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS", 2500);
        intent.Call<AndroidJavaObject>("putExtra",
            "android.speech.extra.SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS", 2500);
        intent.Call<AndroidJavaObject>("putExtra",
            "android.speech.extra.SPEECH_INPUT_MINIMUM_LENGTH_MILLIS", 3000);
    }

    // ---------------------------------------------------------------- 操作

    public void StartListening()
    {
        IsListening = true;
        RunOnUiThread(() => recognizer?.Call("startListening", intent));
    }

    public void StopListening()
    {
        RunOnUiThread(() => recognizer?.Call("stopListening"));
    }

    public void Cancel()
    {
        IsListening = false;
        RunOnUiThread(() => recognizer?.Call("cancel"));
    }

    /// <summary>毎フレーム呼ぶ。UI スレッドから届いたイベントをメインスレッドで処理する。</summary>
    public void Pump()
    {
        while (mainThreadQueue.TryDequeue(out var action)) action();
    }

    public void Dispose()
    {
        RunOnUiThread(() =>
        {
            if (recognizer == null) return;
            recognizer.Call("destroy");
            recognizer.Dispose();
            recognizer = null;
        });
        IsListening = false;
    }

    void RunOnUiThread(Action action)
    {
        if (activity == null) return;
        activity.Call("runOnUiThread", new AndroidJavaRunnable(action));
    }

    // ---------------------------------------------------------------- コールバック

    void PostFinal(string text)
    {
        mainThreadQueue.Enqueue(() =>
        {
            IsListening = false;
            if (!string.IsNullOrWhiteSpace(text)) OnFinalResult?.Invoke(text);
            else OnError?.Invoke(SttErrorCode.NoMatch, "empty result");
        });
    }

    void PostPartial(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        mainThreadQueue.Enqueue(() => OnPartialResult?.Invoke(text));
    }

    void PostError(int code)
    {
        mainThreadQueue.Enqueue(() =>
        {
            IsListening = false;
            OnError?.Invoke(code, DescribeError(code));
        });
    }

    static string DescribeError(int code)
    {
        switch (code)
        {
            case SttErrorCode.NetworkTimeout:
            case SttErrorCode.Network: return "ネットワークエラー(オフライン用の音声パックが未導入の可能性)";
            case SttErrorCode.Audio: return "音声入力エラー";
            case SttErrorCode.Server: return "認識サーバーエラー";
            case SttErrorCode.Client: return "クライアントエラー";
            case SttErrorCode.SpeechTimeout: return "音声が検出されませんでした";
            case SttErrorCode.NoMatch: return "聞き取れませんでした";
            case SttErrorCode.RecognizerBusy: return "認識エンジンがビジー状態です";
            case SttErrorCode.InsufficientPermissions: return "マイク権限がありません";
            case SttErrorCode.TooManyRequests: return "リクエストが多すぎます";
            case SttErrorCode.ServerDisconnected: return "認識サービスが切断されました";
            case SttErrorCode.LanguageNotSupported: return "この言語には対応していません";
            case SttErrorCode.LanguageUnavailable: return "この言語の音声データがありません(端末の音声認識設定で言語パックを入れてください)";
            default: return "不明なエラー (code=" + code + ")";
        }
    }

    static string FirstResult(AndroidJavaObject bundle)
    {
        if (bundle == null) return null;
        using (var list = bundle.Call<AndroidJavaObject>("getStringArrayList", "results_recognition"))
        {
            if (list == null || list.Call<int>("size") == 0) return null;
            return list.Call<string>("get", 0);
        }
    }

    /// <summary>
    /// RecognitionListener の実装。メソッドのシグネチャ差異で
    /// 呼び出しが失敗しないよう、Invoke をオーバーライドして名前で振り分ける。
    /// </summary>
    class Listener : AndroidJavaProxy
    {
        readonly AndroidSpeechToText owner;

        public Listener(AndroidSpeechToText owner)
            : base("android.speech.RecognitionListener") { this.owner = owner; }

        public override AndroidJavaObject Invoke(string methodName, object[] args)
        {
            switch (methodName)
            {
                case "onResults":
                    owner.PostFinal(FirstResult(args[0] as AndroidJavaObject));
                    return null;
                case "onPartialResults":
                    owner.PostPartial(FirstResult(args[0] as AndroidJavaObject));
                    return null;
                case "onError":
                    owner.PostError(Convert.ToInt32(args[0]));
                    return null;
                case "onReadyForSpeech":
                case "onBeginningOfSpeech":
                case "onRmsChanged":
                case "onBufferReceived":
                case "onEndOfSpeech":
                case "onEvent":
                    return null;
                default:
                    return base.Invoke(methodName, args);
            }
        }
    }
}
#endif