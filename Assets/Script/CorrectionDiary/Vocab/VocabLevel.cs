using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>レベル (A1〜B2) と「レベルを上げる」の提案</summary>
public static class VocabLevel
{
    public const int SuggestWindow = 20;   // 直近の新しい単語の数
    public const int SuggestKnown = 16;    // そのうち Didn't know 以外がこれ以上なら提案 (8 割)

    /// <summary>次のレベル (B2 の次は無い = null)</summary>
    public static string Next(string level)
    {
        int i = CefrWordList.LevelIndex(level);
        return i >= 0 && i + 1 < CefrWordList.Levels.Length ? CefrWordList.Levels[i + 1] : null;
    }

    /// <summary>今のレベルにしてからの新しい単語の評価が 20 個そろい、うち 16 個以上が Didn't know 以外</summary>
    public static bool ShouldSuggestUp(IEnumerable<VocabReview> reviews, DateTime levelSetAtUtc)
    {
        var last = (reviews ?? Enumerable.Empty<VocabReview>())
            .Where(r => r.IsNew && r.At >= levelSetAtUtc)
            .OrderByDescending(r => r.At)
            .Take(SuggestWindow)
            .ToList();
        return last.Count == SuggestWindow && last.Count(r => r.Rating >= 2) >= SuggestKnown;
    }
}
