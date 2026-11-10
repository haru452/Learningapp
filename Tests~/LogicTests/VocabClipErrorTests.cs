// 動画の場面の「失敗」の扱いのテスト (最後のレビューの直し)。
// ・YouTube に届かなかった検索は 1 日の回数を使わない。わからない失敗は Error で、準備も止める
// ・Supabase の読み込みの失敗は「場面なし」ではなく Offline
// ・字幕が 1 本も読めなかったら保存しない (30 日の「なし」にしない)
// VocabClipSourceTests() から呼ぶ。
using System;
using System.Collections.Generic;
using System.Linq;

public static partial class LogicTests
{
    private static void VocabClipErrorTests()
    {
        SearchLogRefundTest();
        ClipErrorOfflineRefundTest();
        ClipErrorBadKeyTest();
        ClipErrorPrefetchStopsTest();
        ClipErrorCloudReadTest();
        ClipErrorFillCloudTest();
        ClipErrorNoTranscriptsTest();
        ClipErrorTranscriptNoMatchTest();
        YouTubeTranscriptKeyTests();
    }

    private static void SearchLogRefundTest()
    {
        var d1 = new DateTime(2026, 11, 9, 12, 0, 0);
        var log = new VocabClipSearchLog();
        log.Used(d1); log.Used(d1);
        log.Refund(d1);
        Check(log.Count == 1, "searchlog refund: 2 -> 1");
        log.Refund(d1); log.Refund(d1);
        Check(log.Count == 0, "searchlog refund: not below 0");
        log.Used(d1);
        log.Refund(d1.AddDays(1));
        Check(log.Count == 1 && log.Date == "2026-11-09", "searchlog refund: other day does nothing");
    }

    private static void ClipErrorOfflineRefundTest()
    {
        var f = new ClipFakes();
        f.SearchError = new HttpException(0, "no network");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Offline, "clip error offline: Offline");
        Check(f.Storage.Log.Count == 0, "clip error offline: count unchanged (" + f.Storage.Log.Count + ")");
    }

    private static void ClipErrorBadKeyTest()
    {
        var f = new ClipFakes();
        f.SearchError = new HttpException(403, "HTTP 403 : Forbidden\n\"reason\": \"accessNotConfigured\"");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Error && r.clips.Count == 0, "clip error bad key: Error (" + r.status + ")");
        Check(f.Storage.Log.Count == 0, "clip error bad key: count unchanged (" + f.Storage.Log.Count + ")");
        Check(f.Storage.Load("calm") == null, "clip error bad key: nothing saved");
        Check(f.Logs.Any(l => l.StartsWith("[VocabClips] search failed:") && l.Contains("403") && l.Contains("accessNotConfigured")),
            "clip error bad key: logged " + string.Join(" | ", f.Logs));
        Check(VocabVideosView.MessageFor(ClipStatus.Error) == "No videos for this word yet.", "clip error: message is none text");

        var g = new ClipFakes();
        g.SearchError = new FormatException("bad json");
        var r2 = Run(g.Make().GetAsync("calm", true));
        Check(r2.status == ClipStatus.Error && g.Storage.Log.Count == 0 && g.Storage.Load("calm") == null, "clip error parse: Error, refunded");
    }

    private static void ClipErrorPrefetchStopsTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "s1" && w != "s2" && w != "s3").ToArray());
        f.Clip.SearchError = new HttpException(403, "HTTP 403 : Forbidden\n\"reason\": \"accessNotConfigured\"");
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        Check(f.Clip.SearchCalls == 1, "prefetch error status: one search (" + f.Clip.SearchCalls + ")");
        Check(!p.IsRunning, "prefetch error status: stopped");
    }

    private static void ClipErrorCloudReadTest()
    {
        var f = new ClipFakes();
        f.CloudError = new HttpException(500, "HTTP 500");
        f.Transcripts["v1"] = Lines("keep calm");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Offline && r.clips.Count == 0, "clip error cloud read: Offline (" + r.status + ")");
        Check(f.SearchCalls == 0 && f.Storage.Load("calm") == null && f.Storage.Log.Count == 0, "clip error cloud read: no search, nothing saved");
    }

    private static void ClipErrorFillCloudTest()
    {
        var f = new ClipFakes();
        f.Storage.Save(new VocabClipEntry { Word = "stored", CheckedAt = f.Now, Clips = new List<VocabClip> { CloudClip("stored", "s", 0) } });
        f.CloudError = new HttpException(0, "no network");
        var r = Run(f.Make().FillFromCloudAsync(new[] { "calm", "rare", "stored" }));
        Check(r["calm"] == ClipStatus.Offline && r["rare"] == ClipStatus.Offline, "clip error fill: Offline for asked words");
        Check(r["stored"] == ClipStatus.Ok, "clip error fill: stored word keeps Ok");
        Check(f.Storage.Load("calm") == null, "clip error fill: nothing saved");

        var p = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        p.Ready(p.Session.Concat(p.NextNew).Where(w => w != "s1" && w != "s2").ToArray());
        p.Clip.CloudError = new HttpException(0, "no network");
        var pf = p.Make();
        pf.Kick();
        Wait(pf.RunningTask);
        Check(p.Clip.SearchCalls == 0 && p.Clip.CloudCalls == 1, "prefetch cloud error: no search (" + p.Clip.SearchCalls + "," + p.Clip.CloudCalls + ")");
    }

    private static void ClipErrorNoTranscriptsTest()
    {
        var f = new ClipFakes(); // 字幕は全部 null
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Error && r.clips.Count == 0, "clip no transcripts: Error (" + r.status + ")");
        Check(f.Storage.Load("calm") == null, "clip no transcripts: nothing saved");
        Check(f.Storage.Log.Count == 1, "clip no transcripts: search counted");
        Check(f.Logs.Any(l => l == "[VocabClips] no transcripts for \"calm\" (5 videos)"), "clip no transcripts: logged " + string.Join(" | ", f.Logs));

        var g = new ClipFakes();
        g.TranscriptError = new HttpException(500, "HTTP 500");
        var r2 = Run(g.Make().GetAsync("calm", true));
        Check(r2.status == ClipStatus.Error && g.Storage.Load("calm") == null, "clip transcripts failed: Error, nothing saved");
    }

    private static void ClipErrorTranscriptNoMatchTest()
    {
        var f = new ClipFakes();
        f.Transcripts["v3"] = Lines("nothing to see");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.None, "clip transcript no match: None (" + r.status + ")");
        var saved = f.Storage.Load("calm");
        Check(saved != null && saved.Clips.Count == 0, "clip transcript no match: 0 clips saved");
    }

    // youtube-transcript-api 1.x に合わせた部分 (watch ページのキー、PoToken が要る字幕)
    private static void YouTubeTranscriptKeyTests()
    {
        string html = "<script>ytcfg.set({\"INNERTUBE_API_KEY\":\"AIzaTest_key-1\",\"X\":1});</script>";
        Check(YouTubeTranscriptClient.ExtractInnertubeKey(html) == "AIzaTest_key-1", "yt key: found");
        Check(YouTubeTranscriptClient.ExtractInnertubeKey("{\"INNERTUBE_API_KEY\": \"abc\"}") == "abc", "yt key: space after colon");
        Check(YouTubeTranscriptClient.ExtractInnertubeKey("<html>no key</html>") == null, "yt key: not found -> null");
        Check(YouTubeTranscriptClient.ExtractInnertubeKey(null) == null, "yt key: null -> null");

        string xpe = "{\"captions\":{\"playerCaptionsTracklistRenderer\":{\"captionTracks\":[" +
                     "{\"baseUrl\":\"https://x.test/t?lang=en&exp=xpe&fmt=srv3\",\"languageCode\":\"en\"}]}}}";
        Check(YouTubeTranscriptClient.PickTrackUrl(xpe) == null, "yt pick: exp=xpe (PoToken) -> null");
    }
}
