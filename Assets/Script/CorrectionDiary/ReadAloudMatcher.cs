using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 言い直しの照合: お手本の文 (expected) と、音声認識で聞き取れた文 (heard) を単語ごとに比べる。
/// ・大文字小文字、句読点は無視する。ハイフン・ダッシュは語の区切りとして扱う
/// ・縮約形は展開して比べる (don't ⇔ do not、I'm ⇔ I am など)
/// ・余分に言った語は減点しない。お手本の語のうち、順番どおりに言えた語の割合を一致率にする
/// </summary>
public static class ReadAloudMatcher
{
    public struct Word
    {
        public string Text;     // お手本の語 (元の綴り・句読点のまま)
        public bool Matched;    // 言えていたか
        /// <summary>言えなかった語のまとまりの最後の語にだけ付く: 代わりに聞こえた語 (何も聞こえなければ "")。ほかの語は null</summary>
        public string Heard;
    }

    public class Result
    {
        public List<Word> Words = new List<Word>();
        public int AccuracyPercent;   // 0〜100
    }

    public static Result Match(string expected, string heard)
    {
        var result = new Result();

        // お手本: 元の語ごとに、比べるための部分語 (縮約形を展開したもの) を持つ
        var originals = new List<string>();
        var partsOfWord = new List<List<string>>();
        foreach (string raw in SplitWords(expected))
        {
            var parts = PartsOf(raw);
            if (parts.Count == 0) continue;   // 記号だけの語は数えない
            originals.Add(raw);
            partsOfWord.Add(parts);
        }
        if (originals.Count == 0) return result;

        var expectedParts = new List<string>();
        var ownerOfPart = new List<int>();
        for (int i = 0; i < partsOfWord.Count; i++)
        {
            foreach (string p in partsOfWord[i])
            {
                expectedParts.Add(p);
                ownerOfPart.Add(i);
            }
        }

        var heardRaw = new List<string>();
        var heardParts = new List<string>();
        var heardOwner = new List<int>();
        foreach (string raw in SplitWords(heard))
        {
            var parts = PartsOf(raw);
            if (parts.Count == 0) continue;
            foreach (string p in parts) { heardParts.Add(p); heardOwner.Add(heardRaw.Count); }
            heardRaw.Add(raw);
        }

        int[] pairOf = LongestCommonSubsequence(expectedParts, heardParts);
        var partMatched = new bool[pairOf.Length];
        for (int k = 0; k < pairOf.Length; k++) partMatched[k] = pairOf[k] >= 0;

        var wordOk = new bool[originals.Count];
        for (int i = 0; i < wordOk.Length; i++) wordOk[i] = true;
        for (int k = 0; k < expectedParts.Count; k++)
            if (!partMatched[k]) wordOk[ownerOfPart[k]] = false;

        int matched = 0;
        for (int i = 0; i < originals.Count; i++)
        {
            result.Words.Add(new Word { Text = originals[i], Matched = wordOk[i] });
            if (wordOk[i]) matched++;
        }
        result.AccuracyPercent = (int)Math.Round(matched * 100.0 / originals.Count, MidpointRounding.AwayFromZero);
        AddHeard(result.Words, ownerOfPart, pairOf, heardOwner, heardRaw);
        return result;
    }

    /// <summary>
    /// 言えなかった語のまとまりごとに、その前後で言えた語の間に聞こえた語を、まとまりの最後の語の Heard に入れる
    /// </summary>
    private static void AddHeard(List<Word> words, List<int> ownerOfPart, int[] pairOf, List<int> heardOwner, List<string> heardRaw)
    {
        // 語ごとに、対応した聞こえた部分語の位置の最小・最大 (無ければ -1)
        int n = words.Count;
        var first = new int[n];
        var last = new int[n];
        for (int i = 0; i < n; i++) { first[i] = -1; last[i] = -1; }
        for (int k = 0; k < pairOf.Length; k++)
        {
            int j = pairOf[k], w = ownerOfPart[k];
            if (j < 0) continue;
            if (first[w] < 0) first[w] = j;
            last[w] = j;
        }

        int start = 0;
        while (start < n)
        {
            if (words[start].Matched) { start++; continue; }
            int end = start;
            while (end + 1 < n && !words[end + 1].Matched) end++;

            int lo = -1;   // まとまりの前で言えた語の、最後の聞こえた位置
            for (int i = start - 1; i >= 0 && lo < 0; i--) lo = last[i];
            int hi = heardOwner.Count;   // まとまりの後で言えた語の、最初の聞こえた位置
            for (int i = end + 1; i < n; i++) if (first[i] >= 0) { hi = first[i]; break; }

            var heardWords = new List<string>();
            int prevOwner = -1;
            for (int j = lo + 1; j < hi; j++)
            {
                int o = heardOwner[j];
                if (o == prevOwner) continue;
                heardWords.Add(heardRaw[o]);
                prevOwner = o;
            }
            var wd = words[end];
            wd.Heard = string.Join(" ", heardWords);
            words[end] = wd;
            start = end + 1;
        }
    }

    // ------------------------------------------------------------
    private static IEnumerable<string> SplitWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (string w in text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) yield return w;
    }

    // ハイフン・ダッシュ (well-known, think—honestly) は語の区切り。音声認識は分けて返すため
    private static readonly char[] Dashes = { '-', '\u2010', '\u2011', '\u2012', '\u2013', '\u2014', '\u2015', '\u2212' };

    /// <summary>1 語 (空白区切り) を、比べるための部分語に分ける。記号だけなら空</summary>
    private static List<string> PartsOf(string raw)
    {
        var parts = new List<string>();
        foreach (string piece in raw.Split(Dashes, StringSplitOptions.RemoveEmptyEntries))
            parts.AddRange(Expand(Normalize(piece)));
        return parts;
    }

    /// <summary>小文字にし、英字・数字・アポストロフィ以外を取り除く</summary>
    private static string Normalize(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (char ch in word.ToLowerInvariant())
        {
            char c = ch == '’' || ch == '‘' ? '\'' : ch;
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '\'') sb.Append(c);
        }
        return sb.ToString().Trim('\'');
    }

    /// <summary>縮約形を展開する (例: don't → do not)。空なら空のリスト</summary>
    private static List<string> Expand(string w)
    {
        var parts = new List<string>();
        if (w.Length == 0) return parts;

        switch (w)
        {
            case "can't": parts.Add("can"); parts.Add("not"); return parts;
            case "won't": parts.Add("will"); parts.Add("not"); return parts;
            case "shan't": parts.Add("shall"); parts.Add("not"); return parts;
        }

        string[,] suffixes =
        {
            { "n't", "not" }, { "'re", "are" }, { "'m", "am" }, { "'ll", "will" },
            { "'ve", "have" }, { "'d", "would" }, { "'s", "is" },
        };
        for (int i = 0; i < suffixes.GetLength(0); i++)
        {
            string suffix = suffixes[i, 0];
            if (w.Length > suffix.Length && w.EndsWith(suffix, StringComparison.Ordinal))
            {
                parts.Add(w.Substring(0, w.Length - suffix.Length));
                parts.Add(suffixes[i, 1]);
                return parts;
            }
        }
        parts.Add(w);
        return parts;
    }

    /// <summary>a の各要素が、b との最長共通部分列で対応した b の位置 (入らなければ -1)</summary>
    private static int[] LongestCommonSubsequence(List<string> a, List<string> b)
    {
        int n = a.Count, m = b.Count;
        var len = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                len[i, j] = a[i] == b[j] ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);

        var matched = new int[n];
        for (int i = 0; i < n; i++) matched[i] = -1;
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { matched[x] = y; x++; y++; }
            else if (len[x + 1, y] >= len[x, y + 1]) x++;
            else y++;
        }
        return matched;
    }
}
