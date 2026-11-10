using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Talk の 1 回の会話の記録 (発話・言い直し・使えた単語) と、End で出すまとめ。
/// Unity に依存しない (テストで確かめる)。
/// </summary>
public class TalkSummary
{
    public const int MinTurnsForStreak = 3;   // 3 回以上話したら連続記録に数える

    private readonly List<string> _words;
    private readonly HashSet<string> _used = new HashSet<string>();
    private readonly List<(string said, string corrected, string note)> _corrections = new List<(string, string, string)>();
    private float _first = -1f, _end = -1f;

    public TalkSummary(IList<string> words)
    {
        _words = (words ?? new string[0]).ToList();
    }

    public int Turns { get; private set; }
    /// <summary>最初の発話から End まで (秒)。話していなければ 0</summary>
    public float Seconds => _first < 0f || _end < _first ? 0f : _end - _first;
    public IReadOnlyList<string> Words => _words;
    public IReadOnlyCollection<string> Used => _used;
    public IReadOnlyList<(string said, string corrected, string note)> Corrections => _corrections;
    public bool CountsForStreak => Turns >= MinTurnsForStreak;

    public void AddUserTurn(string text, float atSeconds)
    {
        Turns++;
        if (_first < 0f) _first = atSeconds;
        foreach (string w in TalkPlan.UsedWords(text, _words)) _used.Add(w);
    }

    /// <summary>言い直しを記録する (直しが無い・大文字や句読点だけの違いは記録しない)</summary>
    public void AddCorrection(string said, TalkCorrection c)
    {
        if (!TalkCoach.ShouldShow(said, c)) return;
        _corrections.Add((said ?? "", c.Corrected, c.NoteJa ?? ""));
    }

    public void End(float atSeconds) => _end = atSeconds;

    /// <summary>まとめの文</summary>
    public string Text()
    {
        int sec = (int)Math.Round(Seconds);
        var sb = new StringBuilder();
        sb.Append("Turns: ").Append(Turns).Append("   Time: ").Append(sec / 60).Append(':').Append((sec % 60).ToString("00"));
        if (_words.Count > 0)
            sb.Append("\nWords: ").Append(string.Join("  ", _words.Select(w => _used.Contains(w) ? w + " ✓" : w)));
        if (_corrections.Count == 0)
        {
            sb.Append("\n\nNo corrections. Nice!");
            return sb.ToString();
        }
        sb.Append("\n\nBetter ways to say it:");
        foreach (var c in _corrections)
        {
            sb.Append("\n• ").Append(c.said).Append("\n  → ").Append(c.corrected);
            if (c.note.Length > 0) sb.Append(" (").Append(c.note).Append(')');
        }
        return sb.ToString();
    }
}
