// 単語帳の「動画の場面」を字幕から作る規則。Unity には依存しない (System のみ)。
// Python 版 (Tools~/clips) と同じ結果になるよう fixtures/finder_cases.json を共有する。
using System;
using System.Collections.Generic;
using System.Text;

public class TranscriptLine
{
    public double Start;     // 秒
    public double Duration;  // 秒
    public string Text;
}

public class VocabClip
{
    public string Word, VideoId, Sentence, Channel, Source; // Source は "channel" / "search"
    public int StartMs, EndMs;
}

public static class VocabClipFinder
{
    public const int MaxPerWord = 10, MaxPerVideo = 2, LeadMs = 1000;

    // 小文字にして、[a-z'] 以外で区切り、前後の ' を取る
    public static List<string> Tokens(string text)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text)) return list;
        var sb = new StringBuilder();
        foreach (char raw in text.ToLowerInvariant() + " ")
        {
            if ((raw >= 'a' && raw <= 'z') || raw == '\'') { sb.Append(raw); continue; }
            if (sb.Length == 0) continue;
            var t = sb.ToString().Trim('\'');
            if (t.Length > 0) list.Add(t);
            sb.Length = 0;
        }
        return list;
    }

    // 単語 (空白を含めば連続した単語) が区切りの中に出たら場面にする。1 動画 MaxPerVideo まで、出てきた順
    public static List<VocabClip> FindInVideo(string word, string videoId, string channel, string source, IList<TranscriptLine> lines)
    {
        var result = new List<VocabClip>();
        var target = Tokens(word);
        if (target.Count == 0 || lines == null) return result;
        foreach (var line in lines)
        {
            if (result.Count >= MaxPerVideo) break;
            if (line == null || !ContainsRun(Tokens(line.Text), target)) continue;
            int startMs = (int)Math.Max(0, Math.Round(line.Start * 1000, MidpointRounding.AwayFromZero) - LeadMs);
            int endMs = (int)Math.Round((line.Start + line.Duration) * 1000, MidpointRounding.AwayFromZero);
            result.Add(new VocabClip
            {
                Word = word, VideoId = videoId, Channel = channel, Source = source,
                StartMs = startMs, EndMs = endMs, Sentence = OneLine(line.Text)
            });
        }
        return result;
    }

    // current に found を足す。(VideoId, StartMs) が同じものは足さない。1 動画 MaxPerVideo、全体 MaxPerWord
    public static List<VocabClip> Merge(IList<VocabClip> current, IList<VocabClip> found)
    {
        var result = new List<VocabClip>();
        var seen = new HashSet<string>();
        var perVideo = new Dictionary<string, int>();
        foreach (var list in new[] { current, found })
        {
            if (list == null) continue;
            foreach (var c in list)
            {
                if (result.Count >= MaxPerWord) return result;
                if (c == null || !seen.Add(c.VideoId + "|" + c.StartMs)) continue;
                perVideo.TryGetValue(c.VideoId ?? "", out int n);
                if (n >= MaxPerVideo) continue;
                perVideo[c.VideoId ?? ""] = n + 1;
                result.Add(c);
            }
        }
        return result;
    }

    private static bool ContainsRun(List<string> tokens, List<string> target)
    {
        for (int i = 0; i + target.Count <= tokens.Count; i++)
        {
            int j = 0;
            while (j < target.Count && tokens[i + j] == target[j]) j++;
            if (j == target.Count) return true;
        }
        return false;
    }

    private static string OneLine(string text) =>
        (text ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
}
