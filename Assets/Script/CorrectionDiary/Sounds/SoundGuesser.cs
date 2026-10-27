using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 言えなかった単語のつづりから、苦手な音を推測する (重み 1)。
/// つづりから決められる th / v_b / f_h / r_l / s_sh / final_consonant だけを数え、3 文字未満と機能語は数えない。
/// </summary>
public static class SoundGuesser
{
    private static readonly HashSet<string> Skip = new HashSet<string>
        { "the", "this", "that", "these", "those", "there", "they", "them", "their", "then", "than", "with", "of", "for", "from" };
    private static readonly string[] Clusters = { "pr", "pl", "br", "bl", "cr", "cl", "dr", "fr", "fl", "gr", "gl", "tr" };
    private static readonly string[] FinalEnds = { "ed", "st", "nd", "nt", "ld", "ct", "pt", "ks", "ts" };

    /// <summary>1 語のつづりから音のコード (重なりなし)</summary>
    public static List<string> Guess(string word)
    {
        string w = Clean(word);
        var codes = new List<string>();
        if (w.Length == 0) return codes;
        if (w.Contains("th")) codes.Add("th");
        if (w.Contains("v")) codes.Add("v_b");
        if (w.Contains("f") || w.Contains("ph")) codes.Add("f_h");
        if (w[0] == 'r' || w[0] == 'l' || Clusters.Any(c => w.Contains(c))) codes.Add("r_l");
        if (w.Contains("sh") || w.StartsWith("si") || w.StartsWith("se")) codes.Add("s_sh");
        if (FinalEnds.Any(e => w.EndsWith(e))) codes.Add("final_consonant");
        return codes;
    }

    /// <summary>言えなかった単語の一覧から、推測できた (語, 音) の組</summary>
    public static List<SoundHit> FromMissedWords(IEnumerable<string> words)
    {
        var hits = new List<SoundHit>();
        foreach (string raw in words ?? Enumerable.Empty<string>())
        {
            string w = Clean(raw);
            if (!IsCountable(w)) continue;
            foreach (string code in Guess(w)) hits.Add(new SoundHit { Code = code, Label = "", Word = w });
        }
        return hits;
    }

    /// <summary>数える語か (3 文字以上で、機能語ではない。that's / they're などは ' の前で判断する)</summary>
    public static bool IsCountable(string word)
    {
        string w = Clean(word);
        int apo = w.IndexOf('\'');
        string stem = apo >= 0 ? w.Substring(0, apo) : w;
        return w.Length >= 3 && stem.Length > 0 && !Skip.Contains(stem);
    }

    /// <summary>多い順のコード (同じ数なら先に出た順)</summary>
    public static List<string> Top(IEnumerable<SoundHit> hits, int n = 3) =>
        (hits ?? Enumerable.Empty<SoundHit>())
            .Select((h, i) => (h.Code, i))
            .GroupBy(x => x.Code)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Min(x => x.i))
            .Take(n).Select(g => g.Key).ToList();

    // 小文字にして、英字とアポストロフィだけ残す
    private static string Clean(string word) =>
        new string((word ?? "").ToLowerInvariant().Replace('\u2019', '\'').Where(c => (c >= 'a' && c <= 'z') || c == '\'').ToArray());
}
