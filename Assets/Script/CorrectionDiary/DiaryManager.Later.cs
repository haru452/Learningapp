using System;
using UnityEngine;

/// <summary>
/// DiaryManager の続き: Gemini が混んでいて添削できなかった日記を端末に残し、あとで自動で送る (GeminiLater)。
/// 軽いモデルには切り替えない。連続記録は話した日に数える。
/// </summary>
public partial class DiaryManager
{
    /// <summary>あとで送ると決めたときに画面に出す文 (どの練習でも同じ)</summary>
    public const string SavedForLaterMessage =
        "Gemini is busy. Your answer was saved and will be sent automatically later. You will get a notice here.";

    /// <summary>混んでいたら日記を残して true (Retry は不要になる)。それ以外の失敗は false</summary>
    private bool SaveForLater(Exception e)
    {
        if (_engine != Engine.Gemini || string.IsNullOrEmpty(_pendingText) || !GeminiLater.IsBusy(e)) return false;
        var job = new GeminiLater.Job
        {
            kind = GeminiLater.Diary,
            model = _gemini.Model,
            fallbackModel = _gemini.FallbackModel,
            sendToDiscord = sendToDiscord,
            includeOriginal = includeOriginalInDiscord,
            underlineChanges = underlineChangesInDiscord,
            text = _pendingText,
        };
        if (!GeminiLater.Add(job)) return false;

        _pendingText = null;
        if (streakView != null) _ = streakView.MarkTodayDoneAsync();   // 話した日を、日記を書いた日として数える
        NotifyReminderServerAsync();
        SetStatus(SavedForLaterMessage);
        return true;
    }

    /// <summary>あとで添削できた日記を、画面が空いていれば出す (GeminiLaterJobs から)</summary>
    public void ShowLaterCorrection(string original, string corrected, string explanation)
    {
        if (this == null || _busy || _active || correctedText == null) return;
        if (transcriptText != null) transcriptText.text = original;
        ShowCorrected(original, corrected);
        if (explanationText != null) explanationText.text = explanation;
        _lastOriginal = original;
        _lastCorrected = corrected;
        _lastExplanation = explanation;
        SetStatus("Your saved diary was corrected.");
    }
}
