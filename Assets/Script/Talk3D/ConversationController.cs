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
/// ステップ5: 音声認識 → LLM → 読み上げ をつなぐ会話コントローラ。
///
///  [マイク] → STT → テキスト → LLM(ストリーミング)
///                                  ↓ 1文できるたびに
///                         文キュー → TTS → [スピーカー] + 口パク
///
/// 状態: Stopped → Starting → Listening → Thinking → Speaking → Listening ...
///
/// ・LLM が生成している間に、できた文から順に読み上げる(待ち時間の短縮)
/// ・読み上げ中はマイクを止める(自分の声を拾うループを防ぐ)
/// ・無音/聞き取り失敗のときは自動で聞き取りをやり直す
///
/// このコンポーネントを使うときは、SttEchoController と LlmChatTestUI は無効にすること。
/// (DeviceStats は LlmChatTestUI.cs 内にあるので、ファイル自体は残しておく)
/// </summary>
public class ConversationController : MonoBehaviour
{
    [SerializeField] LLMAgent llmAgent;
    [SerializeField] PseudoLipSync lipSync;
    [SerializeField] CharacterAnimator character;
    [SerializeField] MessageWindow messageWindow;

    [Header("表示")]
    [Tooltip("オンにすると、認識結果・返事の全文・所要時間・メモリなどの確認用の文字も表示する")]
    [SerializeField] bool showDebug = false;

    [Header("音声認識(話しかける言語) 例: en-US / ja-JP")]
    [SerializeField] string sttLocale = "en-US";

    [Header("読み上げ(返事の言語) 例: en/US, ja/JP")]
    [SerializeField] string ttsLanguage = "en";
    [SerializeField] string ttsCountry = "US";

    [Header("声の調整(Android 標準 TTS)")]
    [Tooltip("1 = 標準。1.2〜1.5 くらいで、かわいい高めの声になる。上げすぎると不自然")]
    [Range(0.5f, 2f)] [SerializeField] float voicePitch = 1.3f;
    [Range(0.5f, 2f)] [SerializeField] float voiceRate = 1.05f;
    [Tooltip("空なら端末の既定の声。使える声の名前は、実機の起動時に Console(Logcat)に出る")]
    [SerializeField] string voiceName = "";

    [Header("発話のつなぎ合わせ")]
    [Tooltip("認識が終わっても、この秒数のあいだ続きを待つ。0 で無効(すぐ送信)")]
    [SerializeField] float mergeWindowSec = 1.5f;

    // 認識が終わっても、続きを待っている間の「ここまでの発話」
    string pendingUserText = "";
    float commitDeadline = float.PositiveInfinity;

    enum State { Stopped, Starting, Listening, Thinking, Speaking }

    State state = State.Stopped;
    ISpeechToText stt;
    ITextToSpeech tts;

    // 世代番号。止めた/やり直した後に古い非同期処理が動かないようにする
    int sessionId;
    bool resumeOnFocus;

    // LLM
    bool llmReady;
    float llmProgress;

    // 返信の文切り出し
    readonly Queue<string> sentenceQueue = new Queue<string>();
    int consumed;
    bool generationDone;

    // 表示用
    string status = "LLM を準備中…";
    string partialText = "";
    string userShown = "";
    string replyShown = "";
    float tUser;
    float latencySec = -1f;
    string statsText = "";
    float nextStatsTime;

    // ------------------------------------------------------------------ ライフサイクル

    void Awake()
    {
        if (lipSync == null) lipSync = FindFirstObjectByType<PseudoLipSync>();
        if (llmAgent == null) llmAgent = FindFirstObjectByType<LLMAgent>();
        if (character == null) character = FindFirstObjectByType<CharacterAnimator>();

        if (messageWindow == null) messageWindow = FindFirstObjectByType<MessageWindow>();
        if (messageWindow == null) messageWindow = gameObject.AddComponent<MessageWindow>();

#if UNITY_ANDROID && !UNITY_EDITOR
        tts = new AndroidTts(ttsLanguage, ttsCountry);
#else
        tts = new EditorDummyTts();
#endif
        tts.Init();
        tts.SetVoiceStyle(voicePitch, voiceRate);
        tts.SetVoiceName(voiceName);
    }

    async void Start()
    {
        if (llmAgent == null)
        {
            status = "エラー: LLMAgent がシーンにありません";
            return;
        }

        try
        {
            await LLM.WaitUntilModelSetup(p => llmProgress = p);

            status = "ウォームアップ中…";
            var tcs = new TaskCompletionSource<bool>();
            _ = llmAgent.Warmup(() => tcs.TrySetResult(true));
            await tcs.Task;

            llmReady = true;
            status = "準備完了。「会話を始める」を押してください";
        }
        catch (Exception e)
        {
            status = "LLM エラー: " + e.Message;
            Debug.LogException(e);
        }
    }

    void Update()
    {
        stt?.Pump();
        if (lipSync != null) lipSync.IsSpeaking = tts.IsSpeaking;

        // 続きを待っていて、そのまま無言が続いたら、ここまでの発話を確定して LLM へ送る
        if (state == State.Listening
            && !string.IsNullOrEmpty(pendingUserText)
            && Time.realtimeSinceStartup >= commitDeadline)
        {
            CommitUserText();
        }

        if (Time.realtimeSinceStartup >= nextStatsTime)
        {
            nextStatsTime = Time.realtimeSinceStartup + 1f;
            statsText = DeviceStats.Describe();
        }
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

    // ------------------------------------------------------------------ 開始 / 停止

    async void StartConversation()
    {
        if (state != State.Stopped || !llmReady) return;

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
        llmAgent.CancelRequests();
        sentenceQueue.Clear();
        partialText = "";
        pendingUserText = "";
        commitDeadline = float.PositiveInfinity;
        status = "停止中";

        if (character != null) character.SetMode(CharacterAnimator.Mode.Idle);
        if (messageWindow != null) messageWindow.Hide();
    }

    /// <summary>
    /// タッチ反応など、LLM を通さずに一言しゃべらせる。
    /// ・会話を止めている間 / 聞き取り中(ユーザーが話していないとき)だけ話す
    /// ・考え中・返事中は割り込まず、何もしない
    /// 話し終えたら、元の状態(停止 or 聞き取り)に戻る。
    /// </summary>
    public async void SpeakReaction(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        bool wasStopped = state == State.Stopped;
        bool listeningFree = state == State.Listening
            && string.IsNullOrEmpty(pendingUserText)
            && string.IsNullOrEmpty(partialText);
        if (!wasStopped && !listeningFree) return;

        int id = ++sessionId;
        state = State.Speaking;
        status = "リアクション中…";
        if (stt != null) stt.Cancel();   // 自分の声を拾わない

        if (character != null) character.SetMode(CharacterAnimator.Mode.Speaking);

        try
        {
            if (messageWindow != null) messageWindow.ShowCharacterLine(line);
            await tts.SpeakAsync(line, destroyCancellationToken);
            await Task.Delay(500, destroyCancellationToken);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { Debug.LogException(e); }

        if (id != sessionId || state != State.Speaking) return;

        if (wasStopped)
        {
            state = State.Stopped;
            status = "停止中";
            if (character != null) character.SetMode(CharacterAnimator.Mode.Idle);
        }
        else
        {
            BeginListening();
        }
    }

    void CreateStt()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        stt = new AndroidSpeechToText();
#else
        stt = new EditorDummySpeechToText();
#endif
        stt.OnPartialResult += text =>
        {
            if (state != State.Listening) return;
            partialText = text;
            commitDeadline = float.PositiveInfinity; // 話している最中は確定しない
            if (character != null) character.NotifyUserSpeaking(); // 相づち

            // 認識途中の文を、ユーザー側の発言としてウィンドウに出す(続き待ちの分もつなげる)
            if (messageWindow != null)
            {
                string shown = string.IsNullOrEmpty(pendingUserText) ? text : pendingUserText + " " + text;
                messageWindow.ShowUserLine(shown);
            }
        };
        stt.OnFinalResult += OnUserFinal;
        stt.OnError += HandleSttError;
        stt.Init(sttLocale);
    }

    void BeginListening()
    {
        if (state == State.Stopped) return;
        state = State.Listening;
        partialText = "";

        bool waitingForMore = !string.IsNullOrEmpty(pendingUserText);
        status = waitingForMore ? "続きを聞いています…(無言なら送信します)" : "聞いています…";

        // 続き待ちのときは、一定時間なにも話されなければ確定する
        commitDeadline = waitingForMore
            ? Time.realtimeSinceStartup + mergeWindowSec
            : float.PositiveInfinity;

        stt.StartListening();

        if (character != null) character.SetMode(CharacterAnimator.Mode.Listening);
    }

    // ------------------------------------------------------------------ 発話のつなぎ合わせ

    /// <summary>認識が1回終わったとき。すぐ送らず、続きを待つ。</summary>
    void OnUserFinal(string text)
    {
        if (state != State.Listening) return;

        pendingUserText = string.IsNullOrEmpty(pendingUserText)
            ? text
            : pendingUserText + " " + text;
        userShown = pendingUserText;
        partialText = "";

        if (messageWindow != null) messageWindow.ShowUserLine(pendingUserText);

        if (mergeWindowSec <= 0f)
        {
            CommitUserText();
            return;
        }

        // 少し置いて聞き取りをやり直し、続きが話されるか見る
        RestartAfter(0.2f);
    }

    /// <summary>待っていた発話を確定して、LLM に送る。</summary>
    void CommitUserText()
    {
        string text = pendingUserText;
        pendingUserText = "";
        commitDeadline = float.PositiveInfinity;

        if (string.IsNullOrWhiteSpace(text))
        {
            RestartAfter(0.3f);
            return;
        }
        HandleUserSpeech(text);
    }

    // ------------------------------------------------------------------ ユーザー発話 → LLM → 読み上げ

    async void HandleUserSpeech(string userText)
    {
        if (state != State.Listening) return;

        int id = sessionId;
        userShown = userText;
        partialText = "";
        replyShown = "";
        consumed = 0;
        generationDone = false;
        sentenceQueue.Clear();
        latencySec = -1f;
        tUser = Time.realtimeSinceStartup;

        state = State.Thinking;
        status = "考え中…";

        if (character != null)
        {
            character.SetMode(CharacterAnimator.Mode.Thinking);
            character.Nod(1, 10f, 0.5f); // 「なるほど」のうなずき
        }

        if (messageWindow != null) messageWindow.ShowThinking();

        // 念のため認識を止める(返事の声を拾わない)
        stt.Cancel();

        // 読み上げ側のループを先に走らせ、LLM が文を出すたびに読ませる
        Task speaker = SpeakerLoop(id);

        string full = null;
        try
        {
            full = await llmAgent.Chat(userText, soFar => OnReplyPartial(id, soFar), null, true);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

        if (id != sessionId) return;

        // 生成が終わったので、文末記号のない残りも1文として読ませる
        string source = string.IsNullOrEmpty(replyShown) ? full : replyShown;
        if (!string.IsNullOrEmpty(source) && source.Length > consumed)
        {
            string rest = source.Substring(consumed).Trim();
            if (SentenceSplitter.HasLetterOrDigit(rest)) sentenceQueue.Enqueue(rest);
            consumed = source.Length;
        }
        generationDone = true;

        await speaker;
        if (id != sessionId) return;

        // 声の余韻を拾わないよう、少し間を置く
        try { await Task.Delay(600, destroyCancellationToken); }
        catch (OperationCanceledException) { return; }
        if (id != sessionId) return;

        BeginListening();
    }

    /// <summary>LLM から「ここまでの返信全体」が届くたびに呼ばれる</summary>
    void OnReplyPartial(int id, string soFar)
    {
        if (id != sessionId || state == State.Stopped) return;
        if (soFar == null || soFar.Length < consumed) return;

        replyShown = soFar;

        while (SentenceSplitter.TryTake(soFar, consumed, out string sentence, out int end))
        {
            consumed = end;
            sentenceQueue.Enqueue(sentence);
        }
    }

    /// <summary>文キューを監視して、できた文から順に読み上げる</summary>
    async Task SpeakerLoop(int id)
    {
        bool first = true;
        try
        {
            while (id == sessionId)
            {
                if (sentenceQueue.Count > 0)
                {
                    string raw = sentenceQueue.Dequeue();

                    // [happy] のような感情タグを読み取って、表情に反映する
                    if (character != null)
                    {
                        foreach (Match m in TagRegex.Matches(raw))
                            character.SetEmotion(m.Groups[1].Value);
                    }

                    string s = Sanitize(raw);   // タグを除いた、読み上げる文
                    if (!SentenceSplitter.HasLetterOrDigit(s)) continue;

                    if (first)
                    {
                        first = false;
                        latencySec = Time.realtimeSinceStartup - tUser; // 話し終えてから声が出るまで
                    }

                    state = State.Speaking;
                    status = "話しています…";

                    if (character != null)
                    {
                        character.SetMode(CharacterAnimator.Mode.Speaking);
                        if (UnityEngine.Random.value < 0.6f) character.Nod(1, 6f, 0.4f); // 文頭の軽いうなずき
                    }
                    if (messageWindow != null) messageWindow.ShowCharacterLine(s);
                    await tts.SpeakAsync(s, destroyCancellationToken);
                }
                else if (generationDone)
                {
                    break;
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>読み上げに不要な記号を除く</summary>
    static readonly Regex TagRegex = new Regex(@"\[([A-Za-z]+)\]");

    static string Sanitize(string s)
    {
        s = TagRegex.Replace(s, "");   // 感情タグは読み上げない
        return s.Replace("*", "").Replace("#", "").Trim();
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

        GUILayout.BeginArea(new Rect(margin, margin, Screen.width - margin * 2f, Screen.height - margin * 2f));

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
                GUILayout.Label("返事: " + TagRegex.Replace(replyShown, "").Trim(), label);

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