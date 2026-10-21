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
        public string localId;                    // この結果の端末内の ID (後から言い直しの一致率を入れるため)
        public int readAloudAccuracy = -1;        // 言い直しの一致率 (0〜100)。まだ無いときは -1

        public int? ReadAloudAccuracy => readAloudAccuracy >= 0 ? readAloudAccuracy : (int?)null;

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

    /// <summary>結果を一時保存し、その結果の ID を返す (SetReadAloud に使う)。保存しなかったら null</summary>
    public static string Add(string kind, string topic, int seconds, GeminiApiClient.TopicGrade grade,
                             string transcript, DateTime takenAtUtc, int? readAloudAccuracy = null)
    {
        if (grade == null) return null;
        string localId = Guid.NewGuid().ToString("N");

        var list = Load();
        list.Add(new Item
        {
            kind = kind ?? "daily",
            topic = topic ?? "",
            seconds = seconds,
            transcript = transcript ?? "",
            takenAtUtc = takenAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            grade = grade,
            localId = localId,
            readAloudAccuracy = readAloudAccuracy ?? -1,
        });

        if (list.Count > MaxItems) list.RemoveRange(0, list.Count - MaxItems);   // 古いものから捨てる
        Save(list);
        return localId;
    }

    /// <summary>一時保存した結果に、言い直しの一致率を入れる (もう送ってあって見つからなければ false)</summary>
    public static bool SetReadAloud(string localId, int accuracy)
    {
        if (string.IsNullOrEmpty(localId)) return false;
        var list = Load();
        var item = list.Find(i => i.localId == localId);
        if (item == null) return false;
        item.readAloudAccuracy = Math.Max(0, Math.Min(100, accuracy));
        Save(list);
        return true;
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
