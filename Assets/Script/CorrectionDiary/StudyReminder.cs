using System;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

/// <summary>
/// 「記録が切れちゃうよ」通知をスマホの中で予約する (Mobile Notifications パッケージ)。
/// 予約は 1 つだけ。連続記録の表示を更新するたびに (DiaryStreakView.Refresh) 予約し直す。
/// 通信がなくても届く。Editor では何もしない。
/// </summary>
public static class StudyReminder
{
    private const string ChannelId = "streak_reminder";
    private const string IosId = "streak_reminder";

    private static string _lastKey;     // 前に予約した内容。同じなら予約し直さない
    private static int _version;        // 後から呼ばれた予約を優先する
    private static Task _permission;    // 通知の許可は、起動ごとに 1 回だけ聞く

    /// <param name="today">記録上の「今日」</param>
    /// <param name="doneToday">今日はもう練習したか</param>
    /// <param name="streak">連続日数</param>
    public static void Schedule(DateTime today, bool doneToday, int streak)
    {
        if (Application.isEditor) return;
        DateTime? fire = StudyReminderPlan.NextFireTime(DateTime.Now, today, doneToday, streak);
        string key = fire.HasValue ? fire.Value.ToString("yyyy-MM-dd HH:mm") + "|" + streak : "none";
        if (key == _lastKey) return;
        _lastKey = key;
        _ = ScheduleAsync(fire, streak, ++_version);
    }

    private static async Task ScheduleAsync(DateTime? fire, int streak, int version)
    {
        try
        {
            if (_permission == null) _permission = RequestPermissionAsync();
            await _permission;
            if (version != _version) return;   // 待っている間に、新しい予約が来た

            CancelAll();
            if (!fire.HasValue) return;
            Send(fire.Value, streak);
            Debug.Log("[Reminder] scheduled at " + fire.Value.ToString("yyyy-MM-dd HH:mm") + " (streak " + streak + ")");
        }
        catch (Exception e)
        {
            _lastKey = null;   // 次の更新で、もう一度試す
            Debug.LogWarning("[Reminder] could not schedule the notification: " + e.Message);
        }
    }

#if UNITY_ANDROID
    private static async Task RequestPermissionAsync()
    {
        AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel
        {
            Id = ChannelId,
            Name = "記録のリマインダー",
            Description = "連続記録が切れそうな日の夜に知らせます",
            Importance = Importance.Default,
        });
        var request = new PermissionRequest();   // Android 13 以上で「通知を許可しますか」が出る
        while (request.Status == PermissionStatus.RequestPending) await Task.Yield();
    }

    private static void CancelAll() => AndroidNotificationCenter.CancelAllNotifications();

    private static void Send(DateTime fire, int streak)
    {
        var n = new AndroidNotification
        {
            Title = StudyReminderPlan.Title,
            Text = StudyReminderPlan.Body(streak),
            FireTime = fire,
        };
        AndroidNotificationCenter.SendNotification(n, ChannelId);
    }
#elif UNITY_IOS
    private static async Task RequestPermissionAsync()
    {
        using (var req = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Sound, false))
        {
            while (!req.IsFinished) await Task.Yield();
        }
    }

    private static void CancelAll() => iOSNotificationCenter.RemoveScheduledNotification(IosId);

    private static void Send(DateTime fire, int streak)
    {
        iOSNotificationCenter.ScheduleNotification(new iOSNotification
        {
            Identifier = IosId,
            Title = StudyReminderPlan.Title,
            Body = StudyReminderPlan.Body(streak),
            ShowInForeground = true,
            ForegroundPresentationOption = PresentationOption.Alert | PresentationOption.Sound,
            Trigger = new iOSNotificationCalendarTrigger
            {
                Year = fire.Year, Month = fire.Month, Day = fire.Day, Hour = fire.Hour, Minute = fire.Minute,
                Repeats = false,
            },
        });
    }
#else
    private static Task RequestPermissionAsync() => Task.CompletedTask;
    private static void CancelAll() { }
    private static void Send(DateTime fire, int streak) { }
#endif
}
