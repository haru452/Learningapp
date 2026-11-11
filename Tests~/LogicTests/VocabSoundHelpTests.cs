// 単語帳の「Didn't sound right?」(Gemini の返事の読み方・音ごとの練習動画の保存) のテスト。Program.cs の Main から呼ぶ。
using System;
using System.Linq;

public static partial class LogicTests
{
    private static void VocabSoundHelpTests()
    {
        // ---- Gemini の返事 ----
        string json = "{\"problem\":\"th が s になっています。\",\"howTo\":\"舌を歯の間に。\",\"sounds\":[" +
                      "{\"code\":\"th\"},{\"code\":\"xx\"},{\"code\":\"th\"},{\"code\":\"other\",\"otherName\":\"\"}," +
                      "{\"code\":\"other\",\"otherName\":\"Z Sound\"},{\"code\":\"stress\"}]}";
        var h = GeminiApiClient.ParseWordHelp(json, "Think");
        Check(h != null, "word help: parsed");
        if (h != null)
        {
            Check(h.Word == VocabDeck.Key("Think"), "word help: word is the deck key (" + h.Word + ")");
            Check(h.Problem == "th が s になっています。" && h.HowTo == "舌を歯の間に。", "word help: problem and howTo");
            Check(h.Sounds.Select(s => s.Code).SequenceEqual(new[] { "th", "other" }),
                "word help: unknown, duplicate and nameless other dropped, max 2 (" + string.Join(",", h.Sounds.Select(s => s.Code)) + ")");
            Check(h.Sounds.Count == 2 && h.Sounds[1].Label == "z", "word help: other name normalized (" + (h.Sounds.Count == 2 ? h.Sounds[1].Label : "") + ")");
        }
        var fine = GeminiApiClient.ParseWordHelp("{\"problem\":\"きれいに言えています。\",\"howTo\":\"\",\"sounds\":[]}", "cat");
        Check(fine != null && fine.Sounds.Count == 0, "word help: fine -> no sounds");
        Check(GeminiApiClient.ParseWordHelp("{\"problem\":\"\",\"howTo\":\"\"}", "cat") == null, "word help: empty -> null");
        Check(GeminiApiClient.ParseWordHelp("not json", "cat") == null, "word help: broken -> null");
        Check(GeminiApiClient.ParseWordHelp("[1,2]", "cat") == null, "word help: array -> null");

        // ---- 練習動画の保存 ----
        var now = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        var v = new YouTubeSearchClient.Video { id = "abc123", channel = "Rachel\tEnglish", title = "TH\nsound" };
        string saved = VocabSoundHelp.EncodeVideo(v, now);
        var back = VocabSoundHelp.DecodeVideo(saved, now.AddDays(10));
        Check(back != null && back.id == "abc123" && back.channel == "Rachel English" && back.title == "TH sound",
            "sound video: round trip, tabs and newlines become spaces");
        Check(VocabSoundHelp.DecodeVideo(saved, now.AddDays(VocabSoundHelp.VideoCacheDays + 1)) == null, "sound video: older than 30 days -> null");
        Check(VocabSoundHelp.DecodeVideo("", now) == null && VocabSoundHelp.DecodeVideo("x\tid\tc\tt", now) == null
              && VocabSoundHelp.DecodeVideo("1\t\tc\tt", now) == null, "sound video: broken -> null");
    }
}
