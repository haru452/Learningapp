using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>シャドーイングの判定: 5 文をつなげて書き起こしと照合し、文ごとに分ける</summary>
public static class ShadowScorer
{
    public class SentenceScore
    {
        public string Text;
        public List<ReadAloudMatcher.Word> Words = new List<ReadAloudMatcher.Word>();
        public int Accuracy;
    }

    public static (List<SentenceScore> scores, int average, bool empty) Score(IList<string> sentences, string heard)
    {
        sentences = sentences ?? new List<string>();
        bool empty = string.IsNullOrWhiteSpace(heard);
        var all = ReadAloudMatcher.Match(string.Join(" ", sentences), empty ? "" : heard).Words;

        var scores = new List<SentenceScore>();
        int pos = 0;
        foreach (string s in sentences)
        {
            int count = ReadAloudMatcher.Match(s ?? "", "").Words.Count;
            var words = all.Skip(pos).Take(count).ToList();
            pos += count;
            int ok = words.Count(w => w.Matched);
            scores.Add(new SentenceScore
            {
                Text = s ?? "",
                Words = words,
                Accuracy = words.Count == 0 ? 0 : (int)Math.Round(100.0 * ok / words.Count, MidpointRounding.AwayFromZero),
            });
        }
        int average = scores.Count == 0 ? 0
            : (int)Math.Round(scores.Average(x => x.Accuracy), MidpointRounding.AwayFromZero);
        return (scores, empty ? 0 : average, empty);
    }

    private const int HeardNoteMaxWords = 8;

    /// <summary>赤い語のあとに出す「代わりに聞こえた語」。何も聞こえなければ null。長ければ 8 語で切る</summary>
    public static string HeardNote(string heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) return null;
        var words = heard.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= HeardNoteMaxWords ? string.Join(" ", words)
            : string.Join(" ", words.Take(HeardNoteMaxWords)) + " …";
    }
}
