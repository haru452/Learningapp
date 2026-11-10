using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SummaryPracticeManager の続き: 3. 終了 → 採点、結果の送信・保存、連続記録。
/// </summary>
public partial class SummaryPracticeManager
{
    // ------------------------------------------------------------
    // 3. 終了 → 採点
    // ------------------------------------------------------------
    private async Task FinishSpeakingAsync()
    {
        if (_state != State.Speaking || _finishing) return;
        _finishing = true;
        _listening = false;
        _busy = true;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
        if (timerText != null) timerText.text = "";
        SetLoading(true);
        RefreshButtons();

        if (_useNative)
        {
            if (nativeStt.IsListening)
            {
                // 最終結果が届くのを少しだけ待つ (届かなければ途中結果を使う)
                SetStatus("Finishing...");
                int before = _segments.Count;
                nativeStt.StopListening();

                float deadline = Time.realtimeSinceStartup + StopTimeoutMs / 1000f;
                while (nativeStt.IsListening && Time.realtimeSinceStartup < deadline)
                    await Task.Yield();
                if (this == null) return;

                if (nativeStt.IsListening) nativeStt.Cancel();
                if (_segments.Count == before) CommitPartial();
            }
            else
            {
                CommitPartial();
            }

            _pendingText = Compose(null).Trim();
            if (transcriptText != null) transcriptText.text = _pendingText;
        }
        else
        {
            _pendingWav = recorder.StopRecording();
        }

        SetState(State.Grading);
        _busy = false;

        if (string.IsNullOrWhiteSpace(_pendingText) && _pendingWav == null)
        {
            _pendingText = null;
            SetLoading(false);
            SetState(State.Idle);
            SetStatus("No speech was recognized. Tap \"New Video\" to try another one.");
            return;
        }

        await GradeAsync();
    }

    private async Task GradeAsync()
    {
        _busy = true;
        SetState(State.Grading);
        SetLoading(true);
        SetStatus("Grading your summary...");

        try
        {
            var grade = await _gemini.GradeVideoSummaryAsync(
                _video.WatchUrl, timeLimitSeconds, _pendingText, _pendingWav);

            _pendingText = null;
            _pendingWav = null;
            ShowGrade(grade);
            SaveResultToCloudAsync(_video, grade);   // ログイン中なら保存 (待たない)
            CountAsStudyDay();                       // 要約練習をした日も、連続記録に数える

            string discordError = await SendToDiscordAsync(_video, grade);
            SetState(State.Idle);
            SetStatus(discordError == null
                ? "Done! Tap \"Next Video\" to try another one."
                : "Graded, but Discord failed: " + discordError);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetState(State.Idle);   // _pending* が残っているので、メインボタンが「Retry Grading」になる
            SetStatus("Error: " + FirstLine(e.Message) + "  (You can retry the grading)");
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            RefreshButtons();
        }
    }

    private void ShowGrade(GeminiApiClient.SummaryGrade g)
    {
        if (transcriptText != null) transcriptText.text = g.transcript;
        if (scoreText != null)
        {
            scoreText.text =
                $"Score: {g.score} / 100\n" +
                $"<size=60%>Content {g.contentScore}/{GeminiApiClient.ContentMax}  ·  " +
                $"Organization {g.organizationScore}/{GeminiApiClient.OrganizationMax}  ·  " +
                $"Grammar & vocabulary {g.languageScore}/{GeminiApiClient.LanguageMax}</size>";
        }

        if (feedbackText != null)
        {
            var sb = new StringBuilder();
            AppendSection(sb, "Good points", g.goodPoints);
            AppendSection(sb, "Missed points", g.missedPoints);
            AppendSection(sb, "Language", g.languageFeedback);
            feedbackText.text = sb.ToString().TrimEnd();
        }

        if (referenceText != null)
        {
            var sb = new StringBuilder();
            AppendSection(sb, "Model summary", g.referenceSummary);
            AppendSection(sb, "Your summary, improved", g.improvedSummary);
            referenceText.text = sb.ToString().TrimEnd();
        }
    }

    // ------------------------------------------------------------
    // 結果の送信・保存
    // ------------------------------------------------------------
    // 失敗しても採点結果は画面に残る。失敗したときはエラーの1行目を返す (成功・送信しない場合は null)
    private async Task<string> SendToDiscordAsync(YouTubeSearchClient.Video video, GeminiApiClient.SummaryGrade g)
    {
        if (!sendToDiscord || !_discord.IsConfigured) return null;

        SetStatus("Sending to Discord...");
        try
        {
            await _discord.SendSummaryResultAsync(DateTime.Now, video.title, video.WatchUrl, g);
            return null;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return FirstLine(e.Message);
        }
    }

    // 画面の処理を待たせないよう、結果は待たない。失敗しても練習には影響しない。
    // ログインしていないとき・送れなかったときは端末に一時保存し、ログインしたときに SupabaseService がまとめて送る。
    private async void SaveResultToCloudAsync(YouTubeSearchClient.Video video, GeminiApiClient.SummaryGrade g)
    {
        if (supabase == null || !supabase.IsConfigured) return;

        DateTime takenAt = DateTime.UtcNow;
        // アプリ起動後、まだログインを復元していない場合もあるので、ここで復元してから判断する
        if (!await supabase.EnsureSignedInAsync())
        {
            PendingSummaryStore.Add(video.id, video.title, timeLimitSeconds, g, takenAt);
            Debug.Log("[SummaryPractice] not signed in: the result was kept on this device and will be uploaded after signing in.");
            return;
        }

        try
        {
            await supabase.SaveSummaryResultAsync(video.id, video.title, timeLimitSeconds, g, takenAt);
            try { await supabase.SaveMistakesAsync("summary", g.mistakes, takenAt); }   // よくあるミスの集計用
            catch (Exception e) { Debug.LogWarning("[SummaryPractice] could not save mistakes: " + e.Message); }
            if (PendingSummaryStore.Count > 0) _ = supabase.UploadPendingSummaryResultsAsync();   // 前に送れなかった分も、ついでに送る
            var diary = FindDiary();
            if (diary != null) diary.RefreshStreakFromCloud();   // サーバーが数えた連続記録を、カードに反映する
        }
        catch (SupabaseService.SupabaseException e) when (e.StatusCode == 400)
        {
            Debug.LogWarning("[SummaryPractice] the result could not be saved (invalid data): " + e.Message);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SummaryPractice] could not save the result to the cloud. Keeping it to upload later: " + e.Message);
            PendingSummaryStore.Add(video.id, video.title, timeLimitSeconds, g, takenAt);
        }
    }

    // ------------------------------------------------------------
    // 連続記録 (要約練習をした日も数える)
    // ------------------------------------------------------------
    private DiaryManager _diary;

    private DiaryManager FindDiary()
    {
        if (_diary == null) _diary = FindObjectOfType<DiaryManager>(true);
        return _diary;
    }

    private void CountAsStudyDay()
    {
        var diary = FindDiary();
        if (diary != null) diary.CountOtherPracticeAsStudyDay();
    }

    private static void AppendSection(StringBuilder sb, string heading, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.Append("<b>").Append(heading).Append("</b>\n").Append(body.Trim()).Append("\n\n");
    }
}
