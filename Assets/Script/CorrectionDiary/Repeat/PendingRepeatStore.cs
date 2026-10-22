using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// ログインしていないとき (または保存に失敗したとき) のリピーティングの結果を、端末に一時保存しておく。
/// ログインすると SupabaseService がまとめて送り、送れたものから消す。(お題の PendingTopicStore と同じ仕組み)
/// </summary>
public static class PendingRepeatStore
{
    private const string PrefsKey = "repeat_pending_v1";
    private const int MaxBatches = 30;

    [Serializable]
    public class Batch
    {
        public string takenAtUtc;                       // 練習した日時 (UTC, ISO 8601)
        public List<RepeatResult> results = new List<RepeatResult>();

        public DateTime TakenAtUtc =>
            DateTime.TryParse(takenAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
                ? t
                : DateTime.UtcNow;
    }

    [Serializable]
    private class BatchList
    {
        public List<Batch> items = new List<Batch>();
    }

    public static int Count => Load().Count;

    public static void Add(IList<RepeatResult> results, DateTime takenAtUtc)
    {
        if (results == null || results.Count == 0) return;
        var list = Load();
        list.Add(new Batch
        {
            takenAtUtc = takenAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            results = new List<RepeatResult>(results),
        });
        if (list.Count > MaxBatches) list.RemoveRange(0, list.Count - MaxBatches);   // 古いものから捨てる
        Save(list);
    }

    public static List<Batch> Load()
    {
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return new List<Batch>();
        try
        {
            return JsonUtility.FromJson<BatchList>(json)?.items ?? new List<Batch>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PendingRepeatStore] could not read saved results: " + e.Message);
            return new List<Batch>();
        }
    }

    public static void Save(List<Batch> items)
    {
        if (items == null || items.Count == 0) PlayerPrefs.DeleteKey(PrefsKey);
        else PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new BatchList { items = items }));
        PlayerPrefs.Save();
    }
}
