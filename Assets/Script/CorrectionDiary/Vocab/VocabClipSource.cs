// 単語の動画の場面を集める流れ: 端末の保存 → Supabase の一覧 → (許されれば) その場で YouTube を検索して字幕を調べる (B)。
// 通信と保存はコンストラクタで受け取る関数・IVocabClipStorage に任せ、Unity には依存しない (テストで偽物に差し替える)。
// Unity の通信はメインのスレッドで動くので、スレッドもロックも使わない。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public enum ClipStatus { Ok, None, Offline, Limit, NoKey }

public class VocabClipSource
{
    /// <summary>B の 1 回で字幕を調べる動画の数と、そこで止める場面の数</summary>
    public const int MaxVideosTried = 5, EnoughClips = 3;

    private readonly Func<IList<string>, Task<List<VocabClip>>> _getCloud;
    private readonly Func<IList<VocabClip>, Task> _addCloud;
    private readonly Func<string, Task<List<YouTubeSearchClient.Video>>> _search;
    private readonly Func<string, Task<List<TranscriptLine>>> _transcript;
    private readonly IVocabClipStorage _storage;
    private readonly Func<DateTime> _nowUtc;
    private readonly Action<string> _log;
    private readonly Dictionary<string, Task<(ClipStatus status, List<VocabClip> clips)>> _inFlight
        = new Dictionary<string, Task<(ClipStatus status, List<VocabClip> clips)>>();

    /// <param name="getCloud">Supabase から場面を読む。ログインしていなければ null</param>
    /// <param name="addCloud">Supabase に場面を足す。ログインしていなければ null</param>
    /// <param name="search">YouTube で単語の動画を探す。キーが無ければ null</param>
    /// <param name="log">警告の出し先 (Unity では Debug.LogWarning を渡す)。null なら何もしない</param>
    public VocabClipSource(
        Func<IList<string>, Task<List<VocabClip>>> getCloud,
        Func<IList<VocabClip>, Task> addCloud,
        Func<string, Task<List<YouTubeSearchClient.Video>>> search,
        Func<string, Task<List<TranscriptLine>>> transcript,
        IVocabClipStorage storage,
        Func<DateTime> nowUtc,
        Action<string> log = null)
    {
        _getCloud = getCloud;
        _addCloud = addCloud;
        _search = search;
        _transcript = transcript;
        _storage = storage;
        _nowUtc = nowUtc;
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// 単語の場面を返す。保存 → 雲 → (allowSearch かつ回数が残っていれば) B の順。
    /// 同じ単語を同時に頼まれたら同じ Task を返す。
    /// </summary>
    public Task<(ClipStatus status, List<VocabClip> clips)> GetAsync(string word, bool allowSearch)
    {
        string key = VocabDeck.Key(word);
        if (_inFlight.TryGetValue(key, out var running)) return running;
        var task = RunAsync(key, allowSearch);
        if (!task.IsCompleted) _inFlight[key] = task; // すぐ終わったもの (保存にあった等) は登録しない
        return task;
    }

    private async Task<(ClipStatus status, List<VocabClip> clips)> RunAsync(string key, bool allowSearch)
    {
        try { return await GetCoreAsync(key, allowSearch); }
        finally { _inFlight.Remove(key); }
    }

    private async Task<(ClipStatus status, List<VocabClip> clips)> GetCoreAsync(string word, bool allowSearch)
    {
        // 1. 端末の保存
        var stored = _storage.Load(word);
        if (VocabClipStore.IsReady(stored, _nowUtc())) return FromEntry(stored);

        // 2. Supabase の一覧
        if (_getCloud != null)
        {
            var cloud = await GetCloudSafeAsync(new[] { word });
            var mine = cloud.Where(c => VocabDeck.Key(c.Word) == word).ToList();
            if (mine.Count > 0)
            {
                Save(word, mine);
                return (ClipStatus.Ok, mine);
            }
        }

        // 3〜5. その場の検索をしてよいか
        if (!allowSearch) return None();
        if (_search == null) return (ClipStatus.NoKey, new List<VocabClip>());
        var local = _nowUtc().ToLocalTime();
        var log = _storage.LoadLog();
        if (!log.CanSearch(local)) return (ClipStatus.Limit, new List<VocabClip>());
        log.Used(local);
        _storage.SaveLog(log); // 失敗しても YouTube の枠は使うので、呼ぶ前に数える

        // 6. 検索
        List<YouTubeSearchClient.Video> videos;
        try { videos = await _search(word) ?? new List<YouTubeSearchClient.Video>(); }
        catch (HttpException e) when (YouTubeSearchClient.IsQuotaExceeded(e))
        {
            log.Block(local);
            _storage.SaveLog(log);
            return (ClipStatus.Limit, new List<VocabClip>());
        }
        catch (HttpException e) when (e.StatusCode == 0)
        {
            return (ClipStatus.Offline, new List<VocabClip>());
        }
        catch (Exception e)
        {
            _log("[VocabClips] video search failed: " + e.Message);
            return None(); // 保存しないので、あとでまた調べる
        }

        // 7. 上から字幕を調べる
        var clips = new List<VocabClip>();
        int tried = 0;
        foreach (var v in videos)
        {
            if (tried >= MaxVideosTried || clips.Count >= EnoughClips) break;
            if (v == null || string.IsNullOrEmpty(v.id)) continue;
            tried++;
            List<TranscriptLine> lines;
            try { lines = await _transcript(v.id); }
            catch (HttpException e) when (e.StatusCode == 0)
            {
                if (clips.Count == 0) return (ClipStatus.Offline, new List<VocabClip>());
                break; // 見つけた分は残す
            }
            catch (Exception e)
            {
                _log("[VocabClips] transcript failed: " + e.Message);
                continue;
            }
            if (lines == null) continue;
            clips = VocabClipFinder.Merge(clips, VocabClipFinder.FindInVideo(word, v.id, v.channel, "search", lines));
        }

        // 8. 保存 (0 場面でも「調べた」として保存) と、雲への追加
        Save(word, clips);
        if (clips.Count == 0) return None();
        if (_addCloud != null)
        {
            try { await _addCloud(clips); }
            catch (Exception e) { _log("[VocabClips] could not add clips to the cloud: " + e.Message); }
        }
        return (ClipStatus.Ok, clips);
    }

    /// <summary>
    /// 単語をまとめて 1 回で Supabase に問い合わせ、見つかった単語を保存する。
    /// 返す辞書のキーは正規化した単語 (重複は 1 つ)。保存済み (準備済み) の単語は問い合わせない。
    /// </summary>
    public async Task<Dictionary<string, ClipStatus>> FillFromCloudAsync(IList<string> words)
    {
        var result = new Dictionary<string, ClipStatus>();
        var remaining = new List<string>();
        var now = _nowUtc();
        foreach (var w in words ?? new List<string>())
        {
            string key = VocabDeck.Key(w);
            if (key.Length == 0 || result.ContainsKey(key)) continue;
            var stored = _storage.Load(key);
            if (VocabClipStore.IsReady(stored, now)) { result[key] = FromEntry(stored).status; continue; }
            result[key] = ClipStatus.None;
            remaining.Add(key);
        }
        if (_getCloud == null || remaining.Count == 0) return result;

        var cloud = await GetCloudSafeAsync(remaining);
        foreach (var group in cloud.GroupBy(c => VocabDeck.Key(c.Word)))
        {
            if (!remaining.Contains(group.Key)) continue;
            Save(group.Key, group.ToList());
            result[group.Key] = ClipStatus.Ok;
        }
        return result;
    }

    private async Task<List<VocabClip>> GetCloudSafeAsync(IList<string> words)
    {
        try { return (await _getCloud(words) ?? new List<VocabClip>()).Where(c => c != null).ToList(); }
        catch (Exception e)
        {
            _log("[VocabClips] could not read clips from the cloud: " + e.Message);
            return new List<VocabClip>();
        }
    }

    private void Save(string word, List<VocabClip> clips)
    {
        _storage.Save(new VocabClipEntry { Word = word, Clips = clips, CheckedAt = _nowUtc() });
    }

    private static (ClipStatus status, List<VocabClip> clips) FromEntry(VocabClipEntry e)
        => e.Clips != null && e.Clips.Count > 0 ? (ClipStatus.Ok, e.Clips) : None();

    private static (ClipStatus status, List<VocabClip> clips) None() => (ClipStatus.None, new List<VocabClip>());
}
