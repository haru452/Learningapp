using System.Text.RegularExpressions;

/// <summary>単語帳 (同じ語は 1 つだけ)</summary>
public class VocabDeck
{
    /// <summary>同じ語かを比べるキー: 小文字、前後の空白なし、空白は 1 つ</summary>
    public static string Key(string word) => Regex.Replace((word ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
}
