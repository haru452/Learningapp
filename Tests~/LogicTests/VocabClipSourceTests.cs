// 単語帳の動画の場面を集める流れ (VocabClipSource) と端末の保存 (VocabClipStore) のテスト。
// 通信は偽物の関数、保存はメモリの IVocabClipStorage で置き換える。VocabClipTests() から呼ぶ。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public static partial class LogicTests
{
    // メモリだけの保存先
    private class MemClipStorage : IVocabClipStorage
    {
        public Dictionary<string, VocabClipEntry> Entries = new Dictionary<string, VocabClipEntry>();
        public VocabClipSearchLog Log = new VocabClipSearchLog();
        public int Saves;
        public VocabClipEntry Load(string word) => Entries.TryGetValue(VocabDeck.Key(word), out var e) ? e : null;
        public void Save(VocabClipEntry e) { Saves++; Entries[VocabDeck.Key(e.Word)] = e; }
        public VocabClipSearchLog LoadLog() => VocabClipSearchLog.FromJson(Log.ToJson());
        public void SaveLog(VocabClipSearchLog log) => Log = VocabClipSearchLog.FromJson(log.ToJson());
    }

    // 偽物の通信の組
    private class ClipFakes
    {
        public DateTime Now = new DateTime(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);
        public MemClipStorage Storage = new MemClipStorage();
        public List<VocabClip> Cloud = new List<VocabClip>();
        public List<VocabClip> Added;
        public int CloudCalls, SearchCalls, AddCalls;
        public List<string> TranscriptCalls = new List<string>();
        public List<string> SearchedWords = new List<string>();
        public Exception SearchError;
        public Exception TranscriptError;
        public Exception CloudError;
        public List<string> Logs = new List<string>();
        public TaskCompletionSource<List<YouTubeSearchClient.Video>> SearchGate;
        public Dictionary<string, List<TranscriptLine>> Transcripts = new Dictionary<string, List<TranscriptLine>>();
        public bool SignedIn = true, HasKey = true;

        public VocabClipSource Make()
        {
            Func<IList<string>, Task<List<VocabClip>>> getCloud = words =>
            {
                CloudCalls++;
                if (CloudError != null) return Task.FromException<List<VocabClip>>(CloudError);
                var keys = new HashSet<string>(words);
                return Task.FromResult(Cloud.Where(c => keys.Contains(c.Word)).ToList());
            };
            Func<IList<VocabClip>, Task> addCloud = clips => { AddCalls++; Added = clips.ToList(); return Task.CompletedTask; };
            Func<string, Task<List<YouTubeSearchClient.Video>>> search = word =>
            {
                SearchCalls++;
                SearchedWords.Add(word);
                if (SearchError != null) return Task.FromException<List<YouTubeSearchClient.Video>>(SearchError);
                if (SearchGate != null) return SearchGate.Task;
                return Task.FromResult(TenVideos());
            };
            Func<string, Task<List<TranscriptLine>>> transcript = id =>
            {
                TranscriptCalls.Add(id);
                if (TranscriptError != null) return Task.FromException<List<TranscriptLine>>(TranscriptError);
                return Task.FromResult(Transcripts.TryGetValue(id, out var l) ? l : null);
            };
            return new VocabClipSource(SignedIn ? getCloud : null, SignedIn ? addCloud : null,
                HasKey ? search : null, transcript, Storage, () => Now, Logs.Add);
        }

        public string Today => Now.ToLocalTime().ToString("yyyy-MM-dd");
    }

    private static List<YouTubeSearchClient.Video> TenVideos()
    {
        var list = new List<YouTubeSearchClient.Video>();
        for (int i = 1; i <= 10; i++) list.Add(new YouTubeSearchClient.Video { id = "v" + i, title = "t" + i, channel = "ch" + i, durationSeconds = 300 });
        return list;
    }

    private static List<TranscriptLine> Lines(params string[] texts)
    {
        var list = new List<TranscriptLine>();
        for (int i = 0; i < texts.Length; i++) list.Add(new TranscriptLine { Start = 10 + i * 5, Duration = 4, Text = texts[i] });
        return list;
    }

    private static VocabClip CloudClip(string word, string video, int start)
        => new VocabClip { Word = word, VideoId = video, StartMs = start, EndMs = start + 3000, Sentence = "stay " + word, Channel = "c", Source = "channel" };

    private static T Run<T>(Task<T> t) => t.GetAwaiter().GetResult();

    private static void VocabClipSourceTests()
    {
        VocabClipStoreJsonTests();
        ClipSourceStoredTest();
        ClipSourceCloudTest();
        ClipSourceSearchStopsTest();
        ClipSourceNoneTest();
        ClipSourceQuotaTest();
        ClipSourceDailyLimitTest();
        ClipSourceOfflineTest();
        ClipSourceSignedOutTest();
        ClipSourceNoKeyTest();
        ClipSourceSameTaskTest();
        ClipSourceFillFromCloudTest();
        VocabClipErrorTests();
    }

    private static void VocabClipStoreJsonTests()
    {
        var at = new DateTime(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);
        var e = new VocabClipEntry { Word = "calm", CheckedAt = at, Clips = new List<VocabClip> { CloudClip("calm", "a", 1000) } };
        var back = VocabClipStore.EntryFromJson(VocabClipStore.EntryToJson(e));
        Check(back != null && back.Word == "calm" && back.CheckedAt == at && back.CheckedAt.Kind == DateTimeKind.Utc, "clipstore json: word/checkedAt");
        Check(back != null && back.Clips.Count == 1 && back.Clips[0].VideoId == "a" && back.Clips[0].StartMs == 1000, "clipstore json: clips");
        Check(VocabClipStore.EntryFromJson("{broken") == null, "clipstore json: broken -> null");
        Check(VocabClipStore.EntryFromJson("") == null && VocabClipStore.EntryFromJson("[]") == null, "clipstore json: empty/array -> null");

        Check(!VocabClipStore.IsReady(null, at), "clipstore ready: null");
        Check(VocabClipStore.IsReady(e, at.AddDays(400)), "clipstore ready: clips any age");
        var none = new VocabClipEntry { Word = "x", CheckedAt = at, Clips = new List<VocabClip>() };
        Check(VocabClipStore.IsReady(none, at.AddDays(29)), "clipstore ready: 0 clips 29 days");
        Check(!VocabClipStore.IsReady(none, at.AddDays(31)), "clipstore ready: 0 clips 31 days");
    }

    private static void ClipSourceStoredTest()
    {
        var f = new ClipFakes();
        f.Storage.Save(new VocabClipEntry { Word = "calm", CheckedAt = f.Now.AddDays(-100), Clips = new List<VocabClip> { CloudClip("calm", "a", 0) } });
        var r = Run(f.Make().GetAsync("Calm", true));
        Check(r.status == ClipStatus.Ok && r.clips.Count == 1, "clipsource stored: ok");
        Check(f.CloudCalls == 0 && f.SearchCalls == 0 && f.TranscriptCalls.Count == 0, "clipsource stored: no network");
    }

    private static void ClipSourceCloudTest()
    {
        var f = new ClipFakes();
        f.Cloud.Add(CloudClip("calm", "a", 0));
        f.Cloud.Add(CloudClip("other", "b", 0));
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Ok && r.clips.Count == 1 && r.clips[0].Word == "calm", "clipsource cloud: ok");
        Check(f.CloudCalls == 1 && f.SearchCalls == 0, "clipsource cloud: no search");
        var saved = f.Storage.Load("calm");
        Check(saved != null && saved.Clips.Count == 1 && saved.CheckedAt == f.Now, "clipsource cloud: saved");
    }

    private static void ClipSourceSearchStopsTest()
    {
        var f = new ClipFakes();
        f.Transcripts["v1"] = Lines("nothing here");
        f.Transcripts["v2"] = Lines("keep calm", "so calm now", "calm again");
        f.Transcripts["v3"] = Lines("be calm");
        f.Transcripts["v4"] = Lines("calm calm");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Ok && r.clips.Count == 3, "clipsource search: 3 clips (" + r.clips.Count + ")");
        Check(string.Join(",", f.TranscriptCalls) == "v1,v2,v3", "clipsource search: transcripts " + string.Join(",", f.TranscriptCalls));
        Check(r.clips.All(c => c.Source == "search" && c.Word == "calm"), "clipsource search: source=search");
        Check(r.clips.Count(c => c.VideoId == "v2") == 2 && r.clips[2].VideoId == "v3" && r.clips[2].Channel == "ch3", "clipsource search: per video / channel");
        Check(f.Storage.Load("calm")?.Clips.Count == 3, "clipsource search: saved");
        Check(f.AddCalls == 1 && f.Added.Count == 3, "clipsource search: added to cloud");
        Check(f.Storage.Log.Count == 1 && f.Storage.Log.Date == f.Today, "clipsource search: log used once");
    }

    private static void ClipSourceNoneTest()
    {
        var f = new ClipFakes(); // 1 本だけ字幕が読めるが単語は出ない (残りは null)
        f.Transcripts["v1"] = Lines("nothing here");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.None && r.clips.Count == 0, "clipsource none: None");
        Check(f.TranscriptCalls.Count == 5, "clipsource none: 5 videos tried (" + f.TranscriptCalls.Count + ")");
        var saved = f.Storage.Load("calm");
        Check(saved != null && saved.Clips.Count == 0 && saved.CheckedAt == f.Now, "clipsource none: 0 clips saved");
        Check(f.AddCalls == 0, "clipsource none: nothing added to cloud");
        f.Now = f.Now.AddHours(2);
        var r2 = Run(f.Make().GetAsync("calm", true));
        Check(r2.status == ClipStatus.None && f.SearchCalls == 1 && f.CloudCalls == 1, "clipsource none: same day no search");
        f.Now = f.Now.AddDays(31);
        Run(f.Make().GetAsync("calm", true));
        Check(f.SearchCalls == 2, "clipsource none: 31 days later searches again");
    }

    private static void ClipSourceQuotaTest()
    {
        var f = new ClipFakes();
        f.SearchError = new HttpException(403, "HTTP 403\n\"reason\": \"quotaExceeded\"");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Limit && r.clips.Count == 0, "clipsource quota: Limit");
        Check(f.Storage.Log.BlockedDate == f.Today, "clipsource quota: blocked today");
        Check(f.Storage.Load("calm") == null, "clipsource quota: nothing saved");
    }

    private static void ClipSourceDailyLimitTest()
    {
        var f = new ClipFakes();
        var local = f.Now.ToLocalTime();
        for (int i = 0; i < VocabClipSearchLog.DailyLimit; i++) f.Storage.Log.Used(local);
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Limit && f.SearchCalls == 0, "clipsource limit: 30 used -> Limit, no search");
    }

    private static void ClipSourceOfflineTest()
    {
        var f = new ClipFakes();
        f.SearchError = new HttpException(0, "no network");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Offline && r.clips.Count == 0, "clipsource offline: Offline");
        Check(f.Storage.Load("calm") == null, "clipsource offline: nothing saved");

        var g = new ClipFakes();
        g.TranscriptError = new HttpException(0, "no network");
        var r2 = Run(g.Make().GetAsync("calm", true));
        Check(r2.status == ClipStatus.Offline && g.Storage.Load("calm") == null, "clipsource offline: transcript offline -> Offline");
    }

    private static void ClipSourceSignedOutTest()
    {
        var f = new ClipFakes { SignedIn = false };
        f.Transcripts["v1"] = Lines("keep calm");
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.Ok && r.clips.Count == 1, "clipsource signed out: Ok");
        Check(f.Storage.Load("calm")?.Clips.Count == 1 && f.AddCalls == 0 && f.CloudCalls == 0, "clipsource signed out: saved only");
    }

    private static void ClipSourceNoKeyTest()
    {
        var f = new ClipFakes { HasKey = false };
        var r = Run(f.Make().GetAsync("calm", true));
        Check(r.status == ClipStatus.NoKey && f.CloudCalls == 1, "clipsource no key: NoKey");
        Check(f.Storage.Load("calm") == null, "clipsource no key: nothing saved");

        var g = new ClipFakes();
        var r2 = Run(g.Make().GetAsync("calm", false));
        Check(r2.status == ClipStatus.None && g.SearchCalls == 0 && g.Storage.Load("calm") == null, "clipsource no search allowed: None");
    }

    private static void ClipSourceSameTaskTest()
    {
        var f = new ClipFakes();
        f.SearchGate = new TaskCompletionSource<List<YouTubeSearchClient.Video>>();
        f.Transcripts["v1"] = Lines("keep calm");
        var src = f.Make();
        var t1 = src.GetAsync("calm", true);
        var t2 = src.GetAsync("Calm ", true);
        Check(ReferenceEquals(t1, t2), "clipsource same task: same Task");
        f.SearchGate.SetResult(TenVideos());
        var r1 = Run(t1);
        Check(r1.status == ClipStatus.Ok && f.SearchCalls == 1, "clipsource same task: search once");
        var t3 = src.GetAsync("calm", true);
        Check(!ReferenceEquals(t1, t3) && Run(t3).status == ClipStatus.Ok && f.SearchCalls == 1, "clipsource same task: removed after done");
    }

    private static void ClipSourceFillFromCloudTest()
    {
        var f = new ClipFakes();
        f.Storage.Save(new VocabClipEntry { Word = "stored", CheckedAt = f.Now, Clips = new List<VocabClip> { CloudClip("stored", "s", 0) } });
        f.Storage.Save(new VocabClipEntry { Word = "empty", CheckedAt = f.Now, Clips = new List<VocabClip>() });
        f.Cloud.Add(CloudClip("calm", "a", 0));
        f.Cloud.Add(CloudClip("calm", "b", 0));
        var r = Run(f.Make().FillFromCloudAsync(new[] { "Calm", "stored", "empty", "rare", "calm" }));
        Check(f.CloudCalls == 1, "clipsource fill: one cloud call");
        Check(r.Count == 4 && r["calm"] == ClipStatus.Ok && r["stored"] == ClipStatus.Ok && r["empty"] == ClipStatus.None && r["rare"] == ClipStatus.None, "clipsource fill: statuses");
        Check(f.Storage.Load("calm")?.Clips.Count == 2 && f.Storage.Load("rare") == null, "clipsource fill: saved found only");

        var g = new ClipFakes { SignedIn = false };
        var r2 = Run(g.Make().FillFromCloudAsync(new[] { "calm" }));
        Check(r2["calm"] == ClipStatus.None && g.Storage.Load("calm") == null, "clipsource fill: signed out -> None");
    }
}
