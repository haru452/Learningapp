using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 「今日の発音練習」: その日最初の練習のあと、苦手 1 位の音 (30 日で 3 点以上) の練習動画を YouTube で探し、発音用の Discord に送る。
/// 1 日 1 回 (夜 3 時区切り)。1 日 2 回失敗したらその日はやめる。直近 30 日に送った動画は避ける。画面は待たない。
/// ログインしていない・Discord の送り先が無い・YouTube のキーが無いときは何もしない (失敗に数えない)。
/// </summary>
public static class DailySoundPractice
{
    private const string SentKey = "sound_daily_sent_at";
    private const string FailKey = "sound_daily_fail_at";
    private const string FailsKey = "sound_daily_fails";
    private const string VideosKey = "sound_daily_videos";
    public const int MinPoints = 3;

    private static bool _running;   // 送っている最中に次の練習が終わっても、2 回送らない

    public static async Task TryAsync(SupabaseService supabase, string youtubeApiKey, DiscordClient pronDiscord)
    {
        if (_running) return;
        if (supabase == null || !supabase.IsConfigured || !supabase.IsSignedIn) return;
        if (pronDiscord == null || !pronDiscord.IsConfigured || string.IsNullOrWhiteSpace(youtubeApiKey)) return;

        DateTime now = DateTime.Now;
        DateTime lastFail = Load(FailKey);
        int fails = DailySoundPlanner.PracticeDay(lastFail) == DailySoundPlanner.PracticeDay(now) ? PlayerPrefs.GetInt(FailsKey, 0) : 0;
        if (!DailySoundPlanner.ShouldTry(Load(SentKey), lastFail, fails, now)) return;

        _running = true;
        try
        {
            var weak = await supabase.GetWeakSoundsAsync(30);
            var top = weak.FirstOrDefault();
            if (top == null || top.Points < MinPoints) return;   // まだ数が少ない: 送らない (失敗ではない)

            var youtube = new YouTubeSearchClient(youtubeApiKey);
            var found = await youtube.SearchVideosByTitleAsync(SoundTypes.SearchQuery(top.Sound, top.Label), 240, 1200, 15);   // 30 日同じ動画を避けられるように多めに
            var sent = LoadVideos();
            var video = DailySoundPlanner.PickVideo(found, sent, DateTime.UtcNow);
            if (video == null) throw new Exception("no practice video found");

            await pronDiscord.SendSoundPracticeAsync(DailySoundPlanner.PracticeDay(now), top, video);   // 夜 3 時区切りの日付
            Save(SentKey, now);
            sent[video.id] = DateTime.UtcNow;
            SaveVideos(sent);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DailySoundPractice] " + e.Message);
            Save(FailKey, now);
            PlayerPrefs.SetInt(FailsKey, fails + 1);
            PlayerPrefs.Save();
        }
        finally
        {
            _running = false;
        }
    }

    private static DateTime Load(string key) =>
        long.TryParse(PlayerPrefs.GetString(key, ""), out long ticks) && ticks > 0 ? new DateTime(ticks) : DateTime.MinValue;

    private static void Save(string key, DateTime t)
    {
        PlayerPrefs.SetString(key, t.Ticks.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.Save();
    }

    // "id=ticks;id=ticks" (UTC)
    private static Dictionary<string, DateTime> LoadVideos()
    {
        var d = new Dictionary<string, DateTime>();
        foreach (string part in PlayerPrefs.GetString(VideosKey, "").Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            if (long.TryParse(part.Substring(eq + 1), out long ticks)) d[part.Substring(0, eq)] = new DateTime(ticks, DateTimeKind.Utc);
        }
        return d;
    }

    private static void SaveVideos(Dictionary<string, DateTime> d)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-DailySoundPlanner.AvoidDays - 1);
        PlayerPrefs.SetString(VideosKey, string.Join(";", d.Where(kv => kv.Value > cutoff)
            .Select(kv => kv.Key + "=" + kv.Value.Ticks.ToString(CultureInfo.InvariantCulture))));
        PlayerPrefs.Save();
    }
}
