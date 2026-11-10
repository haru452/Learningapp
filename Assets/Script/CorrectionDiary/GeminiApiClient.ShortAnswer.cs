using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// GeminiApiClient の続き: 短い答えの練習 (Versant の Short Answer Questions) の問題を作る。
/// </summary>
public partial class GeminiApiClient
{
    public const int ShortAnswerCount = 10;

    private const string ShortAnswerPrompt =
        "Create {0} short-answer questions like the \"Short Answer Questions\" part of the Versant English Test. " +
        "Each question is one short spoken question (5 to 14 words) about everyday common knowledge, " +
        "answered with one word or a very short phrase. Mix topics (objects, places, people, time, numbers, food, body, nature, " +
        "jobs, opposites). Keep vocabulary easy (CEFR A2-B1). Example: \"What do you use to open a door?\" -> key.\n" +
        "For each item return:\n" +
        "- question: the question, as it will be read aloud\n" +
        "- answers: 2 to 6 accepted answers in lowercase without articles: the main answer, its plural or singular, " +
        "and common synonyms (for example [\"key\", \"keys\"]). Numbers as words.\n" +
        "Every question must have a clear, single best answer. Do not repeat these recent questions:\n{1}";

    public const string ShortAnswerSchema =
        "{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\",\"properties\":{" +
        "\"question\":{\"type\":\"STRING\"}," +
        "\"answers\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\"}}}," +
        "\"required\":[\"question\",\"answers\"]}}";

    /// <summary>問題を作る。recent は最近出した問題 (重ならないように Gemini に伝える)</summary>
    public async Task<List<ShortAnswerQuestion>> GenerateShortAnswerQuestionsAsync(IList<string> recent, int count = ShortAnswerCount)
    {
        string avoid = string.Join("\n", (recent ?? new List<string>()).Take(60).Select(q => "- " + q));
        string prompt = string.Format(ShortAnswerPrompt, count, avoid.Length > 0 ? avoid : "(none)");
        string json = await GeneratePartsAsync(TextPart(prompt), ShortAnswerSchema, "\"temperature\":1.0");
        var list = ParseShortAnswerQuestions(json);
        if (list.Count == 0) throw new Exception("Gemini returned no questions: " + json);
        return list.Take(count).ToList();
    }

    /// <summary>問題の JSON を読む。問題文か答えが空のもの・同じ問題は捨てる。答えは小文字・重なりなし</summary>
    public static List<ShortAnswerQuestion> ParseShortAnswerQuestions(string json)
    {
        var result = new List<ShortAnswerQuestion>();
        object root;
        try { root = MiniJson.Parse(json); }
        catch (Exception) { return result; }
        if (!(root is List<object> items)) return result;
        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            if (!(item is Dictionary<string, object> d)) continue;
            string q = JsonGet.Str(d, "question").Trim();
            var answers = (d.TryGetValue("answers", out var a) ? a as List<object> : null) ?? new List<object>();
            var list = answers.OfType<string>().Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0)
                              .Distinct().Take(8).ToList();
            if (q.Length == 0 || list.Count == 0 || !seen.Add(q.ToLowerInvariant())) continue;
            result.Add(new ShortAnswerQuestion { Question = q, Answers = list });
        }
        return result;
    }
}
