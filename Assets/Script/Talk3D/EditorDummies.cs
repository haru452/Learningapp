#if !(UNITY_ANDROID && !UNITY_EDITOR)
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#pragma warning disable CS0067 // 未使用イベントの警告を抑制

// ============================================================================
// PCのエディタ用のダミー(Android 実機では使われない)
//
//  EditorDummyTts          … 音は出ないが、文字数に応じた時間だけ「話している」状態になる
//  EditorDummySpeechToText … 聞き取り開始の2.5秒後に、決まった文を「認識結果」として返す
//
// 会話の流れや口パクを、PCだけで確認するために使う。
// ============================================================================

public class EditorDummyTts : ITextToSpeech
{
    const float SecondsPerChar = 0.12f;
    float endTime;

    public void Init() { }

    public bool IsSpeaking => Time.realtimeSinceStartup < endTime;

    public async Task SpeakAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        float start = Mathf.Max(Time.realtimeSinceStartup, endTime);
        endTime = start + text.Length * SecondsPerChar;

        while (IsSpeaking)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    public void Stop() => endTime = 0f;

    public void SetVoiceStyle(float pitch, float rate) { }

    public void SetVoiceName(string voiceName) { }

    public void Dispose() { }
}

public class EditorDummySpeechToText : ISpeechToText
{
    public event Action<string> OnPartialResult;
    public event Action<string> OnFinalResult;
    public event Action<int, string> OnError;

    public bool IsListening { get; private set; }

    const float DelaySeconds = 2.5f;
    const string DummyText = "Hello, this is a test of speech recognition.";
    float fireAt = -1f;

    public void Init(string locale) { }

    public void StartListening()
    {
        IsListening = true;
        fireAt = Time.realtimeSinceStartup + DelaySeconds;
    }

    public void StopListening()
    {
        if (IsListening) fireAt = Time.realtimeSinceStartup;
    }

    public void Cancel()
    {
        IsListening = false;
        fireAt = -1f;
    }

    public void Pump()
    {
        if (!IsListening || fireAt < 0f || Time.realtimeSinceStartup < fireAt) return;
        fireAt = -1f;
        IsListening = false;
        OnFinalResult?.Invoke(DummyText);
    }

    public void Dispose() { }
}
#endif
