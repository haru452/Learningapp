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
        bool threw = false;
        try { MerriamWebsterParser.Parse("<html>", "x"); } catch (FormatException) { threw = true; }
        Check(threw, "mw: html -> FormatException");
    }
}
