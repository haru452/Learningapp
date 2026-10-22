using System;
using System.Collections.Generic;

/// <summary>Gemini が切り出した文と時間を確かめ、続いた 5 文ずつのかたまりにする</summary>
public static class ShadowChunker
{
    public const int ChunkSize = 5;
    public const int MinLeftover = 3;
    public const int MinWords = 4;
    public const int MaxWords = 20;
    public const double Pad = 0.2;            // 切り出すときの前後の余裕 (秒)
    public const double Gap = 0.5;            // 文の間 (秒)
    public const double Tail = 1.5;           // 最後の文の後、録音を止めるまで (秒)
    public const double MaxChunkSeconds = 55; // 録音の上限 (60 秒) より短く

    public class Line
    {
        public string Text;
        public string Speaker;
        public double Start;
        public double End;
    }

    public static List<List<Line>> Chunk(IList<Line> lines, double audioSeconds)
    {
        var good = new List<Line>();
        double prevStart = double.NegativeInfinity;
        if (lines != null)
        {
            foreach (var l in lines)
            {
                if (l == null) continue;
                bool timeOk = !double.IsNaN(l.Start) && !double.IsNaN(l.End)
                              && l.Start >= 0 && l.End > l.Start && l.End <= audioSeconds && l.Start >= prevStart;
                if (!double.IsNaN(l.Start)) prevStart = Math.Max(prevStart, l.Start);
                if (!timeOk) continue;
                int words = WordCount(l.Text);
                if (words < MinWords || words > MaxWords) continue;
                good.Add(l);
            }
        }

        var chunks = new List<List<Line>>();
        for (int i = 0; i < good.Count; i += ChunkSize)
        {
            int n = Math.Min(ChunkSize, good.Count - i);
            if (n < MinLeftover) break;
            var chunk = good.GetRange(i, n);
            if (Duration(chunk, audioSeconds) <= MaxChunkSeconds) chunks.Add(chunk);
        }
        return chunks;
    }

    /// <summary>1 かたまりを流して録音し終えるまでの長さ (秒)</summary>
    public static double Duration(IList<Line> chunk, double audioSeconds)
    {
        double total = 0;
        foreach (var l in chunk)
        {
            var w = Window(l, audioSeconds);
            total += Math.Max(l.End - l.Start + 2 * Pad, w.end - w.start);
        }
        return total + Gap * Math.Max(0, chunk.Count - 1) + Tail;
    }

    public static (double start, double end) Window(Line l, double audioSeconds) =>
        (Math.Max(0, l.Start - Pad), Math.Min(audioSeconds, l.End + Pad));

    /// <summary>{"sentences":[{"text","speaker","start","end"}]} を読む。読めなければ空</summary>
    public static List<Line> ParseGemini(string json)
    {
        var list = new List<Line>();
        try
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) return list;
            if (!root.TryGetValue("sentences", out object arr) || !(arr is List<object> rows)) return list;
            foreach (var o in rows)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                string text = TatoebaCatalog.Str(d, "text").Trim();
                if (text.Length == 0) continue;
                list.Add(new Line
                {
                    Text = text,
                    Speaker = TatoebaCatalog.Str(d, "speaker").Trim(),
                    Start = d.TryGetValue("start", out object s) && s is double sd ? sd : double.NaN,
                    End = d.TryGetValue("end", out object e) && e is double ed ? ed : double.NaN,
                });
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    private static int WordCount(string s) =>
        (s ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
