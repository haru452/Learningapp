using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// カラオケの採点のあとの共通の処理: 録音を切ったものを順に Gemini に送る、結果をクラウド・Discord・苦手な音に残す。
/// 歌った直後 (KaraokeSession) と、混んでいてあとで送るとき (GeminiLaterJobs) の両方で使う。
/// </summary>
public static class KaraokeResults
{
    public const string Model = "gemini-3.8-flash";
    public const string FallbackModel = "gemini-3.7-flash";

    /// <summary>採点に使うクライアント (カラオケのキー。無ければ日記のキー。上限ならほかの機能のキーを借りる)</summary>
    public static GeminiApiClient CreateGemini(string model = Model, string fallback = FallbackModel)
    {
        var s = ApiSecrets.Data;
        return new GeminiApiClient(ApiSecrets.Pick(s.geminiKaraokeApiKey, s.geminiApiKey), model, fallback,
            backupApiKeys: new[] { s.geminiApiKey, s.geminiSummaryApiKey, s.geminiTopicApiKey, s.geminiTtsApiKey, s.geminiShadowApiKey });
    }

    public static DiscordClient CreateDiscord()
    {
        var s = ApiSecrets.Data;
        string channel = s.karaokeDiscordChannelId;
        return new DiscordClient(s.karaokeDiscordWebhookUrl, string.IsNullOrWhiteSpace(channel) ? "" : s.discordBotToken, channel);
    }

    /// <summary>
    /// 切った録音を 1 つずつ採点し、行の結果をまとめる。Gemini の失敗はそのまま投げる (混んでいればあとで送る)
    /// </summary>
    public static async Task<KaraokeCheck> CheckAllAsync(GeminiApiClient gemini, IList<byte[]> wavs,
                                                         IList<List<(int index, string text)>> chunkLines)
    {
        var all = new KaraokeCheck();
        var advice = new List<string>();
        for (int i = 0; i < Math.Min(wavs.Count, chunkLines.Count); i++)
        {
            if (wavs[i] == null || wavs[i].Length == 0 || chunkLines[i].Count == 0) continue;
            var check = await gemini.CheckKaraokeAsync(chunkLines[i], wavs[i]);
            all.Lines.AddRange(check.Lines);
            if (!string.IsNullOrWhiteSpace(check.Advice)) advice.Add(check.Advice.Trim());
        }
        all.Lines = all.Lines.OrderBy(l => l.Line).ToList();
        all.Advice = string.Join("\n", advice);
        return all;
    }

    /// <summary>
    /// クラウド (連続記録と苦手な音) と Discord に残す。失敗は Console だけ。Discord に送れたら true
    /// </summary>
    public static async Task<bool> SaveAsync(string title, string artist, string videoId, int score, KaraokeCheck check, DateTime takenAtUtc)
    {
        var supabase = UnityEngine.Object.FindObjectOfType<SupabaseService>(true);
        if (supabase != null && supabase.IsConfigured)
        {
            try
            {
                if (await supabase.EnsureSignedInAsync())
                {
                    await supabase.CompleteKaraokeAsync(title, artist, videoId, score, check.Lines, takenAtUtc);
                    var hits = KaraokeScorer.Sounds(check.Lines);
                    if (hits.Count > 0) await supabase.SavePronunciationIssuesAsync(hits, 1, "karaoke", takenAtUtc);
                    var diary = UnityEngine.Object.FindObjectOfType<DiaryManager>(true);
                    if (diary != null) diary.RefreshStreakFromCloud();
                }
            }
            catch (Exception e) { Debug.LogWarning("[Karaoke] could not save the result to the cloud: " + e.Message); }
        }

        var discord = CreateDiscord();
        if (!discord.IsConfigured) return false;
        try
        {
            await discord.SendKaraokeResultAsync(takenAtUtc.ToLocalTime(), title, artist, score, check.Lines, check.Advice);
            return true;
        }
        catch (Exception e) { Debug.LogWarning("[Karaoke] could not send to Discord: " + e.Message); return false; }
    }
}
