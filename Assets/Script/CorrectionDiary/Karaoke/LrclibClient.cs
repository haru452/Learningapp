using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.Networking;

/// <summary>LRCLIB で見つかった曲 (Synced = 時刻つきの歌詞)</summary>
public class LrcSong
{
    public long Id;
    public string Title = "";
    public string Artist = "";
    public double Duration;
    public string Synced = "";
}

/// <summary>
/// LRCLIB (https://lrclib.net) で時刻つきの歌詞を探す。キーは要らない。
/// User-Agent はアプリの名前だけ (個人の情報は送らない)。時刻の無い歌詞 (plainLyrics だけ) は使わない。
/// </summary>
public class LrclibClient
{
    private const string SearchUrl = "https://lrclib.net/api/search?q=";
    private const string UserAgent = "EnglishVoiceCorrector (Unity)";

    public async Task<List<LrcSong>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<LrcSong>();
        using (var req = UnityWebRequest.Get(SearchUrl + Uri.EscapeDataString(query.Trim())))
        {
            req.timeout = 30;
            req.SetRequestHeader("User-Agent", UserAgent);
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
                throw new HttpException(req.responseCode, $"HTTP {req.responseCode} : {req.error}");
            return ParseSearch(req.downloadHandler.text);
        }
    }

    // ---------------- 純粋な部分 (テストあり) ----------------

    public static List<LrcSong> ParseSearch(string json)
    {
        var result = new List<LrcSong>();
        object root;
        try { root = MiniJson.Parse(json); }
        catch (Exception) { return result; }
        if (!(root is List<object> items)) return result;
        foreach (var item in items)
        {
            if (!(item is Dictionary<string, object> d)) continue;
            string synced = JsonGet.Str(d, "syncedLyrics");
            if (string.IsNullOrWhiteSpace(synced) || LrcLyrics.Parse(synced).Count == 0) continue;
            result.Add(new LrcSong
            {
                Id = d.TryGetValue("id", out var id) && id is double n ? (long)n : 0,
                Title = JsonGet.Str(d, "trackName").Trim(),
                Artist = JsonGet.Str(d, "artistName").Trim(),
                Duration = d.TryGetValue("duration", out var du) && du is double sec ? sec : 0,
                Synced = synced,
            });
        }
        return result;
    }

    /// <summary>動画の長さに一番近い歌詞 (ライブ版などを避ける)。videoSeconds が 0 なら先頭。無ければ null</summary>
    public static LrcSong PickBest(IList<LrcSong> songs, int videoSeconds)
    {
        if (songs == null || songs.Count == 0) return null;
        if (videoSeconds <= 0) return songs[0];
        return songs.OrderBy(s => Math.Abs(s.Duration - videoSeconds)).First();   // 同じ差なら検索の順
    }

    /// <summary>YouTube で探す言葉 (歌詞動画は元の曲と同じ長さのことが多い)</summary>
    public static string VideoQuery(LrcSong song) => (song.Title + " " + song.Artist).Trim() + " lyrics";
}
