using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Diary の画面を、起動時に縦に並べ直して UiTheme の見た目にする (シーンは変えない)。
/// 部品は移すだけなので、DiaryManager・DiaryStreakView とのつながりはそのまま。
/// 並び: 題とエンジンの選択 → 連続記録のカード (日数・ひとこと・月〜日の丸) → 録音ボタン → Retry
///       → Transcript のカード → Corrected のカード。
/// </summary>
public static class DiaryScreenStyle
{
    private const float DaySize = 76f;

    public static void Apply()
    {
        var manager = Object.FindObjectsOfType<DiaryManager>(true).FirstOrDefault();
        var screen = manager != null ? manager.transform.parent : null;   // "dairy screen" (名前に特殊な空白がある)
        var canvas = screen != null ? screen.Find("Canvas") : null;
        if (canvas == null || canvas.Find("Root") != null) return;

        var record = canvas.Find("RecordButton");
        var retry = canvas.Find("RetryButton (1)");
        var transcript = canvas.Find("lawText (TMP)")?.GetComponent<TMP_Text>();
        var corrected = canvas.Find("fixText (TMP) (1)")?.GetComponent<TMP_Text>();
        var streak = canvas.Find("Streak_Panel");
        if (record == null || transcript == null || corrected == null)
        {
            Debug.LogWarning("[UiStyle] Diary screen parts were not found. Kept the old layout.");
            return;
        }
        TMP_FontAsset font = transcript.font;

        // 前の背景の板は使わない (カードを作り直す)
        foreach (var name in new[] { "Panel (2)", "Panel (3)" })
            canvas.Find(name)?.gameObject.SetActive(false);

        var root = UiStyle.ScreenRoot(canvas);
        UiStyle.TopRow(root.transform, font, "Diary", UiStyle.FindDropdown());
        if (streak != null) StreakCard(streak, root.transform);

        record.SetParent(root.transform, false);
        UiStyle.Size(record.gameObject, UiTheme.ButtonHeight);
        UiStyle.Primary(record.GetComponent<Button>());
        foreach (var t in record.GetComponentsInChildren<TMP_Text>(true)) UiStyle.Stretch(t.transform);

        if (retry != null)   // 送れなかったときだけ DiaryManager が出す
        {
            bool shown = retry.gameObject.activeSelf;
            retry.SetParent(root.transform, false);
            UiStyle.Size(retry.gameObject, UiTheme.MinTouch);
            UiStyle.Secondary(retry.GetComponent<Button>());
            foreach (var t in retry.GetComponentsInChildren<TMP_Text>(true)) UiStyle.Stretch(t.transform);
            retry.gameObject.SetActive(shown);
        }

        UiStyle.TextCard(root.transform, font, "Transcript", UiTheme.SubText, transcript, 1f);
        UiStyle.TextCard(root.transform, font, "Corrected", UiTheme.Primary, corrected, 1.4f);

        // 前の画面は部品ごとに倍率がばらばらなので、全部 1 に戻す
        UiStyle.ResetScale(root.transform);
    }

    /// <summary>連続記録: 日数 (大きく) → ひとこと (灰色) → 月〜日の丸 (横に均等)</summary>
    private static void StreakCard(Transform streak, Transform root)
    {
        streak.SetParent(root, false);
        UiStyle.Surface(streak.gameObject, UiTheme.Surface, outline: true);
        UiStyle.Size(streak.gameObject, -1, 0);
        var column = UiStyle.Column(streak.gameObject, 28, 12);
        column.padding = new RectOffset(32, 32, 24, 28);

        var texts = new List<TMP_Text>();
        var days = new List<Transform>();
        foreach (Transform child in streak)
        {
            var t = child.GetComponent<TMP_Text>();
            if (t != null) texts.Add(t);
            else days.Add(child);
        }
        for (int i = 0; i < texts.Count; i++)
        {
            bool title = i == 0;   // 1 つ目が「_ Days Streak」、2 つ目がひとこと
            UiStyle.Text(texts[i], title ? UiTheme.HeadingSize : UiTheme.CaptionSize, title ? UiTheme.Text : UiTheme.SubText,
                         title ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            UiStyle.Size(texts[i].gameObject, title ? 56 : 72);
        }

        var week = UiStyle.NewUi("Week", streak);
        UiStyle.Size(week, DaySize + 8);
        var row = UiStyle.Row(week, 8);
        row.childForceExpandWidth = true;
        foreach (var day in days)
        {
            day.SetParent(week.transform, false);
            UiStyle.Size(day.gameObject, DaySize, 0, -1, 1);
            foreach (Transform part in day)
            {
                var r = (RectTransform)part;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
                r.anchoredPosition = Vector2.zero;
                var letter = part.GetComponent<TMP_Text>();
                if (letter != null)
                {
                    r.sizeDelta = new Vector2(DaySize, DaySize);
                    UiStyle.Text(letter, UiTheme.CaptionSize, UiTheme.Text, FontStyles.Bold, TextAlignmentOptions.Center);
                    letter.raycastTarget = false;
                }
                else
                {
                    r.sizeDelta = new Vector2(DaySize - 8, DaySize - 8);
                    // 丸の色は DiaryStreakView が付ける。済んだ日の印 (check) だけ水色にする
                    if (part.name.StartsWith("check")) part.GetComponent<Image>().color = new Color(UiTheme.Primary.r, UiTheme.Primary.g, UiTheme.Primary.b, 0.55f);
                }
            }
            day.Find("m")?.SetAsLastSibling();   // 曜日の文字を丸の上に
        }
    }
}
