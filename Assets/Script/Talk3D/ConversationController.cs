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
/// ・返事の感情（[happy] などのタグ、無ければ言葉から推測）をキャラクターの表情に反映する
///
/// このコンポーネントを使うときは、SttEchoController と LlmChatTestUI は無効にすること。
/// (DeviceStats は LlmChatTestUI.cs 内にあるので、ファイル自体は残しておく)
/// </summary>
public partial class ConversationController : MonoBehaviour
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

    [Header("感情")]
    [Tooltip("ON: 起動時に、LLM のシステムプロンプトへ「返事の最初に感情タグを付ける」指示を自動で足す")]
    [SerializeField] bool addEmotionInstruction = true;
    [Tooltip("ON: ユーザーの発話から感情を読み取り、考えている間の表情にも反映する（悲しい話には悲しい顔など）")]
    [SerializeField] bool reactToUserEmotion = true;

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

    // 返事の感情（1回の返事の間、同じ表情を保つ）
    string replyEmotion;

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

            if (addEmotionInstruction) AddEmotionInstruction();

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

        CoachSilenceTick();   // 黙っていたら、今日の単語を使いたくなる質問をさせる

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
        CoachBeginSession();   // 今日の単語とレベルを指示に足し、練習の記録を始める

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
        CoachUserTurnStarted();
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
            CoachHeardVoice();
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
        CoachHeardVoice();

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

    /// <param name="nudge">true: あなたの発話ではなく、黙っていたときの Lufka への頼み (記録・添削・画面には出さない)</param>
    async void HandleUserSpeech(string userText, bool nudge = false)
    {
        if (state != State.Listening) return;

        int id = sessionId;
        if (!nudge) userShown = userText;
        partialText = "";
        replyShown = "";
        consumed = 0;
        generationDone = false;
        sentenceQueue.Clear();
        replyEmotion = null;
        latencySec = -1f;
        tUser = Time.realtimeSinceStartup;

        state = State.Thinking;
        status = "考え中…";
        if (!nudge)
        {
            CoachUserSpoke();
            CoachOnUserTurn(id, userText);   // 添削を裏で始める (返事は待たせない)
        }

        if (character != null)
        {
            character.SetMode(CharacterAnimator.Mode.Thinking);
            character.Nod(1, 10f, 0.5f); // 「なるほど」のうなずき

            // ユーザーが悲しい話・うれしい話をしたら、考えている間もそれに合わせた顔をする（共感）
            if (reactToUserEmotion && !nudge)
            {
                string userEmotion = EmotionDetector.FromWords(userText);
                if (userEmotion == EmotionDetector.Sad || userEmotion == EmotionDetector.Happy || userEmotion == EmotionDetector.Surprised)
                    character.SetEmotion(userEmotion);
            }
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

        lastLufkaLine = Sanitize(string.IsNullOrEmpty(replyShown) ? full ?? "" : replyShown);   // 次の添削の文脈

        // 声の余韻を拾わないよう、少し間を置く
        try { await Task.Delay(600, destroyCancellationToken); }
        catch (OperationCanceledException) { return; }
        if (id != sessionId) return;

        BeginListening();
        CoachUserTurnStarted();   // ここから黙っている時間を数える
    }

    /// <summary>LLM から「ここまでの返信全体」が届くたびに呼ばれる</summary>
    void OnReplyPartial(int id, string soFar)
    {
        if (id != sessionId || state == State.Stopped) return;
        if (soFar == null || soFar.Length < consumed) return;

        replyShown = soFar;

        // 感情タグは、読み上げより先に表情へ反映する（タグが文の後ろに付いても拾えるよう、返事全体から探す）
        if (replyEmotion == null)
        {
            string tag = EmotionDetector.FindTag(soFar);
            if (tag != null) ApplyReplyEmotion(tag);
        }

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

                    // 文ごとに感情を確認する。タグがあればそれを、まだ感情が決まっていなければ言葉から推測する
                    string sentenceTag = EmotionDetector.FindTag(raw);
                    if (sentenceTag != null) ApplyReplyEmotion(sentenceTag);
                    else if (replyEmotion == null) ApplyReplyEmotion(EmotionDetector.FromWords(EmotionDetector.StripTags(raw)));   // 見つからなければ今の表情のまま
                    else if (character != null) character.SetEmotion(replyEmotion);   // 話している間は表情を保つ

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
    static string Sanitize(string s)
    {
        s = EmotionDetector.StripTags(s);   // 感情タグは読み上げない
        return s.Replace("*", "").Replace("#", "").Trim();
    }

}