using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using LLMUnity;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// ConversationController の続き: 感情・認識エラー・マイク権限・画面の表示。
/// </summary>
public partial class ConversationController
{
    // ------------------------------------------------------------------ 感情

    /// <summary>LLM のシステムプロンプトに、感情タグを付ける指示を足す（すでにあれば何もしない）</summary>
    void AddEmotionInstruction()
    {
        string prompt = llmAgent.systemPrompt ?? "";
        if (prompt.Contains("emotion tag")) return;
        llmAgent.systemPrompt = prompt.TrimEnd() + " " + EmotionDetector.PromptInstruction;
    }

    /// <summary>この返事の感情を決めて、表情に反映する</summary>
    void ApplyReplyEmotion(string emotion)
    {
        if (string.IsNullOrEmpty(emotion)) return;
        replyEmotion = emotion;
        if (character != null) character.SetEmotion(emotion);
    }

    // ------------------------------------------------------------------ 認識エラー

    void HandleSttError(int code, string message)
    {
        if (state != State.Listening) return;

        // 続きを待っている間に「無言タイムアウト/聞き取れず」になった = 話し終えたので確定する
        if (!string.IsNullOrEmpty(pendingUserText))
        {
            CommitUserText();
            return;
        }

        switch (code)
        {
            case SttErrorCode.SpeechTimeout:
            case SttErrorCode.NoMatch:
                RestartAfter(0.3f);
                break;

            case SttErrorCode.RecognizerBusy:
                stt.Cancel();
                RestartAfter(1.0f);
                break;

            case SttErrorCode.Unavailable:
            case SttErrorCode.InsufficientPermissions:
            case SttErrorCode.LanguageNotSupported:
            case SttErrorCode.LanguageUnavailable:
                StopConversation();
                status = "エラー: " + message;
                Debug.LogError("[STT] " + code + " " + message);
                break;

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
        int fontSize = Mathf.Max(20, Screen.height / 60);
        var label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };
        var button = new GUIStyle(GUI.skin.button) { fontSize = fontSize + 2 };

        float margin = Screen.width * 0.04f;
        float btnH = Screen.height * 0.07f;

        // 下のタブ (BottomNavBar) が出ている間は、その上までにする (ボタンがタブと重ならないように)
        float bottom = BottomNavBar.ReservedPixels;
        GUILayout.BeginArea(new Rect(margin, margin, Screen.width - margin * 2f, Screen.height - margin * 2f - bottom));

        string progress = (!llmReady && llmProgress > 0f && llmProgress < 1f)
            ? $" ({(int)(llmProgress * 100)}%)" : "";
        GUILayout.Label($"状態: {status}{progress}", label);

        // 確認用の表示(返事や認識結果は、メッセージウィンドウに出るので、普段は隠す)
        if (showDebug)
        {
            if (state == State.Listening && !string.IsNullOrEmpty(partialText))
                GUILayout.Label("認識中: " + partialText, label);

            if (!string.IsNullOrEmpty(userShown)) GUILayout.Label("あなた: " + userShown, label);
            if (!string.IsNullOrEmpty(replyShown))
                GUILayout.Label("返事: " + EmotionDetector.StripTags(replyShown) + (replyEmotion != null ? "  (感情: " + replyEmotion + ")" : ""), label);

            if (latencySec >= 0f)
                GUILayout.Label($"話し終えてから声が出るまで: {latencySec:F2} 秒", label);
            GUILayout.Label(statsText, label);
        }

        GUILayout.FlexibleSpace();

        bool running = state != State.Stopped;
        GUI.enabled = llmReady;
        if (GUILayout.Button(running ? "会話を止める" : "会話を始める", button, GUILayout.Height(btnH)))
        {
            if (running) StopConversation();
            else StartConversation();
        }
        GUI.enabled = true;

        GUILayout.EndArea();
    }
}
