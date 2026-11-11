using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ConversationController の続き: 英会話の練習 (画面の添削・今日の単語・まとめと連続記録)。
/// 返事は今まで通り端末の LLM がすぐ話し、添削は Gemini が裏で作って画面の上に出す (声では言わない。返事を待たせない)。
/// </summary>
public partial class ConversationController
{
    GeminiApiClient coachGemini;
    TalkSummary coachSummary;
    bool coachActive;            // 会話の記録の途中 (アプリを離れて戻ったときは同じ記録を続ける)
    bool coachShowSummary;
    bool coachCounted;
    float coachStart;
    string lastLufkaLine = "";

    int coachTurn;                    // 発話の番号 (画面の ✎ を今の発話の添削だけにする)
    readonly TalkCoach.CorrectionSlot latest = new TalkCoach.CorrectionSlot();   // 画面に出す最新の言い直し

    /// <summary>会話を始めるとき: 今日の単語とレベルを Lufka への指示に足し、記録を始める</summary>
    void CoachBeginSession()
    {
        coachShowSummary = false;
        if (coachActive) return;   // アプリを離れて戻った: 同じ記録を続ける
        coachActive = true;
        coachCounted = false;
        coachStart = Time.realtimeSinceStartup;
        latest.NewTurn(++coachTurn, "");
        lastLufkaLine = "";
        nudgesInRow = 0;
        nudgedWords.Clear();
        lastVoiceAt = float.PositiveInfinity;

        var words = new List<string>();
        try { words = TalkPlan.PickWords(VocabStore.LoadReviews(), VocabStore.LoadDeck().Cards, DateTime.Now); }
        catch (Exception e) { Debug.LogWarning("[Talk] could not read the vocabulary: " + e.Message); }
        coachSummary = new TalkSummary(words);
        if (llmAgent != null)
            llmAgent.systemPrompt = TalkPlan.WithInstruction(llmAgent.systemPrompt, TalkPlan.Instruction(VocabSettings.Level, words));
    }

    /// <summary>発話が決まったとき: 記録して、添削を裏で始める</summary>
    void CoachOnUserTurn(int id, string text)
    {
        coachSummary?.AddUserTurn(text, Time.realtimeSinceStartup - coachStart);
        latest.NewTurn(++coachTurn, text);   // 前の発話の ✎ は消す
        if (TalkCoach.ShouldCheck(text)) _ = CorrectInBackground(id, coachTurn, text, lastLufkaLine);
    }

    async Task CorrectInBackground(int id, int turn, string said, string lufkaLine)
    {
        TalkCorrection c = null;
        try { c = await Gemini().CorrectTalkAsync(said, lufkaLine); }
        catch (Exception e) { Debug.LogWarning("[Talk] correction failed: " + e.Message); }
        // 古い会話 (End・やり直しのあと) の結果は使わない
        if (c != null && id == sessionId && coachActive)
        {
            coachSummary?.AddCorrection(said, c);
            latest.Offer(turn, c);   // 遅れて届いた前の発話の添削では、画面を上書きしない
        }
    }

    /// <summary>End: 会話を止めて、まとめを出し、3 回以上話していれば連続記録に数える</summary>
    void CoachEnd()
    {
        StopConversation();
        if (!coachActive || coachSummary == null) return;
        coachActive = false;
        coachSummary.End(Time.realtimeSinceStartup - coachStart);
        coachShowSummary = true;
        if (coachSummary.CountsForStreak && !coachCounted)
        {
            coachCounted = true;
            _ = CountStudyDayAsync();
        }
    }

    // 端末の連続記録とリマインダー + ログイン中ならクラウド (失敗は Console だけ)。単語帳と同じ
    async Task CountStudyDayAsync()
    {
        var diary = FindFirstObjectByType<DiaryManager>(FindObjectsInactive.Include);
        if (diary != null) diary.CountOtherPracticeAsStudyDay();
        var supabase = FindFirstObjectByType<SupabaseService>(FindObjectsInactive.Include);
        if (supabase == null || !supabase.IsConfigured) return;
        try
        {
            if (!await supabase.EnsureSignedInAsync()) return;
            await supabase.CompleteTalkSessionAsync(DateTime.UtcNow);
            if (diary != null) diary.RefreshStreakFromCloud();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Talk] could not count the study day in the cloud: " + e.Message);
        }
    }

    GeminiApiClient Gemini()
    {
        if (coachGemini != null) return coachGemini;
        var s = ApiSecrets.Data;
        coachGemini = new GeminiApiClient(s.geminiApiKey, "gemini-flash-lite-latest", "gemini-3.8-flash",
            backupApiKeys: new[] { s.geminiTopicApiKey, s.geminiSummaryApiKey, s.geminiTtsApiKey, s.geminiShadowApiKey });
        return coachGemini;
    }

    // ------------------------------------------------------------------ 黙っているとき
    float lastVoiceAt = float.PositiveInfinity;   // あなたの番が始まった・最後に声が聞こえた時刻 (無限 = 数えない)
    int nudgesInRow;                               // あなたが話さないまま続けて質問した回数
    readonly List<string> nudgedWords = new List<string>();

    /// <summary>あなたの番が始まった (Lufka が話し終えた・会話を始めた): 黙っている時間を数え始める</summary>
    void CoachUserTurnStarted() => lastVoiceAt = Time.realtimeSinceStartup;

    /// <summary>あなたの声が聞こえた (認識の途中・確定): 数え直す</summary>
    void CoachHeardVoice() => lastVoiceAt = Time.realtimeSinceStartup;

    /// <summary>発話が決まった: 続けての質問の回数を戻す</summary>
    void CoachUserSpoke() => nudgesInRow = 0;

    /// <summary>Update から: 聞いている間に SilenceSeconds 黙っていたら、まだ使っていない今日の単語を使わせる質問をさせる</summary>
    void CoachSilenceTick()
    {
        if (!coachActive || coachSummary == null || state != State.Listening) return;
        if (!string.IsNullOrEmpty(pendingUserText) || !string.IsNullOrEmpty(partialText)) return;
        if (Time.realtimeSinceStartup - lastVoiceAt < TalkPlan.SilenceSeconds) return;
        if (nudgesInRow >= TalkPlan.MaxNudgesInRow) { lastVoiceAt = float.PositiveInfinity; return; }
        string word = TalkPlan.NextNudgeWord(coachSummary.Words, coachSummary.Used, nudgedWords);
        if (word == null) { lastVoiceAt = float.PositiveInfinity; return; }   // 全部使えた: 何もしない
        lastVoiceAt = float.PositiveInfinity;
        nudgesInRow++;
        nudgedWords.Remove(word);
        nudgedWords.Add(word);   // 最後に質問した語を後ろへ
        Debug.Log("[Talk] quiet for " + TalkPlan.SilenceSeconds + "s. Asking a question for \"" + word + "\".");
        HandleUserSpeech(TalkPlan.NudgeQuery(word), nudge: true);
    }

    /// <summary>OnGUI の中で: 今日の単語・最新の言い直し・まとめ</summary>
    void CoachGui(GUIStyle label, GUIStyle button, float buttonHeight)
    {
        if (coachShowSummary)
        {
            int pressed = TalkCoachView.Summary(coachSummary, coachCounted, label, button, buttonHeight);
            if (pressed == 1) StartConversation();
            else if (pressed == 2) coachShowSummary = false;
            return;
        }
        if (coachActive && coachSummary != null) TalkCoachView.Words(coachSummary.Words, coachSummary.Used, label);
        if (coachActive) TalkCoachView.Correction(latest.Said, latest.Value, label);
    }
}
