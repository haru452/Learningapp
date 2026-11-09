using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 単語帳の画面の見た目を、Stitch のデザイン (Acoustic Intelligence: 濃い紺・インディゴ・シアン) に寄せる。
/// 背景は ほかの画面と同じ半透明の灰色のままにして、カード・ボタン・タブだけ新しい色にする。
/// 名前で探すので、VocabScreenBuilder で付けた名前を変えたらここも直す。
/// 何度実行しても同じ結果になる (Tools → Vocabulary → Update Vocabulary Screen から呼ばれる)。
/// </summary>
public static class VocabStyler
{
    private const string UndoName = "Style Vocabulary Screen";

    private static readonly Color Card = Hex("#111827", 0.95f);       // カード・一覧
    private static readonly Color Raised = Hex("#1F2937");             // ふつうのボタン
    private static readonly Color TabTrack = Hex("#171B26");           // タブの土台 (選ばれていない時の色と同じ)
    private static readonly Color Indigo = Hex("#4F46E5");             // 主なボタン
    private static readonly Color Cyan = Hex("#06B6D4");
    private static readonly Color Sheet = Hex("#1E2235", 0.98f);       // ダイアログ

    private static Sprite _round;

    private static Sprite Round => _round != null ? _round : _round = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    /// <summary>画面の下の色・形を変える。変えた数を返す</summary>
    public static int Apply(GameObject screen)
    {
        int n = 0;
        foreach (var t in screen.GetComponentsInChildren<Transform>(true))
        {
            string name = t.name;
            string parent = t.parent != null ? t.parent.name : "";
            if (name == "Card") n += Panel(t, Card, outline: true);
            else if (name == "SummaryScroll" || name == "WordList") n += Panel(t, Card, outline: false);
            else if (name == "Dialog") n += Panel(t, Sheet, outline: true);
            else if (name == "SubTabs") n += Track(t);
            else if (name == "StartButton" || name == "SayButton") n += Fill(t, Indigo);
            else if (name == "SoundButton") n += Fill(t, Raised);
            else if (name == "Rating1") n += Fill(t, Hex("#9F1D3A"));   // Didn't know
            else if (name == "Rating2") n += Fill(t, Hex("#92600A"));   // Hard
            else if (name == "Rating3") n += Fill(t, Hex("#0B7A58"));   // Knew it
            else if (name == "Rating4") n += Fill(t, Hex("#0E7490"));   // Easy
            else if (t.GetComponent<Button>() != null && parent != "SubTabs" && parent != "RatingRow") n += Fill(t, Raised);
        }
        n += AddProgressBar(screen);
        return n;
    }

    private static int Panel(Transform t, Color color, bool outline)
    {
        var image = t.GetComponent<Image>();
        if (image == null) return 0;
        Undo.RecordObject(image, UndoName);
        image.sprite = Round;
        image.type = Image.Type.Sliced;
        image.color = color;

        var line = t.GetComponent<Outline>();
        if (outline && line == null) line = Undo.AddComponent<Outline>(t.gameObject);
        if (line != null)
        {
            line.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            line.effectDistance = new Vector2(2f, -2f);
            line.enabled = outline;
        }
        return 1;
    }

    // タブ: 土台を丸い箱にして、選ばれたタブだけ色を付ける (色の切り替えは VocabManager.Highlight)
    private static int Track(Transform t)
    {
        var image = t.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(t.gameObject);
        Undo.RecordObject(image, UndoName);
        image.sprite = Round;
        image.type = Image.Type.Sliced;
        image.color = TabTrack;
        image.raycastTarget = false;

        var layout = t.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
        {
            Undo.RecordObject(layout, UndoName);
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6;
        }
        int n = 1;
        foreach (var button in t.GetComponentsInChildren<Button>(true)) n += Fill(button.transform, TabTrack);
        return n;
    }

    private static int Fill(Transform t, Color color)
    {
        var image = t.GetComponent<Image>();
        if (image == null) return 0;
        Undo.RecordObject(image, UndoName);
        image.sprite = Round;
        image.type = Image.Type.Sliced;
        image.color = color;
        var button = t.GetComponent<Button>();
        if (button != null)
        {
            var colors = button.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
        }
        return 1;
    }

    // 「Word 4 / 10」の下に細い進み具合の棒を足す (すでにあれば何もしない)
    private static int AddProgressBar(GameObject screen)
    {
        var text = screen.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ProgressText");
        var manager = screen.GetComponentInChildren<VocabManager>(true);
        if (text == null || manager == null) return 0;
        if (text.parent.Find("ProgressBar") != null) return 0;

        var track = new GameObject("ProgressBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(track, UndoName);
        track.transform.SetParent(text.parent, false);
        track.transform.SetSiblingIndex(text.GetSiblingIndex() + 1);
        var trackImage = track.GetComponent<Image>();
        trackImage.sprite = Round;
        trackImage.type = Image.Type.Sliced;
        trackImage.color = Raised;
        trackImage.raycastTarget = false;
        var le = track.GetComponent<LayoutElement>();
        le.preferredHeight = 12;
        le.minHeight = 12;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);
        var rect = (RectTransform)fill.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var fillImage = fill.GetComponent<Image>();
        fillImage.sprite = Round;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillAmount = 0f;
        fillImage.color = Cyan;
        fillImage.raycastTarget = false;
        track.SetActive(false);   // 学習中だけ VocabTodayView が出す

        var so = new SerializedObject(manager);
        var p = so.FindProperty("progressFill");
        if (p != null)
        {
            p.objectReferenceValue = fillImage;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        return 1;
    }

    private static Color Hex(string hex, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        c.a = alpha;
        return c;
    }
}
