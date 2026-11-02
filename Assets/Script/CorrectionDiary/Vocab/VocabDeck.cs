using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>単語帳 (同じ語は 1 つだけ)。純粋なロジック。保存は VocabStore</summary>
public class VocabDeck
{
    public const string FilterAll = "all";

    public List<VocabCard> Cards { get; }

    public VocabDeck(List<VocabCard> cards)
    {
        Cards = cards ?? new List<VocabCard>();
    }

    /// <summary>同じ語かを比べるキー: 小文字、前後の空白なし、空白は 1 つ</summary>
    public static string Key(string word) => Regex.Replace((word ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    public VocabCard Find(string word)
    {
        string k = Key(word);
        return Cards.FirstOrDefault(c => c.Word == k);
    }

    /// <summary>学習中に足す。同じ語がすでにある・空なら null</summary>
    public VocabCard Add(string word, string source, string myMeaning, string cefr, DateTime nowUtc)
    {
        string k = Key(word);
        if (k.Length == 0 || Find(k) != null) return null;
        var card = new VocabCard
        {
            Word = k, Source = source ?? "manual", MyMeaning = (myMeaning ?? "").Trim(),
            Cefr = string.IsNullOrEmpty(cefr) ? CefrWordList.None : cefr, Status = VocabCard.Learning, CreatedAt = nowUtc,
        };
        Cards.Add(card);
        return card;
    }

    public bool Remove(string word)
    {
        var c = Find(word);
        return c != null && Cards.Remove(c);
    }

    /// <summary>学習中に戻す (次の回の最初に出る)</summary>
    public void MoveToLearning(string word)
    {
        var c = Find(word);
        if (c == null) return;
        c.Status = VocabCard.Learning;
        c.DueAt = null;
    }

    /// <summary>Words の一覧。levelFilter: all / A1..B2 / —。学習中は新しく足した順、known は次の復習の近い順</summary>
    public List<VocabCard> List(string status, string levelFilter)
    {
        var q = Cards.Where(c => c.Status == status);
        if (!string.IsNullOrEmpty(levelFilter) && levelFilter != FilterAll) q = q.Where(c => c.Cefr == levelFilter);
        return status == VocabCard.Known
            ? q.OrderBy(c => c.DueAt ?? DateTime.MaxValue).ThenBy(c => c.Word).ToList()
            : q.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Word).ToList();
    }
}
