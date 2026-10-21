using System;
using System.Collections.Generic;

/// <summary>
/// 添削で見つかったミス1件。Gemini の返事とクラウド (mistakes 表) で同じ形を使う。
/// JSON の項目名は type / wrong / fixed (fixed は C# の予約語なので @fixed と書く)。
/// </summary>
[Serializable]
public class Mistake
{
    public string type;     // MistakeTypes.All のどれか
    public string wrong;    // 直す前の短い語句
    public string @fixed;   // 直した後の短い語句
}

/// <summary>
/// ミスの種類 (固定 10 種類)。サーバー (supabase/topic.sql) と Web の表示名と同じ並び。
/// </summary>
public static class MistakeTypes
{
    public const string Other = "other";

    public static readonly string[] All =
    {
        "article", "tense", "preposition", "agreement", "plural",
        "verb_form", "word_order", "word_choice", "missing_word", Other,
    };

    private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
    {
        { "article", "Articles" },
        { "tense", "Tense" },
        { "preposition", "Prepositions" },
        { "agreement", "Subject-verb agreement" },
        { "plural", "Singular & plural" },
        { "verb_form", "Verb forms" },
        { "word_order", "Word order" },
        { "word_choice", "Word choice" },
        { "missing_word", "Missing words" },
        { Other, "Other" },
    };

    /// <summary>小文字にして前後の空白を取る。10 種類に無いものは "other"。</summary>
    public static string Normalize(string type)
    {
        string t = (type ?? "").Trim().ToLowerInvariant();
        return Labels.ContainsKey(t) ? t : Other;
    }

    /// <summary>画面に出す英語の名前</summary>
    public static string Label(string type) => Labels[Normalize(type)];

    /// <summary>種類ごとの件数 (種類は Normalize してから数える)</summary>
    public static Dictionary<string, int> Count(IEnumerable<Mistake> mistakes)
    {
        var counts = new Dictionary<string, int>();
        if (mistakes == null) return counts;
        foreach (var m in mistakes)
        {
            if (m == null) continue;
            string t = Normalize(m.type);
            counts.TryGetValue(t, out int n);
            counts[t] = n + 1;
        }
        return counts;
    }
}
