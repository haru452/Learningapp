using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// SupabaseService の続き: 日記・要約・お題・リピーティングの結果の保存と、ログイン前の結果の送信。
/// </summary>
public partial class SupabaseService
{
    // ------------------------------------------------------------
    // Diary (直近7日分だけクラウドに保存される。古いものはサーバーが自動で消す。見るのはWeb版)
    // ------------------------------------------------------------

    /// <summary>添削した日記を1件保存する。</summary>
    public async Task SaveDiaryAsync(string original, string corrected, string explanation, string engine)
    {
        string extra =
            ",\"p_original\":\"" + HttpUtil.JsonEscape(original ?? "") + "\"" +
            ",\"p_corrected\":\"" + HttpUtil.JsonEscape(corrected ?? "") + "\"" +
            ",\"p_explanation\":\"" + HttpUtil.JsonEscape(explanation ?? "") + "\"" +
            ",\"p_engine\":\"" + HttpUtil.JsonEscape(engine ?? "") + "\"";
        await RpcAsync("save_diary_entry", extra);
    }

    // ------------------------------------------------------------
    // 要約練習 (Versant 対策) の採点結果。点数の推移を見るため、古いものも残る (見るのはWeb版)
    // ------------------------------------------------------------

    /// <summary>採点結果を1件保存する。</summary>
    /// <param name="takenAtUtc">
    /// 練習した日時。ログイン前の結果を後から送るときに、記録の日付をその日にするために使う。
    /// (サーバーの関数が古く、この引数を受け付けない場合は、付けずに送り直す)
    /// </param>
    public async Task SaveSummaryResultAsync(
        string videoId, string videoTitle, int timeLimitSeconds, GeminiApiClient.SummaryGrade grade,
        DateTime? takenAtUtc = null)
    {
        string Field(string name, string value) =>
            ",\"" + name + "\":\"" + HttpUtil.JsonEscape(value ?? "") + "\"";

        string extra =
            Field("p_video_id", videoId) +
            Field("p_video_title", videoTitle) +
            ",\"p_time_limit\":" + timeLimitSeconds +
            ",\"p_score\":" + grade.score +
            ",\"p_content_score\":" + grade.contentScore +
            ",\"p_organization_score\":" + grade.organizationScore +
            ",\"p_language_score\":" + grade.languageScore +
            Field("p_transcript", grade.transcript) +
            Field("p_reference", grade.referenceSummary) +
            Field("p_good_points", grade.goodPoints) +
            Field("p_missed_points", grade.missedPoints) +
            Field("p_language_feedback", grade.languageFeedback) +
            Field("p_improved", grade.improvedSummary);

        if (takenAtUtc == null)
        {
            await RpcAsync("save_summary_result", extra);
            return;
        }

        string takenAt = takenAtUtc.Value.ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await RpcAsync("save_summary_result", extra + Field("p_taken_at", takenAt));
        }
        catch (SupabaseException e) when (e.StatusCode == 404)
        {
            // summary.sql を更新する前のデータベース (p_taken_at が無い) → 日時なしで保存する
            Debug.LogWarning("[Supabase] save_summary_result does not accept p_taken_at yet. Saving without it.");
            await RpcAsync("save_summary_result", extra);
        }
    }

    // ------------------------------------------------------------
    // ログイン前に受けた要約練習の結果を、ログインしたときにまとめて送る
    // ------------------------------------------------------------
    private bool _uploadingPending;

    /// <summary>端末に一時保存されていた結果を送り終えたとき (引数: 送れた件数)</summary>
    public event Action<int> PendingSummaryUploaded;

    /// <summary>
    /// 端末に一時保存されている要約練習の結果を、古い順にクラウドへ送る。送れたものは端末から消す。
    /// 通信できないなどで失敗したら、残りは次回 (次のログイン・アプリの起動時) に送る。送れた件数を返す。
    /// </summary>
    public async Task<int> UploadPendingSummaryResultsAsync()
    {
        if (_uploadingPending || !IsSignedIn) return 0;
        _uploadingPending = true;
        int uploaded = 0;
        try
        {
            var list = PendingSummaryStore.Load();
            while (list.Count > 0 && IsSignedIn)
            {
                var item = list[0];
                try
                {
                    await SaveSummaryResultAsync(item.videoId, item.videoTitle, item.timeLimitSeconds, item.grade, item.TakenAtUtc);
                    uploaded++;
                    try { await SaveMistakesAsync("summary", item.grade?.mistakes, item.TakenAtUtc); }
                    catch (Exception e) { Debug.LogWarning("[Supabase] could not save mistakes of a pending summary: " + e.Message); }
                }
                catch (SupabaseException e) when (e.StatusCode == 400)
                {
                    // 内容が不正で、何度送っても保存できないもの → 捨てる
                    Debug.LogWarning("[Supabase] dropped a pending summary result that cannot be saved: " + e.Message);
                }
                catch (Exception e)
                {
                    // 通信エラーなど → 残しておき、次回に送る
                    Debug.LogWarning("[Supabase] could not upload pending summary results now: " + e.Message);
                    break;
                }

                list.RemoveAt(0);
                PendingSummaryStore.Save(list);
            }
        }
        finally
        {
            _uploadingPending = false;
        }

        if (uploaded > 0)
        {
            Debug.Log($"[Supabase] uploaded {uploaded} summary result(s) taken before signing in.");
            PendingSummaryUploaded?.Invoke(uploaded);
        }
        return uploaded;
    }

    // ------------------------------------------------------------
    // お題スピーキングの結果と、よくあるミス (supabase/topic.sql)
    // ------------------------------------------------------------
    public struct WeakPoint
    {
        public string Type;          // MistakeTypes.All のどれか
        public int Count;            // 直近 30 日の回数
        public int PreviousCount;    // その前の 30 日の回数
    }

    [Serializable] private class WeakRow { public string type; public int count; public int previous_count; }
    [Serializable] private class WeakRows { public WeakRow[] rows; }

    private static string TakenAtJson(DateTime? takenAtUtc) =>
        takenAtUtc == null
            ? ""
            : ",\"p_taken_at\":\"" + takenAtUtc.Value.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) + "\"";

    /// <summary>お題スピーキングの結果 (とミス) を1件保存する。保存した結果の id を返す</summary>
    public async Task<long> SaveTopicResultAsync(
        string kind, string topic, int seconds, GeminiApiClient.TopicGrade g, string transcript,
        int? readAloudAccuracy, DateTime? takenAtUtc = null)
    {
        string Field(string name, string value) =>
            ",\"" + name + "\":\"" + HttpUtil.JsonEscape(value ?? "") + "\"";

        string extra =
            Field("p_kind", kind) +
            Field("p_topic", topic) +
            ",\"p_time_limit\":" + seconds +
            ",\"p_score\":" + g.score +
            ",\"p_content_score\":" + g.contentScore +
            ",\"p_organization_score\":" + g.organizationScore +
            ",\"p_language_score\":" + g.languageScore +
            Field("p_transcript", transcript) +
            Field("p_corrected", g.corrected) +
            Field("p_natural", g.natural) +
            Field("p_good_points", g.goodPoints) +
            Field("p_fix_points", g.fixPoints) +
            Field("p_phrases", g.phrases != null ? string.Join("\n", g.phrases) : "") +
            ",\"p_read_aloud_accuracy\":" + (readAloudAccuracy.HasValue ? readAloudAccuracy.Value.ToString() : "null") +
            ",\"p_mistakes\":" + MistakeTypes.ToJsonArray(g.mistakes) +
            TakenAtJson(takenAtUtc);

        string body = await RpcAsync("save_topic_result", extra);
        return long.TryParse(body.Trim(), out long id) ? id : 0;
    }

    /// <summary>言い直しの一致率を、保存済みの結果にあとから入れる</summary>
    public async Task SetTopicReadAloudAsync(long id, int accuracy)
    {
        if (id <= 0) return;
        await RpcAsync("set_topic_read_aloud", ",\"p_id\":" + id + ",\"p_accuracy\":" + Mathf.Clamp(accuracy, 0, 100));
    }

    /// <summary>日記・要約の添削で見つかったミスを保存する。0 件なら何もしない</summary>
    public async Task SaveMistakesAsync(string source, Mistake[] mistakes, DateTime? takenAtUtc = null)
    {
        if (mistakes == null || mistakes.Length == 0) return;
        string extra =
            ",\"p_source\":\"" + HttpUtil.JsonEscape(source ?? "") + "\"" +
            ",\"p_mistakes\":" + MistakeTypes.ToJsonArray(mistakes) +
            TakenAtJson(takenAtUtc);
        await RpcAsync("save_mistakes", extra);
    }

    /// <summary>
    /// 直近 30 日で多いミスの種類 (最大 3 つ、多い順)。
    /// ログインしていない・通信できない・サーバーが古いときは空 (例外は出さない)。
    /// </summary>
    public async Task<WeakPoint[]> GetWeakPointsAsync()
    {
        try
        {
            if (!await EnsureSignedInAsync()) return new WeakPoint[0];
            string body = await RpcAsync("get_my_weak_points");
            var rows = JsonUtility.FromJson<WeakRows>("{\"rows\":" + body + "}");
            if (rows?.rows == null) return new WeakPoint[0];
            var list = new WeakPoint[rows.rows.Length];
            for (int i = 0; i < list.Length; i++)
                list[i] = new WeakPoint
                {
                    Type = MistakeTypes.Normalize(rows.rows[i].type),
                    Count = rows.rows[i].count,
                    PreviousCount = rows.rows[i].previous_count,
                };
            return list;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Supabase] could not get weak points: " + e.Message);
            return new WeakPoint[0];
        }
    }

    // ------------------------------------------------------------
    // リピーティング
    // ------------------------------------------------------------
    /// <summary>リピーティング 1 回分の結果を保存する (最大 10 文)。保存した件数を返す</summary>
    public async Task<int> SaveRepeatResultsAsync(IList<RepeatResult> results, DateTime takenAtUtc)
    {
        string body = await RpcAsync("save_repeat_results", RepeatRpc.SaveArgs(results, takenAtUtc));
        return int.TryParse((body ?? "").Trim(), out int n) ? n : 0;
    }

    /// <summary>次の練習に出す文 (言えなかった文と自分の添削文)。ログインしていない・失敗したら空</summary>
    public async Task<List<RepeatSentence>> GetRepeatSentencesAsync(int missed = 2, int own = 2)
    {
        try
        {
            if (!await EnsureSignedInAsync()) return new List<RepeatSentence>();
            string body = await RpcAsync("get_my_repeat_sentences", ",\"p_missed\":" + missed + ",\"p_own\":" + own);
            return RepeatRpc.ParseSentences(body);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Supabase] could not get repeat sentences: " + e.Message);
            return new List<RepeatSentence>();
        }
    }

    private bool _uploadingPendingRepeat;

    /// <summary>端末に一時保存されているリピーティングの結果を、古い順に送る。送れた回数を返す</summary>
    public async Task<int> UploadPendingRepeatResultsAsync()
    {
        if (_uploadingPendingRepeat || !IsSignedIn) return 0;
        _uploadingPendingRepeat = true;
        int uploaded = 0;
        try
        {
            var list = PendingRepeatStore.Load();
            while (list.Count > 0 && IsSignedIn)
            {
                var batch = list[0];
                try
                {
                    await SaveRepeatResultsAsync(batch.results, batch.TakenAtUtc);
                    uploaded++;
                }
                catch (SupabaseException e) when (e.StatusCode == 400)
                {
                    Debug.LogWarning("[Supabase] dropped pending repeat results that cannot be saved: " + e.Message);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Supabase] could not upload pending repeat results now: " + e.Message);
                    break;
                }
                list.RemoveAt(0);
                PendingRepeatStore.Save(list);
            }
        }
        finally
        {
            _uploadingPendingRepeat = false;
        }
        if (uploaded > 0) Debug.Log($"[Supabase] uploaded {uploaded} repeat practice(s) done before signing in.");
        return uploaded;
    }

    // ------------------------------------------------------------
    // ログイン前に受けたお題スピーキングの結果を、ログインしたときにまとめて送る
    // ------------------------------------------------------------
    private bool _uploadingPendingTopic;

    /// <summary>端末に一時保存されているお題スピーキングの結果を、古い順に送る。送れた件数を返す</summary>
    public async Task<int> UploadPendingTopicResultsAsync()
    {
        if (_uploadingPendingTopic || !IsSignedIn) return 0;
        _uploadingPendingTopic = true;
        int uploaded = 0;
        try
        {
            var list = PendingTopicStore.Load();
            while (list.Count > 0 && IsSignedIn)
            {
                var item = list[0];
                try
                {
                    await SaveTopicResultAsync(item.kind, item.topic, item.seconds, item.grade, item.transcript,
                        item.ReadAloudAccuracy, item.TakenAtUtc);
                    uploaded++;
                }
                catch (SupabaseException e) when (e.StatusCode == 400)
                {
                    Debug.LogWarning("[Supabase] dropped a pending topic result that cannot be saved: " + e.Message);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Supabase] could not upload pending topic results now: " + e.Message);
                    break;
                }

                list.RemoveAt(0);
                PendingTopicStore.Save(list);
            }
        }
        finally
        {
            _uploadingPendingTopic = false;
        }

        if (uploaded > 0) Debug.Log($"[Supabase] uploaded {uploaded} topic result(s) taken before signing in.");
        return uploaded;
    }
}
