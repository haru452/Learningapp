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
}
