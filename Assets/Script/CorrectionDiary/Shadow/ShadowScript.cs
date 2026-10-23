using System;
using System.Collections.Generic;

/// <summary>シャドーイングのお手本 (Gemini が YouTube の動画から作った文字起こし) を読む</summary>
public static class ShadowScript
{
    public const int MaxWordsPerSentence = 40;
    public const int MaxWords = 3000;   // 15 分の動画 (約 2200 語) が入るように

    /// <summary>{"sentences":[{"text"}]} を読む。空・長すぎる文は捨て、全体 1500 語まで。読めなければ空</summary>
    public static List<string> Parse(string json)
    {
        var list = new List<string>();
        try
        {
            if (!(MiniJson.Parse(json ?? "") is Dictionary<string, object> root)) return list;
            if (!root.TryGetValue("sentences", out object arr) || !(arr is List<object> rows)) return list;
            int total = 0;
            foreach (var o in rows)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                string text = TatoebaCatalog.Str(d, "text").Trim();
                int words = WordCount(text);
                if (words == 0 || words > MaxWordsPerSentence) continue;
                if (total + words > MaxWords) break;
                total += words;
                list.Add(text);
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    /// <summary>途中で止めたとき: 録音した割合までの文 (+1 文の余裕)。ほぼ最後までなら全部</summary>
    public static List<string> SentencesUpTo(IList<string> sentences, double fraction)
    {
        var result = new List<string>(sentences ?? new List<string>());
        if (fraction >= 0.98 || result.Count == 0) return result;
        int n = (int)Math.Ceiling(result.Count * Math.Max(0, fraction)) + 1;
        return result.GetRange(0, Math.Max(1, Math.Min(result.Count, n)));
    }

    /// <summary>
    /// 動画のどこまでシャドーイングしたか (0〜1)。動画の再生位置があればそれ (一時停止・広告の時間を数えない)、
    /// 無ければ録音した時間。最後まで流れたら 1
    /// </summary>
    public static double Fraction(double videoSeconds, double recordedSeconds, double durationSeconds, bool ended)
    {
        if (ended || durationSeconds <= 0) return 1.0;
        double f = videoSeconds > 0 ? videoSeconds / durationSeconds : recordedSeconds / durationSeconds;
        return Math.Max(0, Math.Min(1, f));
    }

    /// <summary>録音を parts 個に分けたうちの part 番目に当たるお手本の文 (前後 2 文の余裕)。書き起こしの手がかりにする</summary>
    public static string ReferenceSlice(IList<string> sentences, int part, int parts)
    {
        if (sentences == null || sentences.Count == 0) return "";
        if (parts <= 1) return string.Join(" ", sentences);
        int n = sentences.Count;
        int from = Math.Max(0, n * part / parts - 2);
        int to = Math.Min(n, (int)Math.Ceiling((double)n * (part + 1) / parts) + 2);
        var slice = new List<string>();
        for (int i = from; i < to; i++) slice.Add(sentences[i]);
        return string.Join(" ", slice);
    }

    private static int WordCount(string s) =>
        (s ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
}
