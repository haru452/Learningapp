using System;

/// <summary>単語の記憶の強さ。Stability = 想起率が 90% に下がるまでの日数の目安、Difficulty = 1〜10</summary>
public struct FsrsMemory
{
    public double Stability;
    public double Difficulty;
}

/// <summary>
/// 復習の日程 (FSRS-4.5、標準パラメータ)。評価: 1 = Didn't know, 2 = Hard, 3 = Knew it, 4 = Easy。
/// 純粋な計算だけ (Unity を使わない)
/// </summary>
public static class Fsrs
{
    public static readonly double[] W =
    {
        0.4872, 1.4003, 3.7145, 13.8206, 5.1618, 1.2298, 0.8975, 0.031, 1.6474,
        0.1367, 1.0461, 2.1072, 0.0793, 0.3246, 1.587, 0.2272, 2.8755,
    };

    private const double Decay = -0.5;
    private const double Factor = 19.0 / 81.0;
    public const int MaxIntervalDays = 36500;

    /// <summary>最後に復習してから elapsedDays 日後に思い出せる確率</summary>
    public static double Retrievability(double elapsedDays, double stability) =>
        Math.Pow(1 + Factor * Math.Max(0, elapsedDays) / Math.Max(stability, 0.01), Decay);

    /// <summary>想起率が retention まで下がる日数 (1〜36500 日)</summary>
    public static int IntervalDays(double stability, double retention)
    {
        double days = stability / Factor * (Math.Pow(retention, 1 / Decay) - 1);
        if (double.IsNaN(days) || days < 1) return 1;
        if (days > MaxIntervalDays) return MaxIntervalDays;
        return Math.Max(1, (int)Math.Round(days, MidpointRounding.AwayFromZero));
    }

    /// <summary>初めて評価したときの記憶</summary>
    public static FsrsMemory First(int rating)
    {
        int g = ClampRating(rating);
        return new FsrsMemory { Stability = W[g - 1], Difficulty = ClampD(InitialDifficulty(g)) };
    }

    /// <summary>2 回目以降の評価のあとの記憶。elapsedDays が負 (時計が戻った) なら 0 として計算する</summary>
    public static FsrsMemory Next(FsrsMemory m, int rating, double elapsedDays)
    {
        int g = ClampRating(rating);
        double s = Math.Max(m.Stability, 0.01);
        double d = ClampD(m.Difficulty);
        double r = Retrievability(elapsedDays, s);

        double nd = ClampD(W[7] * InitialDifficulty(3) + (1 - W[7]) * (d - W[6] * (g - 3)));
        double ns;
        if (g == 1)
        {
            ns = W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1, W[13]) - 1) * Math.Exp(W[14] * (1 - r));
            ns = Math.Min(ns, s);   // 忘れたのに強くはならない
        }
        else
        {
            double hard = g == 2 ? W[15] : 1;
            double easy = g == 4 ? W[16] : 1;
            ns = s * (Math.Exp(W[8]) * (11 - d) * Math.Pow(s, -W[9]) * (Math.Exp(W[10] * (1 - r)) - 1) * hard * easy + 1);
        }
        if (double.IsNaN(ns) || ns < 0.01) ns = 0.01;
        return new FsrsMemory { Stability = ns, Difficulty = nd };
    }

    /// <summary>
    /// ボタンの目安の日数。memory が null = 初めての単語。Didn't know は 0 (次の回)
    /// </summary>
    public static int PreviewDays(FsrsMemory? memory, int rating, double elapsedDays, double retention)
    {
        if (ClampRating(rating) == 1) return 0;
        var m = memory.HasValue ? Next(memory.Value, rating, elapsedDays) : First(rating);
        return IntervalDays(m.Stability, retention);
    }

    /// <summary>"Next time" / "in 4 days" / "in 2 months" / "in 1 year"</summary>
    public static string IntervalLabel(int days)
    {
        if (days <= 0) return "Next time";
        if (days < 30) return days == 1 ? "in 1 day" : "in " + days + " days";
        if (days < 365)
        {
            int months = (int)Math.Round(days / 30.0, MidpointRounding.AwayFromZero);
            return months == 1 ? "in 1 month" : "in " + months + " months";
        }
        int years = (int)Math.Round(days / 365.0, MidpointRounding.AwayFromZero);
        return years == 1 ? "in 1 year" : "in " + years + " years";
    }

    private static double InitialDifficulty(int g) => W[4] - (g - 3) * W[5];
    private static double ClampD(double d) => double.IsNaN(d) ? 5 : Math.Min(10, Math.Max(1, d));
    private static int ClampRating(int g) => Math.Min(4, Math.Max(1, g));
}
