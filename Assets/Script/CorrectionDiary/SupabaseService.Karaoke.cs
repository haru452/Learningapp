using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>SupabaseService の続き: カラオケの 1 曲分を残して、連続記録を進める (karaoke.sql)</summary>
public partial class SupabaseService
{
    [Serializable] private class KaraokeRows { public KaraokeRow[] rows; }
    [Serializable] private class KaraokeRow { public int streak; public bool already_done; }

    /// <summary>返すのは (連続日数, もう今日数えてあったか)。ログインしていないときは呼ばない</summary>
    public async Task<(int streak, bool alreadyDone)> CompleteKaraokeAsync(string title, string artist, string videoId, int score,
                                                                            IList<KaraokeLineResult> results, DateTime takenAtUtc,
                                                                            string advice = "")
    {
        string body = await RpcAsync("complete_karaoke_session",
            ",\"p_title\":\"" + HttpUtil.JsonEscape(title ?? "") + "\"" +
            ",\"p_artist\":\"" + HttpUtil.JsonEscape(artist ?? "") + "\"" +
            ",\"p_video_id\":\"" + HttpUtil.JsonEscape(videoId ?? "") + "\"" +
            ",\"p_score\":" + Mathf.Clamp(score, 0, 100) +
            ",\"p_items\":" + PracticeJson.KaraokeItems(results) +
            ",\"p_timezone\":\"Asia/Tokyo\",\"p_taken_at\":\"" + PracticeJson.Utc(takenAtUtc) + "\"" +
            ",\"p_advice\":\"" + HttpUtil.JsonEscape(advice ?? "") + "\"");   // Gemini のアドバイス (Web で見られる)
        var row = JsonUtility.FromJson<KaraokeRows>("{\"rows\":" + body + "}")?.rows?.FirstOrDefault();
        return row == null ? (0, false) : (row.streak, row.already_done);
    }
}
