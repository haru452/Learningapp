// 単語帳 (Vocab) のテスト。Program.cs の Main から VocabTests() を呼ぶ。
using System;
using System.Collections.Generic;
using System.Linq;

public static partial class LogicTests
{
    private static void VocabTests()
    {
        VocabFsrsTests();
        VocabDictionaryTests();
        VocabQueueTests();
        VocabPronTests();
        CefrTests();
        VocabLevelTests();
        VocabJsonTests();
        CefrBundledTests();
        VocabGeminiTests();
        VocabDictionaryProblemTests();
        VocabLoadTests();
    }

    private static bool Near(double a, double b, double eps = 1e-9) => Math.Abs(a - b) <= eps;

    // ---------------- FSRS ----------------
    private static void VocabFsrsTests()
    {
        var w = Fsrs.W;
        Check(w.Length == 17 && Near(w[0], 0.4872) && Near(w[16], 2.8755), "fsrs: 17 default parameters (FSRS-4.5)");
        for (int g = 1; g <= 4; g++)
            Check(Near(Fsrs.First(g).Stability, w[g - 1]), "fsrs: first S for rating " + g);
        Check(Near(Fsrs.First(3).Difficulty, w[4]), "fsrs: first D for Good = w4");
        Check(Near(Fsrs.First(1).Difficulty, Math.Min(10, w[4] + 2 * w[5])), "fsrs: first D for Again");
        Check(Near(Fsrs.First(4).Difficulty, w[4] - w[5]), "fsrs: first D for Easy");

        foreach (double s in new[] { 3.7145, 13.8206, 100 })
            Check(Math.Abs(Fsrs.IntervalDays(s, 0.9) - s) <= 1, "fsrs: r=0.9 -> I ~= S (" + s + " -> " + Fsrs.IntervalDays(s, 0.9) + ")");
        Check(Fsrs.IntervalDays(0.01, 0.9) == 1, "fsrs: interval at least 1 day");
        Check(Fsrs.IntervalDays(1e9, 0.9) == 36500, "fsrs: interval at most 36500 days");
        Check(Fsrs.IntervalDays(10, 0.8) > Fsrs.IntervalDays(10, 0.95), "fsrs: lower retention -> longer interval");
        Check(Near(Fsrs.Retrievability(0, 5), 1), "fsrs: R(0) = 1");
        Check(Fsrs.Retrievability(10, 5) < Fsrs.Retrievability(1, 5), "fsrs: R falls over time");

        var m = new FsrsMemory { Stability = 10, Difficulty = 5 };
        double s2 = Fsrs.Next(m, 2, 10).Stability, s3 = Fsrs.Next(m, 3, 10).Stability, s4 = Fsrs.Next(m, 4, 10).Stability;
        Check(s2 < s3 && s3 < s4, $"fsrs: Hard < Good < Easy ({s2:F2} {s3:F2} {s4:F2})");
        Check(Fsrs.Next(m, 1, 10).Stability < 10, "fsrs: forgetting lowers S");
        foreach (double d in new[] { 1.0, 5.0, 10.0 })
            for (int g = 1; g <= 4; g++)
            {
                double nd = Fsrs.Next(new FsrsMemory { Stability = 3, Difficulty = d }, g, 2).Difficulty;
                Check(nd >= 1 && nd <= 10, $"fsrs: D stays 1..10 (D={d}, G={g} -> {nd})");
            }
        var neg = Fsrs.Next(m, 3, -3);
        var zero = Fsrs.Next(m, 3, 0);
        Check(Near(neg.Stability, zero.Stability) && Near(neg.Difficulty, zero.Difficulty) && !double.IsNaN(neg.Stability),
            "fsrs: negative elapsed days counted as 0");

        int p2 = Fsrs.PreviewDays(null, 2, 0, 0.9), p3 = Fsrs.PreviewDays(null, 3, 0, 0.9), p4 = Fsrs.PreviewDays(null, 4, 0, 0.9);
        Check(Fsrs.PreviewDays(null, 1, 0, 0.9) == 0, "fsrs: preview for Didn't know = next time (0)");
        Check(p2 < p3 && p3 < p4, $"fsrs: preview grows Hard < Good < Easy ({p2} {p3} {p4})");
        Check(Fsrs.PreviewDays(m, 3, 10, 0.9) == Fsrs.IntervalDays(s3, 0.9), "fsrs: preview of a review uses Next");

        var labels = new (int days, string text)[]
        {
            (0, "Next time"), (1, "in 1 day"), (4, "in 4 days"), (30, "in 1 month"), (45, "in 2 months"),
            (60, "in 2 months"), (400, "in 1 year"), (800, "in 2 years"),
        };
        foreach (var (days, text) in labels)
            Check(Fsrs.IntervalLabel(days) == text, "fsrs label " + days + " -> " + Fsrs.IntervalLabel(days));
    }
    // ---------------- 辞書 (Merriam-Webster Learner's) ----------------
    private const string MwApple =
        "[{\"meta\":{\"id\":\"apple pie\"},\"hwi\":{\"hw\":\"apple pie\"},\"fl\":\"noun\",\"shortdef\":[\"a pie\"]}," +
        "{\"meta\":{\"id\":\"apple:1\",\"stems\":[\"apple\"]},\"hwi\":{\"hw\":\"ap*ple\",\"prs\":[{\"ipa\":\"ˈæpəl\",\"sound\":{\"audio\":\"apple001\"}}]}," +
        "\"fl\":\"noun\",\"shortdef\":[\"a round fruit\",\"the tree\"]," +
        "\"def\":[{\"sseq\":[[[\"sense\",{\"dt\":[[\"text\",\"{bc}a fruit\"],[\"vis\",[{\"t\":\"an {it}apple{/it} a day\"},{\"t\":\"{wi}apples{/wi} are red\"},{\"t\":\"third\"}]]]}]]]}]}]";

    private static void VocabDictionaryTests()
    {
        var r = MerriamWebsterParser.Parse(MwApple, "apple");
        Check(r.Found && r.Entries.Count == 1, "mw: only the entry whose id is the word");
        if (r.Entries.Count == 1)
        {
            var e = r.Entries[0];
            Check(e.Headword == "apple", "mw: headword without * (" + e.Headword + ")");
            Check(e.Pos == "noun" && e.Ipa == "ˈæpəl", "mw: pos and ipa");
            Check(e.Definitions.SequenceEqual(new[] { "a round fruit", "the tree" }), "mw: shortdef");
            Check(e.Examples.SequenceEqual(new[] { "an apple a day", "apples are red" }), "mw: 2 examples from vis, cleaned");
            Check(e.AudioUrl == "https://media.merriam-webster.com/audio/prons/en/us/mp3/a/apple001.mp3", "mw: audio url " + e.AudioUrl);
        }
        var other = MerriamWebsterParser.Parse(MwApple, "pie");
        Check(other.Found && other.Entries.Count == 1 && other.Entries[0].Headword == "apple pie", "mw: no match -> first entry");

        var sug = MerriamWebsterParser.Parse("[\"apply\",\"apple\"]", "appel");
        Check(!sug.Found && sug.Suggestions.SequenceEqual(new[] { "apply", "apple" }), "mw: strings -> suggestions");
        var none = MerriamWebsterParser.Parse("[]", "zzz");
        Check(!none.Found && none.Suggestions.Count == 0 && none.Entries.Count == 0, "mw: [] -> not found");

        Check(MerriamWebsterParser.Clean("{bc}a {it}red{/it} fruit {ldquo}x{rdquo} {sx|pome||}") == "a red fruit “x” pome",
            "mw: clean markup -> " + MerriamWebsterParser.Clean("{bc}a {it}red{/it} fruit {ldquo}x{rdquo} {sx|pome||}"));
        Check(MerriamWebsterParser.Clean("see {a_link|run} and {d_link|walk|walk:1}") == "see run and walk", "mw: links keep the word");

        const string Base = "https://media.merriam-webster.com/audio/prons/en/us/mp3/";
        Check(MerriamWebsterParser.AudioUrl("bixapple01") == Base + "bix/bixapple01.mp3", "mw: bix folder");
        Check(MerriamWebsterParser.AudioUrl("ggask01") == Base + "gg/ggask01.mp3", "mw: gg folder");
        Check(MerriamWebsterParser.AudioUrl("3d00001") == Base + "number/3d00001.mp3", "mw: number folder");
        Check(MerriamWebsterParser.AudioUrl("_aaa") == Base + "number/_aaa.mp3", "mw: symbol -> number folder");
        Check(MerriamWebsterParser.AudioUrl("") == "", "mw: no audio -> empty");

        var bare = MerriamWebsterParser.Parse("[{\"meta\":{\"id\":\"go\"},\"shortdef\":[]}]", "go");
        Check(bare.Found && bare.Entries.Count == 1 && bare.Entries[0].Ipa == "" && bare.Entries[0].AudioUrl == "" && bare.Entries[0].Pos == ""
              && bare.Entries[0].Headword == "go", "mw: no hwi/prs/fl -> empty fields");
        // 派生語 (calm の中の calmness): 元の語の音声・発音記号は使わない
        const string MwCalm =
            "[{\"meta\":{\"id\":\"calm:1\"},\"hwi\":{\"hw\":\"calm\",\"prs\":[{\"ipa\":\"ˈkɑːm\",\"sound\":{\"audio\":\"calm0001\"}}]}," +
            "\"fl\":\"adjective\",\"shortdef\":[\"not angry\"],\"uros\":[{\"ure\":\"calm*ly\",\"fl\":\"adverb\"}," +
            "{\"ure\":\"calm*ness\",\"fl\":\"noun\",\"utxt\":[[\"vis\",[{\"t\":\"a feeling of {it}calmness{/it}\"}]]]}]}]";
        var calmness = MerriamWebsterParser.Parse(MwCalm, "calmness");
        Check(calmness.Found && calmness.Entries.Count == 1, "mw: run-on word found");
        if (calmness.Entries.Count == 1)
        {
            var e = calmness.Entries[0];
            Check(e.Headword == "calmness" && e.Pos == "noun", "mw: run-on headword and pos (" + e.Headword + " " + e.Pos + ")");
            Check(e.AudioUrl == "" && e.Ipa == "", "mw: run-on without prs -> no audio of the base word (" + e.AudioUrl + ")");
            Check(e.Definitions.SequenceEqual(new[] { "from \"calm\" (adjective):", "not angry" }), "mw: run-on definitions from the base word");
            Check(e.Examples.SequenceEqual(new[] { "a feeling of calmness" }), "mw: run-on examples from utxt");
        }
        var withPron = MerriamWebsterParser.Parse(MwCalm.Replace("\"fl\":\"adverb\"}", "\"fl\":\"adverb\",\"prs\":[{\"ipa\":\"ˈkɑːmli\",\"sound\":{\"audio\":\"calm0002\"}}]}"), "calmly");
        Check(withPron.Entries.Count == 1 && withPron.Entries[0].AudioUrl.EndsWith("/c/calm0002.mp3") && withPron.Entries[0].Ipa == "ˈkɑːmli",
            "mw: run-on with its own prs -> its own audio");
        Check(MerriamWebsterParser.Parse(MwCalm, "calm").Entries[0].AudioUrl.EndsWith("/c/calm0001.mp3"), "mw: base word keeps its audio");

        bool threw = false;
        try { MerriamWebsterParser.Parse("<html>", "x"); } catch (FormatException) { threw = true; }
        Check(threw, "mw: html -> FormatException");
    }

    // ---------------- 出題の順・1 回分 ----------------
    private static readonly DateTime VNow = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static VocabCard Learning(string w, int minutesAgo) =>
        new VocabCard { Word = w, Status = "learning", CreatedAt = VNow.AddMinutes(-minutesAgo) };

    private static VocabCard Known(string w, double dueInDays) =>
        new VocabCard { Word = w, Status = "known", Stability = 5, Difficulty = 5, Reps = 1,
                        LastReviewAt = VNow.AddDays(-5), DueAt = VNow.AddDays(dueInDays), KnownAt = VNow.AddDays(-5) };

    private static void VocabQueueTests()
    {
        var deck = new List<VocabCard>
        {
            Known("due2", -2), Learning("l-new", 1), Known("future", 3), Known("due5", -5), Learning("l-old", 100), Known("due1", -1),
        };
        var (cards, newCount) = VocabQueue.Plan(deck, VNow, 10);
        Check(cards.Select(c => c.Word).SequenceEqual(new[] { "l-old", "l-new", "due5", "due2", "due1" }),
            "queue: learning (oldest first) -> due (oldest due first): " + string.Join(",", cards.Select(c => c.Word)));
        Check(newCount == 5, "queue: new words fill the rest (" + newCount + ")");

        var manyDue = Enumerable.Range(0, 51).Select(i => Known("d" + i, -1 - i)).ToList();
        var (c51, n51) = VocabQueue.Plan(manyDue, VNow, 10);
        Check(c51.Count == 10 && n51 == 0, "queue: over 50 due -> no new words");
        var fiftyDue = Enumerable.Range(0, 50).Select(i => Known("d" + i, -1 - i)).ToList();
        Check(VocabQueue.Plan(fiftyDue.Take(3).ToList(), VNow, 10).newCount == 7, "queue: 3 due -> 7 new");
        var manyLearning = Enumerable.Range(0, 12).Select(i => Learning("l" + i, i)).ToList();
        var (c12, n12) = VocabQueue.Plan(manyLearning, VNow, 10);
        Check(c12.Count == 10 && n12 == 0, "queue: 12 learning, size 10 -> 10 cards, no new");
        Check(VocabQueue.Plan(new List<VocabCard>(), VNow, 5).newCount == 5, "queue: empty deck -> all new");

        var list = CefrWordList.Parse("[{\"w\":\"apple\",\"pos\":\"noun\",\"cefr\":\"A1\"},{\"w\":\"book\",\"pos\":\"noun\",\"cefr\":\"A1\"}," +
                                      "{\"w\":\"cat\",\"pos\":\"noun\",\"cefr\":\"A1\"},{\"w\":\"dog\",\"pos\":\"noun\",\"cefr\":\"A1\"}," +
                                      "{\"w\":\"eagle\",\"pos\":\"noun\",\"cefr\":\"B1\"}]");
        var cand = VocabQueue.NewCandidates(list, "A1", new[] { "book", "cat" }, new Random(4));
        Check(cand.OrderBy(x => x).SequenceEqual(new[] { "apple", "dog" }), "queue: candidates skip words in the deck and other levels");
        Check(cand.SequenceEqual(VocabQueue.NewCandidates(list, "A1", new[] { "book", "cat" }, new Random(4))), "queue: same seed, same order");
        Check(VocabQueue.NewCandidates(list, "A1", new[] { "apple", "book", "cat", "dog" }, new Random(1)).Count == 0, "queue: none left -> empty");

        // 先に決めた新しい単語を先頭に出す
        var rnd = new List<string> { "dog", "apple", "cat" };
        var ord = VocabQueue.OrderCandidates(new List<string> { "cat", "dog" }, rnd, new[] { "book" });
        Check(ord.SequenceEqual(new[] { "cat", "dog", "apple" }), "order: nextNew first, then the rest: " + string.Join(",", ord));
        Check(VocabQueue.OrderCandidates(new List<string> { "cat", "dog" }, rnd, new[] { "cat" }).SequenceEqual(new[] { "dog", "apple" }),
            "order: nextNew word in the deck is dropped");
        Check(VocabQueue.OrderCandidates(new List<string> { "Cat", "cat", "dog" }, rnd, new string[0]).SequenceEqual(new[] { "cat", "dog", "apple" }),
            "order: no duplicates, keys normalized");
        Check(VocabQueue.OrderCandidates(new List<string>(), rnd, new string[0]).SequenceEqual(rnd), "order: empty nextNew -> same as random");
        Check(VocabQueue.OrderCandidates(null, rnd, null).SequenceEqual(rnd), "order: null nextNew -> same as random");
        Check(VocabQueue.OrderCandidates(new List<string> { "eagle", "dog" }, rnd, new string[0]).SequenceEqual(new[] { "dog", "apple", "cat" }),
            "order: nextNew word from another level is dropped");
        var rem = VocabQueue.RemainingNextNew(new List<string> { "cat", "dog", "eagle", "apple" }, new[] { "Cat" }, rnd);
        Check(rem.SequenceEqual(new[] { "dog", "apple" }), "remaining: minus used and minus not-candidates: " + string.Join(",", rem));
        Check(VocabQueue.RemainingNextNew(null, null, null).Count == 0, "remaining: null -> empty");
        var remNf = VocabQueue.RemainingNextNew(new List<string> { "cat", "dog", "apple" }, new[] { "cat" }, rnd, new[] { "Dog" });
        Check(remNf.SequenceEqual(new[] { "apple" }), "remaining: dictionary-miss word dropped: " + string.Join(",", remNf));

        // 1 回分: 評価してカードが変わる。同じ回では出し直さない
        var a = Learning("alpha", 10); var b = Learning("beta", 5);
        var session = new VocabSession(new List<VocabCard> { a, b }, 0.9);
        Check(session.Current == a && session.Count == 2 && !session.Done, "session: starts with the first card");
        var p = session.PreviewDays(VNow);
        Check(p.Length == 4 && p[0] == 0 && p[1] < p[2] && p[2] < p[3], "session: previews " + string.Join(",", p));
        var rv = session.Rate(1, VNow);
        Check(a.Status == "learning" && a.MissCount == 1 && a.DueAt == null && a.Reps == 1 && a.LastReviewAt == VNow,
            "session: Didn't know -> learning, miss +1, no due");
        Check(rv.Word == "alpha" && rv.Rating == 1 && rv.IsNew, "session: review of a new word");
        Check(session.Current == b, "session: moves on (not shown again)");
        session.Rate(3, VNow);
        Check(b.Status == "known" && b.DueAt > VNow && b.KnownAt == VNow, "session: Knew it -> known with due date");
        Check(session.Done && session.Current == null, "session: done after the last card");

        var k = Known("again", -1);
        DateTime firstKnown = k.KnownAt.Value;
        var s2 = new VocabSession(new List<VocabCard> { k }, 0.9);
        var rk = s2.Rate(4, VNow);
        Check(k.KnownAt == firstKnown && !rk.IsNew && Near(rk.ElapsedDays, 5), "session: second Known keeps KnownAt; elapsed 5 days");
    }

    // ---------------- 発音の判定 ----------------
    private static void VocabPronTests()
    {
        Check(VocabPronJudge.Judge("apple", "I said apple.") == "clear", "pron: word in the sentence -> clear");
        Check(VocabPronJudge.Judge("Apple", "APPLE!") == "clear", "pron: case and marks ignored");
        Check(VocabPronJudge.Judge("apple", "apples") == "close", "pron: plural is not the same -> close");
        Check(VocabPronJudge.Judge("banana", "bandana") == "close", "pron: 1 letter off -> close");
        Check(VocabPronJudge.Judge("banana", "orange") == "again", "pron: different word -> again");
        Check(VocabPronJudge.Judge("banana", "") == "again", "pron: nothing heard -> again");
        Check(VocabPronJudge.Judge("ice cream", "I like ice cream") == "clear", "pron: two words -> clear");
        Check(VocabPronJudge.Judge("ice cream", "I like ice scream") == "close", "pron: two words, close");
        Check(VocabPronJudge.Judge("go", "no") == "close", "pron: short word allows 1 letter");
        Check(VocabPronJudge.Better("close", "clear") == "clear" && VocabPronJudge.Better("", "again") == "again"
              && VocabPronJudge.Better("clear", "again") == "clear", "pron: better result");
    }

    // ---------------- CEFR-J ----------------
    private static void CefrTests()
    {
        var list = CefrWordList.Parse("[{\"w\":\"light\",\"pos\":\"noun\",\"cefr\":\"A2\"},{\"w\":\"light\",\"pos\":\"adjective\",\"cefr\":\"A1\"}," +
                                      "{\"w\":\"the\",\"pos\":\"determiner\",\"cefr\":\"A1\"},{\"w\":\"be\",\"pos\":\"be-verb\",\"cefr\":\"A1\"}," +
                                      "{\"w\":\"can\",\"pos\":\"modal auxiliary\",\"cefr\":\"A1\"},{\"w\":\"can\",\"pos\":\"noun\",\"cefr\":\"B1\"}]");
        Check(list.LevelFor("light", new[] { "noun" }) == "A2", "cefr: pos match -> that level");
        Check(list.LevelFor("light", new[] { "verb" }) == "A1", "cefr: no pos match -> lowest");
        Check(list.LevelFor("Light ", new string[0]) == "A1", "cefr: case/space and no dict pos -> lowest");
        Check(list.LevelFor("zebra", new[] { "noun" }) == "—", "cefr: missing word -> dash");
        Check(list.LevelFor("the", new[] { "definite article" }) == "A1", "cefr: article -> determiner");
        Check(list.LevelForPos("be", "verb") == "A1", "cefr: be-verb matches verb");
        Check(list.LevelForPos("can", "noun") == "B1" && list.LevelForPos("can", "modal verb") == "A1", "cefr: modal verb");
        Check(list.LevelForPos("light", "verb") == null, "cefr: pos not in list -> null");
        Check(list.WordsAt("A1").Contains("light") && !list.WordsAt("A2").Contains("light"), "cefr: word belongs to its lowest level");
        Check(!list.WordsAt("B1").Contains("can"), "cefr: can is A1 only");
        Check(CefrWordList.Parse("oops").WordsAt("A1").Count() == 0, "cefr: broken json -> empty");
    }

    // ---------------- レベルの提案 ----------------
    private static List<VocabReview> NewReviews(int good, int bad, DateTime start) =>
        Enumerable.Range(0, good + bad).Select(i => new VocabReview
        {
            Word = "w" + i, Rating = i < bad ? 1 : 3, At = start.AddMinutes(i), IsNew = true,
        }).ToList();

    private static void VocabLevelTests()
    {
        DateTime set = VNow.AddDays(-10);
        Check(VocabLevel.ShouldSuggestUp(NewReviews(16, 4, set.AddDays(1)), set), "level: 16 of 20 -> suggest");
        Check(!VocabLevel.ShouldSuggestUp(NewReviews(15, 5, set.AddDays(1)), set), "level: 15 of 20 -> no");
        Check(!VocabLevel.ShouldSuggestUp(NewReviews(19, 0, set.AddDays(1)), set), "level: only 19 -> no");
        Check(!VocabLevel.ShouldSuggestUp(NewReviews(20, 0, set.AddDays(-1)), set), "level: before the level was set -> no");
        var mixed = NewReviews(20, 0, set.AddDays(1));
        mixed.AddRange(Enumerable.Range(0, 5).Select(i => new VocabReview { Word = "r" + i, Rating = 1, At = set.AddDays(2), IsNew = false }));
        Check(VocabLevel.ShouldSuggestUp(mixed, set), "level: reviews of old words are not counted");
        var recentBad = NewReviews(20, 0, set.AddDays(1));
        recentBad.AddRange(NewReviews(0, 5, set.AddDays(2)));
        Check(!VocabLevel.ShouldSuggestUp(recentBad, set), "level: only the newest 20 count");
        Check(VocabLevel.Next("A1") == "A2" && VocabLevel.Next("B1") == "B2" && VocabLevel.Next("B2") == null, "level: next");
    }

    // ---------------- 端末の保存の形 ----------------
    private static void VocabJsonTests()
    {
        var full = new VocabCard
        {
            Word = "apple", Cefr = "A1", Source = "manual", MyMeaning = "りんご \"赤い\"\n果物", Status = "known",
            DueAt = VNow.AddDays(3), Stability = 3.7145, Difficulty = 5.1618, LastReviewAt = VNow, Reps = 2, MissCount = 1,
            KnownAt = VNow.AddDays(-1), CreatedAt = VNow.AddDays(-9), PronBest = "close", PronAttempts = 3, GeminiPron = "good",
        };
        var empty = new VocabCard { Word = "x y", CreatedAt = VNow };
        var back = VocabJson.DeckFromJson(VocabJson.DeckToJson(new List<VocabCard> { full, empty }));
        Check(back.Count == 2, "vjson: 2 cards back");
        if (back.Count == 2)
        {
            var b = back[0];
            Check(b.Word == "apple" && b.Cefr == "A1" && b.Source == "manual" && b.MyMeaning == full.MyMeaning && b.Status == "known",
                "vjson: strings round trip");
            Check(b.DueAt == full.DueAt && b.LastReviewAt == VNow && b.KnownAt == full.KnownAt && b.CreatedAt == full.CreatedAt,
                "vjson: dates round trip");
            Check(b.DueAt.Value.Kind == DateTimeKind.Utc, "vjson: dates are UTC");
            Check(Near(b.Stability, 3.7145) && Near(b.Difficulty, 5.1618) && b.Reps == 2 && b.MissCount == 1 && b.PronAttempts == 3,
                "vjson: numbers round trip");
            Check(b.PronBest == "close" && b.GeminiPron == "good", "vjson: pron round trip");
            Check(back[1].DueAt == null && back[1].KnownAt == null && back[1].LastReviewAt == null, "vjson: null dates");
        }
        Check(VocabJson.DeckFromJson("oops").Count == 0 && VocabJson.DeckFromJson(null).Count == 0 && VocabJson.DeckFromJson("{}").Count == 0,
            "vjson: broken -> empty");
        var minimal = VocabJson.DeckFromJson("[{\"word\":\"X \"},{\"cefr\":\"A1\"}]");
        Check(minimal.Count == 1 && minimal[0].Word == "x" && minimal[0].Status == "learning" && minimal[0].Reps == 0
              && minimal[0].DueAt == null && minimal[0].Cefr == "—", "vjson: missing fields -> defaults, no word -> skipped");

        var reviews = new List<VocabReview>
        {
            new VocabReview { Word = "apple", Rating = 3, At = VNow, ElapsedDays = 1.5, Stability = 4.2, Difficulty = 5, IsNew = true },
        };
        var rb = VocabJson.ReviewsFromJson(VocabJson.ReviewsToJson(reviews));
        Check(rb.Count == 1 && rb[0].Word == "apple" && rb[0].Rating == 3 && rb[0].At == VNow && Near(rb[0].ElapsedDays, 1.5)
              && Near(rb[0].Stability, 4.2) && rb[0].IsNew, "vjson: reviews round trip");
        Check(VocabJson.ReviewsFromJson("[1,2").Count == 0, "vjson: broken reviews -> empty");

        var dict = MerriamWebsterParser.Parse(MwApple, "apple");
        dict.Suggestions.Add("apply");
        var db = VocabJson.DictFromJson(VocabJson.DictToJson(dict));
        Check(db != null && db.Found && db.Entries.Count == 1 && db.Entries[0].Ipa == dict.Entries[0].Ipa
              && db.Entries[0].Examples.SequenceEqual(dict.Entries[0].Examples) && db.Entries[0].AudioUrl == dict.Entries[0].AudioUrl
              && db.Entries[0].Definitions.SequenceEqual(dict.Entries[0].Definitions) && db.Suggestions.SequenceEqual(new[] { "apply" }),
            "vjson: dictionary round trip");
        Check(db != null && db.Version == DictionaryResult.CurrentVersion, "vjson: dictionary version saved");
        var oldSaved = VocabJson.DictFromJson("{\"found\":true,\"suggestions\":[],\"entries\":[]}");
        Check(oldSaved != null && oldSaved.Version == 0, "vjson: old saved dictionary -> version 0 (looked up again)");
        Check(VocabJson.DictFromJson("nope") == null, "vjson: broken dictionary -> null");

        // 単語帳
        var deck = new VocabDeck(new List<VocabCard>());
        var added = deck.Add("Apple ", "search", "", "A1", VNow);
        Check(added != null && added.Word == "apple" && added.Status == "learning" && added.CreatedAt == VNow, "deck: add");
        Check(deck.Add("apple", "manual", "x", "A1", VNow) == null && deck.Cards.Count == 1, "deck: same word only once");
        Check(deck.Find(" APPLE") == added, "deck: find by key");
        deck.Add("ice  cream", "manual", "", "—", VNow.AddMinutes(1));
        deck.Add("book", "auto", "", "A2", VNow.AddMinutes(2));
        Check(deck.List("learning", "—").Select(c => c.Word).SequenceEqual(new[] { "ice cream" }), "deck: filter dash");
        Check(deck.List("learning", "A2").Select(c => c.Word).SequenceEqual(new[] { "book" }), "deck: filter A2");
        Check(deck.List("learning", "all").Select(c => c.Word).SequenceEqual(new[] { "book", "ice cream", "apple" }),
            "deck: learning newest first");
        var k1 = deck.Find("apple"); k1.Status = "known"; k1.DueAt = VNow.AddDays(5);
        var k2 = deck.Find("book"); k2.Status = "known"; k2.DueAt = VNow.AddDays(1);
        Check(deck.List("known", "all").Select(c => c.Word).SequenceEqual(new[] { "book", "apple" }), "deck: known by next review");
        deck.MoveToLearning("apple");
        Check(k1.Status == "learning" && k1.DueAt == null, "deck: back to learning");
        Check(deck.Remove("book") && !deck.Remove("book") && deck.Cards.Count == 2, "deck: remove");
    }

    // ---------------- 同梱の CEFR-J (Assets/Resources/CefrjWords.json) ----------------
    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static void CefrBundledTests()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ThisFile()), "..", "..", "Assets", "Resources", "CefrjWords.json");
        Check(System.IO.File.Exists(path), "cefr file: CefrjWords.json exists");
        if (!System.IO.File.Exists(path)) return;
        var list = CefrWordList.Parse(System.IO.File.ReadAllText(path));
        Check(list.Count > 6000, "cefr file: many words (" + list.Count + ")");
        Check(list.WordsAt("A1").Count() >= 1000, "cefr file: 1000+ A1 words (" + list.WordsAt("A1").Count() + ")");
        Check(list.LevelFor("apple", new[] { "noun" }) == "A1", "cefr file: apple is A1");
        Check(list.Contains("a.m."), "cefr file: a.m. (first form)");
        Check(CefrWordList.Levels.All(l => list.WordsAt(l).Any()), "cefr file: every level has words");
    }

    // ---------------- Gemini のまとめ採点 ----------------
    private static void VocabGeminiTests()
    {
        System.Text.Json.JsonElement root = default;
        bool ok = true;
        try { root = System.Text.Json.JsonDocument.Parse(GeminiApiClient.VocabPronSchema).RootElement; }
        catch (Exception e) { ok = false; Check(false, "vocab schema is not valid JSON: " + e.Message); }
        if (ok)
        {
            Check(root.GetProperty("type").GetString() == "ARRAY", "vocab schema: array");
            var item = root.GetProperty("items");
            var ratings = item.GetProperty("properties").GetProperty("rating").GetProperty("enum").EnumerateArray().Select(x => x.GetString());
            Check(ratings.SequenceEqual(new[] { "good", "close", "practice" }), "vocab schema: ratings");
            var codes = item.GetProperty("properties").GetProperty("sounds").GetProperty("items").GetProperty("enum")
                .EnumerateArray().Select(x => x.GetString()).ToList();
            Check(codes.Count == 11 && SoundTypes.All.All(c => codes.Contains(c)) && codes.Contains("other"), "vocab schema: 10 sounds + other");
            var req = item.GetProperty("required").EnumerateArray().Select(x => x.GetString());
            Check(req.SequenceEqual(new[] { "word", "rating", "advice" }), "vocab schema: required");
        }

        string json = "[{\"word\":\"apple\",\"rating\":\"good\",\"advice\":\"よい\",\"sounds\":[]}," +
                      "{\"word\":\"zzz\",\"rating\":\"good\",\"advice\":\"x\"}," +
                      "{\"word\":\"Banana\",\"rating\":\"bad\",\"advice\":\"x\"}," +
                      "{\"word\":\"cat\",\"rating\":\"practice\",\"advice\":\"a\",\"sounds\":[\"th\",\"xx\"]}," +
                      "{\"word\":\"apple\",\"rating\":\"practice\",\"advice\":\"dup\"}]";
        var res = GeminiApiClient.ParseWordPronunciations(json, new[] { "apple", "banana", "cat" });
        Check(res.Select(r => r.Word).SequenceEqual(new[] { "apple", "cat" }), "vocab gemini: unknown words/ratings dropped, first of duplicates");
        if (res.Count == 2)
        {
            Check(res[0].Rating == "good" && res[0].Advice == "よい" && res[0].Sounds.Count == 0, "vocab gemini: apple");
            Check(res[1].Sounds.SequenceEqual(new[] { "th" }), "vocab gemini: unknown sound dropped");
        }
        Check(GeminiApiClient.ParseWordPronunciations("not json", new[] { "a" }).Count == 0, "vocab gemini: broken -> empty");
    }

    // ---------------- 辞書の返事が JSON でない (キーの間違いなど) ----------------
    private static void VocabDictionaryProblemTests()
    {
        Check(MerriamWebsterParser.ProblemMessage("[]") == null && MerriamWebsterParser.ProblemMessage(MwApple) == null,
            "mw problem: JSON array -> no problem");
        string bad = MerriamWebsterParser.ProblemMessage("Invalid API key. Not subscribed for this reference.");
        Check(bad != null && bad.Contains("merriamWebsterApiKey"), "mw problem: plain text -> check the key (" + bad + ")");
        Check(MerriamWebsterParser.ProblemMessage("<html><body>error</body></html>") != null, "mw problem: html -> message");
        bool threw = false;
        try { MerriamWebsterParser.ParseOrThrow("Invalid API key.", "x"); }
        catch (DictionaryKeyException e) { threw = e.Message.Contains("merriamWebsterApiKey"); }
        Check(threw, "mw problem: ParseOrThrow -> DictionaryKeyException");
        Check(MerriamWebsterParser.ParseOrThrow(MwApple, "apple").Found, "mw problem: ParseOrThrow parses good JSON");
    }

    // ---------------- 読み込みの細かいところ ----------------
    private static void VocabLoadTests()
    {
        // 同じ語が deck.json に 2 回あっても 1 枚 (最初のもの)
        var dup = VocabJson.DeckFromJson("[{\"word\":\"Apple\",\"reps\":3},{\"word\":\"apple \",\"reps\":9},{\"word\":\"book\"}]");
        Check(dup.Count == 2 && dup[0].Word == "apple" && dup[0].Reps == 3 && dup[1].Word == "book", "vjson: duplicate words merged on load");

        // 履歴が読めるか (読めないファイルは上書きせずに退避するため)
        Check(VocabJson.TryReviewsFromJson("[]", out var r0) && r0.Count == 0, "vjson: [] reviews readable");
        Check(VocabJson.TryReviewsFromJson("[{\"word\":\"a\",\"rating\":3}]", out var r1) && r1.Count == 1, "vjson: reviews readable");
        Check(!VocabJson.TryReviewsFromJson("[{\"word\":", out _), "vjson: broken reviews not readable");
        Check(!VocabJson.TryReviewsFromJson("{\"word\":\"a\"}", out _), "vjson: object (not array) not readable");
        Check(VocabJson.TryReviewsFromJson("", out var r2) && r2.Count == 0, "vjson: empty file = no reviews");
    }
}
