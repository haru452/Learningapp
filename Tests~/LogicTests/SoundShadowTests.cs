using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// 苦手な発音・ハイライト・シャドーイングのテスト。Program.cs の Main から呼ぶ。
/// </summary>
public static partial class LogicTests
{
    // ---------------- 苦手な発音 ----------------
    private static void SoundTests()
    {
        Check(SoundTypes.All.Length == 10 && SoundTypes.IsKnown("other") && SoundTypes.IsKnown("th") && !SoundTypes.IsKnown("x"), "sounds: 10 + other");
        Check(SoundTypes.NormalizeOtherName("Z sound (zoo)") == "z" && SoundTypes.NormalizeOtherName("The \"W\" Sound") == "the w"
              && SoundTypes.NormalizeOtherName("NG sounds") == "ng" && SoundTypes.NormalizeOtherName("(x)") == ""
              && SoundTypes.NormalizeOtherName("  Dark   L / clear-L  ") == "dark l / clear-l", "sounds: other name normalised like SQL");
        Check(SoundTypes.NormalizeOtherName(string.Concat(Enumerable.Repeat("ab ", 30))).Length <= 30, "sounds: other name 30 chars");
        Check(SoundTypes.Label("th", null) == "TH" && SoundTypes.Label("r_l", null) == "R / L" && SoundTypes.Label("other", "z") == "Other: z",
              "sounds: app labels");
        Check(SoundTypes.Example("th") == "think / this", "sounds: example");
        Check(SoundTypes.SearchQuery("th", null) == "how to pronounce TH sound English pronunciation"
              && SoundTypes.SearchQuery("other", "z") == "how to pronounce z sound English pronunciation", "sounds: search query");

        string G(string w) => string.Join(",", SoundGuesser.Guess(w));
        foreach (var (w, want) in new[] { ("think", "th"), ("very", "v_b"), ("phone", "f_h"), ("right", "r_l"), ("play", "r_l"), ("tree", "r_l"),
                                          ("car", ""), ("she", "s_sh"), ("sit", "s_sh"), ("walked", "final_consonant"), ("hand", "final_consonant"),
                                          ("three", "th"), ("light", "r_l"), ("food", "f_h") })
            Check(G(w) == want, "guess " + w + " -> [" + G(w) + "] want [" + want + "]");
        var hits = SoundGuesser.FromMissedWords(new[] { "the", "this", "of", "think", "cat", "Play!", "go" });
        Check(hits.Count == 2 && hits[0].Code == "th" && hits[0].Word == "think" && hits[1].Code == "r_l" && hits[1].Word == "play",
              "guess: function words, short words and punctuation ignored (" + string.Join(";", hits.Select(h => h.Word + ":" + h.Code)) + ")");
        Check(SoundGuesser.FromMissedWords(new[] { "the", "of", "a" }).Count == 0, "guess: nothing to count");
        Check(SoundGuesser.FromMissedWords(new[] { "that's", "there's", "they're", "them", "their", "That\u2019s" }).Count == 0,
              "guess: contractions and them/their are function words");
        Check(DailySoundPlanner.PracticeDay(DateTime.MinValue) == DateTime.MinValue, "daily: MinValue (never) does not throw");
        Check(GeminiApiClient.ParsePronunciation("{\"comment\":\"x\",\"sounds\":[{\"code\":\"th\"}]}", "The weather is nice.").Sounds[0].Word == "weather",
              "pron: example word skips function words");
        Check(DailySoundPlanner.PracticeDay(new DateTime(2026, 10, 24, 1, 30, 0)) == new DateTime(2026, 10, 23), "daily: 01:30 belongs to the day before");
        var top = SoundGuesser.Top(SoundGuesser.FromMissedWords(new[] { "play", "think", "three", "tree", "right", "very" }), 3);
        Check(top.SequenceEqual(new[] { "r_l", "th", "v_b" }), "guess: top 3 by count (" + string.Join(",", top) + ")");

        var json = JsonDocument.Parse(SoundRpc.IssuesJson(new List<SoundHit>
        {
            new SoundHit { Code = "th", Label = "ignored", Word = "think" },
            new SoundHit { Code = "other", Label = "z", Word = "zoo" },
        }, 1, "repeat")).RootElement;
        Check(json.GetArrayLength() == 2 && json[0].GetProperty("sound").GetString() == "th" && json[0].GetProperty("label").GetString() == ""
              && json[0].GetProperty("weight").GetInt32() == 1 && json[0].GetProperty("word").GetString() == "think"
              && json[0].GetProperty("source").GetString() == "repeat" && json[1].GetProperty("label").GetString() == "z",
              "rpc: issues JSON (label only for other)");
        var weak = SoundRpc.ParseWeakSounds("[{\"sound\":\"th\",\"label\":\"\",\"points\":9,\"previous_points\":3,\"words\":[\"think\",\"three\"]}," +
                                            "{\"sound\":\"other\",\"label\":\"z\",\"points\":3,\"previous_points\":null,\"words\":[]}]");
        Check(weak.Count == 2 && weak[0].Sound == "th" && weak[0].Points == 9 && weak[0].PreviousPoints == 3
              && weak[0].Words.SequenceEqual(new[] { "think", "three" }) && weak[1].Label == "z" && weak[1].PreviousPoints == null,
              "rpc: parse weak sounds");
        Check(SoundRpc.ParseWeakSounds("oops").Count == 0, "rpc: broken -> empty");

        // 発音チェックの返事 (Gemini)
        try
        {
            var codes = JsonDocument.Parse(GeminiApiClient.PronunciationSchema).RootElement.GetProperty("properties").GetProperty("sounds")
                .GetProperty("items").GetProperty("properties").GetProperty("code").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToList();
            Check(codes.SequenceEqual(SoundTypes.All.Concat(new[] { "other" })), "pron schema: code enum = 10 + other");
        }
        catch (Exception e) { Check(false, "pron schema: " + e.Message); }
        var fb = GeminiApiClient.ParsePronunciation(
            "{\"comment\":\"Say think with your tongue.\",\"sounds\":[{\"code\":\"th\"},{\"code\":\"xx\"},{\"code\":\"other\",\"otherName\":\"\"}," +
            "{\"code\":\"other\",\"otherName\":\"Z sound (zoo)\"},{\"code\":\"r_l\"}]}", "I think the zoo is big.");
        Check(fb.Comment == "Say think with your tongue." && fb.Sounds.Count == 2 && fb.Sounds[0].Code == "th" && fb.Sounds[0].Word == "think"
              && fb.Sounds[1].Code == "other" && fb.Sounds[1].Label == "z" && fb.Sounds[1].Word == "",
              "pron: unknown / empty other dropped, max 2, word from the sentence (" + string.Join(";", fb.Sounds.Select(x => x.Code + ":" + x.Label + ":" + x.Word)) + ")");
        Check(GeminiApiClient.ParsePronunciation("{\"comment\":\"ok\"}", "x").Sounds.Count == 0, "pron: no sounds -> empty list");

        // 今日送るか
        var now = new DateTime(2026, 10, 23, 10, 0, 0);
        Check(!DailySoundPlanner.ShouldTry(new DateTime(2026, 10, 23, 8, 0, 0), DateTime.MinValue, 0, now), "daily: sent today -> no");
        Check(DailySoundPlanner.ShouldTry(new DateTime(2026, 10, 22, 23, 0, 0), DateTime.MinValue, 0, new DateTime(2026, 10, 23, 3, 0, 0)),
              "daily: sent 23:00 yesterday, now 03:00 -> yes");
        Check(!DailySoundPlanner.ShouldTry(new DateTime(2026, 10, 23, 2, 0, 0), DateTime.MinValue, 0, new DateTime(2026, 10, 23, 2, 30, 0)),
              "daily: 02:00 is still the day before");
        Check(!DailySoundPlanner.ShouldTry(DateTime.MinValue, new DateTime(2026, 10, 23, 9, 0, 0), 2, now), "daily: failed twice today -> no");
        Check(DailySoundPlanner.ShouldTry(DateTime.MinValue, new DateTime(2026, 10, 22, 9, 0, 0), 2, now), "daily: failures yesterday don't count");
        var vids = new List<YouTubeSearchClient.Video> { new YouTubeSearchClient.Video { id = "a" }, new YouTubeSearchClient.Video { id = "b" } };
        var sent = new Dictionary<string, DateTime> { ["a"] = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) };
        var nowUtc = new DateTime(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);
        Check(DailySoundPlanner.PickVideo(vids, sent, nowUtc)?.id == "b", "daily: skip videos sent within 30 days");
        sent["b"] = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        Check(DailySoundPlanner.PickVideo(vids, sent, nowUtc)?.id == "a", "daily: all sent -> first one");
        sent["a"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        Check(DailySoundPlanner.PickVideo(vids, sent, nowUtc)?.id == "a", "daily: sent over 30 days ago is fine again");
        Check(DailySoundPlanner.PickVideo(new List<YouTubeSearchClient.Video>(), sent, nowUtc) == null, "daily: none found -> null");

        // Discord
        var topTh = new WeakSound { Sound = "th", Label = "", Points = 9, Words = new List<string> { "think", "three", "weather" } };
        var video = new YouTubeSearchClient.Video { id = "abc123", title = "TH sound", channel = "Teacher" };
        string msg = DiscordClient.SoundPracticeMessage(new DateTime(2026, 10, 23), topTh, video);
        Check(msg.StartsWith("**Today's pronunciation practice - 2026-10-23**") && msg.Contains("Your weakest sound: TH (think / this) \u2014 9 points in 30 days")
              && msg.Contains("Words you missed: think, three, weather") && msg.Contains("TH sound (Teacher)")
              && msg.Contains("https://www.youtube.com/watch?v=abc123"), "sound discord: " + msg);
        string msgOther = DiscordClient.SoundPracticeMessage(new DateTime(2026, 10, 23),
            new WeakSound { Sound = "other", Label = "z", Points = 6 }, video);
        Check(msgOther.Contains("Your weakest sound: Other: z \u2014 6 points in 30 days") && !msgOther.Contains("Words you missed"),
              "sound discord: other, no words line");
    }

    // 日記の添削: 直った語だけを色付きの太字にする (アプリの画面用)
    private static void HighlightTests()
    {
        string h = TextDiff.HighlightChanges("i go to park yesterday", "I went to the park yesterday.", "#40c057", out int n);
        Check(h == "I <b><color=#40c057>went</color></b> to <b><color=#40c057>the</color></b> park yesterday." && n == 2,
              "highlight: changed and added words coloured (" + h + ", " + n + ")");
        string same = TextDiff.HighlightChanges("I like tea", "I like tea.", "#40c057", out int n0);
        Check(same == "I like tea." && n0 == 0, "highlight: punctuation / case only -> no colour (" + same + ")");
        string many = TextDiff.HighlightChanges("he go school", "He goes to school every day.", "#40c057", out int n2);
        Check(many == "He <b><color=#40c057>goes to</color></b> school <b><color=#40c057>every day</color></b>." && n2 == 4,
              "highlight: neighbouring changes joined, punctuation outside (" + many + ")");
        string tag = TextDiff.HighlightChanges("a b", "a <b>x</b> b", "#40c057", out _);
        Check(!tag.Contains("<b>x</b>") && tag.Contains("<\u200B"), "highlight: '<' in the text is not read as a tag (" + tag + ")");
        string raw = TextDiff.HighlightChanges("x", "Not_bold *here*", "#40c057", out _);
        Check(raw.Contains("Not_bold *here") && !raw.Contains("\\"), "highlight: no Discord escaping (" + raw + ")");
        Check(TextDiff.HighlightChanges("x", "", "#40c057", out _) == "", "highlight: empty");
    }

    private static void ShadowTests()
    {
        // お手本
        string script = "{\"sentences\":[{\"text\":\"I like green tea.\"},{\"text\":\"  \"},{\"text\":\"" +
            string.Join(" ", Enumerable.Repeat("word", 41)) + "\"},{\"text\":\"We play soccer.\"}]}";
        var ss = ShadowScript.Parse(script);
        Check(ss.SequenceEqual(new[] { "I like green tea.", "We play soccer." }), "script: empty and 41-word sentences dropped (" + string.Join("|", ss) + ")");
        Check(ShadowScript.Parse("nope").Count == 0 && ShadowScript.Parse(null).Count == 0, "script: broken JSON -> empty");
        var ten = Enumerable.Range(1, 10).Select(i => "Sentence " + i + ".").ToList();
        string many = "{\"sentences\":[" + string.Join(",", Enumerable.Range(0, 200).Select(i => "{\"text\":\"one two three four five six seven eight nine ten\"}")) + "]}";
        Check(ShadowScript.Parse(many).Count == 200, "script: a 15-minute video (2000 words) is kept whole (" + ShadowScript.Parse(many).Count + ")");
        string tooMany = "{\"sentences\":[" + string.Join(",", Enumerable.Range(0, 400).Select(i => "{\"text\":\"one two three four five six seven eight nine ten\"}")) + "]}";
        Check(ShadowScript.Parse(tooMany).Count == 300, "script: up to 3000 words");
        Check(Math.Abs(ShadowScript.Fraction(240, 420, 600, false) - 0.4) < 1e-9, "fraction: video time (4:00 of 10:00) wins over wall clock with a pause");
        Check(Math.Abs(ShadowScript.Fraction(0, 300, 600, false) - 0.5) < 1e-9, "fraction: no video time -> wall clock");
        Check(ShadowScript.Fraction(100, 120, 600, true) == 1.0, "fraction: video ended -> 1");
        var slice = ShadowScript.ReferenceSlice(ten, 1, 2);
        Check(slice.StartsWith("Sentence 5.") || slice.StartsWith("Sentence 4."), "reference: part 2 of 2 starts mid-way (" + slice + ")");
        Check(ShadowScript.ReferenceSlice(ten, 0, 1).Contains("Sentence 10."), "reference: single part -> whole");
        Check(ShadowScript.SentencesUpTo(ten, 1.0).Count == 10 && ShadowScript.SentencesUpTo(ten, 0.99).Count == 10, "script: whole video -> all");
        Check(ShadowScript.SentencesUpTo(ten, 0.5).Count == 6, "script: half -> 5 + 1 (" + ShadowScript.SentencesUpTo(ten, 0.5).Count + ")");
        Check(ShadowScript.SentencesUpTo(ten, 0.0).Count == 1, "script: stopped at once -> 1");

        // 録音を 180 秒ずつに分ける
        var parts = WavSplitter.Split(FakeWav(420), 180);
        Check(parts.Count == 3, "wav split: 7 min -> 3 parts (" + parts.Count + ")");
        if (parts.Count == 3)
        {
            Check(BitConverter.ToInt32(parts[0], 40) == 180 * 16000 * 2 && BitConverter.ToInt32(parts[2], 40) == 60 * 16000 * 2,
                  "wav split: data sizes 180 s / 60 s");
            Check(BitConverter.ToInt32(parts[1], 4) == parts[1].Length - 8 && BitConverter.ToInt32(parts[1], 24) == 16000,
                  "wav split: RIFF size and sample rate in each part");
        }
        var shortParts = WavSplitter.Split(FakeWav(5), 180);
        Check(shortParts.Count == 1 && shortParts[0].Length == FakeWav(5).Length, "wav split: short recording -> 1 part");
        var tail = PcmAudio.ToWav(new float[(int)(360.5 * 16000)], 1, 16000);
        Check(WavSplitter.Split(tail, 180).Count == 2, "wav split: 0.5 s left over joins the last part");
        Check(WavSplitter.Split(null, 180).Count == 0, "wav split: null -> none");

        // 検索結果
        YouTubeSearchClient.Video V(string id, int sec) => new YouTubeSearchClient.Video { id = id, title = id, channel = "c", durationSeconds = sec };
        var found = new List<YouTubeSearchClient.Video> { V("a", 59), V("b", 60), V("c", 901), V("d", 900), V("e", 300), V("f", 120), V("g", 400), V("h", 500) };
        var picked = YouTubeSearchClient.PickTitleResults(found, 60, 900, 5);
        Check(picked.Select(x => x.id).SequenceEqual(new[] { "b", "d", "e", "f", "g" }), "search: 1-15 min, order kept, 5 max (" + string.Join(",", picked.Select(x => x.id)) + ")");
        Check(YouTubeSearchClient.FormatDuration(365) == "6:05" && YouTubeSearchClient.FormatDuration(60) == "1:00", "search: m:ss");

        // 照合 (今のまま)
        var sentences = new[] { "I like green tea.", "She went home early today.", "We play soccer." };
        var sc = ShadowScorer.Score(sentences, "i like green tea we play soccer");
        Check(sc.scores.Select(x => x.Accuracy).SequenceEqual(new[] { 100, 0, 100 }) && sc.average == 67 && !sc.empty,
            "score: per sentence " + string.Join(",", sc.scores.Select(x => x.Accuracy)) + " avg " + sc.average);
        Check(sc.scores[1].Words.Count == 5 && sc.scores[1].Words.All(x => !x.Matched), "score: missed sentence words all red");
        var none = ShadowScorer.Score(sentences, "  ");
        Check(none.empty && none.average == 0, "score: nothing heard -> empty");

        foreach (int t in new[] { 3, 4, 7, 8, 22, 23, 26 }) Check(EarphoneCheck.IsEarphone(t), "earphone type " + t);
        foreach (int t in new[] { 1, 2, 27, 0 }) Check(!EarphoneCheck.IsEarphone(t), "not earphone type " + t);

        // Gemini のスキーマ
        foreach (var (name, json) in new[] { ("video script", GeminiApiClient.VideoScriptSchema), ("shadow transcript", GeminiApiClient.ShadowTranscriptSchema),
                                             ("shadow advice", GeminiApiClient.ShadowAdviceSchema) })
        {
            try { JsonDocument.Parse(json); Check(true, name + " schema is JSON"); }
            catch (Exception e) { Check(false, name + " schema JSON: " + e.Message); }
        }
        try
        {
            var props = JsonDocument.Parse(GeminiApiClient.ShadowTranscriptSchema).RootElement.GetProperty("properties");
            Check(props.GetProperty("transcript").GetProperty("type").GetString() == "STRING"
                  && props.GetProperty("notes").GetProperty("type").GetString() == "ARRAY", "shadow transcript schema: transcript + notes");
            var item = JsonDocument.Parse(GeminiApiClient.VideoScriptSchema).RootElement.GetProperty("properties").GetProperty("sentences")
                .GetProperty("items").GetProperty("properties");
            Check(item.TryGetProperty("text", out _), "video script schema: sentences[].text");
        }
        catch (Exception e) { Check(false, "shadow schemas: " + e.Message); }

        // 保存 (点数だけ)
        var rows = new List<RepeatResult>
        {
            new RepeatResult { sentence = "I like green tea.", accuracy = 100, source = "youtube", voice = "human", mode = "shadow" },
            new RepeatResult { sentence = "We play soccer.", accuracy = 50, heard = "we play", source = "tatoeba", voice = "human" },
        };
        var rj = JsonDocument.Parse(RepeatRpc.ResultsJson(rows)).RootElement;
        Check(rj[0].GetProperty("mode").GetString() == "shadow" && rj[1].GetProperty("mode").GetString() == "repeat", "rpc: mode (empty -> repeat)");
        Check(rj[0].GetProperty("sentence").GetString() == "" && rj[0].GetProperty("heard").GetString() == ""
              && rj[0].GetProperty("accuracy").GetInt32() == 100 && rj[0].GetProperty("source").GetString() == "youtube",
              "rpc: shadow sends the score only (no text, no heard)");
        Check(rj[1].GetProperty("sentence").GetString() == "We play soccer." && rj[1].GetProperty("heard").GetString() == "we play",
              "rpc: repeat still sends text");
        string shadowArgs = RepeatRpc.SaveArgs(rows, new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc));
        string repeatArgs = RepeatRpc.SaveArgs(rows.Skip(1).ToList(), new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc));
        Check(shadowArgs.Contains("\"p_version\":2") && shadowArgs.Contains("\"p_taken_at\":\"2026-10-22T12:00:00Z\"")
              && shadowArgs.StartsWith(",\"p_results\":["), "rpc: shadow save asks for the new function (old server fails -> kept on phone)");
        Check(!repeatArgs.Contains("p_version") && repeatArgs.Contains("\"p_taken_at\""), "rpc: repeat-only save unchanged");

        // Discord
        string sm = DiscordClient.ShadowMessage(new DateTime(2026, 10, 22, 21, 0, 0), "My test video", "https://www.youtube.com/watch?v=abc",
            67, "- 語尾をはっきり\n- 遅れないように", sc.scores);
        Check(sm.StartsWith("\U0001F3A7 **Shadowing - 2026-10-22 21:00**"), "shadow discord header: " + sm);
        Check(sm.Contains("My test video") && sm.Contains("<https://www.youtube.com/watch?v=abc>") && sm.Contains("Average: 67%")
              && sm.Contains("語尾をはっきり") && sm.Contains("✅ 100%  I like green tea.") && sm.Contains("⚠️   0%  She went home early today."),
              "shadow discord body");
        Check(sm.IndexOf("Advice", StringComparison.Ordinal) < sm.IndexOf("I like green tea.", StringComparison.Ordinal), "shadow discord: advice before sentences");
    }
}
