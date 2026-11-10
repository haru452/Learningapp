// カラオケ (歌詞の読み込み・ずらし・切り方・LRCLIB・Gemini の採点・点数) のテスト。Program.cs の Main から呼ぶ。
using System;
using System.Collections.Generic;
using System.Linq;

public static partial class LogicTests
{
    private static void KaraokeTests()
    {
        // ---- Task 1: LrcLyrics ----
        var lines = LrcLyrics.Parse("[ar:Someone]\n[00:12.50]Hello world\n[00:15.00][01:15.00]Chorus line\n[00:20.00]\nbroken\n[00:18]No hundredths");
        Check(lines.Count == 5, "lrc: 5 lines (tag and broken dropped) " + lines.Count);
        Check(lines.Count == 5 && lines[0].Time == 12.5 && lines[0].Text == "Hello world", "lrc: first line");
        Check(lines.Count == 5 && lines[2].Time == 18 && lines[3].IsBlank && lines[4].Time == 75 && lines[4].Text == "Chorus line",
              "lrc: sorted, blank kept, repeated time");
        Check(LrcLyrics.Parse("[00:01.5]a")[0].Time == 1.5 && LrcLyrics.Parse("[00:01.123]a")[0].Time == 1.123, "lrc: fraction digits");
        Check(LrcLyrics.Parse(null).Count == 0 && LrcLyrics.Parse("").Count == 0, "lrc: empty");
        Check(LrcLyrics.AutoOffset(200, 198.5) == 0 && LrcLyrics.AutoOffset(215, 200) == 15, "lrc: offset within 3s is 0, small intro shifts");
        // 長い終わりのおまけ (40 秒) や、動画の方が短いときはずらさない (± ボタンで直す)
        Check(LrcLyrics.AutoOffset(240, 200) == 0 && LrcLyrics.AutoOffset(196, 200) == 0, "lrc: big or negative difference -> 0");
        Check(LrcLyrics.IndexAt(lines, 12.0, 0) == -1 && LrcLyrics.IndexAt(lines, 16, 0) == 1 && LrcLyrics.IndexAt(lines, 16, 2) == 0,
              "lrc: line at time with offset");
        // 1 番だけ歌って止めた: それより後の行は入らない
        Check(LrcLyrics.SungLines(lines, 16, 0).All(l => l.Time < 16), "lrc: stop early -> only sung lines");
        Check(LrcLyrics.SungLines(lines, 16, 0).Count == 2 && LrcLyrics.SungLines(lines, 100, 0).Count == 4, "lrc: sung lines skip blanks");
        var long4 = LrcLyrics.Parse(string.Join("\n", Enumerable.Range(0, 24).Select(i => $"[{i * 10 / 60:00}:{i * 10 % 60:00}.00]line {i}")));
        var chunks = LrcLyrics.Chunks(long4, 0, 0, 240);
        Check(chunks.Count == 2 && chunks.All(c => c.end - c.start <= 180) && chunks[0].end == chunks[1].start,
              "lrc: chunks <= 180s on line borders");
        Check(chunks.SelectMany(c => c.lines).Count() == 24 && chunks.SelectMany(c => c.lines).Distinct().Count() == 24,
              "lrc: every line in one chunk");
        // 録音を 30 秒から始めて 65 秒で止めた (offset 5): 録音の秒で切る・止めた後の行は入らない
        var mid = LrcLyrics.Chunks(long4, 5, 30, 65);
        Check(mid.Count == 1 && mid[0].start == 0 && mid[0].end == 35 && mid[0].lines.First() == 2 && mid[0].lines.Last() == 5,
              "lrc: chunk in recording seconds " + string.Join(",", mid.Select(c => c.start + "-" + c.end + ":" + string.Join("/", c.lines))));

        // ---- Task 2: LrclibClient ----
        string json = "[{\"id\":1,\"trackName\":\"Song\",\"artistName\":\"A\",\"duration\":233.0,\"syncedLyrics\":\"[00:01.00]x\"}," +
                      "{\"id\":2,\"trackName\":\"Song (Live)\",\"artistName\":\"A\",\"duration\":280.0,\"syncedLyrics\":\"[00:01.00]y\"}," +
                      "{\"id\":3,\"trackName\":\"Song\",\"artistName\":\"A\",\"duration\":233.0,\"syncedLyrics\":null,\"plainLyrics\":\"x\"}]";
        var songs = LrclibClient.ParseSearch(json);
        Check(songs.Count == 2 && songs[0].Id == 1, "lrclib: only synced lyrics");
        Check(LrclibClient.PickBest(songs, 278).Id == 2 && LrclibClient.PickBest(songs, 0).Id == 1, "lrclib: closest length");
        Check(LrclibClient.PickBest(new List<LrcSong>(), 100) == null, "lrclib: none -> null");
        Check(LrclibClient.ParseSearch("oops").Count == 0 && LrclibClient.ParseSearch("[]").Count == 0, "lrclib: bad or empty");
        Check(LrclibClient.VideoQuery(songs[0]) == "Song A lyrics", "lrclib: video query");

        // ---- Task 3: Gemini の採点と点数 ----
        var ls = new List<(int, string)> { (0, "I want it that way"), (1, "Tell me why") };
        var check = GeminiApiClient.ParseKaraokeCheck("{\"lines\":[{\"line\":0,\"missed\":[{\"word\":\"That,\",\"sound\":\"th\"},{\"word\":\"banana\",\"sound\":\"r_l\"}," +
            "{\"word\":\"way\",\"sound\":\"zzz\"}]},{\"line\":7,\"missed\":[{\"word\":\"why\",\"sound\":\"th\"}]}],\"advice\":\"thを意識\"}", ls);
        Check(check.Lines.Count == 1 && check.Lines[0].Missed.Count == 2 && check.Lines[0].Missed[0].Word == "that",
              "karaoke: drop unknown words and lines");
        Check(check.Lines.Count == 1 && check.Lines[0].Missed[1].Sound == "other" && check.Lines[0].Text == "I want it that way",
              "karaoke: unknown sound -> other, text kept");
        Check(check.Advice == "thを意識", "karaoke: advice");
        var sung = LrcLyrics.Parse("[00:01.00]I want it that way\n[00:05.00]\n[00:06.00]Tell me why");
        var one = new List<KaraokeLineResult> { new KaraokeLineResult { Line = 0, Text = "I want it that way",
            Missed = { new KaraokeMiss { Word = "that", Sound = "th" } } } };
        Check(KaraokeScorer.Score(sung.Where(l => !l.IsBlank).ToList(), one) == 88, "karaoke: score 7/8 words");
        Check(KaraokeScorer.Score(new List<LrcLine>(), one) == 0, "karaoke: no words -> 0");
        Check(KaraokeScorer.Highlight("I want it that way", one[0].Missed, "#f00") == "I want it <color=#f00>that</color> way", "karaoke: highlight");
        Check(KaraokeScorer.Highlight("That's that.", new[] { new KaraokeMiss { Word = "that" } }, "#f00") == "That's <color=#f00>that</color>.",
              "karaoke: highlight whole word only");
        Check(KaraokeScorer.Sounds(one).Single().Code == "th" && KaraokeScorer.Sounds(one).Single().Word == "that", "karaoke: sounds");
        // 壊れた返事は null (100 点にしない)。lines が空の正しい返事は空の結果
        Check(GeminiApiClient.ParseKaraokeCheck("not json", ls) == null && GeminiApiClient.ParseKaraokeCheck("{\"advice\":\"x\"}", ls) == null
              && GeminiApiClient.ParseKaraokeCheck("[]", ls) == null, "karaoke: broken reply -> null");
        Check(GeminiApiClient.ParseKaraokeCheck("{\"lines\":[],\"advice\":\"\"}", ls)?.Lines.Count == 0, "karaoke: valid empty reply");
        // ---- Task 4: 保存の JSON ----
        var kj = PracticeJson.KaraokeItems(new[] { new KaraokeLineResult { Line = 0, Text = "say \"hi\"", Missed = { new KaraokeMiss { Word = "hi", Sound = "th" } } },
                                                   new KaraokeLineResult { Line = 1, Text = "ok" }, null });
        Check(kj == "[{\"line\":0,\"text\":\"say \\\"hi\\\"\",\"words\":[{\"word\":\"hi\",\"sound\":\"th\"}]}]", "karaoke json: " + kj);
        Check(PracticeJson.KaraokeItems(null) == "[]", "karaoke json: null -> []");
        // ---- Task 5: Discord の文 ----
        var many = new List<KaraokeLineResult> { new KaraokeLineResult { Line = 0, Text = "x" } };
        for (int i = 0; i < 20; i++) many[0].Missed.Add(new KaraokeMiss { Word = "w" + i, Sound = "th" });
        string msg = DiscordClient.KaraokeMessage(new DateTime(2026, 11, 10), "Song", "Artist", 75, many, "thを練習しよう");
        Check(msg.Contains("カラオケ 2026-11-10") && msg.Contains("Song / Artist") && msg.Contains("点数: 75 / 100") && msg.Contains("thを練習しよう"),
              "karaoke discord: header, score, advice");
        Check(msg.Contains("w0 (th)") && msg.Contains("w14 (th)") && !msg.Contains("w15 (th)"), "karaoke discord: max 15 words");
        Check(DiscordClient.KaraokeMessage(new DateTime(2026, 11, 10), "Song", "", 100, new List<KaraokeLineResult>(), "").Contains("言えなかった単語: なし"),
              "karaoke discord: no words");

        // ---- Task 7: あとで送る ----
        Check(GeminiLater.KindLabel(GeminiLater.Karaoke) == "Karaoke", "later: karaoke label");
        Check(GeminiLater.ParseIndexes("0,2,5").SequenceEqual(new[] { 0, 2, 5 }) && GeminiLater.ParseIndexes("").Count == 0
              && GeminiLater.ParseIndexes("x,1").SequenceEqual(new[] { 1 }) && GeminiLater.ParseIndexes(null).Count == 0, "later: parse indexes");
        // ---- Task 6: 録音の切り出し ----
        var pcm = new byte[16000 * 2 * 10];   // 16kHz・モノラル・10 秒
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (byte)(i / 32000);   // 1 秒ごとに値が変わる
        var wav10 = WavSplitter.FromPcm(pcm, 1, 16000);
        Check(Math.Abs(KaraokeWav.Seconds(wav10) - 10) < 0.001, "wav: 10 seconds");
        var cut = KaraokeWav.Slice(wav10, 2.0, 4.5);
        Check(cut.Length == 44 + 16000 * 2 * 2 + 16000 && cut[44] == 2 && cut[cut.Length - 1] == 4, "wav: slice 2.0-4.5s");
        Check(KaraokeWav.Slice(wav10, 9, 30).Length == 44 + 32000 && KaraokeWav.Slice(wav10, 11, 12) == null && KaraokeWav.Slice(null, 0, 1) == null,
              "wav: clamp to the end, outside -> null");
        // ---- 一時停止しても、録音と動画の時刻を合わせる ----
        var clock = new KaraokeClock();
        clock.Add(rec: 0.5, video: 0.5);
        clock.Add(rec: 10.5, video: 10.5);
        clock.Add(rec: 25.5, video: 11.5);   // 13 秒止まっていた (録音は進み、動画は 1 秒だけ)
        Check(clock.RecordSeconds(5) == 5 && clock.RecordSeconds(12) == 26 && clock.RecordSeconds(0) == 0, "clock: maps video to recording after a pause");
        Check(new KaraokeClock().RecordSeconds(7) == 7 && new KaraokeClock(2).RecordSeconds(7) == 5, "clock: no anchors -> start offset");
        var paused = LrcLyrics.Chunks(long4, 0, clock.RecordSeconds, 30);
        Check(paused.Count == 1 && paused[0].lines.First() == 0 && paused[0].lines.Last() == 1 && paused[0].end == 30,
              "lrc: chunks with a clock " + string.Join(",", paused.Select(c => c.start + "-" + c.end + ":" + string.Join("/", c.lines))));
    }
}
