using System;

/// <summary>
/// 「記録が切れちゃうよ」通知を、いつ・何と出すかを決める (Unity を使わないので、テストできる)。
/// 連続記録がある日に、まだ練習していなければ、その日の 21 時に知らせる。
/// </summary>
public static class StudyReminderPlan
{
    /// <summary>知らせる時刻 (21 時)</summary>
    public const int Hour = 21;

    public const string Title = "記録が切れちゃうよ";

    /// <summary>
    /// 次に知らせる時刻。知らせなくてよいなら null。
    /// </summary>
    /// <param name="now">今の時刻 (端末のローカル時刻)</param>
    /// <param name="today">記録上の「今日」(深夜 3 時までは前の日)</param>
    /// <param name="doneToday">今日はもう練習したか</param>
    /// <param name="streak">連続日数 (今日まだなら、昨日までの日数)</param>
    public static DateTime? NextFireTime(DateTime now, DateTime today, bool doneToday, int streak)
    {
        if (streak <= 0) return null;   // 切れる記録がない
        // 今日やった → 明日の 21 時。まだ → 今日の 21 時 (もう過ぎていたら知らせない)
        DateTime fire = (doneToday ? today.Date.AddDays(1) : today.Date).AddHours(Hour);
        return fire > now ? fire : (DateTime?)null;
    }

    public static string Body(int streak) =>
        "連続 " + streak + " 日の記録が、今日で切れそうです。少しだけ練習しよう。";
}
