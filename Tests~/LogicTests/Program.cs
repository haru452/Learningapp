// Unity を使わない部分 (ミスの種類・言い直しの照合など) のテスト。
// Unity はフォルダ名が "~" で終わるものを読まないので、ここに置いてもビルドには入らない。
// 実行: Roslyn (csc) で、このファイルとテスト対象の .cs をまとめてコンパイルして実行する。
// 最後に "ALL LOGIC TESTS PASSED" と出れば成功 (失敗すると終了コード 1)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public static class LogicTests
{
    private static int _failed;

    private static void Check(bool ok, string what)
    {
        if (ok) return;
        _failed++;
        Console.WriteLine("FAILED: " + what);
    }

    private static Mistake M(string type) => new Mistake { type = type, wrong = "w", @fixed = "f" };

    public static int Main()
    {
        MistakeTypeTests();
        ReadAloudTests();
        SchemaTests();
        TopicCatalogTests();
        MistakesJsonTests();

        if (_failed > 0)
        {
            Console.WriteLine(_failed + " test(s) failed");
            return 1;
        }
        Console.WriteLine("ALL LOGIC TESTS PASSED");
        return 0;
    }

    private static void MistakeTypeTests()
    {
        Check(MistakeTypes.All.Length == 10, "10 mistake types");
        Check(MistakeTypes.Normalize(" Tense ") == "tense", "normalize trims and lowercases");
        Check(MistakeTypes.Normalize("spelling") == "other", "unknown -> other");
        Check(MistakeTypes.Normalize(null) == "other", "null -> other");
        Check(MistakeTypes.Label("verb_form") == "Verb forms", "label");
        Check(MistakeTypes.Label("nonsense") == "Other", "unknown label");
        var c = MistakeTypes.Count(new[] { M("tense"), M("TENSE"), M("xyz") });
        Check(c["tense"] == 2 && c["other"] == 1, "count normalizes types");
        Check(MistakeTypes.Count(null).Count == 0, "count of null is empty");
    }

    private static void ReadAloudTests()
    {
        var r = ReadAloudMatcher.Match("I don't like it.", "i do not like it");
        Check(r.AccuracyPercent == 100 && r.Words.Count == 4 && r.Words[1].Text == "don't" && r.Words.TrueForAll(w => w.Matched),
            "contraction matches its expansion");

        r = ReadAloudMatcher.Match("I went to the park yesterday.", "I went the park yesterday");
        Check(r.AccuracyPercent == 83 && !r.Words[2].Matched && r.Words[5].Text == "yesterday.", "missing word: 5/6");

        r = ReadAloudMatcher.Match("She has two cats.", "she has two cat");
        Check(r.AccuracyPercent == 75 && !r.Words[3].Matched, "wrong word form: 3/4");

        r = ReadAloudMatcher.Match("Hello, world!", "hello hello world");
        Check(r.AccuracyPercent == 100, "extra words are not penalized");

        r = ReadAloudMatcher.Match("I’m fine.", "I am fine");
        Check(r.AccuracyPercent == 100, "curly apostrophe");

        Check(ReadAloudMatcher.Match("Hi there.", "").AccuracyPercent == 0, "nothing heard -> 0");
        Check(ReadAloudMatcher.Match("Hi there.", null).AccuracyPercent == 0, "null heard -> 0");
        var empty = ReadAloudMatcher.Match("", "anything");
        Check(empty.Words.Count == 0 && empty.AccuracyPercent == 0, "empty expected");
    }

    // Gemini に渡す JSON スキーマが正しい JSON で、決めた形になっていること
    private static void SchemaTests()
    {
        var schemas = new (string name, string json)[]
        {
            ("diary", GeminiApiClient.DiarySchema),
            ("summary", GeminiApiClient.SummaryGradeSchema),
            ("topicGrade", GeminiApiClient.TopicGradeSchema),
            ("topicPrompt", GeminiApiClient.TopicPromptSchema),
            ("pronunciation", GeminiApiClient.PronunciationSchema),
        };
        foreach (var (name, json) in schemas)
        {
            try { JsonDocument.Parse(json); }
            catch (Exception e) { Check(false, name + " schema is not valid JSON: " + e.Message); }
        }

        var grade = JsonDocument.Parse(GeminiApiClient.TopicGradeSchema).RootElement;
        var props = grade.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Check(props.Take(5).SequenceEqual(new[] { "goodPoints", "fixPoints", "contentScore", "organizationScore", "languageScore" }),
            "topic grade: reasons come before scores");
        var order = grade.GetProperty("propertyOrdering").EnumerateArray().Select(e => e.GetString()).ToList();
        Check(order.SequenceEqual(props), "topic grade: propertyOrdering matches properties");

        foreach (var (name, json) in new[] { ("diary", GeminiApiClient.DiarySchema), ("summary", GeminiApiClient.SummaryGradeSchema), ("topicGrade", GeminiApiClient.TopicGradeSchema) })
        {
            var root = JsonDocument.Parse(json).RootElement;
            bool has = root.GetProperty("properties").TryGetProperty("mistakes", out var m);
            Check(has, name + ": has mistakes");
            if (!has) continue;
            var types = m.GetProperty("items").GetProperty("properties").GetProperty("type").GetProperty("enum")
                .EnumerateArray().Select(e => e.GetString()).ToArray();
            Check(types.SequenceEqual(MistakeTypes.All), name + ": mistake type enum matches MistakeTypes.All");
        }

        var prompt = JsonDocument.Parse(GeminiApiClient.TopicPromptSchema).RootElement.GetProperty("properties");
        Check(prompt.TryGetProperty("kind", out _) && prompt.TryGetProperty("topic", out _) && prompt.TryGetProperty("hint", out _),
            "topic prompt: kind/topic/hint");
        Check(GeminiApiClient.TopicContentMax + GeminiApiClient.TopicOrganizationMax + GeminiApiClient.TopicLanguageMax == 100
              && GeminiApiClient.TopicContentMax == 40, "topic score split 40/30/30");
    }

    private static void TopicCatalogTests()
    {
        Check(TopicCatalog.SecondsFor("daily") == 45, "daily 45s");
        Check(TopicCatalog.SecondsFor("exam") == 60, "exam 60s");
        Check(TopicCatalog.SecondsFor("work") == 60, "work 60s");
        Check(TopicCatalog.SecondsFor("??") == 60, "unknown 60s");
    }

    // クラウドに送るミスの JSON (jsonb としてそのまま RPC の本文に入れる)
    private static void MistakesJsonTests()
    {
        var one = new[] { new Mistake { type = "tense", wrong = "I go", @fixed = "I went" } };
        Check(MistakeTypes.ToJsonArray(one) == "[{\"type\":\"tense\",\"wrong\":\"I go\",\"fixed\":\"I went\"}]", "one mistake json");
        Check(MistakeTypes.ToJsonArray(null) == "[]", "null -> []");
        Check(MistakeTypes.ToJsonArray(new Mistake[0]) == "[]", "empty -> []");

        var tricky = new[] { new Mistake { type = "Other", wrong = "say \"hi\"\nnow", @fixed = "back\\slash" }, null };
        string json = MistakeTypes.ToJsonArray(tricky);
        var parsed = JsonDocument.Parse(json).RootElement;
        Check(parsed.GetArrayLength() == 1, "null items skipped");
        Check(parsed[0].GetProperty("wrong").GetString() == "say \"hi\"\nnow", "quotes and newline escaped");
        Check(parsed[0].GetProperty("fixed").GetString() == "back\\slash", "backslash escaped");
        Check(parsed[0].GetProperty("type").GetString() == "other", "type normalized");
    }
}
