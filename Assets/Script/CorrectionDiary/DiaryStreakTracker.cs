using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 「日記を書いた日」を記録し、連続日数・最長記録・週ごとの達成状況を計算する。
/// 記録は PlayerPrefs に保存される (アプリを閉じても残る。アプリを削除すると消える)。
///
/// ・日付は端末のローカル時刻で判定する
/// ・連続日数は「今日 or 昨日まで」を数える。
///   今日まだ書いていなくても、昨日まで続いていれば、連続は途切れていない扱いにする
/// </summary>
public class DiaryStreakTracker
{
    private const string PrefsKey = "diary_streak_v1";
    private const string DateFormat = "yyyy-MM-dd";

    [Serializable]
    private class SaveData
    {
        public List<string> dates = new List<string>();
    }

    private readonly HashSet<DateTime> _days = new HashSet<DateTime>();

    public DiaryStreakTracker()
    {
        Load();
    }

    /// <summary>日記を書いた日の合計</summary>
    public int TotalDays => _days.Count;

    public bool IsDone(DateTime day)
    {
        return _days.Contains(Normalize(day));
    }

    /// <summary>その日を「達成」として記録する。新しく記録されたら true (すでに記録済みなら false)。</summary>
    public bool MarkDone(DateTime day)
    {
        bool added = _days.Add(Normalize(day));
        if (added) Save();
        return added;
    }

    /// <summary>
    /// 連続した日を、まとめて記録する (クラウドの記録を、この端末に写すときに使う)。
    /// 最後の日 lastDay から、さかのぼって count 日ぶん。保存は1回だけ。新しく増えた日数を返す。
    /// </summary>
    public int MarkRangeDone(DateTime lastDay, int count)
    {
        int added = 0;
        DateTime d = Normalize(lastDay);
        for (int i = 0; i < count; i++)
        {
            if (_days.Add(d.AddDays(-i))) added++;
        }
        if (added > 0) Save();
        return added;
    }

    /// <summary>現在の連続日数。今日が未達成でも、昨日まで続いていれば、その日数を返す。</summary>
    public int GetCurrentStreak(DateTime today)
    {
        DateTime cursor = Normalize(today);
        if (!_days.Contains(cursor))
            cursor = cursor.AddDays(-1);

        int count = 0;
        while (_days.Contains(cursor))
        {
            count++;
            cursor = cursor.AddDays(-1);
        }
        return count;
    }

    /// <summary>これまでで一番長かった連続日数</summary>
    public int GetBestStreak()
    {
        if (_days.Count == 0) return 0;

        var sorted = new List<DateTime>(_days);
        sorted.Sort();

        int best = 0;
        int run = 0;
        DateTime? previous = null;

        foreach (DateTime day in sorted)
        {
            run = (previous.HasValue && (day - previous.Value).Days == 1) ? run + 1 : 1;
            if (run > best) best = run;
            previous = day;
        }
        return best;
    }

    /// <summary>その日を含む週の月曜日 (週は月曜はじまり)</summary>
    public static DateTime StartOfWeek(DateTime day)
    {
        DateTime d = Normalize(day);
        int daysFromMonday = ((int)d.DayOfWeek + 6) % 7;   // 日曜 = 0 → 6
        return d.AddDays(-daysFromMonday);
    }

    // ------------------------------------------------------------
    // Persistence
    // ------------------------------------------------------------
    private void Load()
    {
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data == null || data.dates == null) return;

            foreach (string s in data.dates)
            {
                if (DateTime.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out DateTime parsed))
                {
                    _days.Add(Normalize(parsed));
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DiaryStreak] Could not load saved data: " + e.Message);
        }
    }

    private void Save()
    {
        var sorted = new List<DateTime>(_days);
        sorted.Sort();

        var data = new SaveData();
        foreach (DateTime d in sorted)
            data.dates.Add(d.ToString(DateFormat, CultureInfo.InvariantCulture));

        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    // 時刻や Kind (Local/Unspecified) の違いで、同じ日が別物にならないよう、年月日だけにそろえる
    private static DateTime Normalize(DateTime d)
    {
        return new DateTime(d.Year, d.Month, d.Day);
    }
}
