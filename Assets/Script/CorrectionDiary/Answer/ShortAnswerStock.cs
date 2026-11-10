using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 短い答えの練習の問題の作り置き (端末の answer_stock.json)。
/// アプリを開いたら裏で 10 問作っておき、Start ですぐ始められるようにする。使った分は裏で作り足す。
/// 答えなかった問題は戻す (使わなかった問題は保存したまま)。
/// </summary>
public static class ShortAnswerStock
{
    public const int Size = GeminiApiClient.ShortAnswerCount;

    private static Task _filling = Task.CompletedTask;
    private const int MaxRetries = 5;
    private const int RetryDelayMs = 60000;
    private static int _retries;

    /// <summary>最後に作れなかった理由 (作れたら空)。画面のメッセージに使う</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>Gemini が混んでいて作れなかったか (503 / UNAVAILABLE / high demand)</summary>
    public static bool IsBusy(string error) =>
        !string.IsNullOrEmpty(error) && (error.Contains("503") || error.Contains("UNAVAILABLE") || error.Contains("high demand"));

    private static string PathOf => Path.Combine(Application.persistentDataPath, "answer_stock.json");

    /// <summary>作り置きの数</summary>
    public static int Count => Load().Count;

    /// <summary>作り置きから最大 n 問を取り出す (残りは保存したまま)</summary>
    public static List<ShortAnswerQuestion> Take(int n)
    {
        var all = Load();
        var (taken, rest) = Split(all, n);
        Save(rest);
        return taken;
    }

    /// <summary>使わなかった問題を、作り置きの先頭に戻す</summary>
    public static void PutBack(IEnumerable<ShortAnswerQuestion> unused)
    {
        var list = (unused ?? Enumerable.Empty<ShortAnswerQuestion>()).Where(q => q != null).ToList();
        if (list.Count == 0) return;
        Save(Merge(list, Load()));
    }

    /// <summary>
    /// 作り置きが Size 問より少なければ、裏で作り足す (同時に動くのは 1 つ。動いていればそれを返す)。失敗は Console だけ
    /// </summary>
    public static Task FillAsync(GeminiApiClient client, Func<IList<string>> recent)
    {
        if (!_filling.IsCompleted) return _filling;
        if (client == null || Count >= Size) return Task.CompletedTask;
        _filling = DoFillAsync(client, recent);
        return _filling;
    }

    private static async Task DoFillAsync(GeminiApiClient client, Func<IList<string>> recent)
    {
        try
        {
            var have = Load();
            // 最近出した問題と作り置きの問題は、もう作らないように伝える
            var avoid = have.Select(q => q.Question).Concat(recent?.Invoke() ?? new List<string>()).ToList();
            var made = await client.GenerateShortAnswerQuestionsAsync(avoid);
            Save(Merge(Load(), made));   // 作っている間に取り出された分も考えて、読み直してから足す
            LastError = "";
            _retries = 0;
        }
        catch (Exception e)
        {
            LastError = e.Message ?? "";
            Debug.LogWarning("[Answer] could not make questions in advance: " + e.Message);
            // Gemini が混んでいるときなどは、アプリを開いている間 1 分おきに作り直す (最大 5 回)
            if (_retries < MaxRetries)
            {
                _retries++;
                _ = RetryLaterAsync(client, recent);
            }
        }
    }

    private static async Task RetryLaterAsync(GeminiApiClient client, Func<IList<string>> recent)
    {
        await Task.Delay(RetryDelayMs);
        await FillAsync(client, recent);
    }

    // ---------------- 純粋な部分 (テストあり) ----------------

    public static (List<ShortAnswerQuestion> taken, List<ShortAnswerQuestion> rest) Split(IList<ShortAnswerQuestion> all, int n)
    {
        var list = (all ?? new List<ShortAnswerQuestion>()).ToList();
        int k = Math.Max(0, Math.Min(n, list.Count));
        return (list.Take(k).ToList(), list.Skip(k).ToList());
    }

    /// <summary>first → second の順に並べ、同じ問題 (大文字小文字を無視) は 1 つにする</summary>
    public static List<ShortAnswerQuestion> Merge(IEnumerable<ShortAnswerQuestion> first, IEnumerable<ShortAnswerQuestion> second)
    {
        var seen = new HashSet<string>();
        var result = new List<ShortAnswerQuestion>();
        foreach (var q in (first ?? Enumerable.Empty<ShortAnswerQuestion>()).Concat(second ?? Enumerable.Empty<ShortAnswerQuestion>()))
            if (q != null && !string.IsNullOrWhiteSpace(q.Question) && seen.Add(q.Question.Trim().ToLowerInvariant())) result.Add(q);
        return result;
    }

    /// <summary>Gemini の返事と同じ形の JSON (読むのは GeminiApiClient.ParseShortAnswerQuestions)</summary>
    public static string ToJson(IEnumerable<ShortAnswerQuestion> list)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var q in list ?? Enumerable.Empty<ShortAnswerQuestion>())
        {
            if (q == null) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"question\":\"").Append(HttpUtil.JsonEscape(q.Question)).Append("\",\"answers\":[")
              .Append(string.Join(",", (q.Answers ?? new List<string>()).Select(a => "\"" + HttpUtil.JsonEscape(a) + "\"")))
              .Append("]}");
        }
        return sb.Append(']').ToString();
    }

    // ---------------- 保存 ----------------

    private static List<ShortAnswerQuestion> Load()
    {
        try { return File.Exists(PathOf) ? GeminiApiClient.ParseShortAnswerQuestions(File.ReadAllText(PathOf)) : new List<ShortAnswerQuestion>(); }
        catch (Exception e) { Debug.LogWarning("[Answer] could not read the question stock: " + e.Message); return new List<ShortAnswerQuestion>(); }
    }

    private static void Save(List<ShortAnswerQuestion> list)
    {
        try { File.WriteAllText(PathOf, ToJson(list)); }
        catch (Exception e) { Debug.LogWarning("[Answer] could not save the question stock: " + e.Message); }
    }
}
