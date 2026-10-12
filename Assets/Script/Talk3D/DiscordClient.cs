using System;
using System.Collections.Generic;
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

    // ------------------------------------------------------------
    // Internal
    // ------------------------------------------------------------
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