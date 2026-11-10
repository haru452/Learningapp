using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>時刻つきの歌詞の 1 行 (Time = 曲の始まりからの秒)</summary>
public class LrcLine
{
    public double Time;
    public string Text = "";
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// LRC 形式の歌詞 ([mm:ss.xx]歌詞) を読み、動画とのずれ・今の行・歌った行・採点に送る切り方を計算する (純粋な部分)。
/// offset は「歌詞の時刻に足すと動画の時刻になる秒」(正 = 動画にイントロがある)。
/// </summary>
public static class LrcLyrics
{
    /// <summary>動画と歌詞の長さの差がこれ以内なら、ずらさない</summary>
    public const double SameLengthSeconds = 3;
    /// <summary>これより長い差はイントロと見ない (終わりのおまけの映像など。± ボタンで直す)</summary>
    public const double MaxAutoIntroSeconds = 15;
    /// <summary>採点に 1 回で送る録音の最大の秒</summary>
    public const int MaxChunkSeconds = 180;

    private static readonly Regex Stamp = new Regex(@"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]");

    public static List<LrcLine> Parse(string lrc)
    {
        var result = new List<LrcLine>();
        if (string.IsNullOrEmpty(lrc)) return result;
        foreach (string raw in lrc.Replace("\r", "").Split('\n'))
        {
            var times = new List<double>();
            int pos = 0;
            Match m;
            // 行の先頭に続く時刻だけを読む ([00:15.00][01:15.00]歌詞)
            while ((m = Stamp.Match(raw, pos)).Success && m.Index == pos)
            {
                double t = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
                if (m.Groups[3].Success)
                    t += int.Parse(m.Groups[3].Value) / Math.Pow(10, m.Groups[3].Value.Length);
                times.Add(t);
                pos = m.Index + m.Length;
            }
            if (times.Count == 0) continue;   // [ar:..] などのタグ・壊れた行
            string text = raw.Substring(pos).Trim();
            foreach (double t in times) result.Add(new LrcLine { Time = t, Text = text });
        }
        return result.OrderBy(l => l.Time).ToList();   // OrderBy は同じ時刻の順を保つ
    }

    /// <summary>動画の方が 3〜15 秒長ければ、その差をイントロと見てずらす。それ以外は 0</summary>
    public static double AutoOffset(int videoSeconds, double lyricsSeconds)
    {
        double diff = videoSeconds - lyricsSeconds;
        return diff > SameLengthSeconds && diff <= MaxAutoIntroSeconds ? diff : 0;
    }

    /// <summary>動画の時刻で今の行 (始まった最後の行)。まだ始まっていなければ -1</summary>
    public static int IndexAt(IList<LrcLine> lines, double videoTime, double offset)
    {
        int index = -1;
        for (int i = 0; lines != null && i < lines.Count; i++)
        {
            if (lines[i].Time + offset <= videoTime) index = i;
            else break;
        }
        return index;
    }

    /// <summary>止めた時刻 (動画の時刻) までに始まった、空でない行。歌わなかった残りの行は入らない</summary>
    public static List<LrcLine> SungLines(IList<LrcLine> lines, double stoppedAt, double offset) =>
        (lines ?? new List<LrcLine>()).Where(l => !l.IsBlank && l.Time + offset < stoppedAt).ToList();

    /// <summary>
    /// 録音 (動画の recordStart〜recordEnd 秒) を、行の境目で最大 maxSeconds ずつに切る。
    /// start / end は録音の始まりからの秒。lines はその部分の行番号 (空行は入れない)
    /// </summary>
    public static List<(double start, double end, List<int> lines)> Chunks(IList<LrcLine> lines, double offset,
        double recordStart, double recordEnd, int maxSeconds = MaxChunkSeconds) =>
        Chunks(lines, offset, t => t - recordStart, recordEnd - recordStart, maxSeconds);

    /// <summary>toRecord: 動画の時刻 → 録音の秒 (一時停止があっても合わせる KaraokeClock.RecordSeconds)。recordSeconds = 録音の長さ</summary>
    public static List<(double start, double end, List<int> lines)> Chunks(IList<LrcLine> lines, double offset,
        Func<double, double> toRecord, double recordSeconds, int maxSeconds = MaxChunkSeconds)
    {
        var result = new List<(double start, double end, List<int> lines)>();
        if (lines == null || toRecord == null || recordSeconds <= 0) return result;
        double total = recordSeconds;
        double Rec(double videoTime) => Math.Max(0, Math.Min(total, toRecord(videoTime)));

        double chunkStart = 0, chunkEnd = 0;
        var current = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].IsBlank) continue;
            double start = lines[i].Time + offset;
            double end = i + 1 < lines.Count ? lines[i + 1].Time + offset : double.MaxValue;
            if (toRecord(start) >= total || (end < double.MaxValue && toRecord(end) <= 0)) continue;   // 録音していない部分
            double s = Rec(start), e = end == double.MaxValue ? total : Rec(end);
            if (current.Count > 0 && e - chunkStart > maxSeconds)
            {
                result.Add((chunkStart, chunkEnd, current));
                current = new List<int>();
            }
            if (current.Count == 0) chunkStart = s;
            current.Add(i);
            chunkEnd = e;
        }
        if (current.Count > 0) result.Add((chunkStart, chunkEnd, current));
        return result;
    }

    /// <summary>単語の数 (点数の分母)</summary>
    public static int WordCount(string text) => Words(text).Count;

    /// <summary>歌詞の単語 (小文字、前後の記号なし。don't などの ' は残す)</summary>
    public static List<string> Words(string text) =>
        (text ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Clean).Where(w => w.Length > 0).ToList();

    public static string Clean(string word) =>
        (word ?? "").Trim().Trim(".,!?;:\"()[]{}…-—“”‘’".ToCharArray()).Trim('\'').ToLowerInvariant();
}
