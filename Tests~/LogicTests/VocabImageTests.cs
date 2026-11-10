// 単語カードの裏の画像 (Pixabay) のテスト。Program.cs の Main から呼ぶ。
using System;

public static partial class LogicTests
{
    private static void VocabImageTests()
    {
        string json = "{\"total\":2,\"hits\":[{\"id\":1,\"webformatURL\":\"https://pixabay.com/get/a_640.jpg\",\"previewURL\":\"p1\"}," +
                      "{\"id\":2,\"webformatURL\":\"https://pixabay.com/get/b_640.jpg\"}]}";
        Check(PixabayClient.PickImageUrl(json) == "https://pixabay.com/get/a_640.jpg", "pixabay pick: first hit");
        Check(PixabayClient.PickImageUrl("{\"total\":0,\"hits\":[]}") == null, "pixabay pick: no hits -> null");
        Check(PixabayClient.PickImageUrl("{\"hits\":[{\"id\":1}]}") == null, "pixabay pick: no url -> null");
        Check(PixabayClient.PickImageUrl("not json") == null && PixabayClient.PickImageUrl(null) == null, "pixabay pick: bad json -> null");

        string url = PixabayClient.SearchUrl("K&1", "look after");
        Check(url.StartsWith("https://pixabay.com/api/?key=K%261&q=look%20after"), "pixabay url: key and word escaped " + url);
        Check(url.Contains("safesearch=true") && url.Contains("lang=en"), "pixabay url: safe search, english");

        var now = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        Check(VocabImageStore.NoneIsFresh(now.AddDays(-29), now), "image none: 29 days -> still none");
        Check(!VocabImageStore.NoneIsFresh(now.AddDays(-31), now), "image none: 31 days -> ask again");

        // Settings の「今日の動画の検索」
        var day = new DateTime(2026, 11, 10, 12, 0, 0);
        Check(VocabSearchLimitRow.Describe(new VocabClipSearchLog { Date = "2026-11-10", Count = 29 }, day) == "Video searches today: 29 / 30",
              "limit row: today's count");
        Check(VocabSearchLimitRow.Describe(new VocabClipSearchLog { Date = "2026-11-09", Count = 30 }, day) == "Video searches today: 0 / 30",
              "limit row: yesterday's count is 0");
        Check(VocabSearchLimitRow.Describe(new VocabClipSearchLog { Date = "2026-11-10", Count = 3, BlockedDate = "2026-11-10" }, day)
              .Contains("YouTube's daily limit"), "limit row: blocked note");

        // 前の単語に戻る: 直前の評価を取り消し、予定も元に戻す
        var t0 = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var known = new VocabCard { Word = "old", Status = VocabCard.Known, Reps = 3, Stability = 5, Difficulty = 4, DueAt = t0, LastReviewAt = t0.AddDays(-5) };
        var fresh = new VocabCard { Word = "new" };
        var ses = new VocabSession(new System.Collections.Generic.List<VocabCard> { known, fresh, new VocabCard { Word = "x" } }, 0.9);
        Check(!ses.CanGoBack && ses.Back() == null, "back: nothing to undo at first");
        ses.Rate(1, t0);
        Check(known.Status == VocabCard.Learning && known.MissCount == 1 && ses.CanGoBack, "back: rated old as didn't know");
        var backTo = ses.Back();
        Check(backTo == known && ses.Index == 0 && known.Status == VocabCard.Known && known.Reps == 3 && known.MissCount == 0
              && known.DueAt == t0 && known.Stability == 5, "back: old card restored");
        ses.Rate(3, t0); ses.Rate(4, t0);
        Check(ses.Back() == fresh && fresh.Reps == 0 && fresh.Status == VocabCard.Learning && !fresh.KnownAt.HasValue, "back: new card restored");
        Check(ses.Back() == known && ses.Index == 0, "back: twice in a row");
        ses.Rate(3, t0); ses.Rate(3, t0); ses.Rate(3, t0);
        Check(ses.Done && !ses.CanGoBack, "back: not after the last card (summary)");

        // 例文: 名詞と動詞がある語は、両方の例文を出す
        var dict = new DictionaryResult { Found = true, Entries = new System.Collections.Generic.List<DictEntry>
        {
            new DictEntry { Pos = "noun", Examples = { "a long walk", "an evening walk", "a third one" } },
            new DictEntry { Pos = "verb", Examples = { "I walk to school.", "a long walk" } },
            new DictEntry { Pos = "noun", Examples = { "more noun" } },
            new DictEntry { Pos = "adverb" },
        } };
        var groups = VocabContext.ExamplesByPos(dict);
        Check(groups.Count == 2 && groups[0].pos == "noun" && groups[1].pos == "verb", "examples: noun and verb groups, empty pos dropped");
        Check(groups[0].examples.Count == 2 && groups[1].examples.Count == 2 && groups[1].examples[0] == "I walk to school.",
              "examples: 2 per part of speech");
        Check(VocabContext.ExamplesByPos(null).Count == 0 && VocabContext.ExamplesByPos(new DictionaryResult { Found = false }).Count == 0,
              "examples: no dictionary -> none");
    }
}
