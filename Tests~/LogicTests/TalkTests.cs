// Talk (Lufka と英会話の練習): 添削・今日の単語・まとめ
using System;
using System.Collections.Generic;
using System.Linq;

public static partial class LogicTests
{
    private static void TalkCoachTests()
    {
        var ok = TalkCoach.Parse("{\"ok\":true,\"corrected\":\"\",\"note_ja\":\"\"}");
        Check(ok != null && ok.Ok, "talk parse: ok");
        var fix = TalkCoach.Parse("{\"ok\":false,\"corrected\":\"I went to Osaka yesterday.\",\"note_ja\":\"過去のことは went\"}");
        Check(fix != null && !fix.Ok && fix.Corrected == "I went to Osaka yesterday." && fix.NoteJa == "過去のことは went", "talk parse: fix");
        Check(TalkCoach.Parse("not json") == null && TalkCoach.Parse("{\"ok\":false,\"corrected\":\"\"}") == null, "talk parse: bad -> null");
        Check(TalkCoach.ShouldShow("I go to Osaka yesterday", fix), "talk show: fix");
        Check(!TalkCoach.ShouldShow("i went to osaka yesterday", fix), "talk show: only case/punctuation differs -> not shown");
        Check(!TalkCoach.ShouldShow("x y", ok) && !TalkCoach.ShouldShow("x y", null), "talk show: ok/null -> not shown");
        Check(!TalkCoach.ShouldCheck("Yes.") && !TalkCoach.ShouldCheck("123 456") && TalkCoach.ShouldCheck("I go"), "talk should check");
        Check(System.Text.Json.JsonDocument.Parse(GeminiApiClient.TalkCorrectionSchema) != null, "talk schema is JSON");

        // 画面の ✎ は、今の発話の添削だけ (遅れて届いた前の発話の添削で上書きしない。次の発話で消える)
        var slot = new TalkCoach.CorrectionSlot();
        var fix2 = new TalkCorrection { Ok = false, Corrected = "She goes to school." };
        slot.NewTurn(1, "I go to Osaka yesterday");
        slot.NewTurn(2, "She go to school");
        Check(!slot.Offer(1, fix) && slot.Value == null, "talk slot: late correction of an older turn is ignored");
        Check(slot.Offer(2, fix2) && slot.Value == fix2 && slot.Said == "She go to school", "talk slot: current turn shown");
        slot.NewTurn(3, "Thanks");
        Check(slot.Value == null, "talk slot: next utterance clears it");
        Check(!slot.Offer(3, ok) && slot.Value == null, "talk slot: ok -> nothing shown");
    }

    private static void TalkPlanTests()
    {
        // ---- 黙っているときの質問 ----
        var ws = new[] { "calm", "borrow", "rescue" };
        Check(TalkPlan.NextNudgeWord(ws, new string[0], new string[0]) == "calm", "nudge: first unused word");
        Check(TalkPlan.NextNudgeWord(ws, new[] { "calm" }, new string[0]) == "borrow", "nudge: skip used word");
        Check(TalkPlan.NextNudgeWord(ws, new string[0], new[] { "calm" }) == "borrow", "nudge: skip already asked word");
        Check(TalkPlan.NextNudgeWord(ws, new[] { "rescue" }, new[] { "calm", "borrow" }) == "calm", "nudge: all asked -> first unused again");
        Check(TalkPlan.NextNudgeWord(ws, ws, new string[0]) == null, "nudge: all used -> null");
        Check(TalkPlan.NextNudgeWord(null, null, null) == null, "nudge: no words -> null");
        string q = TalkPlan.NudgeQuery(" borrow ");
        Check(q.Contains("\"borrow\"") && q.Contains("question"), "nudge: query names the word");

        var now = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Local);
        VocabReview R(string w, int daysAgo, int h = 10) => new VocabReview { Word = w, At = now.Date.AddDays(-daysAgo).AddHours(h) };
        var cards = new List<VocabCard> {
            new VocabCard { Word = "calm", Status = VocabCard.Learning, LastReviewAt = now.AddDays(-1) },
            new VocabCard { Word = "brave", Status = VocabCard.Known, DueAt = now.AddDays(2) },
            new VocabCard { Word = "quiet", Status = VocabCard.Learning, LastReviewAt = now.AddDays(-3) } };
        var w = TalkPlan.PickWords(new[] { R("study", 0, 9), R("run", 0, 11), R("study", 0, 12), R("old", 1) }, cards, now);
        Check(string.Join(",", w) == "study,run,calm,quiet,brave", "talk words: today first, then learning, then due (" + string.Join(",", w) + ")");
        Check(TalkPlan.PickWords(new VocabReview[0], new VocabCard[0], now).Count == 0, "talk words: no vocab -> none");
        Check(TalkPlan.PickWords(null, null, now).Count == 0, "talk words: null -> none");
        var many = Enumerable.Range(0, 9).Select(i => R("w" + i, 0, 1 + i)).ToArray();
        Check(TalkPlan.PickWords(many, cards, now).Count == TalkPlan.MaxWords, "talk words: at most 5");
        string ins = TalkPlan.Instruction("A2", new[] { "study", "run" });
        Check(ins.Contains("A2") && ins.Contains("study, run"), "talk instruction: level and words");
        Check(TalkPlan.Instruction("", new string[0]).Contains("B1") && !TalkPlan.Instruction("", new string[0]).Contains("learning these words"), "talk instruction: default level, no words");
        string p1 = TalkPlan.WithInstruction("You are Lufka.", "X");
        Check(TalkPlan.WithInstruction(p1, "Y") == "You are Lufka." + TalkPlan.Marker + "Y", "talk prompt: old practice part replaced");
        var used = TalkPlan.UsedWords("Yesterday I Went running and she studies a lot", new[] { "go", "run", "study", "calm" });
        Check(string.Join(",", used) == "go,run,study", "talk used words: went/running/studies (" + string.Join(",", used) + ")");
        Check(string.Join(",", TalkPlan.UsedWords("I was making tea, then I tried it", new[] { "make", "try", "tea" })) == "make,try,tea", "talk used words: making/tried");
    }

    private static void TalkSummaryTests()
    {
        var s = new TalkSummary(new[] { "go", "calm" });
        s.AddUserTurn("Hello there", 10f);
        s.AddUserTurn("I go to Osaka yesterday", 20f);
        s.AddCorrection("I go to Osaka yesterday", new TalkCorrection { Ok = false, Corrected = "I went to Osaka yesterday.", NoteJa = "過去のことは went" });
        s.AddCorrection("Hello there", new TalkCorrection { Ok = true });
        Check(!s.CountsForStreak && s.Turns == 2, "talk summary: 2 turns -> not counted");
        s.AddUserTurn("It was fun", 40f);
        s.End(70f);
        Check(s.CountsForStreak && s.Turns == 3 && Math.Abs(s.Seconds - 60f) < 0.01f, "talk summary: 3 turns, 60 s");
        Check(s.Used.Contains("go") && !s.Used.Contains("calm") && s.Corrections.Count == 1, "talk summary: used words and corrections");
        string t = s.Text();
        Check(t.Contains("3") && t.Contains("1:00") && t.Contains("I go to Osaka yesterday") && t.Contains("I went to Osaka yesterday.") && t.Contains("calm"), "talk summary text: " + t);

        var empty = new TalkSummary(new string[0]);
        empty.End(5f);
        string et = empty.Text();
        Check(empty.Turns == 0 && empty.Seconds == 0f && et.Contains("No corrections") && !et.Contains("Words:"), "talk summary: empty (" + et + ")");
    }
}
