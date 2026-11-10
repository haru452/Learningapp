using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>DiscordClient の続き: カラオケの結果 (日本語)。送り先はカラオケ用のチャンネルだけ</summary>
public partial class DiscordClient
{
    private const int KaraokeMaxWords = 15;

    public static string KaraokeMessage(DateTime date, string title, string artist, int score,
                                        IList<KaraokeLineResult> results, string advice)
    {
        string song = string.IsNullOrWhiteSpace(artist) ? title : title + " / " + artist;
        var words = (results ?? new List<KaraokeLineResult>()).SelectMany(r => r.Missed)
            .Select(m => m.Word + " (" + m.Sound + ")").Take(KaraokeMaxWords).ToList();
        return "\U0001F3A4 **カラオケ " + date.ToString("yyyy-MM-dd") + "**: " + song + "\n" +
               "点数: " + score + " / 100\n" +
               "言えなかった単語: " + (words.Count == 0 ? "なし" : string.Join(", ", words)) + "\n" +
               (string.IsNullOrWhiteSpace(advice) ? "" : "\n" + advice.Trim() + "\n");
    }

    public async Task SendKaraokeResultAsync(DateTime date, string title, string artist, int score,
                                             IList<KaraokeLineResult> results, string advice)
    {
        if (!IsConfigured) throw new InvalidOperationException("Discord is not configured.");
        List<string> chunks = Split(KaraokeMessage(date, title, artist, score, results, advice), MaxMessageLength);
        for (int i = 0; i < chunks.Count; i++)
        {
            await PostAsync(chunks[i]);
            if (i < chunks.Count - 1) await Task.Delay(500);   // レート制限対策
        }
    }
}
