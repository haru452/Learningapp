using System;

/// <summary>
/// Gemini の読み上げが利用上限 (429) になった日は、その日のうちはもう呼ばない (端末の読み上げを使う)。
/// 1 日の区切りは夜中 3:00 (日記・連続記録と同じ)。ここは日付の判定だけ (保存は RepeatAudioPlayer が PlayerPrefs にする)。
/// </summary>
public static class TtsQuotaGate
{
    public const int DayBoundaryHour = 3;

    /// <summary>上限になった時刻 (端末の時刻) と今が、同じ「練習の日」なら true</summary>
    public static bool IsBlocked(DateTime blockedAtLocal, DateTime nowLocal, int boundaryHour = DayBoundaryHour)
    {
        if (blockedAtLocal == DateTime.MinValue) return false;
        return blockedAtLocal.AddHours(-boundaryHour).Date == nowLocal.AddHours(-boundaryHour).Date;
    }
}
