using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Talk の練習の表示 (OnGUI): 今日の単語の並び・最新の言い直し・まとめのパネル。
/// ConversationController.OnGUI の中から呼ぶ (同じ GUILayout の領域に並べる)。
/// </summary>
public static class TalkCoachView
{
    private const string SaidColor = "#9aa0a6";    // 言った文 (灰色)
    private const string BetterColor = "#40c057";  // よりよい言い方 (緑)

    /// <summary>今日の単語。使えた語に ✓。単語が無ければ何も出さない</summary>
    public static void Words(IReadOnlyList<string> words, IReadOnlyCollection<string> used, GUIStyle label)
    {
        if (words == null || words.Count == 0) return;
        string line = string.Join("   ", words.Select(w => used != null && used.Contains(w) ? "<b>" + w + " ✓</b>" : w));
        GUILayout.Label("Today's words:  " + line, label);
    }

    /// <summary>最新の言い直し (✎ 言った文 → よりよい言い方 (説明))</summary>
    public static void Correction(string said, TalkCorrection c, GUIStyle label)
    {
        if (!TalkCoach.ShouldShow(said, c)) return;
        string note = string.IsNullOrEmpty(c.NoteJa) ? "" : "  (" + c.NoteJa + ")";
        GUILayout.Label("✎ <color=" + SaidColor + ">" + said + "</color>\n   → <color=" + BetterColor + ">" + c.Corrected + "</color>" + note, label);
    }

    /// <summary>まとめのパネル。押したボタン: 1 = Talk again、2 = Close、0 = なし</summary>
    public static int Summary(TalkSummary summary, bool counted, GUIStyle label, GUIStyle button, float buttonHeight)
    {
        if (summary == null) return 0;
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("<b>Summary</b>", label);
        GUILayout.Label(summary.Text(), label);
        GUILayout.Label(counted ? "Counted for your streak today." : "Talk " + TalkSummary.MinTurnsForStreak + " times or more to count for your streak.", label);
        int pressed = 0;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Talk again", button, GUILayout.Height(buttonHeight))) pressed = 1;
        if (GUILayout.Button("Close", button, GUILayout.Height(buttonHeight))) pressed = 2;
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        return pressed;
    }
}
