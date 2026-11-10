// 単語帳の動画の場面を端末に保存する。
// VocabClipEntry・IVocabClipStorage・VocabClipStore (JSON と準備済みの判定) は Unity に依存しない。
// Unity に触れるのはファイルに読み書きする VocabClipFileStorage だけ。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>1 単語の場面と、最後に調べた日時 (UTC)。Clips が空なら「調べたが 0 場面」</summary>
public class VocabClipEntry
{
    public string Word;
    public List<VocabClip> Clips = new List<VocabClip>();
    public DateTime CheckedAt;
}

/// <summary>場面と検索回数の保存先 (テストではメモリの偽物に差し替える)</summary>
public interface IVocabClipStorage
{
    /// <summary>無い・壊れていれば null</summary>
    VocabClipEntry Load(string word);
    void Save(VocabClipEntry e);
    /// <summary>無い・壊れていれば新しい記録</summary>
    VocabClipSearchLog LoadLog();
    void SaveLog(VocabClipSearchLog log);
}

public static class VocabClipStore
{
    /// <summary>0 場面だった単語を、もう一度 YouTube で調べるまでの日数</summary>
    public const int NoneRetryDays = 30;

    /// <summary>もう通信しなくてよいか: 場面が 1 つ以上ある、または 0 場面で調べてから 30 日以内</summary>
    public static bool IsReady(VocabClipEntry e, DateTime nowUtc)
    {
        if (e == null) return false;
        if (e.Clips != null && e.Clips.Count > 0) return true;
        return nowUtc - e.CheckedAt < TimeSpan.FromDays(NoneRetryDays);
    }

    public static string EntryToJson(VocabClipEntry e)
    {
        var at = DateTime.SpecifyKind(e.CheckedAt, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
        // clips は必ず最後に置く (EntryFromJson が "clips" の後ろを切り出して読むため)
        return "{\"word\":\"" + HttpUtil.JsonEscape(e.Word ?? "") + "\",\"checkedAt\":\"" + at
            + "\",\"clips\":" + SupabaseService.BuildClipsJson(e.Clips) + "}";
    }

    /// <summary>壊れた JSON・オブジェクトでないもの・word か checkedAt が無いものは null</summary>
    public static VocabClipEntry EntryFromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        Dictionary<string, object> obj;
        try { obj = MiniJson.Parse(json) as Dictionary<string, object>; }
        catch (Exception) { return null; }
        if (obj == null) return null;
        if (!(obj.TryGetValue("word", out var w) && w is string word) || word.Length == 0) return null;
        if (!(obj.TryGetValue("checkedAt", out var a) && a is string ats)) return null;
        if (!DateTime.TryParse(ats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) return null;
        if (!(obj.TryGetValue("clips", out var c) && c is List<object>)) return null;
        // 文字列の中の " は \" になるので、,"clips": は値の区切りにしか現れない
        int i = json.IndexOf(",\"clips\":", StringComparison.Ordinal);
        int end = json.LastIndexOf('}');
        if (i < 0 || end <= i) return null;
        int start = i + ",\"clips\":".Length;
        return new VocabClipEntry
        {
            Word = word,
            CheckedAt = at.ToUniversalTime(),
            Clips = SupabaseService.ParseClips(json.Substring(start, end - start)),
        };
    }
}

/// <summary>persistentDataPath/vocab/clips/&lt;単語&gt;.json と vocab/clip_search.json に読み書きする</summary>
public class VocabClipFileStorage : IVocabClipStorage
{
    // clips_v2: 英語以外の動画を外す前 (clips) の保存は使わず、調べ直す
    private static string ClipsDir => Path.Combine(VocabStore.Root, "clips_v2");
    private static string LogPath => Path.Combine(VocabStore.Root, "clip_search.json");
    private static string EntryPath(string word) => Path.Combine(ClipsDir, VocabStore.SafeName(word) + ".json");

    public VocabClipEntry Load(string word)
    {
        try
        {
            string path = EntryPath(word);
            if (!File.Exists(path)) return null;
            return VocabClipStore.EntryFromJson(File.ReadAllText(path));
        }
        catch (Exception e) { Debug.LogWarning("[VocabClips] could not read clips: " + e.Message); return null; }
    }

    public void Save(VocabClipEntry e)
    {
        try { WriteAtomic(EntryPath(e.Word), VocabClipStore.EntryToJson(e)); }
        catch (Exception ex) { Debug.LogWarning("[VocabClips] could not save clips: " + ex.Message); }
    }

    public VocabClipSearchLog LoadLog()
    {
        try
        {
            if (!File.Exists(LogPath)) return new VocabClipSearchLog();
            return VocabClipSearchLog.FromJson(File.ReadAllText(LogPath));
        }
        catch (Exception e) { Debug.LogWarning("[VocabClips] could not read the search log: " + e.Message); return new VocabClipSearchLog(); }
    }

    public void SaveLog(VocabClipSearchLog log)
    {
        try { WriteAtomic(LogPath, log.ToJson()); }
        catch (Exception e) { Debug.LogWarning("[VocabClips] could not save the search log: " + e.Message); }
    }

    // 一時ファイルに書いてから置き換える (VocabStore.WriteAtomic と同じ)
    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}
