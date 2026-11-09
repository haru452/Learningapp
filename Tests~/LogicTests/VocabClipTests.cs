// 単語帳の動画の場面 (VocabClipFinder) のテスト。Program.cs の Main から VocabClipTests() を呼ぶ。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public static partial class LogicTests
{
    // Assets があるフォルダまで上へたどって、リポジトリの根を探す
    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "Assets"))) return d.FullName;
                d = d.Parent;
            }
        }
        var env = Environment.GetEnvironmentVariable("UNITY_REPO");
        return env ?? Directory.GetCurrentDirectory();
    }

    private static void VocabClipTests()
    {
        VocabClipFixtureTests();
        VocabClipMergeTests();
        YouTubeTranscriptTests();
        YouTubeQuotaTests();
        VocabClipSearchLogTests();
        VocabClipSupabaseJsonTests();
        VocabClipSourceTests();
        VocabClipPrefetcherTests();
        YouTubePlayerVarsTests();
    }

    private static void VocabClipFixtureTests()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools~", "clips", "fixtures", "finder_cases.json");
        var cases = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        foreach (var c in cases.EnumerateArray())
        {
            var name = c.GetProperty("name").GetString();
            var lines = c.GetProperty("lines").EnumerateArray().Select(l => new TranscriptLine
            {
                Start = l.GetProperty("start").GetDouble(),
                Duration = l.GetProperty("duration").GetDouble(),
                Text = l.GetProperty("text").GetString()
            }).ToList();
            var expect = c.GetProperty("expect").EnumerateArray().ToList();
            var got = VocabClipFinder.FindInVideo(c.GetProperty("word").GetString(), "v1", "ch", "channel", lines);
            Check(got.Count == expect.Count, "clip finder [" + name + "]: count " + got.Count + " vs " + expect.Count);
            for (int i = 0; i < Math.Min(got.Count, expect.Count); i++)
            {
                Check(got[i].StartMs == expect[i].GetProperty("startMs").GetInt32(), "clip finder [" + name + "]: startMs " + got[i].StartMs);
                Check(got[i].EndMs == expect[i].GetProperty("endMs").GetInt32(), "clip finder [" + name + "]: endMs " + got[i].EndMs);
                Check(got[i].Sentence == expect[i].GetProperty("sentence").GetString(), "clip finder [" + name + "]: sentence '" + got[i].Sentence + "'");
                Check(got[i].VideoId == "v1" && got[i].Channel == "ch" && got[i].Source == "channel", "clip finder [" + name + "]: fields");
            }
        }
        Check(string.Join("|", VocabClipFinder.Tokens("'Don't  stop-it' 2x!")) == "don't|stop|it|x", "clip tokens");
    }

    private static VocabClip MakeClip(string video, int startMs) =>
        new VocabClip { Word = "w", VideoId = video, StartMs = startMs, EndMs = startMs + 1000, Sentence = "s", Channel = "c", Source = "search" };

    private static void VocabClipMergeTests()
    {
        var many = new List<VocabClip>();
        for (int v = 0; v < 6; v++) { many.Add(MakeClip("v" + v, 1000)); many.Add(MakeClip("v" + v, 2000)); }
        Check(VocabClipFinder.Merge(new List<VocabClip>(), many).Count == 10, "clip merge: 12 -> 10");
        var dup = VocabClipFinder.Merge(new List<VocabClip>(), new List<VocabClip> { MakeClip("a", 5), MakeClip("a", 5) });
        Check(dup.Count == 1, "clip merge: same (video,start) once");
        var cur = new List<VocabClip> { MakeClip("a", 5) };
        Check(VocabClipFinder.Merge(cur, new List<VocabClip> { MakeClip("a", 5) }).Count == 1, "clip merge: already in current");
        var three = VocabClipFinder.Merge(new List<VocabClip>(), new List<VocabClip> { MakeClip("a", 1), MakeClip("a", 2), MakeClip("a", 3) });
        Check(three.Count == 2, "clip merge: 3 in one video -> 2");
    }
    // YouTubeTranscriptClient の字幕トラック選びと timedtext の読み取り
    private static void YouTubeTranscriptTests()
    {
        var dir = Path.Combine(FindRepoRoot(), "Tests~", "LogicTests", "fixtures");
        var player = File.ReadAllText(Path.Combine(dir, "player_response.json"));
        Check(YouTubeTranscriptClient.PickTrackUrl(player) == "https://example.test/api/timedtext?v=abc&lang=en", "yt pick: human en preferred, fmt removed");

        string Track(string lang, string kind) =>
            "{\"baseUrl\":\"https://x.test/t?lang=" + lang + "&fmt=srv3\",\"languageCode\":\"" + lang + "\"" + (kind != null ? ",\"kind\":\"" + kind + "\"" : "") + "}";
        string Wrap(params string[] tracks) =>
            "{\"captions\":{\"playerCaptionsTracklistRenderer\":{\"captionTracks\":[" + string.Join(",", tracks) + "]}}}";
        Check(YouTubeTranscriptClient.PickTrackUrl(Wrap(Track("en", "asr"))) == "https://x.test/t?lang=en", "yt pick: asr only");
        Check(YouTubeTranscriptClient.PickTrackUrl(Wrap(Track("ja", null))) == null, "yt pick: ja only -> null");
        Check(YouTubeTranscriptClient.PickTrackUrl("{\"videoDetails\":{}}") == null, "yt pick: no captions -> null");
        Check(YouTubeTranscriptClient.PickTrackUrl("not json") == null, "yt pick: not json -> null");
        Check(YouTubeTranscriptClient.PickTrackUrl(Wrap(Track("en-GB", null), Track("en-US", null))) == "https://x.test/t?lang=en-GB", "yt pick: first human en-*");

        var lines = YouTubeTranscriptClient.ParseTimedText(File.ReadAllText(Path.Combine(dir, "timedtext.xml")));
        Check(lines.Count == 3, "yt timedtext: 3 lines, got " + lines.Count);
        if (lines.Count == 3)
        {
            Check(lines[0].Start == 0.5 && lines[0].Duration == 2.25, "yt timedtext: start/dur");
            Check(lines[0].Text == "It's a test.", "yt timedtext: &#39; -> ' : " + lines[0].Text);
            Check(lines[1].Text == "Tom & Jerry second line", "yt timedtext: entity+newline : " + lines[1].Text);
            Check(lines[2].Start == 5.75 && lines[2].Duration == 0, "yt timedtext: missing dur -> 0");
        }
        Check(YouTubeTranscriptClient.ParseTimedText("<transcript/>").Count == 0, "yt timedtext: empty");
        Check(YouTubeTranscriptClient.ParseTimedText("<<broken").Count == 0, "yt timedtext: invalid xml");
        var adj = YouTubeTranscriptClient.ParseTimedText("<transcript><text start=\"1\" dur=\"1\">a</text><text start=\"2\" dur=\"1\">b</text><text start=\"3\" dur=\"1\">c</text></transcript>");
        Check(adj.Count == 3 && adj[1].Text == "b" && adj[2].Start == 3, "yt timedtext: adjacent elements, got " + adj.Count);
        var nested = YouTubeTranscriptClient.ParseTimedText("<transcript><text start=\"1\" dur=\"1\"><font color=\"#fff\">hi</font> there</text><text start=\"2\" dur=\"1\">next</text></transcript>");
        Check(nested.Count == 2 && nested[0].Text == "hi there" && nested[1].Text == "next", "yt timedtext: child element");
    }

    private static void YouTubeQuotaTests()
    {
        string body = "{\"error\":{\"errors\":[{\"reason\":\"quotaExceeded\"}]}}";
        Check(YouTubeSearchClient.IsQuotaExceeded(new HttpException(403, "HTTP 403 : Forbidden\n" + body)), "quota: 403 quotaExceeded");
        Check(YouTubeSearchClient.IsQuotaExceeded(new HttpException(403, "HTTP 403 : x\n\"reason\": \"dailyLimitExceeded\"")), "quota: 403 dailyLimitExceeded");
        Check(!YouTubeSearchClient.IsQuotaExceeded(new HttpException(403, "HTTP 403 : x\n\"reason\": \"forbidden\"")), "quota: 403 forbidden is not quota");
        Check(!YouTubeSearchClient.IsQuotaExceeded(new HttpException(400, "HTTP 400 : x\nquotaExceeded")), "quota: 400 is not quota");
        Check(!YouTubeSearchClient.IsQuotaExceeded(null), "quota: null");
    }

    private static void VocabClipSearchLogTests()
    {
        var d1 = new DateTime(2026, 11, 9, 23, 59, 0);
        var d2 = d1.AddMinutes(2);
        var log = new VocabClipSearchLog();
        Check(log.CanSearch(d1), "searchlog: new log can search");
        for (int i = 0; i < 29; i++) log.Used(d1);
        Check(log.CanSearch(d1), "searchlog: 29 used still ok");
        log.Used(d1);
        Check(log.Count == 30 && !log.CanSearch(d1), "searchlog: 30 used -> false");
        Check(log.CanSearch(d2), "searchlog: next day true");
        log.Used(d2);
        Check(log.Count == 1 && log.Date == "2026-11-10", "searchlog: next day restarts count");

        var b = new VocabClipSearchLog();
        b.Block(d1);
        Check(!b.CanSearch(d1) && b.CanSearch(d2), "searchlog: block today only");

        var rt = VocabClipSearchLog.FromJson(log.ToJson());
        Check(rt.Date == log.Date && rt.Count == 1 && rt.BlockedDate == "", "searchlog: roundtrip");
        var rb = VocabClipSearchLog.FromJson(b.ToJson());
        Check(rb.BlockedDate == "2026-11-09" && !rb.CanSearch(d1), "searchlog: roundtrip blocked");

        foreach (var bad in new string[] { null, "", "{broken", "[1,2]", "42" })
        {
            var f = VocabClipSearchLog.FromJson(bad);
            Check(f.Count == 0 && f.BlockedDate == "" && f.CanSearch(d1), "searchlog: bad json -> empty: " + bad);
        }
        var partial = VocabClipSearchLog.FromJson("{\"count\":5,\"x\":1}");
        Check(partial.Count == 5 && partial.Date == "", "searchlog: unknown/missing fields");
    }

    // Supabase に送る・受け取る JSON (SupabaseService.VocabClips.cs)
    private static void VocabClipSupabaseJsonTests()
    {
        Check(SupabaseService.BuildClipsJson(null) == "[]" && SupabaseService.BuildClipsJson(new List<VocabClip>()) == "[]", "clips json: empty");
        Check(SupabaseService.BuildWordsJson(null) == "[]" && SupabaseService.BuildWordsJson(new List<string>()) == "[]", "words json: empty");
        var clip = new VocabClip { Word = "run", VideoId = "abc", StartMs = 1500, EndMs = 4200, Sentence = "He said \"go\"\nnow.", Channel = "ch", Source = "channel" };
        var json = SupabaseService.BuildClipsJson(new List<VocabClip> { clip });
        var doc = JsonDocument.Parse(json).RootElement; // 正しい JSON か
        var o = doc[0];
        Check(o.GetProperty("word").GetString() == "run" && o.GetProperty("video_id").GetString() == "abc", "clips json: strings");
        Check(o.GetProperty("start_ms").GetInt32() == 1500 && o.GetProperty("end_ms").GetInt32() == 4200, "clips json: numbers");
        Check(o.GetProperty("sentence").GetString() == "He said \"go\"\nnow.", "clips json: escape");
        Check(o.GetProperty("channel").GetString() == "ch" && o.GetProperty("source").GetString() == "channel", "clips json: channel/source");
        var wj = SupabaseService.BuildWordsJson(new List<string> { "a\"b", "c" });
        Check(JsonDocument.Parse(wj).RootElement[0].GetString() == "a\"b" && wj.EndsWith(",\"c\"]"), "words json: escape");

        var rows = "[{\"id\":\"u1\",\"word\":\"run\",\"video_id\":\"abc\",\"start_ms\":1500,\"end_ms\":4200,\"sentence\":\"Hi.\",\"channel\":null,\"source\":\"search\",\"created_at\":\"x\"},"
                   + "{\"word\":\"nov\"},{\"video_id\":\"v\"},{\"word\":\"w\",\"video_id\":\"v\"}]";
        var got = SupabaseService.ParseClips(rows);
        Check(got.Count == 2, "parse clips: count " + got.Count);
        Check(got[0].Word == "run" && got[0].VideoId == "abc" && got[0].StartMs == 1500 && got[0].EndMs == 4200 && got[0].Sentence == "Hi." && got[0].Source == "search", "parse clips: fields");
        Check(got[1].StartMs == 0 && got[1].EndMs == 0, "parse clips: missing numbers are 0");
        Check(SupabaseService.ParseClips("").Count == 0 && SupabaseService.ParseClips("{\"a\":1}").Count == 0 && SupabaseService.ParseClips("[{bad").Count == 0 && SupabaseService.ParseClips(null).Count == 0, "parse clips: broken -> empty");
        var rt = SupabaseService.ParseClips(json);
        Check(rt.Count == 1 && rt[0].Sentence == clip.Sentence && rt[0].StartMs == 1500 && rt[0].Channel == "ch", "clips json round trip");
    }

    // YouTube プレーヤーに渡す start/end の部分 (切り捨て・切り上げ・end が無いとき)
    private static void YouTubePlayerVarsTests()
    {
        Check(YouTubePlayerView.BuildPlayerVars(12.4, 15.2) == "start:12,end:16", "playerVars 範囲");
        Check(YouTubePlayerView.BuildPlayerVars(12.4, 12.4) == "start:12", "end <= start は start のみ");
        Check(YouTubePlayerView.BuildPlayerVars(12.4, 5) == "start:12", "end < start は start のみ");
        Check(YouTubePlayerView.BuildPlayerVars(-2, 2.1) == "start:0,end:3", "負の start は 0");
        Check(YouTubePlayerView.BuildPlayerVars(double.NaN, 5) == "start:0,end:5", "NaN の start は 0");
        Check(YouTubePlayerView.BuildPlayerVars(3, double.PositiveInfinity) == "start:3", "Infinity の end は付けない");
    }
}
