/// <summary>
/// 生成途中のテキストから、読み上げ可能な「1文」を切り出す。
/// 英語の . ! ? と、日本語の 。！？ 、改行に対応する。
///
/// 英語の区切りは「次の文字が空白」のときだけ確定する。
/// これにより "3.5" や "Mr. Smith" のような途中の . で誤って区切らない。
/// (文末の . は、次のトークンが来るか、生成終了時のフラッシュで確定する)
/// </summary>
public static class SentenceSplitter
{
    static readonly string[] Abbreviations =
        { "mr", "mrs", "ms", "dr", "st", "vs", "e.g", "i.e", "no" };

    /// <param name="text">これまでに生成された返信の全文</param>
    /// <param name="start">すでに切り出した位置(ここから探す)</param>
    /// <param name="sentence">切り出した1文</param>
    /// <param name="end">次に探し始める位置</param>
    public static bool TryTake(string text, int start, out string sentence, out int end)
    {
        sentence = null;
        end = start;
        if (string.IsNullOrEmpty(text)) return false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            bool boundary = false;

            if (c == '。' || c == '！' || c == '？' || c == '\n')
            {
                boundary = true;
            }
            else if (c == '.' || c == '!' || c == '?')
            {
                bool nextIsSpace = i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]);
                if (nextIsSpace && !IsAbbreviation(text, i)) boundary = true;
            }

            if (!boundary) continue;

            string s = text.Substring(start, i - start + 1).Trim();
            if (HasLetterOrDigit(s))
            {
                sentence = s;
                end = i + 1;
                return true;
            }
            start = i + 1; // 句読点だけの断片は読み飛ばす
        }
        return false;
    }

    public static bool HasLetterOrDigit(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (char.IsLetterOrDigit(c)) return true;
        return false;
    }

    static bool IsAbbreviation(string t, int dotIndex)
    {
        if (t[dotIndex] != '.') return false;

        int s = dotIndex;
        while (s > 0 && (char.IsLetter(t[s - 1]) || t[s - 1] == '.')) s--;
        string word = t.Substring(s, dotIndex - s).ToLowerInvariant();

        foreach (var a in Abbreviations)
            if (word == a) return true;
        return false;
    }
}
