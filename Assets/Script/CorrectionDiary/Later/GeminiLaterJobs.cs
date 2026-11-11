using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 残しておいた答えを 1 件、元の練習と同じキー・同じモデルで添削・採点し、クラウドと Discord に送る。
/// 画面 (DiaryManager など) が一度も開かれていなくても動くように、必要なものはここで作る。
/// Gemini の失敗はそのまま投げる (混んでいれば GeminiLaterRunner があとでまた送る)。
/// 保存・Discord の失敗は Console だけ (採点をやり直さないように)。返すのは知らせる文。
/// </summary>
public static class GeminiLaterJobs
{
    public static Task<string> RunAsync(GeminiLater.Job job)
    {
        switch (job.kind)
        {
            case GeminiLater.Diary: return DiaryAsync(job);
            case GeminiLater.Topic: return TopicAsync(job);
            case GeminiLater.Summary: return SummaryAsync(job);
            case GeminiLater.Vocab: return VocabAsync(job);
            case GeminiLater.Karaoke: return KaraokeAsync(job);
            default: throw new InvalidOperationException("unknown kind: " + job.kind);
        }
    }

    private static ApiSecrets S => ApiSecrets.Data;

    private static GeminiApiClient Client(GeminiLater.Job job, string key, params string[] backups) =>
        new GeminiApiClient(key, job.model, job.fallbackModel, backupApiKeys: backups);

    private static byte[] FirstWav(GeminiLater.Job job)
    {
        if (job.wavFiles.Count == 0) return null;
        var wav = GeminiLater.LoadWavs(job)[0];
        if (wav == null || wav.Length == 0) throw new InvalidOperationException("the saved recording is missing");
        return wav;
    }

    private static DateTime LocalTime(GeminiLater.Job job) => job.CreatedUtc.ToLocalTime();

    // ---------------- 日記 ----------------
    private static async Task<string> DiaryAsync(GeminiLater.Job job)
    {
        var s = S;
        var r = await Client(job, s.geminiApiKey, s.geminiSummaryApiKey, s.geminiTopicApiKey, s.geminiTtsApiKey)
            .CorrectDiaryAsync(job.text);

        await CloudAsync("diary", async sb =>
        {
            await sb.SaveDiaryAsync(job.text, r.corrected, r.explanation, "Gemini");
            await sb.SaveMistakesAsync("diary", r.mistakes, job.CreatedUtc);
        });
        bool sent = await DiscordAsync(job, new DiscordClient(s.discordWebhookUrl, s.discordBotToken, s.discordChannelId),
            d => d.SendDiaryAsync(LocalTime(job), r.corrected, r.explanation, job.text, job.includeOriginal, job.underlineChanges));

        var diary = UnityEngine.Object.FindObjectOfType<DiaryManager>(true);
        if (diary != null) diary.ShowLaterCorrection(job.text, r.corrected, r.explanation);
        return "Your saved diary was corrected" + Where(sent);
    }

    // ---------------- お題スピーキング ----------------
    private static async Task<string> TopicAsync(GeminiLater.Job job)
    {
        var s = S;
        var gemini = Client(job, ApiSecrets.Pick(s.geminiTopicApiKey, s.geminiApiKey), s.geminiApiKey, s.geminiSummaryApiKey, s.geminiTtsApiKey);
        byte[] wav = FirstWav(job);
        var g = wav != null
            ? await gemini.GradeTopicAudioAsync(job.topicKind, job.topic, job.seconds, wav, job.focusTypes)
            : await gemini.GradeTopicAsync(job.topicKind, job.topic, job.seconds, job.text, job.focusTypes);
        string transcript = wav != null ? g.transcript : job.text;

        await CloudAsync("topic", async sb =>
        {
            if (!await sb.EnsureSignedInAsync())
            {
                PendingTopicStore.Add(job.topicKind, job.topic, job.seconds, g, transcript, job.CreatedUtc);
                return;
            }
            await sb.SaveTopicResultAsync(job.topicKind, job.topic, job.seconds, g, transcript, null, job.CreatedUtc);
        }, signIn: false);
        bool sent = await DiscordAsync(job, SummaryDiscord(), d => d.SendTopicResultAsync(LocalTime(job), job.topicKind, job.topic, g));
        return $"Your saved Topic answer was graded: {g.score} / 100" + Where(sent);
    }

    // ---------------- 要約 ----------------
    private static async Task<string> SummaryAsync(GeminiLater.Job job)
    {
        var s = S;
        var gemini = Client(job, ApiSecrets.Pick(s.geminiSummaryApiKey, s.geminiApiKey), s.geminiApiKey, s.geminiTopicApiKey, s.geminiTtsApiKey);
        byte[] wav = FirstWav(job);
        var g = await gemini.GradeVideoSummaryAsync(job.watchUrl, job.seconds, wav != null ? null : job.text, wav);

        await CloudAsync("summary", async sb =>
        {
            if (!await sb.EnsureSignedInAsync())
            {
                PendingSummaryStore.Add(job.videoId, job.videoTitle, job.seconds, g, job.CreatedUtc);
                return;
            }
            await sb.SaveSummaryResultAsync(job.videoId, job.videoTitle, job.seconds, g, job.CreatedUtc);
            try { await sb.SaveMistakesAsync("summary", g.mistakes, job.CreatedUtc); }
            catch (Exception e) { Debug.LogWarning("[Later] could not save summary mistakes: " + e.Message); }
        }, signIn: false);
        bool sent = await DiscordAsync(job, SummaryDiscord(), d => d.SendSummaryResultAsync(LocalTime(job), job.videoTitle, job.watchUrl, g));
        return $"Your saved Summary was graded: {g.score} / 100" + Where(sent);
    }

    // ---------------- 単語帳の発音 ----------------
    private static async Task<string> VocabAsync(GeminiLater.Job job)
    {
        var s = S;
        var wavs = GeminiLater.LoadWavs(job);
        var words = new List<string>();
        var recs = new List<byte[]>();
        for (int i = 0; i < Math.Min(job.words.Count, wavs.Count); i++)
            if (wavs[i] != null && wavs[i].Length > 0) { words.Add(job.words[i]); recs.Add(wavs[i]); }
        if (words.Count == 0) throw new InvalidOperationException("the saved recordings are missing");

        var results = await Client(job, ApiSecrets.Pick(s.geminiVocabApiKey, s.geminiApiKey),
                s.geminiApiKey, s.geminiSummaryApiKey, s.geminiTopicApiKey, s.geminiTtsApiKey, s.geminiShadowApiKey)
            .CheckWordPronunciationsAsync(words, recs);

        await CloudAsync("vocab", sb => sb.SaveVocabPronLogsAsync(results, GeminiLater.HeardOf(job), job.CreatedUtc));
        var weak = results.Where(r => r.Sounds != null && r.Sounds.Count > 0).Select(r => r.Word).ToList();
        return $"Your saved pronunciation was checked ({results.Count} words)" +
               (weak.Count > 0 ? ". Weak sounds in: " + string.Join(", ", weak.Take(5)) + ". See the web page." : ".");
    }

    // ---------------- カラオケ ----------------
    private static async Task<string> KaraokeAsync(GeminiLater.Job job)
    {
        var textOf = new Dictionary<int, string>();
        for (int i = 0; i < Math.Min(job.lineIndexes.Count, job.lineTexts.Count); i++) textOf[job.lineIndexes[i]] = job.lineTexts[i];
        var chunks = job.chunkLines
            .Select(c => GeminiLater.ParseIndexes(c).Where(textOf.ContainsKey).Select(i => (i, textOf[i])).ToList()).ToList();
        var wavs = GeminiLater.LoadWavs(job);
        if (wavs.All(w => w == null || w.Length == 0)) throw new InvalidOperationException("the saved recordings are missing");

        var check = await KaraokeResults.CheckAllAsync(KaraokeResults.CreateGemini(job.model, job.fallbackModel), wavs, chunks);
        var sung = textOf.OrderBy(kv => kv.Key).Select(kv => new LrcLine { Text = kv.Value }).ToList();
        int score = KaraokeScorer.Score(sung, check.Lines);
        bool sent = await KaraokeResults.SaveAsync(job.videoTitle, job.artist, job.videoId, score, check, job.CreatedUtc);
        KaraokeResults.RaiseLaterChecked(new KaraokeResults.LaterResult   // カラオケの画面にも点数を出す
        {
            Title = job.videoTitle ?? "", Artist = job.artist ?? "", VideoId = job.videoId ?? "", Score = score, Check = check,
            Rows = textOf.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList(),
        });
        return $"Your saved karaoke was checked: {score} / 100" + Where(sent);
    }

    // ---------------- 共通 ----------------

    private static DiscordClient SummaryDiscord()
    {
        var s = S;
        string channel = s.summaryDiscordChannelId;
        return new DiscordClient(s.summaryDiscordWebhookUrl, string.IsNullOrWhiteSpace(channel) ? "" : s.discordBotToken, channel);
    }

    private static string Where(bool discord) => discord ? ". See Discord and the web page." : ".";

    // ログインしていれば保存する (signIn: false なら、ログインの確認は save の中でする)。失敗は Console だけ
    private static async Task CloudAsync(string what, Func<SupabaseService, Task> save, bool signIn = true)
    {
        var sb = UnityEngine.Object.FindObjectOfType<SupabaseService>(true);
        if (sb == null || !sb.IsConfigured) return;
        try
        {
            if (signIn && !await sb.EnsureSignedInAsync()) return;
            await save(sb);
            var diary = UnityEngine.Object.FindObjectOfType<DiaryManager>(true);
            if (diary != null) diary.RefreshStreakFromCloud();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Later] could not save the {what} result to the cloud: " + e.Message);
        }
    }

    private static async Task<bool> DiscordAsync(GeminiLater.Job job, DiscordClient discord, Func<DiscordClient, Task> send)
    {
        if (!job.sendToDiscord || !discord.IsConfigured) return false;
        try { await send(discord); return true; }
        catch (Exception e) { Debug.LogWarning("[Later] could not send to Discord: " + e.Message); return false; }
    }
}
