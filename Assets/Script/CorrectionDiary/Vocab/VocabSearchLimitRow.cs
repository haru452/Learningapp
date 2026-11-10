using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vocab の Settings に足す 1 行: 「今日の動画の検索: 29 / 30」と Reset。
/// スマホでも、その日の数 (と YouTube の上限の印) を 0 に戻して、すぐ試せるようにする。起動時にコードで作る (シーンは変えない)。
/// </summary>
public class VocabSearchLimitRow
{
    private readonly IVocabClipStorage _storage;
    private readonly TMP_Text _text;

    public VocabSearchLimitRow(GameObject settingsPanel, Transform before, TMP_FontAsset font, IVocabClipStorage storage, Action onReset)
    {
        _storage = storage;
        var row = UiStyle.NewUi("SearchLimitRow", settingsPanel.transform);
        if (before != null) row.transform.SetSiblingIndex(before.GetSiblingIndex());
        UiStyle.Size(row, UiTheme.MinTouch);
        UiStyle.Row(row, UiTheme.Gap);
        _text = UiStyle.NewText("Text", row.transform, font, "", UiTheme.CaptionSize, UiTheme.SubText);
        UiStyle.Size(_text.gameObject, -1, 0, -1, 1);

        var go = UiStyle.NewUi("ResetButton", row.transform);
        var button = go.AddComponent<Button>();
        UiStyle.Size(go, -1, 0, 220, 0);
        var label = UiStyle.NewText("Label", go.transform, font, "Reset", UiTheme.BodySize, UiTheme.Text, FontStyles.Bold);
        UiStyle.Stretch(label.transform);
        UiStyle.Secondary(button);
        button.onClick.AddListener(() =>
        {
            _storage.SaveLog(new VocabClipSearchLog());
            Refresh();
            onReset?.Invoke();
        });
        Refresh();
    }

    public void Refresh() => _text.text = Describe(_storage.LoadLog(), DateTime.Now);

    /// <summary>表示する文 (Unity に依存しない。テストあり)</summary>
    public static string Describe(VocabClipSearchLog log, DateTime localNow)
    {
        string today = localNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        int used = log != null && log.Date == today ? log.Count : 0;
        string text = "Video searches today: " + used + " / " + VocabClipSearchLog.DailyLimit;
        if (log != null && log.BlockedDate == today) text += "\n<size=85%>YouTube's daily limit was reached.</size>";
        return text;
    }
}
