using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// YouTube Data API v3 で、要約の練習に使える短い英語の動画を探す。
///
/// 絞り込み:
///  - 字幕付き (= ちゃんと話している動画が多い) / 埋め込み可 / 英語 / セーフサーチ
///  - 長さが minSeconds〜maxSeconds (Shorts や長すぎる動画を除く)
///
/// 利用枠: search.list は 1回 100ユニット、videos.list は 1ユニット (無料枠は 1日 10,000ユニット)
/// </summary>
public class YouTubeSearchClient
{
    private const string SearchUrl = "https://www.googleapis.com/youtube/v3/search";
    private const string VideosUrl = "https://www.googleapis.com/youtube/v3/videos";

    private readonly string _apiKey;

    public YouTubeSearchClient(string apiKey)
    {
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public class Video
    {
        public string id;
        public string title;
        public string channel;
        public int durationSeconds;

        public string WatchUrl => "https://www.youtube.com/watch?v=" + id;
    }

    // ---- レスポンス (必要部分のみ) ----
    [Serializable] private class SearchResponse { public SearchItem[] items; }
    [Serializable] private class SearchItem { public SearchId id; }
    [Serializable] private class SearchId { public string videoId; }

    [Serializable] private class VideosResponse { public VideoItem[] items; }
    [Serializable] private class VideoItem { public string id; public Snippet snippet; public ContentDetails contentDetails; }
    [Serializable] private class Snippet { public string title; public string channelTitle; }
    [Serializable] private class ContentDetails { public string duration; }

    /// <summary>
    /// query で検索し、条件に合う動画をシャッフルして返す (見つからなければ空)。
    /// </summary>
    public async Task<List<Video>> SearchShortVideosAsync(string query, int minSeconds, int maxSeconds)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("YouTube API key is not set.");

        // videoDuration=short は「4分未満」。正確な長さは videos.list で確認する
        string searchUrl = SearchUrl +
            "?part=id&type=video&maxResults=25" +
            "&videoDuration=short&videoCaption=closedCaption&videoEmbeddable=true" +
            "&relevanceLanguage=en&safeSearch=strict" +
            "&q=" + Uri.EscapeDataString(query) +
            "&key=" + Uri.EscapeDataString(_apiKey);

        var search = JsonUtility.FromJson<SearchResponse>(await HttpUtil.GetAsync(searchUrl));
        var ids = new List<string>();
        if (search?.items != null)
        {
            foreach (var item in search.items)
            {
                if (!string.IsNullOrEmpty(item?.id?.videoId))
                    ids.Add(item.id.videoId);
            }
        }

        var videos = new List<Video>();
        if (ids.Count == 0) return videos;

        string videosUrl = VideosUrl +
            "?part=snippet,contentDetails" +
            "&id=" + string.Join(",", ids) +
            "&key=" + Uri.EscapeDataString(_apiKey);

        var details = JsonUtility.FromJson<VideosResponse>(await HttpUtil.GetAsync(videosUrl));
        if (details?.items == null) return videos;

        foreach (var item in details.items)
        {
            int seconds = ParseIsoDuration(item.contentDetails?.duration);
            if (seconds < minSeconds || seconds > maxSeconds) continue;

            videos.Add(new Video
            {
                id = item.id,
                title = item.snippet?.title ?? "",
                channel = item.snippet?.channelTitle ?? "",
                durationSeconds = seconds
            });
        }

        Shuffle(videos);
        return videos;
    }

    /// <summary>
    /// シャドーイング用: タイトルなどで検索し、minSeconds〜maxSeconds の動画を検索の順のまま最大 limit 件返す。
    /// </summary>
    public async Task<List<Video>> SearchVideosByTitleAsync(string query, int minSeconds = 60, int maxSeconds = 900, int limit = 5)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("YouTube API key is not set.");
        string searchUrl = SearchUrl +
            "?part=id&type=video&maxResults=15&videoEmbeddable=true&relevanceLanguage=en" +
            "&q=" + Uri.EscapeDataString(query ?? "") +
            "&key=" + Uri.EscapeDataString(_apiKey);
        var search = JsonUtility.FromJson<SearchResponse>(await HttpUtil.GetAsync(searchUrl));
        var ids = new List<string>();
        if (search?.items != null)
            foreach (var item in search.items)
                if (!string.IsNullOrEmpty(item?.id?.videoId)) ids.Add(item.id.videoId);
        if (ids.Count == 0) return new List<Video>();

        string videosUrl = VideosUrl + "?part=snippet,contentDetails&id=" + string.Join(",", ids) +
                           "&key=" + Uri.EscapeDataString(_apiKey);
        var details = JsonUtility.FromJson<VideosResponse>(await HttpUtil.GetAsync(videosUrl));
        var byId = new Dictionary<string, Video>();
        if (details?.items != null)
            foreach (var item in details.items)
                byId[item.id] = new Video
                {
                    id = item.id,
                    title = item.snippet?.title ?? "",
                    channel = item.snippet?.channelTitle ?? "",
                    durationSeconds = ParseIsoDuration(item.contentDetails?.duration),
                };
        var ordered = new List<Video>();
        foreach (string id in ids) if (byId.TryGetValue(id, out Video v)) ordered.Add(v);   // 検索の順に戻す
        return PickTitleResults(ordered, minSeconds, maxSeconds, limit);
    }

    /// <summary>長さで絞り、順番はそのまま、最大 limit 件</summary>
    public static List<Video> PickTitleResults(IList<Video> videos, int minSeconds, int maxSeconds, int limit)
    {
        var list = new List<Video>();
        if (videos == null) return list;
        foreach (var v in videos)
        {
            if (v == null || v.durationSeconds < minSeconds || v.durationSeconds > maxSeconds) continue;
            list.Add(v);
            if (list.Count >= limit) break;
        }
        return list;
    }

    /// <summary>365 → "6:05"</summary>
    public static string FormatDuration(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return (seconds / 60) + ":" + (seconds % 60).ToString("00");
    }

    // "PT1M35S" → 95
    private static int ParseIsoDuration(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return 0;
        var m = Regex.Match(iso, @"^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$");
        if (!m.Success) return 0;

        int Get(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        return Get(1) * 3600 + Get(2) * 60 + Get(3);
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
