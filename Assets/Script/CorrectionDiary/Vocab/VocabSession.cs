using System;
using System.Collections.Generic;

/// <summary>
/// 1 回分のカードを順に評価する (純粋なロジック)。同じ回では同じ語を出し直さない
/// (Didn't know の語は学習中に戻り、次の回の最初に出る)
/// </summary>
public class VocabSession
{
    private readonly List<VocabCard> _cards;
    private readonly double _retention;
    private readonly Stack<VocabCard> _before = new Stack<VocabCard>();   // 評価する前の予定 (戻るときに使う)

    public VocabSession(List<VocabCard> cards, double retention)
    {
        _cards = cards ?? new List<VocabCard>();
        _retention = retention;
    }

    public int Index { get; private set; }
    public int Count => _cards.Count;
    public bool Done => Index >= _cards.Count;
    public VocabCard Current => Done ? null : _cards[Index];
    public IReadOnlyList<VocabCard> Cards => _cards;

    /// <summary>前の単語に戻れるか (この回で 1 つ以上評価していて、まだ最後まで行っていない)</summary>
    public bool CanGoBack => Index > 0 && !Done && _before.Count > 0;

    /// <summary>直前の評価を取り消して、その単語に戻る (予定も評価する前に戻す)。戻れなければ null</summary>
    public VocabCard Back()
    {
        if (!CanGoBack) return null;
        Index--;
        var c = _cards[Index];
        c.RestoreSchedule(_before.Pop());
        return c;
    }

    /// <summary>今のカードを評価してカードを書き換え、次へ進む。履歴の 1 行を返す</summary>
    public VocabReview Rate(int rating, DateTime nowUtc)
    {
        var c = Current;
        if (c == null) throw new InvalidOperationException("session is done");
        int g = Math.Min(4, Math.Max(1, rating));
        _before.Push(c.ScheduleCopy());
        bool isNew = c.Reps == 0;
        double elapsed = Elapsed(c, nowUtc);
        var m = isNew ? Fsrs.First(g) : Fsrs.Next(new FsrsMemory { Stability = c.Stability, Difficulty = c.Difficulty }, g, elapsed);

        c.Stability = m.Stability;
        c.Difficulty = m.Difficulty;
        c.Reps++;
        c.LastReviewAt = nowUtc;
        if (g == 1)
        {
            c.Status = VocabCard.Learning;
            c.MissCount++;
            c.DueAt = null;
        }
        else
        {
            c.Status = VocabCard.Known;
            c.DueAt = nowUtc.AddDays(Fsrs.IntervalDays(m.Stability, _retention));
            if (!c.KnownAt.HasValue) c.KnownAt = nowUtc;
        }
        Index++;
        return new VocabReview
        {
            Word = c.Word, Rating = g, At = nowUtc, ElapsedDays = elapsed,
            Stability = m.Stability, Difficulty = m.Difficulty, IsNew = isNew,
        };
    }

    /// <summary>4 つのボタンの目安の日数 (Didn't know は 0 = 次の回)</summary>
    public int[] PreviewDays(DateTime nowUtc)
    {
        var c = Current;
        var result = new int[4];
        if (c == null) return result;
        FsrsMemory? m = c.Reps == 0 ? (FsrsMemory?)null : new FsrsMemory { Stability = c.Stability, Difficulty = c.Difficulty };
        double elapsed = Elapsed(c, nowUtc);
        for (int g = 1; g <= 4; g++) result[g - 1] = Fsrs.PreviewDays(m, g, elapsed, _retention);
        return result;
    }

    // 前の評価からの日数 (時計が戻っていたら 0)
    private static double Elapsed(VocabCard c, DateTime nowUtc) =>
        c.LastReviewAt.HasValue ? Math.Max(0, (nowUtc - c.LastReviewAt.Value).TotalDays) : 0;
}
