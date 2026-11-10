using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DiaryManager の続き: クラウド保存、ほかの練習の日の連続記録、Worker への連絡、Discord、添削のやり直し。
/// </summary>
public partial class DiaryManager
{
    // ------------------------------------------------------------
    // Cloud (添削した日記を、直近7日分だけ保存する。古いものはサーバーが自動で消す)
    // ------------------------------------------------------------
    // 失敗しても、日記の処理には影響しない (画面の処理を待たせない)
    // mistakes: Gemini で添削したときのミスの一覧 (DeepL Write のときは null → 保存しない)
    private async void SaveDiaryToCloudAsync(string original, string corrected, string explanation, Mistake[] mistakes)
    {
        if (supabase == null) return;

        try
        {
            // アプリ起動後、まだログインを復元していない場合もあるので、ここで復元してから保存する
            if (!await supabase.EnsureSignedInAsync()) return;
            string engineName = _engine == Engine.Gemini ? "Gemini" : "DeepL Write";
            await supabase.SaveDiaryAsync(original, corrected, explanation, engineName);
            await supabase.SaveMistakesAsync("diary", mistakes);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DiaryManager] could not save the diary to the cloud: " + e.Message);
        }
    }

    // ------------------------------------------------------------
    // 要約練習など、日記以外の練習をした日も連続記録に数える (SummaryPracticeManager から呼ぶ)
    // ------------------------------------------------------------
    /// <summary>
    /// 端末内の連続記録に今日を記録し、夜のリマインダーにも「今日はやった」と連絡する。
    /// ログイン中のクラウドの連続記録は、採点結果を保存したときにサーバーが数える。
    /// </summary>
    public void CountOtherPracticeAsStudyDay()
    {
        if (streakView != null) streakView.MarkTodayDone();
        NotifyReminderServerAsync();
    }

    /// <summary>連続記録カードを、クラウドの最新の値で表示し直す</summary>
    public void RefreshStreakFromCloud()
    {
        if (streakView != null) _ = streakView.RefreshFromCloudAsync();
    }

    // ------------------------------------------------------------
    // Reminder (Worker に「今日は書いた」と連絡する)
    // ------------------------------------------------------------
    // 画面の処理を待たせないよう、結果は待たない。失敗しても日記の処理には影響しない
    private async void NotifyReminderServerAsync()
    {
        // 日記の画面を一度も開いていない (Awake 前) ときのために、ここでも用意する
        if (_reminder == null)
        {
            var secrets = ApiSecrets.Data;
            _reminder = new ReminderClient(ApiSecrets.Pick(secrets.reminderWorkerUrl, reminderWorkerUrl),
                                           ApiSecrets.Pick(secrets.reminderSecret, reminderSecret));
        }
        if (!sendReminderPing || !_reminder.IsConfigured) return;

        try
        {
            DateTime date = streakView != null ? streakView.TodayDate : DateTime.Now.Date;
            int streak = streakView != null ? streakView.GetCurrentStreak() : 0;
            await _reminder.NotifyDiaryDoneAsync(date, streak);
        }
        catch (Exception e)
        {
            // 連絡できなかった場合、夜に「まだ書いていない」という通知が来てしまうことがある
            Debug.LogWarning("[Reminder] Could not notify the reminder server: " + e.Message);
        }
    }

    // ------------------------------------------------------------
    // Discord
    // ------------------------------------------------------------
    // 送信に失敗しても例外は投げない (添削結果は画面に残し、Retry で再送できる)
    private async Task SendToDiscordAsync()
    {
        if (!sendToDiscord || !_discord.IsConfigured) return;

        _discordPending = true;
        try
        {
            SetStatus("Sending to Discord...");
            await _discord.SendDiaryAsync(
                DateTime.Now,
                _lastCorrected,
                _lastExplanation,
                _lastOriginal,
                includeOriginalInDiscord,
                underlineChangesInDiscord);

            _discordPending = false;
            SetStatus("Done (sent to Discord)");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Corrected, but Discord failed: " + FirstLine(e.Message) + "  (You can retry)");
        }
    }

    // ------------------------------------------------------------
    // Retry (添削だけやり直す)
    // ------------------------------------------------------------
    private async void OnRetryClicked()
    {
        if (_busy || _active) return;
        if (string.IsNullOrEmpty(_pendingText) && !_discordPending) return;

        _busy = true;
        recordButton.interactable = false;
        SetLoading(true);
        _engine = CurrentEngine;   // 失敗したエンジンから切り替えて再試行できる

        try
        {
            if (!string.IsNullOrEmpty(_pendingText))
                await CorrectAsync(_pendingText);   // 成功すれば、そのまま Discord にも送られる
            else
                await SendToDiscordAsync();         // 添削は済んでいて、Discord だけ失敗していた場合
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Error: " + FirstLine(e.Message) + "  (You can retry the correction)");
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            recordButton.interactable = true;
            UpdateRetryButton();
        }
    }
}
