using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 単語帳の画面の見た目を、アプリ共通の決まり (UiTheme: 濃い紺 + 水色) にそろえる。
/// 背景は紺、カードと一覧は一段明るい紺、主なボタン (Start・Say it) は水色に濃い文字、ほかのボタンは紺に白い文字。
/// 名前で探すので、VocabScreenBuilder で付けた名前を変えたらここも直す。
/// 何度実行しても同じ結果になる (Tools → Vocabulary → Update Vocabulary Screen から呼ばれる)。
/// </summary>
public static class VocabStyler
{
    private const string UndoName = "Style Vocabulary Screen";

    private static readonly Color Card = UiTheme.Surface;              // カード・一覧
    private static readonly Color Raised = UiTheme.SurfaceRaised;      // ふつうのボタン
    private static readonly Color TabTrack = UiTheme.Surface;          // タブの土台 (選ばれていない時の色と同じ)
    private static readonly Color Primary = UiTheme.Primary;           // 主なボタン (水色)
    private static readonly Color Cyan = UiTheme.Primary;
    private static readonly Color Sheet = UiTheme.Sheet;               // ダイアログ

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
            if (name == "Background" && parent == "Canvas") n += Plain(t, UiTheme.Background);
            else if (name == "Card") n += Panel(t, Card, outline: true);
            else if (name == "SummaryScroll" || name == "WordList") n += Panel(t, Card, outline: false);
            else if (name == "Dialog") n += Panel(t, Sheet, outline: true);
            else if (name == "SubTabs") n += Track(t);
            else if (name == "StartButton" || name == "SayButton") n += Fill(t, Primary, UiTheme.OnPrimary);
            else if (name == "SoundButton") n += Fill(t, Raised);
            else if (name == "Rating1") n += Fill(t, Hex("#9F1D3A"));   // Didn't know
            else if (name == "Rating2") n += Fill(t, Hex("#92600A"));   // Hard
            else if (name == "Rating3") n += Fill(t, Hex("#0B7A58"));   // Knew it
            else if (name == "Rating4") n += Fill(t, Hex("#0E7490"));   // Easy
            else if (t.GetComponent<Button>() != null && parent != "SubTabs" && parent != "RatingRow") n += Fill(t, Raised);
        }
        n += AddProgressBar(screen);
        n += Texts(screen);
        return n;
    }

    // ボタンの外の文字: 見出しは白、説明は灰色。小さすぎる文字は CaptionSize まで上げる
    private static int Texts(GameObject screen)
    {
        int n = 0;
        var lists = screen.GetComponentsInChildren<Transform>(true).Where(t => t.name == "SummaryScroll" || t.name == "WordList").ToList();
        foreach (var text in screen.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.GetComponentInParent<Button>(true) != null) continue;
            string name = text.name;
            // カードの文と、まとめ・単語の一覧の行は、VocabTodayView / VocabWordsView が色を付けるので触らない
            if (name == "CardText" || lists.Any(l => text.transform.IsChildOf(l))) continue;
            Undo.RecordObject(text, UndoName);
            text.color = name == "HeaderText" || name.EndsWith("Title") ? UiTheme.Text : UiTheme.SubText;
            if (name == "HeaderText") { text.fontSize = UiTheme.HeadingSize; text.fontStyle = FontStyles.Bold; }
            if (text.enableAutoSizing) text.fontSizeMin = Mathf.Max(text.fontSizeMin, UiTheme.CaptionSize - 4);
            else if (text.fontSize < UiTheme.CaptionSize) text.fontSize = UiTheme.CaptionSize;
            n++;
        }
        return n;
    }

    private static int Plain(Transform t, Color color)
    {
        var image = t.GetComponent<Image>();
        if (image == null) return 0;
        Undo.RecordObject(image, UndoName);
        image.color = color;
        return 1;
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

    private static int Fill(Transform t, Color color) => Fill(t, color, UiTheme.Text);

    private static int Fill(Transform t, Color color, Color labelColor)
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
            Undo.RecordObject(button, UndoName);
            var colors = button.colors;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);   // 押したら少し暗く (大きさは変えない)
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            foreach (var label in t.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(label, UndoName);
                label.color = labelColor;
            }
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
