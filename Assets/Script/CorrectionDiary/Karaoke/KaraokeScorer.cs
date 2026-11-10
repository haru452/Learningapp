using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>うまく発音できなかった単語と、その原因の音 (SoundTypes のコード)</summary>
public class KaraokeMiss
{
    public string Word = "";
    public string Sound = "";
}

/// <summary>歌詞の 1 行の結果 (Line = 歌詞の行番号)</summary>
public class KaraokeLineResult
{
    public int Line;
    public string Text = "";
    public List<KaraokeMiss> Missed = new List<KaraokeMiss>();
}

/// <summary>Gemini の採点 (1 回分の録音)</summary>
public class KaraokeCheck
{
    public List<KaraokeLineResult> Lines = new List<KaraokeLineResult>();
    public string Advice = "";
}

/// <summary>点数・赤い単語・苦手な音 (純粋な部分。テストあり)</summary>
public static class KaraokeScorer
{
    /// <summary>歌った行の単語のうち、うまく歌えた割合 (0〜100)。歌わなかった行は入れない</summary>
    public static int Score(IList<LrcLine> sung, IEnumerable<KaraokeLineResult> results)
    {
        int words = (sung ?? new List<LrcLine>()).Sum(l => LrcLyrics.WordCount(l.Text));
        if (words == 0) return 0;
        int missed = Math.Min(words, (results ?? Enumerable.Empty<KaraokeLineResult>()).Sum(r => r.Missed.Count));
        return (int)Math.Round(100.0 * (words - missed) / words, MidpointRounding.AwayFromZero);
    }

    /// <summary>赤い単語ごとの苦手な音 (今日の発音の集計に送る。最大 20)</summary>
    public static List<SoundHit> Sounds(IEnumerable<KaraokeLineResult> results) =>
        (results ?? Enumerable.Empty<KaraokeLineResult>())
            .SelectMany(r => r.Missed)
            .Where(m => !string.IsNullOrEmpty(m.Sound))
            .Select(m => new SoundHit { Code = m.Sound, Label = "", Word = m.Word })
            .Take(20).ToList();

    /// <summary>行の中の赤い単語を &lt;color&gt; で囲む (単語ごと。ほかの単語の一部は囲まない)</summary>
    public static string Highlight(string text, IEnumerable<KaraokeMiss> missed, string color)
    {
        var set = new HashSet<string>((missed ?? Enumerable.Empty<KaraokeMiss>()).Select(m => LrcLyrics.Clean(m.Word)).Where(w => w.Length > 0));
        if (set.Count == 0 || string.IsNullOrEmpty(text)) return text ?? "";
        // 単語 = 英字と ' の並び。前後の記号はそのまま残す
        return Regex.Replace(text, @"[A-Za-z][A-Za-z']*", m =>
            set.Contains(LrcLyrics.Clean(m.Value)) ? "<color=" + color + ">" + m.Value + "</color>" : m.Value);
    }
}
