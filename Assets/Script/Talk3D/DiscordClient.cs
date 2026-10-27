using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Discord にメッセージを送信する。
///  - Webhook URL が設定されていれば Webhook で送信 (推奨・簡単)
///  - 無ければ Bot トークン + チャンネルID で送信
/// Discord の1メッセージは2000文字までなので、長い日記は自動で分割して送る。
/// </summary>
public class DiscordClient
{
    private const int MaxMessageLength = 1900;   // 上限2000字に余裕を持たせる
    private const string ApiBase = "https://discord.com/api/v10";
    private const string UserAgent = "DiscordBot (https://example.com, 1.0)";

    private readonly string _webhookUrl;
    private readonly string _botToken;
    private readonly string _channelId;

    public DiscordClient(string webhookUrl, string botToken, string channelId)
    {
        _webhookUrl = webhookUrl?.Trim();
        _botToken = botToken?.Trim();
        _channelId = channelId?.Trim();
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_webhookUrl) ||
        (!string.IsNullOrEmpty(_botToken) && !string.IsNullOrEmpty(_channelId));

    /// <summary>
    /// 日記の添削結果を送信する。
    /// underlineChanges が true なら、添削文のうち「原文(話した内容)と違う単語」に下線を付ける。
    /// includeOriginal が true なら、原文も末尾に付ける。
    /// </summary>
    public async Task SendDiaryAsync(
        DateTime date,
        string corrected,
        string explanation,
        string original,
        bool includeOriginal,
        bool underlineChanges)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord is not configured.");

        string correctedBlock = corrected;
        string correctedNote = null;

        if (underlineChanges && !string.IsNullOrWhiteSpace(original))
        {
            string marked = TextDiff.UnderlineChanges(original, corrected, out int changed);
            if (changed >= 0)   // -1 は「長すぎて比較できなかった」ので、下線なしのまま送る
            {
                correctedBlock = marked;
                correctedNote = changed > 0
                    ? "(__underlined__ = changed from what you said)"
                    : "(no changes from what you said)";
            }
        }

        string full =
            "\U0001F4D3 **Diary - " + date.ToString("yyyy-MM-dd HH:mm") + "**\n\n" +
            Section("Corrected", correctedBlock, correctedNote) +
            Section("Notes", explanation) +
            Section("Original", includeOriginal ? original : null);

        List<string> chunks = Split(full, MaxMessageLength);
        for (int i = 0; i < chunks.Count; i++)
        {
            await PostAsync(chunks[i]);
            if (i < chunks.Count - 1)
                await Task.Delay(500);   // レート制限対策
        }
    }

    /// <summary>要約練習 (Versant 対策) の採点結果を送信する。項目ごとの点数は棒グラフ風に並べる。</summary>
    public async Task SendSummaryResultAsync(
        DateTime date, string videoTitle, string videoUrl, GeminiApiClient.SummaryGrade g)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord is not configured.");

        // 等幅で揃えるため、コードブロックにする
        string breakdown =
            "```\n" +
            ScoreBar("Content", g.contentScore, GeminiApiClient.ContentMax) +
            ScoreBar("Organization", g.organizationScore, GeminiApiClient.OrganizationMax) +
            ScoreBar("Grammar/Vocab", g.languageScore, GeminiApiClient.LanguageMax) +
            "```\n";

        // URL を <> で囲んで、動画のプレビュー(埋め込み)を出さない
        string full =
            "\U0001F3AC **Summary Practice - " + date.ToString("yyyy-MM-dd HH:mm") + "**\n" +
            "Score: **" + g.score + " / 100**\n" +
            breakdown +
            "Video: " + videoTitle + " <" + videoUrl + ">\n\n" +
            Section("Your summary", g.transcript) +
            Section("Good points", g.goodPoints) +
            Section("Missed points", g.missedPoints) +
            Section("Language", g.languageFeedback) +
            Section("Model summary", g.referenceSummary) +
            Section("Your summary, improved", g.improvedSummary);

        List<string> chunks = Split(full, MaxMessageLength);
        for (int i = 0; i < chunks.Count; i++)
        {
            await PostAsync(chunks[i]);
            if (i < chunks.Count - 1)
                await Task.Delay(500);   // レート制限対策
        }
    }

    /// <summary>お題スピーキングの結果を送信する (要約練習と同じチャンネル)。</summary>
    public async Task SendTopicResultAsync(DateTime date, string kind, string topic, GeminiApiClient.TopicGrade g)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord is not configured.");

        string breakdown =
            "```\n" +
            ScoreBar("Content", g.contentScore, GeminiApiClient.TopicContentMax) +
            ScoreBar("Organization", g.organizationScore, GeminiApiClient.TopicOrganizationMax) +
            ScoreBar("Grammar/Vocab", g.languageScore, GeminiApiClient.TopicLanguageMax) +
            "```\n";

        string phrases = g.phrases != null && g.phrases.Length > 0 ? "- " + string.Join("\n- ", g.phrases) : "";
        string full =
            "\U0001F5E3 **Topic Speaking (" + kind + ") - " + date.ToString("yyyy-MM-dd HH:mm") + "**\n" +
            "Topic: " + topic + "\n" +
            "Score: **" + g.score + " / 100**\n" +
            breakdown + "\n" +
            Section("Good points", g.goodPoints) +
            Section("To improve", g.fixPoints) +
            Section("Corrected", g.corrected) +
            Section("More natural", g.natural) +
            Section("Useful phrases", phrases);

        List<string> chunks = Split(full, MaxMessageLength);
        for (int i = 0; i < chunks.Count; i++)
        {
            await PostAsync(chunks[i]);
            if (i < chunks.Count - 1)
                await Task.Delay(500);   // レート制限対策
        }
    }

    /// <summary>リピーティング 1 回分 (5 文) の結果を送る。長いときは分けて送る</summary>
    public async Task SendRepeatResultsAsync(DateTime date, IList<RepeatResult> results)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord is not configured.");
        List<string> chunks = Split(RepeatMessage(date, results), MaxMessageLength);
        for (int i = 0; i < chunks.Count; i++)
        {
            await PostAsync(chunks[i]);
            if (i < chunks.Count - 1)
                await Task.Delay(500);   // レート制限対策
        }
    }

    /// <summary>リピーティングのメッセージ: 平均と、文ごとの一致率 (80% 以上 ✅、未満 ⚠️ と聞き取れた文)</summary>
    public static string RepeatMessage(DateTime date, IList<RepeatResult> results)
    {
        results = results ?? new List<RepeatResult>();
        int average = results.Count == 0 ? 0
            : (int)Math.Round(results.Average(r => r.accuracy), MidpointRounding.AwayFromZero);
        var sb = new System.Text.StringBuilder();
        sb.Append("\U0001F501 **Repeat practice - ").Append(date.ToString("yyyy-MM-dd HH:mm")).Append("**\n")
          .Append("Average: ").Append(average).Append("%\n");
        return AppendResultLines(sb, results);
    }

    /// <summary>シャドーイング 1 回分 (動画 1 本) の結果を送る (リピーティングと同じチャンネル、長いときは分けて送る)</summary>
    public async Task SendShadowResultsAsync(DateTime date, string videoTitle, string videoUrl, int average, string advice,
                                             IList<ShadowScorer.SentenceScore> scores)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord is not configured.");
        List<string> chunks = Split(ShadowMessage(date, videoTitle, videoUrl, average, advice, scores), MaxMessageLength);
        for (int i = 0; i < chunks.Count; i++)
        {
            await PostAsync(chunks[i]);
            if (i < chunks.Count - 1)
                await Task.Delay(500);   // レート制限対策
        }
    }

    /// <summary>シャドーイングのメッセージ: 動画・平均・アドバイス・文ごとの一致率 (80% 以上 ✅、未満 ⚠️)</summary>
    public static string ShadowMessage(DateTime date, string videoTitle, string videoUrl, int average, string advice,
                                       IList<ShadowScorer.SentenceScore> scores)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("\U0001F3A7 **Shadowing - ").Append(date.ToString("yyyy-MM-dd HH:mm")).Append("**\n");
        if (!string.IsNullOrWhiteSpace(videoTitle)) sb.Append(videoTitle.Trim()).Append('\n');
        if (!string.IsNullOrWhiteSpace(videoUrl)) sb.Append('<').Append(videoUrl.Trim()).Append(">\n");   // 埋め込みを出さない
        sb.Append("Average: ").Append(average).Append("%\n");
        if (!string.IsNullOrWhiteSpace(advice)) sb.Append("**Advice**\n").Append(advice.Trim()).Append("\n\n");
        foreach (var s in scores ?? new List<ShadowScorer.SentenceScore>())
        {
            bool ok = s.Accuracy >= 80;
            sb.Append(ok ? "\u2705" : "\u26A0\uFE0F").Append(' ')
              .Append(s.Accuracy.ToString().PadLeft(3)).Append("%  ").Append((s.Text ?? "").Trim()).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static string AppendResultLines(System.Text.StringBuilder sb, IList<RepeatResult> results)
    {
        foreach (var r in results)
        {
            bool ok = r.accuracy >= 80;
            sb.Append(ok ? "\u2705" : "\u26A0\uFE0F").Append(' ')
              .Append(r.accuracy.ToString().PadLeft(3)).Append("%  ").Append((r.sentence ?? "").Trim());
            if (!ok && !string.IsNullOrWhiteSpace(r.heard)) sb.Append("   (heard: ").Append(r.heard.Trim()).Append(')');
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>今日の発音練習 (苦手 1 位の音と、その練習動画) を送る (発音用のチャンネル)</summary>
    public async Task SendSoundPracticeAsync(DateTime date, WeakSound top, YouTubeSearchClient.Video video)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord is not configured.");
        await PostAsync(SoundPracticeMessage(date, top, video));
    }

    /// <summary>今日の発音練習のメッセージ (絵文字は使わない)</summary>
    public static string SoundPracticeMessage(DateTime date, WeakSound top, YouTubeSearchClient.Video video)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("**Today's pronunciation practice - ").Append(date.ToString("yyyy-MM-dd")).Append("**\n");
        string label = SoundTypes.Label(top.Sound, top.Label);
        string example = SoundTypes.Example(top.Sound);
        sb.Append("Your weakest sound: ").Append(label).Append(example.Length > 0 ? " (" + example + ")" : "")
          .Append(" \u2014 ").Append(top.Points).Append(" points in 30 days\n");
        if (top.Words != null && top.Words.Count > 0)
            sb.Append("Words you missed: ").Append(string.Join(", ", top.Words)).Append('\n');
        if (video != null)
            sb.Append("Video: ").Append((video.title ?? "").Trim()).Append(" (").Append((video.channel ?? "").Trim()).Append(")\n")
              .Append(video.WatchUrl);
        return sb.ToString().TrimEnd();
    }

    /// <summary>発音チェックで Gemini からもらったアドバイスを送る (1 メッセージ)</summary>
    public async Task SendPronunciationAsync(DateTime date, string topic, string sentence, string advice)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Discord is not configured.");
        await PostAsync(PronunciationMessage(date, topic, sentence, advice));
    }

    /// <summary>発音チェックのメッセージ。長すぎるアドバイスは 1 メッセージに収まるよう切る</summary>
    public static string PronunciationMessage(DateTime date, string topic, string sentence, string advice)
    {
        string head =
            "\U0001F3A4 **Pronunciation check - " + date.ToString("yyyy-MM-dd HH:mm") + "**\n" +
            (string.IsNullOrWhiteSpace(topic) ? "" : "Topic: " + topic.Trim() + "\n") +
            "> " + (sentence ?? "").Trim().Replace("\n", "\n> ") + "\n\n";
        string body = (advice ?? "").Trim();
        int room = MaxMessageLength - head.Length;
        if (body.Length > room) body = body.Substring(0, Math.Max(0, room - 1)) + "\u2026";
        return head + body;
    }

    // ------------------------------------------------------------
    // Internal
    // ------------------------------------------------------------
    // "Content        ████████░░  38/50"
    private static string ScoreBar(string label, int score, int max)
    {
        const int width = 10;
        int filled = max > 0 ? (int)Math.Round((double)score / max * width) : 0;
        filled = Math.Max(0, Math.Min(width, filled));
        return label.PadRight(14) + new string('█', filled) + new string('░', width - filled) +
               "  " + (score + "/" + max).PadLeft(5) + "\n";
    }

    private static string Section(string title, string body, string note = null)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        string heading = "**" + title + "**" + (string.IsNullOrEmpty(note) ? "" : " " + note);
        return heading + "\n" + body.Trim() + "\n\n";
    }

    private async Task PostAsync(string content)
    {
        // allowed_mentions.parse = [] で、日記中の @everyone などによる通知を無効化する
        string body =
            "{\"content\":\"" + HttpUtil.JsonEscape(content) + "\"," +
            "\"allowed_mentions\":{\"parse\":[]}}";

        string url;
        (string, string)[] headers;

        if (!string.IsNullOrEmpty(_webhookUrl))
        {
            url = _webhookUrl;
            headers = null;
        }
        else
        {
            url = ApiBase + "/channels/" + _channelId + "/messages";
            headers = new[]
            {
                ("Authorization", "Bot " + _botToken),
                ("User-Agent", UserAgent)
            };
        }

        const int maxAttempts = 3;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await HttpUtil.PostJsonAsync(url, body, headers);
                return;
            }
            catch (HttpException e) when (e.StatusCode == 429 && attempt < maxAttempts - 1)
            {
                // レート制限: 少し待って再試行
                await Task.Delay(2000 * (attempt + 1));
            }
        }
    }

    private static int CountMarkers(string text)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf("__", index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += 2;
        }
        return count;
    }

    /// <summary>改行 → スペースの順で区切りの良い位置を探し、max 文字以内に分割する。</summary>
    private static List<string> Split(string text, int max)
    {
        var parts = new List<string>();
        string rest = text.Trim();

        while (rest.Length > max)
        {
            int cut = rest.LastIndexOf('\n', max - 1);
            if (cut < max / 2) cut = rest.LastIndexOf(' ', max - 1);
            if (cut < max / 2) cut = max;
            if (char.IsHighSurrogate(rest[cut - 1])) cut--;   // 絵文字などを途中で切らない

            // 下線 __...__ の途中で切ると、両方のメッセージで装飾が崩れるため、
            // 開きの __ の手前まで戻して切る
            string head = rest.Substring(0, cut);
            if (CountMarkers(head) % 2 == 1)
            {
                int open = head.LastIndexOf("__", StringComparison.Ordinal);
                if (open > max / 4) cut = open;
            }

            parts.Add(rest.Substring(0, cut).TrimEnd());
            rest = rest.Substring(cut).TrimStart();
        }

        if (rest.Length > 0) parts.Add(rest);
        return parts;
    }
}