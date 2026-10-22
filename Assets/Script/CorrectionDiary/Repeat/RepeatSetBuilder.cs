using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// 1 回の練習に出す文を組み立てる: 言えなかった文 (最大 2) → 自分の添削文 (最大 2) と Tatoeba を混ぜる。
/// 同じ文 (大文字・句読点違い) は 1 回だけ。直近に出した Tatoeba の文は、ほかに無いときだけ使う。
/// </summary>
public static class RepeatSetBuilder
{
    public const int MaxMissed = 2;
    public const int MaxOwn = 2;

    public static List<RepeatSentence> Build(IList<RepeatSentence> missed, IList<RepeatSentence> own,
                                             IList<RepeatSentence> tatoeba, ISet<string> recentKeys,
                                             Random rng, int total = 5)
    {
        var used = new HashSet<string>();
        var result = new List<RepeatSentence>();

        bool TryAdd(RepeatSentence s, List<RepeatSentence> into)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Text)) return false;
            string k = Key(s.Text);
            if (k.Length == 0 || !used.Add(k)) return false;
            into.Add(s);
            return true;
        }

        foreach (var s in missed ?? new List<RepeatSentence>())
        {
            if (result.Count >= Math.Min(MaxMissed, total)) break;
            TryAdd(s, result);
        }

        var rest = new List<RepeatSentence>();
        foreach (var s in own ?? new List<RepeatSentence>())
        {
            if (rest.Count >= MaxOwn || result.Count + rest.Count >= total) break;
            TryAdd(s, rest);
        }

        var shuffled = (tatoeba ?? new List<RepeatSentence>()).OrderBy(_ => rng.Next()).ToList();
        recentKeys = recentKeys ?? new HashSet<string>();
        foreach (var pass in new[] { false, true })        // まず直近に出していない文、足りなければ直近の文も
        {
            foreach (var s in shuffled)
            {
                if (result.Count + rest.Count >= total) break;
                if (s == null || recentKeys.Contains(Key(s.Text)) != pass) continue;
                TryAdd(s, rest);
            }
        }

        result.AddRange(rest.OrderBy(_ => rng.Next()));
        return result;
    }

    /// <summary>同じ文かどうかを比べるキー (SQL の _sentence_key と同じ): 小文字、英数字と空白だけ、空白は 1 つ</summary>
    public static string Key(string text)
    {
        var sb = new StringBuilder();
        bool space = false;
        foreach (char raw in (text ?? "").Trim())
        {
            if (char.IsWhiteSpace(raw)) { space = true; continue; }
            char c = char.ToLowerInvariant(raw);
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) continue;
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
