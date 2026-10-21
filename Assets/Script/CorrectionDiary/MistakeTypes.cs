using System;
using System.Collections.Generic;
using System.Text;

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

    /// <summary>
    /// クラウド (save_mistakes / save_topic_result の p_mistakes) に送る JSON 配列。
    /// null の要素は飛ばし、種類は Normalize する。null や空なら "[]"。
    /// </summary>
    public static string ToJsonArray(IEnumerable<Mistake> mistakes)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        if (mistakes != null)
        {
            foreach (var m in mistakes)
            {
                if (m == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"type\":\"").Append(Escape(Normalize(m.type)))
                  .Append("\",\"wrong\":\"").Append(Escape(m.wrong))
                  .Append("\",\"fixed\":\"").Append(Escape(m.@fixed)).Append("\"}");
            }
        }
        return sb.Append(']').ToString();
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
