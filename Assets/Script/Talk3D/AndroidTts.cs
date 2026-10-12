#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Android 標準の TextToSpeech を C# から直接呼ぶ実装。
/// すべての呼び出しは Unity のメインスレッドから行うこと。
/// </summary>
public class AndroidTts : ITextToSpeech
{
    const int QUEUE_ADD = 1;
    const int SUCCESS = 0;
    const int NOT_READY = -100;

    readonly string language;
    readonly string country;

    volatile AndroidJavaObject tts;
    volatile int initStatus = NOT_READY;
    bool languageApplied;
    bool ready;

    // 声の設定(準備ができたら適用する)
    float pitch = 1f;
    float speechRate = 1f;
    string voiceName = "";

    class InitListener : AndroidJavaProxy
    {
        readonly Action<int> callback;
        public InitListener(Action<int> cb)
            : base("android.speech.tts.TextToSpeech$OnInitListener") { callback = cb; }

        // Java 側から呼ばれる(別スレッドの可能性があるので、フラグを立てるだけにする)
        void onInit(int status) => callback(status);
    }

    public AndroidTts(string language = "ja", string country = "JP")
    {
        this.language = language;
        this.country = country;
    }

    public void Init()
    {
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        var activity = player.GetStatic<AndroidJavaObject>("currentActivity");

        // TextToSpeech は UI スレッドで生成する
        activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
        {
            tts = new AndroidJavaObject(
                "android.speech.tts.TextToSpeech",
                activity,
                new InitListener(status => initStatus = status));
        }));
    }

    public bool IsSpeaking => ready && tts != null && tts.Call<bool>("isSpeaking");

    async Task<bool> WaitUntilReady(CancellationToken ct)
    {
        if (ready) return true;

        var sw = Stopwatch.StartNew();
        while (tts == null || initStatus == NOT_READY)
        {
            ct.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds > 10000)
            {
                Debug.LogError("[AndroidTts] 初期化がタイムアウトしました。Manifest の <queries> と TTS エンジンの有無を確認してください。");
                return false;
            }
            await Task.Yield();
        }

        if (initStatus != SUCCESS)
        {
            Debug.LogError($"[AndroidTts] 初期化に失敗しました (status={initStatus})");
            return false;
        }

        if (!languageApplied)
        {
            using var locale = new AndroidJavaObject("java.util.Locale", language, country);
            int result = tts.Call<int>("setLanguage", locale);
            languageApplied = true;
            // LANG_MISSING_DATA = -1, LANG_NOT_SUPPORTED = -2
            if (result < 0)
            {
                Debug.LogWarning($"[AndroidTts] {language}-{country} が使えません (result={result})。" +
                                 "端末の設定で TTS の音声データ(日本語)をインストールしてください。");
            }
        }

        ready = true;
        ApplyVoiceSettings();
        LogAvailableVoices();
        return true;
    }

    // ---------------------------------------------------------------- 声の調整

    public void SetVoiceStyle(float pitch, float rate)
    {
        this.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        speechRate = Mathf.Clamp(rate, 0.5f, 2f);
        if (ready) ApplyVoiceSettings();
    }

    public void SetVoiceName(string name)
    {
        voiceName = name ?? "";
        if (ready) ApplyVoiceSettings();
    }

    void ApplyVoiceSettings()
    {
        if (tts == null) return;

        // 声の種類を先に指定してから、高さと速さを重ねる
        if (!string.IsNullOrEmpty(voiceName) && !TrySetVoice(voiceName))
            Debug.LogWarning($"[AndroidTts] 声 '{voiceName}' が見つかりません。Console の一覧にある名前を指定してください。");

        tts.Call<int>("setPitch", pitch);
        tts.Call<int>("setSpeechRate", speechRate);
    }

    bool TrySetVoice(string name)
    {
        try
        {
            using (var voices = tts.Call<AndroidJavaObject>("getVoices"))
            {
                if (voices == null) return false;
                using (var it = voices.Call<AndroidJavaObject>("iterator"))
                {
                    while (it.Call<bool>("hasNext"))
                    {
                        using (var v = it.Call<AndroidJavaObject>("next"))
                        {
                            if (v.Call<string>("getName") == name)
                                return tts.Call<int>("setVoice", v) == SUCCESS;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AndroidTts] 声の指定に失敗しました: " + e.Message);
        }
        return false;
    }

    /// <summary>この端末で使える声(指定した言語のもの)を、Console に一覧で出す</summary>
    void LogAvailableVoices()
    {
        try
        {
            var sb = new StringBuilder($"[AndroidTts] 使える声(言語: {language}):\n");
            int count = 0;

            using (var voices = tts.Call<AndroidJavaObject>("getVoices"))
            {
                if (voices == null) return;
                using (var it = voices.Call<AndroidJavaObject>("iterator"))
                {
                    while (it.Call<bool>("hasNext"))
                    {
                        using (var v = it.Call<AndroidJavaObject>("next"))
                        using (var locale = v.Call<AndroidJavaObject>("getLocale"))
                        {
                            string tag = locale.Call<string>("toLanguageTag");
                            if (!tag.StartsWith(language, StringComparison.OrdinalIgnoreCase)) continue;

                            string name = v.Call<string>("getName");
                            int quality = v.Call<int>("getQuality");
                            bool network = v.Call<bool>("isNetworkConnectionRequired");
                            sb.AppendLine($"  {name}   ({tag}, quality={quality}{(network ? ", 要ネット" : "")})");
                            count++;
                        }
                    }
                }
            }

            sb.Append($"(合計 {count} 個。この名前を ConversationController の Voice Name に入れると、声が切り替わります)");
            Debug.Log(sb.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AndroidTts] 声の一覧を取得できませんでした: " + e.Message);
        }
    }

    public async Task SpeakAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!await WaitUntilReady(ct)) return;

        try
        {
            using var bundle = new AndroidJavaObject("android.os.Bundle");
            tts.Call<int>("speak", text, QUEUE_ADD, bundle, Guid.NewGuid().ToString());

            // 再生が始まるのを待つ(speak 直後は isSpeaking が false のことがある)
            var sw = Stopwatch.StartNew();
            while (!tts.Call<bool>("isSpeaking") && sw.ElapsedMilliseconds < 1500)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            // 再生が終わる(キューが空になる)のを待つ
            while (tts.Call<bool>("isSpeaking"))
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        if (ready && tts != null) tts.Call<int>("stop");
    }

    public void Dispose()
    {
        if (tts == null) return;
        try
        {
            tts.Call<int>("stop");
            tts.Call("shutdown");
        }
        catch (Exception e) { Debug.LogWarning(e); }
        tts.Dispose();
        tts = null;
        ready = false;
    }
}
#endif