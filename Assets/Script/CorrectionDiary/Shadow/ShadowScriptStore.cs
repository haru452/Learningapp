using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// シャドーイングのお手本 (Gemini が YouTube の動画から作った文字起こし) をスマホに保存して使い回す。
/// persistentDataPath/shadow_scripts/<動画ID>.json、最大 20 本 (git・Web には入れない)。
/// </summary>
public static class ShadowScriptStore
{
    private const int Keep = 20;
    private static string Dir => Path.Combine(Application.persistentDataPath, "shadow_scripts");

    public static async Task<List<string>> GetAsync(GeminiApiClient gemini, YouTubeSearchClient.Video video, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        string path = Path.Combine(Dir, SafeId(video.id) + ".json");
        if (File.Exists(path))
        {
            var cached = ShadowScript.Parse(File.ReadAllText(path));
            if (cached.Count > 0)
            {
                Touch(path);
                return cached;
            }
            TryDelete(path);
        }

        string json = await gemini.TranscribeVideoAsync(video.WatchUrl);
        var sentences = ShadowScript.Parse(json);
        if (sentences.Count > 0)
        {
            File.WriteAllText(path, json);   // 待っている間にタブを移っても、次に使えるように保存する
            Evict();
        }
        ct.ThrowIfCancellationRequested();
        if (sentences.Count == 0) throw new Exception("no English sentences were found in the video");
        return sentences;
    }

    /// <summary>前の版 (BBC) のデータを消す: 音声・切り出し・フィード・進み具合</summary>
    public static void DeleteOldBbcData()
    {
        try
        {
            string old = Path.Combine(Application.persistentDataPath, "shadow");
            if (Directory.Exists(old)) Directory.Delete(old, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] could not delete old BBC data: " + e.Message);
        }
        foreach (string key in new[] { "shadow_progress_v1", "shadow_slow_on" })
            if (PlayerPrefs.HasKey(key)) PlayerPrefs.DeleteKey(key);
    }

    private static string SafeId(string id) => new string((id ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());

    private static void Evict()
    {
        try
        {
            foreach (var f in new DirectoryInfo(Dir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep))
                TryDelete(f.FullName);
        }
        catch (Exception) { }
    }

    private static void Touch(string path)
    {
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (Exception) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }
}
