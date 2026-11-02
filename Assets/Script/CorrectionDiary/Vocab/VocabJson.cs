using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// 単語帳・評価の履歴・辞書の結果を JSON にする / 読む (純粋な関数)。
/// 読めないときは空 (辞書は null)。足りない項目は既定値。時刻は UTC の ISO 8601
/// </summary>
public static class VocabJson
{
    // ---------------- 単語帳 ----------------
    public static string DeckToJson(IList<VocabCard> cards)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < (cards?.Count ?? 0); i++)
        {
            var c = cards[i];
            if (i > 0) sb.Append(",\n");
            sb.Append('{');
            Prop(sb, "word", c.Word, true); Prop(sb, "cefr", c.Cefr); Prop(sb, "source", c.Source);
            Prop(sb, "myMeaning", c.MyMeaning); Prop(sb, "status", c.Status); Prop(sb, "dueAt", c.DueAt);
            Prop(sb, "stability", c.Stability); Prop(sb, "difficulty", c.Difficulty); Prop(sb, "lastReviewAt", c.LastReviewAt);
            Prop(sb, "reps", c.Reps); Prop(sb, "missCount", c.MissCount); Prop(sb, "knownAt", c.KnownAt);
            Prop(sb, "createdAt", (DateTime?)c.CreatedAt); Prop(sb, "pronBest", c.PronBest); Prop(sb, "pronAttempts", c.PronAttempts);
            Prop(sb, "geminiPron", c.GeminiPron);
            sb.Append('}');
        }
        return sb.Append(']').ToString();
    }

    public static List<VocabCard> DeckFromJson(string json)
    {
        var list = new List<VocabCard>();
        var seen = new HashSet<string>();
        foreach (var d in Rows(json))
        {
            string word = VocabDeck.Key(Str(d, "word"));
            if (word.Length == 0 || !seen.Add(word)) continue;   // 同じ語は最初の 1 枚だけ
            list.Add(new VocabCard
            {
                Word = word,
                Cefr = Str(d, "cefr", CefrWordList.None),
                Source = Str(d, "source", "auto"),
                MyMeaning = Str(d, "myMeaning"),
                Status = Str(d, "status") == VocabCard.Known ? VocabCard.Known : VocabCard.Learning,
                DueAt = Date(d, "dueAt"),
                Stability = Num(d, "stability"),
                Difficulty = Num(d, "difficulty"),
                LastReviewAt = Date(d, "lastReviewAt"),
                Reps = (int)Num(d, "reps"),
                MissCount = (int)Num(d, "missCount"),
                KnownAt = Date(d, "knownAt"),
                CreatedAt = Date(d, "createdAt") ?? DateTime.UtcNow,
                PronBest = Str(d, "pronBest"),
                PronAttempts = (int)Num(d, "pronAttempts"),
                GeminiPron = Str(d, "geminiPron"),
            });
        }
        return list;
    }

    // ---------------- 評価の履歴 ----------------
    public static string ReviewsToJson(IList<VocabReview> reviews)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < (reviews?.Count ?? 0); i++)
        {
            var r = reviews[i];
            if (i > 0) sb.Append(",\n");
            sb.Append('{');
            Prop(sb, "word", r.Word, true); Prop(sb, "rating", r.Rating); Prop(sb, "at", (DateTime?)r.At);
            Prop(sb, "elapsedDays", r.ElapsedDays); Prop(sb, "stability", r.Stability); Prop(sb, "difficulty", r.Difficulty);
            sb.Append(",\"isNew\":").Append(r.IsNew ? "true" : "false");
            sb.Append('}');
        }
        return sb.Append(']').ToString();
    }

    /// <summary>履歴として読めるか (空のファイルは「履歴なし」として読める)。読めなければ false</summary>
    public static bool TryReviewsFromJson(string json, out List<VocabReview> reviews)
    {
        reviews = new List<VocabReview>();
        if (string.IsNullOrWhiteSpace(json)) return true;
        try { if (!(MiniJson.Parse(json) is List<object>)) return false; }
        catch (FormatException) { return false; }
        reviews = ReviewsFromJson(json);
        return true;
    }

    public static List<VocabReview> ReviewsFromJson(string json) =>
        Rows(json).Select(d => new VocabReview
        {
            Word = Str(d, "word"),
            Rating = (int)Num(d, "rating"),
            At = Date(d, "at") ?? DateTime.MinValue,
            ElapsedDays = Num(d, "elapsedDays"),
            Stability = Num(d, "stability"),
            Difficulty = Num(d, "difficulty"),
            IsNew = d.TryGetValue("isNew", out object b) && b is bool x && x,
        }).Where(r => r.Word.Length > 0).ToList();

    // ---------------- 辞書の結果 ----------------
    public static string DictToJson(DictionaryResult r)
    {
        var sb = new StringBuilder("{\"found\":").Append(r != null && r.Found ? "true" : "false");
        sb.Append(",\"suggestions\":").Append(StrArray(r?.Suggestions));
        sb.Append(",\"entries\":[");
        var entries = r?.Entries ?? new List<DictEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (i > 0) sb.Append(',');
            sb.Append('{');
            Prop(sb, "headword", e.Headword, true); Prop(sb, "pos", e.Pos); Prop(sb, "ipa", e.Ipa); Prop(sb, "audioUrl", e.AudioUrl);
            sb.Append(",\"definitions\":").Append(StrArray(e.Definitions));
            sb.Append(",\"examples\":").Append(StrArray(e.Examples));
            sb.Append('}');
        }
        return sb.Append("]}").ToString();
    }

    public static DictionaryResult DictFromJson(string json)
    {
        try
        {
            if (!(MiniJson.Parse(json ?? "") is Dictionary<string, object> d)) return null;
            return new DictionaryResult
            {
                Found = d.TryGetValue("found", out object f) && f is bool b && b,
                Suggestions = Strings(d, "suggestions"),
                Entries = (d.TryGetValue("entries", out object es) && es is List<object> list ? list : new List<object>())
                    .OfType<Dictionary<string, object>>()
                    .Select(e => new DictEntry
                    {
                        Headword = Str(e, "headword"), Pos = Str(e, "pos"), Ipa = Str(e, "ipa"), AudioUrl = Str(e, "audioUrl"),
                        Definitions = Strings(e, "definitions"), Examples = Strings(e, "examples"),
                    }).ToList(),
            };
        }
        catch (FormatException) { return null; }
    }

    // ---------------- 部品 ----------------
    private static IEnumerable<Dictionary<string, object>> Rows(string json)
    {
        object v;
        try { v = MiniJson.Parse(json ?? ""); }
        catch (FormatException) { return Enumerable.Empty<Dictionary<string, object>>(); }
        return v is List<object> list ? list.OfType<Dictionary<string, object>>() : Enumerable.Empty<Dictionary<string, object>>();
    }

    private static string Str(Dictionary<string, object> d, string k, string fallback = "") =>
        d.TryGetValue(k, out object v) && v is string s ? s : fallback;

    private static double Num(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out object v) && v is double x && !double.IsNaN(x) ? x : 0;

    private static DateTime? Date(Dictionary<string, object> d, string k)
    {
        if (!d.TryGetValue(k, out object v) || !(v is string s) || s.Length == 0) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
            ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : (DateTime?)null;
    }

    private static List<string> Strings(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out object v) && v is List<object> list ? list.OfType<string>().ToList() : new List<string>();

    private static string Quote(string s) => "\"" + HttpUtil.JsonEscape(s ?? "") + "\"";

    private static string StrArray(IEnumerable<string> items) =>
        "[" + string.Join(",", (items ?? Enumerable.Empty<string>()).Select(Quote)) + "]";

    private static void Prop(StringBuilder sb, string name, string value, bool first = false) =>
        sb.Append(first ? "" : ",").Append('"').Append(name).Append("\":").Append(Quote(value));

    private static void Prop(StringBuilder sb, string name, double value) =>
        sb.Append(",\"").Append(name).Append("\":").Append(value.ToString("R", CultureInfo.InvariantCulture));

    private static void Prop(StringBuilder sb, string name, int value) =>
        sb.Append(",\"").Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

    private static void Prop(StringBuilder sb, string name, DateTime? value) =>
        sb.Append(",\"").Append(name).Append("\":")
          .Append(value.HasValue ? Quote(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture)) : "null");
}
