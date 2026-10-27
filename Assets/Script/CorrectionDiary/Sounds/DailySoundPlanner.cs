using System;
using System.Collections.Generic;

/// <summary>「今日の発音練習」を送るかの判断と、動画の選び方 (純粋なロジック)</summary>
public static class DailySoundPlanner
{
    public const int DayBoundaryHour = 3;     // 夜 3 時区切り
    public const int MaxFailsPerDay = 2;
    public const int AvoidDays = 30;

    /// <summary>練習の日 (夜 3 時より前は前の日)</summary>
    public static DateTime PracticeDay(DateTime local) =>
        local < DateTime.MinValue.AddHours(DayBoundaryHour) ? local.Date : local.AddHours(-DayBoundaryHour).Date;   // MinValue = まだ無い

    /// <summary>今日まだ送っていなくて、今日 2 回失敗していなければ true</summary>
    public static bool ShouldTry(DateTime lastSentLocal, DateTime lastFailLocal, int failsToday, DateTime nowLocal)
    {
        DateTime today = PracticeDay(nowLocal);
        if (lastSentLocal != DateTime.MinValue && PracticeDay(lastSentLocal) == today) return false;
        if (lastFailLocal != DateTime.MinValue && PracticeDay(lastFailLocal) == today && failsToday >= MaxFailsPerDay) return false;
        return true;
    }

    /// <summary>直近 30 日に送った動画を避けて一番上。全部送っていたら一番上。無ければ null</summary>
    public static YouTubeSearchClient.Video PickVideo(IList<YouTubeSearchClient.Video> found, IDictionary<string, DateTime> sentAtUtc, DateTime nowUtc)
    {
        if (found == null || found.Count == 0) return null;
        foreach (var v in found)
        {
            if (v == null) continue;
            if (sentAtUtc == null || !sentAtUtc.TryGetValue(v.id, out DateTime at) || (nowUtc - at).TotalDays > AvoidDays) return v;
        }
        return found[0];
    }
}
