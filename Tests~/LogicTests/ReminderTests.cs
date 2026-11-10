// 「記録が切れちゃうよ」通知の時刻のテスト。Program.cs の Main から ReminderTests() を呼ぶ。
using System;

public static partial class LogicTests
{
    private static void ReminderTests()
    {
        var today = new DateTime(2026, 11, 9);
        DateTime At(int day, int hour, int minute = 0) => new DateTime(2026, 11, day, hour, minute, 0);

        // まだ練習していない・記録あり → 今日の 21 時
        Check(StudyReminderPlan.NextFireTime(At(9, 12), today, false, 5) == At(9, 21), "reminder: not done → today 21:00");
        // 21 時を過ぎた → 知らせない
        Check(StudyReminderPlan.NextFireTime(At(9, 21, 30), today, false, 5) == null, "reminder: past 21:00 → none");
        // 今日やった → 明日の 21 時
        Check(StudyReminderPlan.NextFireTime(At(9, 22), today, true, 6) == At(10, 21), "reminder: done → tomorrow 21:00");
        // 記録がない → 知らせない
        Check(StudyReminderPlan.NextFireTime(At(9, 12), today, false, 0) == null, "reminder: no streak → none");
        // 深夜 1 時 (記録上はまだ 11/9)・11/9 はやった → 11/10 の 21 時
        Check(StudyReminderPlan.NextFireTime(At(10, 1), today, true, 3) == At(10, 21), "reminder: after midnight → same calendar day 21:00");
        Check(StudyReminderPlan.Body(7).Contains("7"), "reminder: body has the streak");
    }
}
