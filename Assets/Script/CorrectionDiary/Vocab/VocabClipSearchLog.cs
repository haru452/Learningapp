using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// 単語帳の動画の場面で、その場で YouTube を検索した回数 (この端末の 1 日分) と、
/// 今日 YouTube の上限に当たったかどうかの記録。Unity に依存しない。保存は呼ぶ側が行う。
/// </summary>
public class VocabClipSearchLog
{
    /// <summary>1 日にその場で検索してよい回数</summary>
    public const int DailyLimit = 30;

    /// <summary>Count を数えている日 (端末の現地の日付、yyyy-MM-dd)</summary>
    public string Date = "";
    public int Count;
    /// <summary>YouTube の上限の返事が来た日 (yyyy-MM-dd)。なければ空</summary>
    public string BlockedDate = "";

    private static string Day(DateTime localNow) => localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public bool CanSearch(DateTime localNow)
    {
        string today = Day(localNow);
        if (BlockedDate == today) return false;
        return Date != today || Count < DailyLimit;
    }

    public void Used(DateTime localNow)
    {
        string today = Day(localNow);
        if (Date != today) { Date = today; Count = 0; }
        Count++;
    }

    public void Block(DateTime localNow)
    {
        BlockedDate = Day(localNow);
    }

    public string ToJson()
    {
        return "{\"date\":\"" + HttpUtil.JsonEscape(Date ?? "") + "\",\"count\":"
            + Count.ToString(CultureInfo.InvariantCulture)
            + ",\"blockedDate\":\"" + HttpUtil.JsonEscape(BlockedDate ?? "") + "\"}";
    }

    /// <summary>壊れた JSON・空・オブジェクトでないものは、新しい (0 回・ブロックなし) ものを返す</summary>
    public static VocabClipSearchLog FromJson(string json)
    {
        var log = new VocabClipSearchLog();
        if (string.IsNullOrEmpty(json)) return log;
        try
        {
            var obj = MiniJson.Parse(json) as Dictionary<string, object>;
            if (obj == null) return log;
            if (obj.TryGetValue("date", out var d) && d is string ds) log.Date = ds;
            if (obj.TryGetValue("blockedDate", out var b) && b is string bs) log.BlockedDate = bs;
            if (obj.TryGetValue("count", out var c) && c is double cd && cd >= 0 && cd < int.MaxValue) log.Count = (int)cd;
        }
        catch (FormatException) { return new VocabClipSearchLog(); }
        return log;
    }
}
