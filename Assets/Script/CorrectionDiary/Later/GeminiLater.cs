using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Gemini が混んでいて添削・採点できなかった答えを端末に残し、あとで自動で送り直すための入れ物。
/// (日記の添削・お題スピーキング・要約・単語帳の発音)。モデルは元の練習と同じものを使う (軽いモデルにはしない)。
/// 保存場所: persistentDataPath/gemini_later/ (jobs.json と録音の .wav)。送るのは GeminiLaterRunner。
/// </summary>
public static class GeminiLater
{
    public const string Diary = "diary";
    public const string Topic = "topic";
    public const string Summary = "summary";
    public const string Vocab = "vocab";
    public const string Karaoke = "karaoke";

    /// <summary>混んでいる以外の失敗をこの回数したら、あきらめて消す</summary>
    public const int MaxFailures = 5;
    /// <summary>この日数より古いものは、あきらめて消す</summary>
    public const int MaxAgeDays = 7;

    [Serializable]
    public class Job
    {
        public string id = "";
        public string kind = "";
        public string createdUtc = "";   // 答えた時刻 (yyyy-MM-ddTHH:mm:ssZ)
        public int failures;             // 混んでいる以外で失敗した回数
        public int busyTries;            // 混んでいて送れなかった回数 (表示用)
        public string model = "";
        public string fallbackModel = "";
        public bool sendToDiscord = true;

        public string text = "";         // 話した文 (録音を送るときは空)
        public List<string> wavFiles = new List<string>();   // 録音 (このフォルダーの中のファイル名)

        // 日記
        public bool includeOriginal = true;
        public bool underlineChanges = true;
        // お題スピーキング
        public string topicKind = "";
        public string topic = "";
        public int seconds;
        public List<string> focusTypes = new List<string>();
        // 要約
        public string videoId = "";
        public string videoTitle = "";
        public string watchUrl = "";
        // 単語帳
        public List<string> words = new List<string>();
        public List<string> heardWords = new List<string>();   // heard の辞書を 2 つの列にしたもの
        public List<string> heardTexts = new List<string>();
        // カラオケ (曲名は videoTitle、動画は videoId)。chunkLines[i] = wavFiles[i] の行番号 ("0,1,2")
        public string artist = "";
        public List<string> chunkLines = new List<string>();
        public List<int> lineIndexes = new List<int>();     // 歌った行 (空行なし)
        public List<string> lineTexts = new List<string>();

        public DateTime CreatedUtc =>
            DateTime.TryParse(createdUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal |
                                                System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : DateTime.UtcNow;
    }

    [Serializable]
    private class JobList { public List<Job> jobs = new List<Job>(); }

    /// <summary>入れたり送ったりしたときに呼ばれる (画面の「あとで送る: N 件」の表示用)</summary>
    public static event Action Changed;

    // ---------------- 純粋な部分 (テストあり) ----------------

    /// <summary>
    /// Gemini が混んでいる・上限・つながらないときのエラーか (あとで送れば通るもの)。
    /// 500 / 503 / 429 / 0 (タイムアウト・電波なし) と、どのキーも上限 (GeminiQuotaException)
    /// </summary>
    public static bool IsBusy(Exception e)
    {
        for (var x = e; x != null; x = x.InnerException)
        {
            if (x is GeminiQuotaException) return true;
            if (x is HttpException h) return h.StatusCode == 0 || h.StatusCode == 429 || h.StatusCode == 500 || h.StatusCode == 503 || h.StatusCode == 504;
            if (x is TimeoutException) return true;
        }
        return false;
    }

    /// <summary>もうあきらめて消すか (失敗が多い・古すぎる)</summary>
    public static bool ShouldDrop(Job job, DateTime nowUtc) =>
        job == null || job.failures >= MaxFailures || (nowUtc - job.CreatedUtc).TotalDays > MaxAgeDays;

    public static string ToJson(List<Job> jobs) => JsonUtility.ToJson(new JobList { jobs = jobs ?? new List<Job>() });

    public static List<Job> FromJson(string json)
    {
        try
        {
            var list = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<JobList>(json);
            return (list?.jobs ?? new List<Job>()).Where(j => j != null && !string.IsNullOrEmpty(j.id) && !string.IsNullOrEmpty(j.kind)).ToList();
        }
        catch (Exception) { return new List<Job>(); }
    }

    public static Dictionary<string, string> HeardOf(Job job)
    {
        var map = new Dictionary<string, string>();
        for (int i = 0; i < Math.Min(job.heardWords.Count, job.heardTexts.Count); i++) map[job.heardWords[i]] = job.heardTexts[i];
        return map;
    }

    /// <summary>"0,2,5" → [0, 2, 5] (数字でないものは捨てる)</summary>
    public static List<int> ParseIndexes(string csv) =>
        (csv ?? "").Split(',').Select(x => int.TryParse(x.Trim(), out int n) ? (int?)n : null).Where(n => n.HasValue).Select(n => n.Value).ToList();

    public static string KindLabel(string kind)
    {
        switch (kind)
        {
            case Diary: return "Diary";
            case Topic: return "Topic";
            case Summary: return "Summary";
            case Vocab: return "Vocab pronunciation";
            case Karaoke: return "Karaoke";
            default: return kind;
        }
    }

    // ---------------- 保存 ----------------

    private static string Dir => Path.Combine(Application.persistentDataPath, "gemini_later");
    private static string ListPath => Path.Combine(Dir, "jobs.json");

    public static int Count => Load().Count;

    public static List<Job> Load()
    {
        try { return File.Exists(ListPath) ? FromJson(File.ReadAllText(ListPath)) : new List<Job>(); }
        catch (Exception e) { Debug.LogWarning("[Later] could not read the saved answers: " + e.Message); return new List<Job>(); }
    }

    private static void Save(List<Job> jobs)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = ListPath + ".tmp";
            File.WriteAllText(tmp, ToJson(jobs));
            if (File.Exists(ListPath)) File.Delete(ListPath);
            File.Move(tmp, ListPath);
        }
        catch (Exception e) { Debug.LogWarning("[Later] could not save the answers: " + e.Message); }
        Changed?.Invoke();
    }

    /// <summary>
    /// 答えを残す。wavs があれば録音もファイルにする。録音が保存できなかったら false (答えは残せない)
    /// </summary>
    public static bool Add(Job job, IList<byte[]> wavs = null)
    {
        if (job == null || string.IsNullOrEmpty(job.kind)) return false;
        job.id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(job.createdUtc)) job.createdUtc = PracticeJson.Utc(DateTime.UtcNow);
        job.wavFiles = new List<string>();
        try
        {
            Directory.CreateDirectory(Dir);
            for (int i = 0; wavs != null && i < wavs.Count; i++)
            {
                string name = job.id + "_" + i + ".wav";
                File.WriteAllBytes(Path.Combine(Dir, name), wavs[i] ?? new byte[0]);
                job.wavFiles.Add(name);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Later] could not save the recording: " + e.Message);
            DeleteFiles(job);
            return false;
        }
        var jobs = Load();
        jobs.Add(job);
        Save(jobs);
        Debug.Log($"[Later] Gemini was busy. Saved a {job.kind} answer to send later ({jobs.Count} waiting).");
        GeminiLaterRunner.Ensure();
        return true;
    }

    public static List<byte[]> LoadWavs(Job job) =>
        job.wavFiles.Select(f => { try { return File.ReadAllBytes(Path.Combine(Dir, f)); } catch (Exception) { return null; } }).ToList();

    /// <summary>送り終わった (またはあきらめた) 答えを消す</summary>
    public static void Remove(Job job)
    {
        var jobs = Load();
        jobs.RemoveAll(j => j.id == job.id);
        Save(jobs);
        DeleteFiles(job);
    }

    /// <summary>回数などを書き換えて保存し直す</summary>
    public static void Update(Job job)
    {
        var jobs = Load();
        int i = jobs.FindIndex(j => j.id == job.id);
        if (i < 0) return;
        jobs[i] = job;
        Save(jobs);
    }

    private static void DeleteFiles(Job job)
    {
        foreach (var f in job.wavFiles ?? new List<string>())
            try { File.Delete(Path.Combine(Dir, f)); } catch (Exception) { }
    }
}
