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

    /// <summary>
    /// 先に決めておいた単語 (nextNew) を保存した順に先頭へ、そのあとランダムの候補。重なりなし。
    /// randomCandidates は「今のレベルで単語帳に無い語」なので、そこに無い nextNew (レベルを変えた・もう単語帳にある) は落ちる
    /// </summary>
    public static List<string> OrderCandidates(List<string> nextNew, List<string> randomCandidates, IEnumerable<string> deckWords)
    {
        var random = randomCandidates ?? new List<string>();
        var have = new HashSet<string>((deckWords ?? Enumerable.Empty<string>()).Select(VocabDeck.Key));
        var inRandom = new HashSet<string>(random.Select(VocabDeck.Key));
        var seen = new HashSet<string>(have);
        var result = new List<string>();
        foreach (string w in nextNew ?? new List<string>())
        {
            string k = VocabDeck.Key(w);
            if (inRandom.Contains(k) && seen.Add(k)) result.Add(k);
        }
        foreach (string w in random)
            if (seen.Add(VocabDeck.Key(w))) result.Add(w);
        return result;
    }

    /// <summary>回のあとに next_new.json へ残す単語: 使った語と、もう候補でない語を除く (順番はそのまま)</summary>
    public static List<string> RemainingNextNew(List<string> nextNew, IEnumerable<string> used, List<string> randomCandidates)
    {
        var usedKeys = new HashSet<string>((used ?? Enumerable.Empty<string>()).Select(VocabDeck.Key));
        var inRandom = new HashSet<string>((randomCandidates ?? new List<string>()).Select(VocabDeck.Key));
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (string w in nextNew ?? new List<string>())
        {
            string k = VocabDeck.Key(w);
            if (!usedKeys.Contains(k) && inRandom.Contains(k) && seen.Add(k)) result.Add(k);
        }
        return result;
    }
}
