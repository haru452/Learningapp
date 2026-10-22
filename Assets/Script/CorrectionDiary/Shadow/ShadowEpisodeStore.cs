using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// シャドーイングの素材 (BBC 6 Minute English) を用意する。
/// フィード → 進み具合 → MP3 をスマホに保存 → (回ごとに 1 回だけ) Gemini で文と時間に切り出す → 次の 5 文。
/// MP3・切り出しの JSON は persistentDataPath/shadow/ にだけ置く (最大 3 回分。git・Web には入れない)。
/// </summary>
public static class ShadowEpisodeStore
{
    private const string ProgressKey = "shadow_progress_v1";
    private const int KeepEpisodes = 3;
    private const int MaxDoneIds = 300;
    private const int MaxTries = 3;   // 使える文が無い回を飛ばすのは 1 回の Start で 3 回まで

    public class Chunk
    {
        public BbcFeed.Episode Episode;
        public AudioClip Full;
        public List<ShadowChunker.Line> Lines;
        public int Index;   // この回の何番目のかたまりか
        public int Total;   // この回のかたまりの数
    }

    [Serializable]
    private class Progress
    {
        public string currentId = "";
        public int nextChunk;
        public List<string> doneIds = new List<string>();
        public string failedId = "";   // 用意に続けて失敗した回
        public int failures;
    }

    private static string Dir => Path.Combine(Application.persistentDataPath, "shadow");
    private static AudioClip _clip;
    private static string _clipId;

    /// <summary>次の 5 文を用意する。用意できなければ例外 (画面は「Could not prepare…」)</summary>
    public static async Task<Chunk> NextChunkAsync(GeminiApiClient gemini, Action<string> status, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        List<BbcFeed.Episode> episodes = BbcFeed.Parse(await LoadFeedAsync(ct));
        if (episodes.Count == 0) throw new Exception("no episodes in the feed");

        var progress = LoadProgress();
        for (int tries = 0; tries < MaxTries; tries++)
        {
            ct.ThrowIfCancellationRequested();
            var ep = episodes.FirstOrDefault(e => e.Id == progress.currentId && !progress.doneIds.Contains(e.Id))
                     ?? episodes.FirstOrDefault(e => !progress.doneIds.Contains(e.Id))
                     ?? episodes[0];   // 全部終わったら、一番新しい回をもう一度
            if (ep.Id != progress.currentId)
            {
                progress.currentId = ep.Id;
                progress.nextChunk = 0;
                progress.doneIds.Remove(ep.Id);
                SaveProgress(progress);
            }

            AudioClip full;
            string json;
            try
            {
                full = await LoadEpisodeAudioAsync(ep, status, ct);
                json = await LoadSegmentsAsync(ep, gemini, status, ct);
            }
            catch (Exception e) when (ShadowFailure.CountsAgainstEpisode(e, Application.internetReachability != NetworkReachability.NotReachable))
            {
                // 同じ回で続けて失敗する (音声が消えた・Gemini が切り出さない) なら、その回を飛ばして次の回へ
                progress.failures = progress.failedId == ep.Id ? progress.failures + 1 : 1;
                progress.failedId = ep.Id;
                if (!ShadowFailure.ShouldSkip(progress.failures)) { SaveProgress(progress); throw; }
                Debug.LogWarning("[Shadow] episode " + ep.Id + " failed " + progress.failures + " times. Skipping it: " + e.Message);
                MarkEpisodeDone(progress, ep.Id);
                continue;
            }
            if (progress.failedId == ep.Id) { progress.failedId = ""; progress.failures = 0; SaveProgress(progress); }
            var chunks = ShadowChunker.Chunk(ShadowChunker.ParseGemini(json), full.length);
            if (progress.nextChunk < chunks.Count)
            {
                return new Chunk
                {
                    Episode = ep, Full = full, Lines = chunks[progress.nextChunk],
                    Index = progress.nextChunk, Total = chunks.Count,
                };
            }
            // 使える文が無い (切り出しがおかしい) か、最後まで終わった回: 飛ばして次の回へ
            if (chunks.Count == 0) Debug.LogWarning("[Shadow] no usable sentences in episode " + ep.Id + ". Skipping it.");
            MarkEpisodeDone(progress, ep.Id);
        }
        throw new Exception("no usable sentences");
    }

    /// <summary>5 文を練習し終えた (結果が出た)。次は次のかたまり、回の最後なら次の回</summary>
    public static void MarkDone(Chunk chunk)
    {
        var progress = LoadProgress();
        if (progress.currentId != chunk.Episode.Id) return;
        progress.nextChunk = chunk.Index + 1;
        if (progress.nextChunk >= chunk.Total) MarkEpisodeDone(progress, chunk.Episode.Id);
        else SaveProgress(progress);
    }

    // ------------------------------------------------------------
    private static void MarkEpisodeDone(Progress progress, string id)
    {
        if (!progress.doneIds.Contains(id)) progress.doneIds.Add(id);
        while (progress.doneIds.Count > MaxDoneIds) progress.doneIds.RemoveAt(0);
        progress.currentId = "";
        progress.nextChunk = 0;
        SaveProgress(progress);
    }

    // フィードが取れないときは、前に取れたフィードを使う (途中の回をオフラインでも続けられる)
    private static async Task<string> LoadFeedAsync(CancellationToken ct)
    {
        string cache = Path.Combine(Dir, "feed.xml");
        using (var req = UnityWebRequest.Get(BbcFeed.FeedUrl))
        {
            req.timeout = 20;
            var op = req.SendWebRequest();
            while (!op.isDone) { if (ct.IsCancellationRequested) { req.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
            if (req.result == UnityWebRequest.Result.Success && BbcFeed.Parse(req.downloadHandler.text).Count > 0)
            {
                File.WriteAllText(cache, req.downloadHandler.text);
                return req.downloadHandler.text;
            }
            Debug.LogWarning("[Shadow] feed download failed: " + req.responseCode + " " + req.error);
        }
        if (File.Exists(cache)) return File.ReadAllText(cache);
        throw new Exception("could not download the episode list");
    }

    private static async Task<AudioClip> LoadEpisodeAudioAsync(BbcFeed.Episode ep, Action<string> status, CancellationToken ct)
    {
        if (_clip != null && _clipId == ep.Id) return _clip;
        string path = Path.Combine(Dir, ep.Id + ".mp3");
        if (!File.Exists(path))
        {
            status?.Invoke("Downloading the episode...");
            string tmp = path + ".part";
            using (var req = new UnityWebRequest(ep.AudioUrl, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(tmp) { removeFileOnAbort = true };
                req.timeout = 180;
                var op = req.SendWebRequest();
                while (!op.isDone) { if (ct.IsCancellationRequested) { req.Abort(); ct.ThrowIfCancellationRequested(); } await Task.Yield(); }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    TryDelete(tmp);
                    throw new Exception("episode download failed: " + req.responseCode + " " + req.error);
                }
            }
            if (!File.Exists(tmp) || new FileInfo(tmp).Length < 10000) { TryDelete(tmp); throw new Exception("episode audio is too small"); }
            File.Move(tmp, path);
            Evict(ep.Id);
        }
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

        status?.Invoke("Loading the audio...");
        AudioClip clip;
        try
        {
            clip = await RepeatAudioPlayer.LoadClipAsync(path, AudioType.MPEG);
        }
        catch (Exception)
        {
            TryDelete(path);   // 壊れたファイルは消して、次はダウンロードし直す
            throw;
        }
        // キャンセルされていても使い回せるように覚えておく (古い回の音声はメモリから消す)
        if (_clip != null && _clipId == ep.Id) { UnityEngine.Object.Destroy(clip); clip = _clip; }   // 同時に読み込んでいた
        else
        {
            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            _clip = clip;
            _clipId = ep.Id;
        }
        ct.ThrowIfCancellationRequested();
        return clip;
    }

    private static async Task<string> LoadSegmentsAsync(BbcFeed.Episode ep, GeminiApiClient gemini, Action<string> status, CancellationToken ct)
    {
        string path = Path.Combine(Dir, ep.Id + ".json");
        if (File.Exists(path)) return File.ReadAllText(path);
        status?.Invoke("Finding the sentences in this episode (first time only, about a minute)...");
        byte[] audio = File.ReadAllBytes(Path.Combine(Dir, ep.Id + ".mp3"));
        string json = await gemini.SegmentEpisodeAsync(audio, "audio/mp3");
        File.WriteAllText(path, json ?? "");   // 待っている間にタブを移っても、結果は保存して次に使う
        ct.ThrowIfCancellationRequested();
        return json;
    }

    // 新しい順に 3 回分だけ残す (今の回は必ず残す)
    private static void Evict(string keepId)
    {
        try
        {
            var files = new DirectoryInfo(Dir).GetFiles("*.mp3").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            int kept = 1;
            foreach (var f in files)
            {
                string id = Path.GetFileNameWithoutExtension(f.Name);
                if (id == keepId) continue;
                if (kept < KeepEpisodes) { kept++; continue; }
                TryDelete(f.FullName);
                TryDelete(Path.Combine(Dir, id + ".json"));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] could not clean up old episodes: " + e.Message);
        }
    }

    private static Progress LoadProgress()
    {
        try
        {
            var p = JsonUtility.FromJson<Progress>(PlayerPrefs.GetString(ProgressKey, ""));
            if (p != null) { p.doneIds = p.doneIds ?? new List<string>(); p.currentId = p.currentId ?? ""; p.failedId = p.failedId ?? ""; return p; }
        }
        catch (Exception) { }
        return new Progress();
    }

    private static void SaveProgress(Progress p)
    {
        PlayerPrefs.SetString(ProgressKey, JsonUtility.ToJson(p));
        PlayerPrefs.Save();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }
}
