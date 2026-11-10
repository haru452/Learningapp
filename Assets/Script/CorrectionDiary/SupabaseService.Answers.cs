using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// SupabaseService の続き: 単語帳の発音の記録 (vocab_pron.sql) と、短い答えの練習 (answer.sql)。
/// </summary>
public partial class SupabaseService
{
    /// <summary>
    /// 単語帳の発音で、苦手な音があった単語を 1 回ずつ残す (音の無い単語は送らない)。失敗・ログインなしは Console だけ
    /// </summary>
    public async Task SaveVocabPronLogsAsync(IList<GeminiApiClient.WordPronResult> results, IDictionary<string, string> heard,
                                            DateTime takenAtUtc)
    {
        string items = PracticeJson.VocabPronItems(results, heard);
        if (items == "[]" || !IsConfigured || !IsSignedIn) return;
        try
        {
            await RpcAsync("save_vocab_pron_logs", ",\"p_items\":" + items + ",\"p_taken_at\":\"" + PracticeJson.Utc(takenAtUtc) + "\"");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Supabase] could not save vocab pronunciation logs: " + e.Message);
        }
    }

    [Serializable] private class AnswerRows { public AnswerRow[] rows; }
    [Serializable] private class AnswerRow { public int streak; public bool already_done; public int saved; }

    /// <summary>短い答えの練習の 1 回分を残して、連続記録を進める。返すのは (連続日数, もう今日数えてあったか)</summary>
    public async Task<(int streak, bool alreadyDone)> CompleteShortAnswerAsync(IList<ShortAnswerResult> results, DateTime takenAtUtc)
    {
        string body = await RpcAsync("complete_short_answer_session",
            ",\"p_items\":" + PracticeJson.ShortAnswerItems(results) + ",\"p_timezone\":\"Asia/Tokyo\",\"p_taken_at\":\"" +
            PracticeJson.Utc(takenAtUtc) + "\"");
        var rows = JsonUtility.FromJson<AnswerRows>("{\"rows\":" + body + "}");
        var row = rows?.rows?.FirstOrDefault();
        return row == null ? (0, false) : (row.streak, row.already_done);
    }
}

/// <summary>練習の結果を RPC に送る JSON にする (Unity に依存しない。テストあり)</summary>
public static class PracticeJson
{
    public static string Utc(DateTime t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>苦手な音のある単語だけ、最大 20 語</summary>
    public static string VocabPronItems(IList<GeminiApiClient.WordPronResult> results, IDictionary<string, string> heard)
    {
        var sb = new StringBuilder("[");
        int n = 0;
        foreach (var r in results ?? new List<GeminiApiClient.WordPronResult>())
        {
            if (r == null || string.IsNullOrWhiteSpace(r.Word) || r.Sounds == null || r.Sounds.Count == 0) continue;
            if (n >= GeminiApiClient.MaxVocabWords) break;
            string h = heard != null && heard.TryGetValue(r.Word, out var x) ? x : "";
            if (n > 0) sb.Append(',');
            sb.Append("{\"word\":\"").Append(HttpUtil.JsonEscape(r.Word))
              .Append("\",\"rating\":\"").Append(HttpUtil.JsonEscape(r.Rating ?? ""))
              .Append("\",\"sounds\":[").Append(string.Join(",", r.Sounds.Select(s => "\"" + HttpUtil.JsonEscape(s) + "\"")))
              .Append("],\"advice\":\"").Append(HttpUtil.JsonEscape(r.Advice ?? ""))
              .Append("\",\"heard\":\"").Append(HttpUtil.JsonEscape(h ?? "")).Append("\"}");
            n++;
        }
        return sb.Append(']').ToString();
    }

    /// <summary>カラオケ: 赤い単語がある行だけ (最大 200 行)</summary>
    public static string KaraokeItems(IEnumerable<KaraokeLineResult> results)
    {
        var parts = (results ?? Enumerable.Empty<KaraokeLineResult>()).Where(r => r != null && r.Missed.Count > 0).Take(200).Select(r =>
            "{\"line\":" + r.Line + ",\"text\":\"" + HttpUtil.JsonEscape(r.Text ?? "") + "\",\"words\":[" +
            string.Join(",", r.Missed.Select(m => "{\"word\":\"" + HttpUtil.JsonEscape(m.Word ?? "") + "\",\"sound\":\"" +
                                                   HttpUtil.JsonEscape(m.Sound ?? "") + "\"}")) + "]}");
        return "[" + string.Join(",", parts) + "]";
    }

    public static string ShortAnswerItems(IList<ShortAnswerResult> results)
    {
        var parts = (results ?? new List<ShortAnswerResult>()).Where(r => r != null && r.Question != null).Take(20).Select(r =>
            "{\"question\":\"" + HttpUtil.JsonEscape(r.Question) + "\",\"answers\":[" +
            string.Join(",", (r.Answers ?? new List<string>()).Select(a => "\"" + HttpUtil.JsonEscape(a) + "\"")) +
            "],\"heard\":\"" + HttpUtil.JsonEscape(r.Heard ?? "") + "\",\"correct\":" + (r.Correct ? "true" : "false") + "}");
        return "[" + string.Join(",", parts) + "]";
    }
}
