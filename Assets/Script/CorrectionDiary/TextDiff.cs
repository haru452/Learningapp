using System.Collections.Generic;
using System.Text;

/// <summary>
/// 原文(話した内容)と添削後の文を単語単位で比較し、
/// 添削後の文のうち「原文と違う単語」だけを Discord の下線記法 __...__ で囲む。
///
/// ・大文字/小文字、前後の句読点の違いは「変更」とみなさない
///   (音声認識の結果には句読点や大文字がほとんど付かないため)
/// ・LCS(最長共通部分列)で比較するので、語順が変わっても正しく差分を取れる
/// </summary>
public static class TextDiff
{
    private const long MaxCells = 4000000;   // 比較表の最大サイズ (約2000語 x 2000語)

    private class Token
    {
        public int Start;
        public int Length;
        public string Key;   // 比較用 (小文字・前後の記号なし)。空なら記号だけのトークン
        public int End => Start + Length;
    }

    /// <summary>
    /// changedWords: 下線を付けた単語数。長すぎて比較できなかった場合は -1 (下線なしで返す)。
    /// 戻り値は Discord 用にエスケープ済みの文字列。
    /// </summary>
    public static string UnderlineChanges(string original, string corrected, out int changedWords)
    {
        changedWords = 0;
        if (string.IsNullOrWhiteSpace(corrected)) return corrected ?? "";

        List<Token> cTokens = Tokenize(corrected);

        // 比較対象 (記号だけのトークンは除く)
        var a = new List<string>();                       // 原文の単語キー
        foreach (Token ot in Tokenize(original ?? ""))
            if (ot.Key.Length > 0) a.Add(ot.Key);

        var bIndex = new List<int>();                     // 添削文の単語 (cTokens 内の位置)
        for (int i = 0; i < cTokens.Count; i++)
            if (cTokens[i].Key.Length > 0) bIndex.Add(i);

        int n = a.Count;
        int m = bIndex.Count;

        if ((long)(n + 1) * (m + 1) > MaxCells)
        {
            changedWords = -1;
            return Escape(corrected);
        }

        // 後ろからの LCS 長テーブル
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == cTokens[bIndex[j]].Key
                    ? dp[i + 1, j + 1] + 1
                    : System.Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        // 前から辿り、原文に対応する単語が無い添削文の単語を「変更あり」にする
        var changed = new bool[cTokens.Count];
        {
            int i = 0, j = 0;
            while (i < n && j < m)
            {
                if (a[i] == cTokens[bIndex[j]].Key) { i++; j++; }
                else if (dp[i + 1, j] >= dp[i, j + 1]) { i++; }          // 原文だけにある単語(削除された語)
                else { changed[bIndex[j]] = true; j++; }                 // 添削文だけにある単語(追加・変更された語)
            }
            for (; j < m; j++) changed[bIndex[j]] = true;
        }

        // 出力を組み立てる (連続する変更語は1つの下線にまとめる)
        var sb = new StringBuilder();
        int pos = 0;
        int t = 0;
        while (t < cTokens.Count)
        {
            Token tok = cTokens[t];

            if (!changed[t])
            {
                sb.Append(Escape(corrected.Substring(pos, tok.End - pos)));
                pos = tok.End;
                t++;
                continue;
            }

            int end = t;
            while (end + 1 < cTokens.Count && changed[end + 1] &&
                   !HasNewline(corrected, cTokens[end].End, cTokens[end + 1].Start))
            {
                end++;
            }

            int spanStart = tok.Start;
            int spanEnd = cTokens[end].End;

            // 先頭・末尾の記号 (引用符やカンマなど) は下線の外に出す
            int coreStart = spanStart;
            while (coreStart < spanEnd && !char.IsLetterOrDigit(corrected[coreStart])) coreStart++;
            int coreEnd = spanEnd;
            while (coreEnd > coreStart && !char.IsLetterOrDigit(corrected[coreEnd - 1])) coreEnd--;

            sb.Append(Escape(corrected.Substring(pos, coreStart - pos)));
            sb.Append("__");
            sb.Append(Escape(corrected.Substring(coreStart, coreEnd - coreStart)));
            sb.Append("__");
            sb.Append(Escape(corrected.Substring(coreEnd, spanEnd - coreEnd)));

            changedWords += end - t + 1;
            pos = spanEnd;
            t = end + 1;
        }

        sb.Append(Escape(corrected.Substring(pos)));
        return sb.ToString();
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private static List<Token> Tokenize(string text)
    {
        var list = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) break;

            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;

            list.Add(new Token
            {
                Start = start,
                Length = i - start,
                Key = Normalize(text.Substring(start, i - start))
            });
        }
        return list;
    }

    // 前後の記号を取り、小文字にする。曲がったアポストロフィは普通のものに揃える
    private static string Normalize(string word)
    {
        int s = 0, e = word.Length;
        while (s < e && !char.IsLetterOrDigit(word[s])) s++;
        while (e > s && !char.IsLetterOrDigit(word[e - 1])) e--;
        return word.Substring(s, e - s).Replace('\u2019', '\'').ToLowerInvariant();
    }

    private static bool HasNewline(string text, int from, int to)
    {
        for (int i = from; i < to; i++)
            if (text[i] == '\n') return true;
        return false;
    }

    // 日記の本文に含まれる * _ ~ ` | が、意図しない装飾にならないようにする
    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            if (c == '\\' || c == '*' || c == '_' || c == '~' || c == '`' || c == '|')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
