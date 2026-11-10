// 20 単語先まで動画の場面を準備する VocabClipPrefetcher のテスト。
// 通信と保存は VocabClipSourceTests.cs の ClipFakes / MemClipStorage を使う。VocabClipTests() から呼ぶ。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public static partial class LogicTests
{
    // 準備役に渡す単語帳まわりの偽物
    private class PrefetchFakes
    {
        public ClipFakes Clip = new ClipFakes();
        public List<string> Session = new List<string>();
        public List<VocabCard> Deck = new List<VocabCard>();
        public List<string> NextNew = new List<string>();
        public List<string> Random = new List<string>();
        public int LoadNextNewCalls, SaveNextNewCalls, RandomCalls;
        public Exception LoadError;
        public List<string> Logs = new List<string>();

        public VocabClipPrefetcher Make()
        {
            return new VocabClipPrefetcher(
                Clip.Make(), Clip.Storage,
                () => Session,
                () => Deck,
                () =>
                {
                    LoadNextNewCalls++;
                    if (LoadError != null) throw LoadError;
                    return NextNew.ToList();
                },
                list => { SaveNextNewCalls++; NextNew = list.ToList(); },
                () => { RandomCalls++; return Random.ToList(); },
                () => Clip.Now,
                Logs.Add);
        }

        /// <summary>単語を準備済みにする (場面 1 つを保存)</summary>
        public void Ready(params string[] words)
        {
            foreach (var w in words)
                Clip.Storage.Save(new VocabClipEntry { Word = w, CheckedAt = Clip.Now, Clips = new List<VocabClip> { CloudClip(w, "r", 0) } });
        }
    }

    private static List<string> Words(string prefix, int n)
        => Enumerable.Range(1, n).Select(i => prefix + i).ToList();

    private static void Wait(Task t) => t.GetAwaiter().GetResult();

    private static void VocabClipPrefetcherTests()
    {
        PrefetchUpcomingOrderTest();
        PrefetchUpcomingCutTest();
        PrefetchTopUpTest();
        PrefetchAllReadyTest();
        PrefetchOneMissingTest();
        PrefetchCloudCoversTest();
        PrefetchKickThreeTimesTest();
        PrefetchLimitStopsTest();
        PrefetchLimitNoSecondPassTest();
        PrefetchNoKeyStopsTest();
        PrefetchStopTest();
        PrefetchTopUpSavedTest();
        PrefetchErrorTest();
    }

    private static void PrefetchUpcomingOrderTest()
    {
        var now = new DateTime(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);
        var deck = new List<VocabCard>
        {
            new VocabCard { Word = "k-late", Status = VocabCard.Known, DueAt = now.AddDays(5) },
            new VocabCard { Word = "l1", Status = VocabCard.Learning },
            new VocabCard { Word = "k-past", Status = VocabCard.Known, DueAt = now.AddDays(-1) },
            new VocabCard { Word = "k-nodue", Status = VocabCard.Known, DueAt = null },
            new VocabCard { Word = "Sess2", Status = VocabCard.Learning },
            new VocabCard { Word = "l2", Status = VocabCard.Learning },
            new VocabCard { Word = "k-soon", Status = VocabCard.Known, DueAt = now.AddHours(2) },
        };
        var r = VocabClipPrefetcher.UpcomingWords(new[] { "sess1", " sess2 ", "", "SESS1" }, deck, new[] { "n1", "L1", "n2" }, now);
        Check(string.Join(",", r) == "sess1,sess2,l1,l2,k-past,k-soon,k-late,n1,n2", "prefetch upcoming: order " + string.Join(",", r));
        var empty = VocabClipPrefetcher.UpcomingWords(null, null, null, now);
        Check(empty.Count == 0, "prefetch upcoming: nulls -> empty");
    }

    private static void PrefetchUpcomingCutTest()
    {
        var now = new DateTime(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);
        var deck = Words("l", 15).Select(w => new VocabCard { Word = w, Status = VocabCard.Learning }).ToList();
        var r = VocabClipPrefetcher.UpcomingWords(Words("s", 3), deck, Words("n", 10), now);
        Check(r.Count == VocabClipPrefetcher.Target && VocabClipPrefetcher.Target == 20, "prefetch upcoming: cut at 20 (" + r.Count + ")");
        Check(r[0] == "s1" && r[3] == "l1" && r[17] == "l15" && r[18] == "n1" && r[19] == "n2", "prefetch upcoming: cut keeps order");
        Check(r.Distinct().Count() == r.Count, "prefetch upcoming: no duplicates");
    }

    private static void PrefetchTopUpTest()
    {
        var r = VocabClipPrefetcher.TopUpNextNew(new[] { "a", "b" }, new[] { "B", "c", "d", "e" }, 2);
        Check(string.Join(",", r) == "a,b,c,d", "prefetch topup: only need, no overlap " + string.Join(",", r));
        var same = new List<string> { "a" };
        var r0 = VocabClipPrefetcher.TopUpNextNew(same, new[] { "x" }, 0);
        Check(r0.Count == 1 && r0[0] == "a" && !ReferenceEquals(r0, same), "prefetch topup: need 0 -> copy");
        var few = VocabClipPrefetcher.TopUpNextNew(null, new[] { "x", "x", "" }, 5);
        Check(string.Join(",", few) == "x", "prefetch topup: fewer candidates than need");
    }

    private static void PrefetchAllReadyTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).ToArray());
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        Check(f.Clip.CloudCalls == 0 && f.Clip.SearchCalls == 0, "prefetch all ready: no network");
        Check(f.RandomCalls == 0 && f.SaveNextNewCalls == 0 && !p.IsRunning, "prefetch all ready: no top up, stopped");
    }

    private static void PrefetchOneMissingTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "n7").ToArray());
        f.Clip.Transcripts["v1"] = Lines("nothing here"); // 字幕は読めるが場面は無い (0 場面で保存)
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        Check(string.Join(",", f.Clip.SearchedWords) == "n7", "prefetch one missing: searched " + string.Join(",", f.Clip.SearchedWords));
        Check(f.Clip.CloudCalls == 2, "prefetch one missing: fill + get (" + f.Clip.CloudCalls + ")");
        Check(VocabClipStore.IsReady(f.Clip.Storage.Load("n7"), f.Clip.Now), "prefetch one missing: now ready");
    }

    private static void PrefetchCloudCoversTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "s2" && w != "n3").ToArray());
        f.Clip.Cloud.Add(CloudClip("s2", "a", 0));
        f.Clip.Cloud.Add(CloudClip("n3", "b", 0));
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        Check(f.Clip.SearchCalls == 0 && f.Clip.CloudCalls == 1, "prefetch cloud covers: no search, one cloud call");
        Check(f.Clip.Storage.Load("s2")?.Clips.Count == 1 && f.Clip.Storage.Load("n3")?.Clips.Count == 1, "prefetch cloud covers: saved");
    }

    private static void PrefetchKickThreeTimesTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "s1" && w != "n1").ToArray());
        f.Clip.Transcripts["v1"] = Lines("nothing here"); // 字幕は読めるが場面は無い (0 場面で保存)
        f.Clip.SearchGate = new TaskCompletionSource<List<YouTubeSearchClient.Video>>();
        var p = f.Make();
        p.Kick();
        var first = p.RunningTask;
        p.Kick();
        p.Kick();
        Check(p.IsRunning && ReferenceEquals(first, p.RunningTask), "prefetch kick x3: one loop");
        Check(f.Clip.SearchCalls == 1 && f.LoadNextNewCalls == 1, "prefetch kick x3: one search in flight");
        f.Clip.SearchGate.SetResult(TenVideos());
        Wait(p.RunningTask);
        Check(!p.IsRunning, "prefetch kick x3: finished");
        Check(string.Join(",", f.Clip.SearchedWords) == "s1,n1", "prefetch kick x3: each word once " + string.Join(",", f.Clip.SearchedWords));
        Check(f.LoadNextNewCalls == 2, "prefetch kick x3: at most 2 passes (" + f.LoadNextNewCalls + ")");
    }

    private static void PrefetchLimitStopsTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "s1" && w != "s2" && w != "s3").ToArray());
        f.Clip.SearchError = new HttpException(403, "HTTP 403\n\"reason\": \"quotaExceeded\"");
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        Check(f.Clip.SearchCalls == 1, "prefetch limit: one search");
        Check(f.Clip.CloudCalls == 2, "prefetch limit: no more words tried (" + f.Clip.CloudCalls + ")");
        Check(f.Logs.Count(l => l.Contains("[VocabClips]")) == 1, "prefetch limit: logged once");
    }

    private static void PrefetchLimitNoSecondPassTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "s1" && w != "s2").ToArray());
        f.Clip.SearchGate = new TaskCompletionSource<List<YouTubeSearchClient.Video>>();
        var p = f.Make();
        p.Kick();
        p.Kick(); // 動いている間の Kick (again)
        f.Clip.SearchGate.SetException(new HttpException(403, "HTTP 403\n\"reason\": \"quotaExceeded\""));
        Wait(p.RunningTask);
        Check(f.LoadNextNewCalls == 1 && f.Clip.CloudCalls == 2, "prefetch limit + again: no second pass");
        Check(!p.IsRunning, "prefetch limit + again: stopped");
        // 外からの次の Kick ではまた試す (記録が「上限の日」なので検索はしない)
        p.Kick();
        Wait(p.RunningTask);
        Check(f.LoadNextNewCalls == 2 && f.Clip.SearchCalls == 1, "prefetch limit: next kick tries again without search");
    }

    private static void PrefetchNoKeyStopsTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Clip.HasKey = false;
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "s1" && w != "s2").ToArray());
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        Check(f.Clip.CloudCalls == 2 && !p.IsRunning, "prefetch no key: stops after first word");
    }

    private static void PrefetchStopTest()
    {
        var f = new PrefetchFakes { Session = Words("s", 5), NextNew = Words("n", 15) };
        f.Ready(f.Session.Concat(f.NextNew).Where(w => w != "s1" && w != "s2").ToArray());
        f.Clip.SearchGate = new TaskCompletionSource<List<YouTubeSearchClient.Video>>();
        var p = f.Make();
        p.Kick();
        p.Stop();
        f.Clip.SearchGate.SetResult(TenVideos());
        Wait(p.RunningTask);
        Check(string.Join(",", f.Clip.SearchedWords) == "s1" && !p.IsRunning, "prefetch stop: only first word");
        p.Kick();
        Wait(p.RunningTask);
        Check(f.Clip.SearchCalls == 1, "prefetch stop: kick after stop does nothing");
    }

    private static void PrefetchTopUpSavedTest()
    {
        var f = new PrefetchFakes
        {
            Session = new List<string> { "s1", "s2" },
            Deck = new List<VocabCard> { new VocabCard { Word = "l1", Status = VocabCard.Learning }, new VocabCard { Word = "s1", Status = VocabCard.Learning } },
            NextNew = new List<string> { "n1" },
            Random = new List<string> { "n1" }.Concat(Words("r", 30)).ToList(),
        };
        f.Ready(new[] { "s1", "s2", "l1", "n1" }.Concat(Words("r", 30)).ToArray());
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        // 3 語 (s1, s2, l1) + next_new 1 語 → 16 語足す
        Check(f.SaveNextNewCalls == 1 && f.NextNew.Count == 17, "prefetch topup saved: 17 (" + f.NextNew.Count + ")");
        Check(f.NextNew[0] == "n1" && f.NextNew[1] == "r1" && f.NextNew[16] == "r16", "prefetch topup saved: appended");
        var up = VocabClipPrefetcher.UpcomingWords(f.Session, f.Deck, f.NextNew, f.Clip.Now);
        Check(up.Count == 20 && up[3] == "n1" && up[4] == "r1" && up[19] == "r16", "prefetch topup saved: at end of upcoming");
        p.Kick();
        Wait(p.RunningTask);
        Check(f.SaveNextNewCalls == 1 && f.RandomCalls == 1, "prefetch topup saved: full -> not saved again");
    }

    private static void PrefetchErrorTest()
    {
        var f = new PrefetchFakes { LoadError = new InvalidOperationException("broken") };
        var p = f.Make();
        p.Kick();
        Wait(p.RunningTask);
        Check(!p.IsRunning && f.Logs.Any(l => l.Contains("broken")), "prefetch error: logged, not stuck");
        f.LoadError = null;
        p.Kick();
        Wait(p.RunningTask);
        Check(f.LoadNextNewCalls == 2, "prefetch error: next kick runs");
    }
}
