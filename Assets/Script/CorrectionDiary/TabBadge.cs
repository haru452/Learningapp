/// <summary>
/// ほかの画面にいる間に採点が終わったことを知らせる文字 (タブの名前に付ける印と、トーストの文)。
/// </summary>
public static class TabBadge
{
    private const string Mark = "●";   // ●

    public static bool Has(string label) => label != null && label.TrimEnd().EndsWith(Mark);

    /// <summary>タブの名前に印を付ける (もう付いていればそのまま)</summary>
    public static string Add(string label)
    {
        string plain = Remove(label);
        return plain.Length == 0 ? Mark : plain + " " + Mark;
    }

    /// <summary>印を外す</summary>
    public static string Remove(string label)
    {
        string s = (label ?? "").TrimEnd();
        if (s.EndsWith(Mark)) s = s.Substring(0, s.Length - Mark.Length).TrimEnd();
        return s;
    }

    /// <summary>トーストの文。score が null なら採点に失敗したとき</summary>
    public static string Message(int? score) =>
        score.HasValue
            ? "Topic: graded — " + score.Value + " / 100"
            : "Topic: grading failed — open Topic and tap Retry";
}
