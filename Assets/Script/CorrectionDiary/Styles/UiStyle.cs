using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面を作り直すときに使う部品の作り方 (色・大きさは UiTheme)。起動時の組み直し (〜ScreenStyle) から呼ぶ。
/// </summary>
public static class UiStyle
{
    private static Sprite _round;

    /// <summary>Unity の角の丸い絵 (UISprite)。無ければ null (四角になる)</summary>
    public static Sprite Round => _round != null ? _round
        : _round = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == "UISprite");

    /// <summary>シーンの中の物を名前で探す (隠れている物も)</summary>
    public static GameObject Find(string name) =>
        Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g.name == name && g.scene.IsValid());

    /// <summary>部品を取る。無ければ足す (Unity の偽の null があるので ?? は使わない)</summary>
    public static T Get<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    public static GameObject NewUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>下の物も含めて、倍率を 1・回転なし・奥行き 0 に戻す (大きさはレイアウトで決める)</summary>
    public static void ResetScale(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            t.localScale = Vector3.one;
            t.localRotation = Quaternion.identity;
            if (t is RectTransform r) r.anchoredPosition3D = new Vector3(r.anchoredPosition.x, r.anchoredPosition.y, 0f);
        }
    }

    public static void Stretch(Transform t, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        var r = (RectTransform)t;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(left, bottom);
        r.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>レイアウトの中での高さ。flexible が 0 なら決めた高さのまま</summary>
    public static LayoutElement Size(GameObject go, float height = -1, float flexibleHeight = 0, float width = -1, float flexibleWidth = -1)
    {
        var le = Get<LayoutElement>(go);
        le.preferredHeight = height;
        le.minHeight = height;
        le.flexibleHeight = flexibleHeight;
        le.preferredWidth = width;
        le.flexibleWidth = flexibleWidth;
        return le;
    }

    public static VerticalLayoutGroup Column(GameObject go, int padding, float spacing)
    {
        var v = Get<VerticalLayoutGroup>(go);
        v.padding = new RectOffset(padding, padding, padding, padding);
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    public static HorizontalLayoutGroup Row(GameObject go, float spacing)
    {
        var h = Get<HorizontalLayoutGroup>(go);
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        return h;
    }

    /// <summary>角の丸い面 (カード・入力欄など)</summary>
    public static Image Surface(GameObject go, Color color, bool outline = false)
    {
        var image = Get<Image>(go);
        if (Round != null) { image.sprite = Round; image.type = Image.Type.Sliced; }
        image.color = color;
        var line = go.GetComponent<Outline>();
        if (outline && line == null) line = go.AddComponent<Outline>();
        if (line != null)
        {
            line.enabled = outline;
            line.effectColor = UiTheme.Border;
            line.effectDistance = new Vector2(2f, -2f);
        }
        return image;
    }

    /// <summary>主なボタン: 水色に濃い文字</summary>
    public static void Primary(Button b) => Button(b, UiTheme.Primary, UiTheme.OnPrimary);

    /// <summary>ふつうのボタン: 明るい紺に白い文字</summary>
    public static void Secondary(Button b) => Button(b, UiTheme.SurfaceRaised, UiTheme.Text);

    /// <param name="keepTextSize">true: 文字の大きさは変えない (倍率をかけて表示する画面で使う)</param>
    public static void Button(Button b, Color background, Color label, bool keepTextSize = false)
    {
        if (b == null) return;
        Surface(b.gameObject, background);
        var colors = b.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);   // 押したら少し暗く (大きさは変えない)
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        colors.fadeDuration = 0.08f;
        b.colors = colors;
        b.transition = Selectable.Transition.ColorTint;
        foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
        {
            t.color = label;
            t.fontStyle = FontStyles.Bold;
            if (keepTextSize) continue;
            t.enableAutoSizing = false;
            t.fontSize = UiTheme.BodySize;
            t.alignment = TextAlignmentOptions.Center;
        }
    }

    public static void Text(TMP_Text t, float size, Color color, FontStyles style = FontStyles.Normal,
                            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        if (t == null) return;
        t.enableAutoSizing = false;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = align;
        t.enableWordWrapping = true;
        t.overflowMode = TextOverflowModes.Overflow;
    }

    public static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, string text, float size, Color color,
                                          FontStyles style = FontStyles.Normal)
    {
        var t = NewUi(name, parent).AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        Text(t, size, color, style, TextAlignmentOptions.MidlineLeft);
        t.raycastTarget = false;
        return t;
    }

    /// <summary>
    /// 縦に流れる中身 (長い文) を入れる場所を作る。返すのは中身を並べる Content
    /// </summary>
    public static Transform Scroll(Transform parent, float flexibleHeight)
    {
        var view = NewUi("Scroll", parent);
        Size(view, -1, flexibleHeight);
        view.AddComponent<RectMask2D>();
        var image = view.AddComponent<Image>();
        image.color = Color.clear;   // 指で動かせるように、見えない面を置く
        var scroll = view.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        var content = NewUi("Content", view.transform);
        var r = (RectTransform)content.transform;
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.offsetMin = r.offsetMax = Vector2.zero;
        var v = Column(content, 0, 16);
        v.padding = new RectOffset(0, 0, 0, 8);
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = r;
        scroll.viewport = (RectTransform)view.transform;
        return content.transform;
    }

    /// <summary>
    /// 画面の中身を縦に並べる "Root" を Canvas の下に作る。上の帯 (右上の設定ボタン) にはかからない。
    /// 下のタブの上までに合わせるのは BottomNavBar (名前 "Canvas/Root" で探す)
    /// </summary>
    public static GameObject ScreenRoot(Transform canvas)
    {
        var root = NewUi("Root", canvas);
        Stretch(root.transform);
        ((RectTransform)root.transform).anchorMax = new Vector2(1f, 0.94f);
        root.transform.SetAsLastSibling();
        var column = Column(root, (int)UiTheme.Gutter, UiTheme.Gap);
        column.padding = new RectOffset((int)UiTheme.Gutter, (int)UiTheme.Gutter, 24, 32);
        return root;
    }

    /// <summary>Diary と Practice が共有する添削エンジンの選択 (名前の後ろに空白がある)</summary>
    public static TMP_Dropdown FindDropdown()
    {
        var go = Find("EngineDropdown ") ?? Find("EngineDropdown");
        return go != null ? go.GetComponent<TMP_Dropdown>() : null;
    }

    /// <summary>画面の題と、右に共有の選択肢 (あれば) を置く行</summary>
    public static void TopRow(Transform root, TMP_FontAsset font, string title, TMP_Dropdown dropdown)
    {
        var top = NewUi("TopRow", root);
        Size(top, MinTouch);
        // 右上の設定ボタン (灰色の丸。MainCanvas にある) と重ならないよう、右側を空ける
        Row(top, UiTheme.Gap).padding = new RectOffset(0, (int)SettingsButtonSpace, 0, 0);
        var t = NewText("Title", top.transform, font, title, UiTheme.TitleSize, UiTheme.Text, FontStyles.Bold);
        Size(t.gameObject, -1, 0, -1, 1);
        if (dropdown == null) return;
        var slot = NewUi("EngineSlot", top.transform);
        Size(slot, 96, 0, 340, 0);
        ResetScale(dropdown.transform);
        Dropdown(dropdown);
        Get<DropdownSlot>(slot).Attach(dropdown);
    }

    private const float MinTouch = UiTheme.MinTouch;
    private const float SettingsButtonSpace = 150f;

    /// <summary>見出しと、縦に流れる本文のカードを新しく作る。本文 (既存の文字) はカードの中へ移す</summary>
    public static GameObject TextCard(Transform root, TMP_FontAsset font, string label, Color labelColor, TMP_Text body, float flexible)
    {
        var card = NewUi(label + "Card", root);
        Surface(card, UiTheme.Surface, outline: true);
        Size(card, -1, flexible);
        var column = Column(card, 28, 12);
        column.padding = new RectOffset(32, 32, 24, 24);
        var title = NewText("Label", card.transform, font, label, 28f, labelColor, FontStyles.Bold);
        Size(title.gameObject, 44);
        var content = Scroll(card.transform, 1f);
        if (body != null)
        {
            body.transform.SetParent(content, false);
            Text(body, 34f, UiTheme.Text);
        }
        return card;
    }

    /// <summary>TMP の選択肢 (Dropdown) を紺に白い文字にする</summary>
    public static void Dropdown(TMP_Dropdown d)
    {
        if (d == null) return;
        Surface(d.gameObject, UiTheme.Surface, outline: true);
        if (d.captionText != null) Text(d.captionText, 28f, UiTheme.Text, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        if (d.itemText != null) Text(d.itemText, 28f, UiTheme.Text, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        foreach (var image in d.GetComponentsInChildren<Image>(true))
        {
            string n = image.name;
            if (n == "Arrow" || n == "Item Checkmark") image.color = UiTheme.Primary;
            else if (n == "Template") Surface(image.gameObject, UiTheme.Sheet, outline: true);
            else if (n == "Item Background") image.color = UiTheme.SurfaceRaised;
        }
    }
}
