// 短い答えの練習と、単語帳の発音の記録の JSON のテスト。Program.cs の Main から呼ぶ。
using System;
using System.Collections.Generic;
using System.Linq;

public static partial class LogicTests
{
    private static void ShortAnswerTests()
    {
        var key = new[] { "key", "keys" };
        Check(ShortAnswerJudge.IsCorrect("A key.", key), "answer: a key");
        Check(ShortAnswerJudge.IsCorrect("you use keys", key), "answer: keys in a sentence");
        Check(ShortAnswerJudge.IsCorrect("The KEY!", key), "answer: case and article");
        Check(!ShortAnswerJudge.IsCorrect("monkey", key), "answer: not part of another word");
        Check(!ShortAnswerJudge.IsCorrect("", key) && !ShortAnswerJudge.IsCorrect(null, key), "answer: empty is wrong");
        Check(ShortAnswerJudge.IsCorrect("there are 4", new[] { "four" }), "answer: digit = word");
        Check(ShortAnswerJudge.IsCorrect("glasses", new[] { "glass" }) && ShortAnswerJudge.IsCorrect("boxes", new[] { "box" })
              && ShortAnswerJudge.IsCorrect("babies", new[] { "baby" }), "answer: plural forms");
        Check(ShortAnswerJudge.IsCorrect("I think a fire station", new[] { "fire station" }), "answer: two words in order");
        Check(!ShortAnswerJudge.IsCorrect("station of fire", new[] { "fire station" }), "answer: two words wrong order");
        Check(!ShortAnswerJudge.IsCorrect("door", null), "answer: no answers -> wrong");

        string json = "[{\"question\":\" What do you use to open a door? \",\"answers\":[\"Key\",\"keys\",\"key\",\" \"]}," +
                      "{\"question\":\"what do you use to open a door?\",\"answers\":[\"key\"]}," +
                      "{\"question\":\"\",\"answers\":[\"x\"]},{\"question\":\"No answers\",\"answers\":[]},\"bad\"]";
        var qs = GeminiApiClient.ParseShortAnswerQuestions(json);
        Check(qs.Count == 1, "questions: one kept (" + qs.Count + ")");
        Check(qs.Count == 1 && qs[0].Question == "What do you use to open a door?" && qs[0].Answers.SequenceEqual(new[] { "key", "keys" }),
              "questions: trimmed, lowercased, distinct");
        Check(GeminiApiClient.ParseShortAnswerQuestions("not json").Count == 0, "questions: bad json -> empty");

        string items = PracticeJson.ShortAnswerItems(new List<ShortAnswerResult>
        {
            new ShortAnswerResult { Question = "Say \"hi\"?", Answers = new List<string> { "hi" }, Heard = "hi", Correct = true },
            null,
        });
        Check(items == "[{\"question\":\"Say \\\"hi\\\"?\",\"answers\":[\"hi\"],\"heard\":\"hi\",\"correct\":true}]", "answer json: " + items);

        var pron = PracticeJson.VocabPronItems(new List<GeminiApiClient.WordPronResult>
        {
            new GeminiApiClient.WordPronResult { Word = "think", Rating = "close", Advice = "舌を出す", Sounds = new List<string> { "th" } },
            new GeminiApiClient.WordPronResult { Word = "apple", Rating = "good", Sounds = new List<string>() },
        }, new Dictionary<string, string> { ["think"] = "sink" });
        Check(pron == "[{\"word\":\"think\",\"rating\":\"close\",\"sounds\":[\"th\"],\"advice\":\"舌を出す\",\"heard\":\"sink\"}]",
              "vocab pron json: only words with sounds " + pron);
        Check(PracticeJson.VocabPronItems(null, null) == "[]", "vocab pron json: null -> []");
        Check(PracticeJson.Utc(new DateTime(2026, 11, 10, 3, 4, 5, DateTimeKind.Utc)) == "2026-11-10T03:04:05Z", "utc format");

        // 作り置き: 取り出し・戻し・保存の形
        ShortAnswerQuestion Q(string text) => new ShortAnswerQuestion { Question = text, Answers = new List<string> { "a" } };
        var stock = new List<ShortAnswerQuestion> { Q("q1"), Q("q2"), Q("q3") };
        var (taken, rest) = ShortAnswerStock.Split(stock, 2);
        Check(taken.Select(q => q.Question).SequenceEqual(new[] { "q1", "q2" }) && rest.Single().Question == "q3", "stock: take 2, keep 1");
        var (all, none) = ShortAnswerStock.Split(stock, 10);
        Check(all.Count == 3 && none.Count == 0, "stock: take more than stored");
        var merged = ShortAnswerStock.Merge(new[] { Q("Q2"), Q("q4") }, stock);
        Check(merged.Select(q => q.Question).SequenceEqual(new[] { "Q2", "q4", "q1", "q3" }), "stock: put back first, no duplicates");
        var back = GeminiApiClient.ParseShortAnswerQuestions(ShortAnswerStock.ToJson(new[]
            { new ShortAnswerQuestion { Question = "What's \"it\"?", Answers = new List<string> { "key", "keys" } } }));
        Check(back.Count == 1 && back[0].Question == "What's \"it\"?" && back[0].Answers.SequenceEqual(new[] { "key", "keys" }),
              "stock: saved json reads back");
        Check(ShortAnswerStock.IsBusy("HTTP 503 : UNAVAILABLE high demand") && !ShortAnswerStock.IsBusy("HTTP 400") && !ShortAnswerStock.IsBusy(null),
              "stock: busy error detected");
    }
}
