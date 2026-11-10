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

    /// <summary>シャドーイングの判定のトーストの文。score が null なら判定できなかったとき</summary>
    public static string ShadowMessage(int? score) =>
        score.HasValue
            ? "Shadowing: checked — " + score.Value + "%"
            : "Shadowing: could not check — open Repeat";

    /// <summary>Android でトーストを出す (ほかの環境ではログだけ)</summary>
    public static void ShowToast(string message)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = player.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new UnityEngine.AndroidJavaRunnable(() =>
                {
                    using (var toast = new UnityEngine.AndroidJavaClass("android.widget.Toast"))
                    using (var t = toast.CallStatic<UnityEngine.AndroidJavaObject>("makeText", activity, message, 1))   // 1 = LENGTH_LONG
                        t.Call("show");
                }));
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning("[TabBadge] could not show a toast: " + e.Message);
        }
#else
        UnityEngine.Debug.Log("[TabBadge] " + message);
#endif
    }
}
