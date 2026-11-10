using System;

/// <summary>単語帳の 1 語 (端末の vocab/deck.json に保存する)</summary>
public class VocabCard
{
    public const string Learning = "learning";
    public const string Known = "known";

    public string Word = "";          // 小文字・前後の空白なし (同じ語は 1 つだけ)
    public string Cefr = "—";    // A1..B2、CEFR-J に無い語は "—"
    public string Source = "auto";    // auto / search / manual
    public string MyMeaning = "";
    public string Status = Learning;  // learning / known
    public DateTime? DueAt;           // 次の復習 (known のときだけ)
    public double Stability;
    public double Difficulty;
    public DateTime? LastReviewAt;
    public int Reps;                  // 評価した回数
    public int MissCount;             // Didn't know の回数
    public DateTime? KnownAt;         // 初めて known になった時刻
    public DateTime CreatedAt;
    public string PronBest = "";      // "" / again / close / clear (1 段階目の一番良い判定)
    public int PronAttempts;
    public string GeminiPron = "";    // "" / good / close / practice (最後の Gemini の判定)

    /// <summary>評価で変わる値 (復習の予定) の写し。評価を取り消すときに RestoreSchedule で戻す</summary>
    public VocabCard ScheduleCopy() => (VocabCard)MemberwiseClone();

    public void RestoreSchedule(VocabCard s)
    {
        if (s == null) return;
        Status = s.Status; DueAt = s.DueAt; Stability = s.Stability; Difficulty = s.Difficulty;
        LastReviewAt = s.LastReviewAt; Reps = s.Reps; MissCount = s.MissCount; KnownAt = s.KnownAt;
    }
}

/// <summary>評価の履歴 (vocab/reviews.json。消さない)</summary>
public class VocabReview
{
    public string Word = "";
    public int Rating;                // 1..4
    public DateTime At;
    public double ElapsedDays;
    public double Stability;          // 評価のあとの値
    public double Difficulty;
    public bool IsNew;                // その語を初めて評価したとき (レベルの提案に使う)
}
