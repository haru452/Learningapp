using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>直近 7 日に出した Tatoeba の文を端末に覚えておく (同じ文ばかり出ないように)</summary>
public static class RecentRepeatLog
{
    private const string PrefsKey = "repeat_recent_v1";
    private const int KeepDays = 7;

    [Serializable] private class Entry { public string key; public long ticks; }
    [Serializable] private class EntryList { public List<Entry> items = new List<Entry>(); }

    public static HashSet<string> Keys()
    {
        var set = new HashSet<string>();
        foreach (var e in Load()) set.Add(e.key);
        return set;
    }

    public static void Add(IEnumerable<string> keys)
    {
        var list = Load();
        long now = DateTime.UtcNow.Ticks;
        foreach (string k in keys)
        {
            if (string.IsNullOrEmpty(k)) continue;
            list.RemoveAll(e => e.key == k);
            list.Add(new Entry { key = k, ticks = now });
        }
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new EntryList { items = list }));
        PlayerPrefs.Save();
    }

    private static List<Entry> Load()
    {
        try
        {
            var list = JsonUtility.FromJson<EntryList>(PlayerPrefs.GetString(PrefsKey, ""))?.items ?? new List<Entry>();
            long limit = DateTime.UtcNow.AddDays(-KeepDays).Ticks;
            list.RemoveAll(e => e == null || e.ticks < limit);
            return list;
        }
        catch (Exception)
        {
            return new List<Entry>();
        }
    }
}
