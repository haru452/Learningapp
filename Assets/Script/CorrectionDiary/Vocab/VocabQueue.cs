using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>1 回分に出す単語を決める (純粋なロジック)</summary>
public static class VocabQueue
{
    /// <summary>期限が来た復習がこれより多いときは、新しい単語を出さない</summary>
    public const int MaxDueForNewWords = 50;

    /// <summary>
    /// 学習中 (古い順) → 期限が来た復習 (期限の古い順) を size まで。
    /// newCount = 足りない分 (期限切れが 50 を超えたら 0)
    /// </summary>
    public static (List<VocabCard> cards, int newCount) Plan(IList<VocabCard> deck, DateTime nowUtc, int size)
    {
        var all = deck ?? new List<VocabCard>();
        var learning = all.Where(c => c.Status != VocabCard.Known)
                          .OrderBy(c => c.LastReviewAt ?? c.CreatedAt).ThenBy(c => c.CreatedAt);
        var due = all.Where(c => c.Status == VocabCard.Known && c.DueAt.HasValue && c.DueAt.Value <= nowUtc)
                     .OrderBy(c => c.DueAt.Value).ToList();
        var cards = learning.Concat(due).Take(Math.Max(0, size)).ToList();
        int newCount = due.Count > MaxDueForNewWords ? 0 : Math.Max(0, size - cards.Count);
        return (cards, newCount);
    }

    /// <summary>そのレベルの新しい単語の候補 (単語帳にある語は状態を問わず除く)。シャッフルして返す</summary>
    public static List<string> NewCandidates(CefrWordList list, string level, IEnumerable<string> deckWords, Random rng)
    {
        var have = new HashSet<string>((deckWords ?? Enumerable.Empty<string>()).Select(VocabDeck.Key));
        var pool = list.WordsAt(level).Where(w => !have.Contains(VocabDeck.Key(w))).ToList();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool;
    }
}
