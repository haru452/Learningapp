using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Practice (Voice Corrector) の画面を、起動時に縦に並べ直して UiTheme の見た目にする (シーンは変えない)。
/// 部品は移すだけなので、AppManager とのつながり (録音ボタン・文字起こし・添削・説明・エンジンの選択) はそのまま。
/// 並び: 題とエンジンの選択 → 録音ボタン → Transcript のカード → Corrected のカード (説明もこの中)。
/// 画面の中身は "Canvas/Root" に入れるので、下のタブの上までに収まる (BottomNavBar が高さを合わせる)。
/// </summary>
public static class PracticeScreenStyle
{
    public static void Apply()
    {
        var screen = UiStyle.Find("conversation_screen");
        var canvas = screen != null ? screen.transform.Find("Canvas") : null;
        if (canvas == null || canvas.Find("Root") != null) return;   // 無い・もう並べ直した

        var record = canvas.Find("Reecording_button");
        var transcriptCard = canvas.Find("Panel (2)");
        var correctedCard = canvas.Find("Panel (3)");
        if (record == null || transcriptCard == null || correctedCard == null)
        {
            Debug.LogWarning("[UiStyle] Practice screen parts were not found. Kept the old layout.");
            return;
        }
        // エンジンの選択と説明は、上の帯 (MainCanvas/Panel (4)) にある
        var header = UiStyle.Find("Panel (4)");
        var dropdown = UiStyle.FindDropdown();
        var explanation = header != null ? header.transform.Find("explanation") : null;
        TMP_FontAsset font = record.GetComponentInChildren<TMP_Text>(true)?.font;

        var root = UiStyle.ScreenRoot(canvas);

        // 1. 題とエンジンの選択 (Diary と共有。開いた画面へ移る)
        UiStyle.TopRow(root.transform, font, "Practice", dropdown);

        // 2. 録音ボタン
        record.SetParent(root.transform, false);
        UiStyle.Size(record.gameObject, UiTheme.ButtonHeight);
        UiStyle.Primary(record.GetComponent<Button>());
        foreach (var t in record.GetComponentsInChildren<TMP_Text>(true)) UiStyle.Stretch(t.transform);

        // 3, 4. カード
        Card(transcriptCard, root.transform, "Text (TMP)", "Transcript_text", null, "Transcript", UiTheme.SubText, 1f);
        Card(correctedCard, root.transform, "text", "corrected_text", explanation, "Corrected", UiTheme.Primary, 1.4f);

        // 上の帯の中身 (エンジンの選択) は画面の中へ移るので、帯は隠す (右上の設定ボタンは別の場所にある)
        if (header != null && dropdown != null) header.SetActive(false);

        // 前の画面は部品ごとに倍率 (scale) がばらばら (3 倍・縦 0.35 倍など) で、移すと大きさがずれる。全部 1 に戻す
        UiStyle.ResetScale(root.transform);
    }

    /// <summary>
    /// 前のパネルを角の丸いカードにする: 見出し → 縦に流れる本文 (と説明)。中の半透明の板は隠す
    /// </summary>
    private static void Card(Transform card, Transform root, string labelName, string bodyName, Transform extra,
                             string labelText, Color labelColor, float flexible)
    {
        var label = card.Find(labelName)?.GetComponent<TMP_Text>();
        var body = card.Find(bodyName)?.GetComponent<TMP_Text>();

        foreach (Transform child in card)
            if (child.GetComponent<TMP_Text>() == null) child.gameObject.SetActive(false);   // 中の板

        card.SetParent(root, false);
        UiStyle.Surface(card.gameObject, UiTheme.Surface, outline: true);
        UiStyle.Size(card.gameObject, -1, flexible);
        var column = UiStyle.Column(card.gameObject, 28, 12);
        column.padding = new RectOffset(32, 32, 24, 24);

        if (label != null)
        {
            label.text = labelText;
            UiStyle.Text(label, 28f, labelColor, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            UiStyle.Size(label.gameObject, 44);
            label.transform.SetAsFirstSibling();
        }
        var content = UiStyle.Scroll(card, 1f);
        if (body != null)
        {
            body.transform.SetParent(content, false);
            UiStyle.Text(body, 34f, UiTheme.Text);
        }
        if (extra != null)
        {
            extra.SetParent(content, false);
            var t = extra.GetComponent<TMP_Text>();
            UiStyle.Text(t, UiTheme.CaptionSize, UiTheme.SubText);
            if (t != null && t.text == "Voice Corrector") t.text = "";   // シーンに残っていた仮の文
        }
    }
}
