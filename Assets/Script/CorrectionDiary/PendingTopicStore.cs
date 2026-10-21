using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// ログインしていないとき (または保存に失敗したとき) のお題スピーキングの結果を、端末に一時保存しておく。
/// ログインすると SupabaseService がまとめてクラウドに送り、送れたものから消す。
/// (要約練習の PendingSummaryStore と同じ仕組み)
/// </summary>
public static class PendingTopicStore
{
    private const string PrefsKey = "topic_pending_v1";
    private const int MaxItems = 50;

    [Serializable]
    public class Item
    {
        public string kind;
        public string topic;
        public int seconds;
        public string transcript;
        public string takenAtUtc;                 // 練習した日時 (UTC, ISO 8601)
        public GeminiApiClient.TopicGrade grade;

        public DateTime TakenAtUtc =>
            DateTime.TryParse(takenAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
                ? t
                : DateTime.UtcNow;
    }

    [Serializable]
    private class ItemList
    {
        public List<Item> items = new List<Item>();
    }

    /// <summary>まだ送っていない結果の件数</summary>
    public static int Count => Load().Count;

    public static void Add(string kind, string topic, int seconds, GeminiApiClient.TopicGrade grade,
                           string transcript, DateTime takenAtUtc)
    {
        if (grade == null) return;

        var list = Load();
        list.Add(new Item
        {
            kind = kind ?? "daily",
            topic = topic ?? "",
            seconds = seconds,
            transcript = transcript ?? "",
            takenAtUtc = takenAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            grade = grade,
        });

        if (list.Count > MaxItems) list.RemoveRange(0, list.Count - MaxItems);   // 古いものから捨てる
        Save(list);
    }

    public static List<Item> Load()
    {
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return new List<Item>();
        try
        {
            var data = JsonUtility.FromJson<ItemList>(json);
            return data?.items ?? new List<Item>();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PendingTopicStore] could not read saved results: " + e.Message);
            return new List<Item>();
        }
    }

    public static void Save(List<Item> items)
    {
        if (items == null || items.Count == 0) PlayerPrefs.DeleteKey(PrefsKey);
        else PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new ItemList { items = items }));
        PlayerPrefs.Save();
    }
}
