using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 下のタブを「Diary / Topic / Vocab / Modes」の 4 つにし、Modes で全部の機能のシートを開くようにする。
/// アプリの起動時に自動で作る (シーンファイルは変えない)。
/// Summary・Topic・Repeat・Talk のタブは隠すだけ。シートのカードは、隠したタブを押したのと同じ動きをする。
/// やめるときは Enabled を false にする。
/// </summary>
public static class ModesNav
{
    public const bool Enabled = true;
    private const string ModesTabName = "Modes";
    private const string RootName = "ModesNav";

    private static readonly Color Dim = UiTheme.Scrim;
    private static readonly Color SheetColor = WithAlpha(UiTheme.Sheet, 0.98f);
    private static readonly Color CardColor = WithAlpha(UiTheme.Surface, 0.95f);
    private static readonly Color Raised = UiTheme.SurfaceRaised;
    private static readonly Color Cyan = UiTheme.Primary;
    private static readonly Color SubText = UiTheme.SubText;

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    // 元のタブの名前 (シーンの中) → カードの内容
    private static readonly (string tab, string title, string desc, string badge)[] Cards =
    {
        ("COnversationbutton", "Practice", "Speak and get corrections", "Main"),
        ("Dairy", "Diary", "English diary & streak", "Streak"),
        ("Summary", "Summary", "Listen and summarize", "Versant"),
        ("Topic", "Topic", "Talk about a topic", "Speaking"),
        ("Repeat", "Repeat", "Repeat & shadowing", "Shadowing"),
        ("Vocab", "Vocab", "Word cards with FSRS", "FSRS"),
        ("talk to lafka", "Talk", "Chat with a 3D character", "3D Beta"),
        (ShortAnswerScreen.TabName, "Answer", "Hear a question, answer fast", "Versant"),
        (KaraokeScreen.TabName, "Karaoke", "Sing along, check your words", "Sing"),
    };
    // カードごとの画面の名前 (Cards と同じ順。今いる画面に「Now」を出すのに使う)
    private static readonly string[] Screens =
        { "conversation_screen", "dairy", "summary screen", "topic screen", "repeat screen", "vocab screen", "talktoLufka",
          ShortAnswerScreen.ScreenName, KaraokeScreen.ScreenName };

    /// <summary>元のタブが出す画面の名前か (短い答えの画面を開くとき、ほかの画面を閉じるのに使う)</summary>
    internal static bool IsScreenName(string name) => name != null && Screens.Any(s => name.StartsWith(s));
    private static readonly string[] KeepVisible = { "Dairy", "Topic", "Vocab" };   // ないと作らない

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Build()
    {
        if (!Enabled) return;
        try
        {
            BuildNow();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ModesNav] could not build the Modes navigation: " + e);
        }
    }

    private static void BuildNow()
    {
        ScreenStyles.ApplyAll();   // 画面の並べ直しを先に (できた Canvas/Root を下のタブの上までに合わせるため)
        ShortAnswerScreen.Install();
        KaraokeScreen.Install();       // カラオケの画面と、隠れたタブ "Karaoke" (シャドーイングとは別のモード)   // 短い答えの練習の画面と、隠れたタブ "Answer" (下でカードになる)
        var tabs = FindTabs();
        if (!KeepVisible.All(tabs.ContainsKey))
        {
            Debug.LogWarning("[ModesNav] Diary / Topic / Vocab tabs were not found in MainCanvas/Panel. Kept the old tabs.");
            return;
        }
        Transform bar = tabs["Dairy"].transform.parent;
        Transform mainCanvas = bar.parent;
        if (GameObject.Find(RootName) != null) return;   // もう作ってある

        bar.gameObject.SetActive(false);   // 上のタブは隠すだけ。下のタブとカードから押す

        TMP_FontAsset font = tabs["Dairy"].GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;
        _rounded = tabs["Dairy"].GetComponent<Image>()?.sprite;   // タブと同じ角の丸い絵
        var sheet = BuildSheet(mainCanvas, tabs, font);

        GameObject ScreenOf(string tab) => tabs.TryGetValue(tab, out var b)
            ? OpenedScreen(b, Screens[System.Array.FindIndex(Cards, c => c.tab == tab)]) : null;
        System.Action Press(string tab) => () => { sheet.component.Close(); if (tabs.TryGetValue(tab, out var b)) b.onClick.Invoke(); };
        var items = new List<BottomNavBar.Item>
        {
            new BottomNavBar.Item { label = "Diary", markFrom = tabs["Dairy"], onClick = Press("Dairy"), screens = new[] { ScreenOf("Dairy") } },
            new BottomNavBar.Item { label = "Topic", markFrom = tabs["Topic"], onClick = Press("Topic"), screens = new[] { ScreenOf("Topic") } },
            new BottomNavBar.Item { label = "Vocab", markFrom = tabs["Vocab"], onClick = Press("Vocab"), screens = new[] { ScreenOf("Vocab") } },
            new BottomNavBar.Item { label = ModesTabName, onClick = sheet.component.Open,
                                    screens = new[] { ScreenOf("COnversationbutton"), ScreenOf("Summary"), ScreenOf("Repeat"), ScreenOf("talk to lafka"),
                                                     ScreenOf(ShortAnswerScreen.TabName) } },
        };
        var roots = Cards.Select(c => ScreenOf(c.tab)).Where(g => g != null).Select(g => g.transform.Find("Canvas/Root") as RectTransform);
        var nav = BottomNavBar.Create(mainCanvas, (RectTransform)tabs["Dairy"].transform, roots, font, items);

        foreach (var e in sheet.entries) e.inBottomBar = items.Any(i => i.markFrom == e.tab);   // ● は下のタブに出す
        sheet.component.Setup(sheet.sheet, sheet.panel, sheet.dim, sheet.close, nav.LabelOf(ModesTabName), nav.TabOf(ModesTabName), sheet.entries);
    }

    // ------------------------------------------------------------
    // タブ
    // ------------------------------------------------------------
    private static Dictionary<string, Button> FindTabs()
    {
        var result = new Dictionary<string, Button>();
        foreach (var b in Object.FindObjectsOfType<Button>(true))
        {
            var p = b.transform.parent;
            if (p == null || p.name != "Panel" || p.parent == null || !p.parent.name.StartsWith("MainCanvas")) continue;
            if (Cards.Any(c => c.tab == b.name) && !result.ContainsKey(b.name)) result[b.name] = b;
        }
        return result;
    }

    // ------------------------------------------------------------
    // シート
    // ------------------------------------------------------------
    private class Built
    {
        public ModesSheet component; public GameObject sheet; public RectTransform panel;
        public Button dim, close; public List<ModesSheet.Entry> entries = new List<ModesSheet.Entry>();
    }

    private static Sprite _rounded;

    private static Built BuildSheet(Transform mainCanvas, Dictionary<string, Button> tabs, TMP_FontAsset font)
    {
        var b = new Built();
        // 画面ごとの Canvas (どれも順番 0) より手前に出すため、独立した Canvas にする
        var root = new GameObject(RootName, typeof(RectTransform));
        root.layer = LayerMask.NameToLayer("UI");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CopyScaler(mainCanvas.GetComponent<CanvasScaler>(), root.AddComponent<CanvasScaler>());
        root.AddComponent<GraphicRaycaster>();
        b.component = root.AddComponent<ModesSheet>();

        b.sheet = NewUi("Sheet", root.transform);
        Stretch(b.sheet);

        var dim = NewUi("Dim", b.sheet.transform);
        Stretch(dim);
        dim.AddComponent<Image>().color = Dim;
        b.dim = dim.AddComponent<Button>();
        b.dim.transition = Selectable.Transition.None;

        var panel = NewUi("Panel", b.sheet.transform);
        b.panel = (RectTransform)panel.transform;
        b.panel.anchorMin = new Vector2(0f, 0f);
        b.panel.anchorMax = new Vector2(1f, 0f);
        b.panel.pivot = new Vector2(0.5f, 0f);
        b.panel.anchoredPosition = Vector2.zero;
        b.panel.sizeDelta = Vector2.zero;
        Rounded(panel.AddComponent<Image>(), SheetColor);
        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(40, 40, 36, 40);
        v.spacing = 24;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = Row("Header", panel.transform, 90, 20);
        var title = Label("Title", header.transform, font, "Modes", 52, Color.white, FontStyles.Bold);
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var hint = Label("Hint", panel.transform, font, "Choose what to practice", 30, SubText, FontStyles.Normal);
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;

        GameObject row = null;
        for (int i = 0; i < Cards.Length; i++)
        {
            var c = Cards[i];
            if (!tabs.TryGetValue(c.tab, out var tab)) continue;
            if (row == null || row.transform.childCount == 2) row = Row("Row", panel.transform, 210, 24);
            b.entries.Add(Card(row.transform, font, c.title, c.desc, c.badge, tab, Screens[i]));
        }
        if (row != null && row.transform.childCount == 1)
            NewUi("Spacer", row.transform).AddComponent<LayoutElement>().flexibleWidth = 1;

        // Close: 下のタブの「Modes」と同じ場所に出す (開いたときに ModesSheet が場所を合わせる)。シートより手前
        var close = NewUi("Close", b.sheet.transform);
        Rounded(close.AddComponent<Image>(), Raised);
        var closeOutline = close.AddComponent<Outline>();
        closeOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f);
        closeOutline.effectDistance = new Vector2(2, -2);
        b.close = close.AddComponent<Button>();
        var closeText = Label("Label", close.transform, font, "Close", 32, Color.white, FontStyles.Bold);
        closeText.alignment = TextAlignmentOptions.Center;
        Stretch(closeText.gameObject);

        b.sheet.SetActive(false);
        return b;
    }

    private static ModesSheet.Entry Card(Transform parent, TMP_FontAsset font, string title, string desc, string badge, Button tab,
                                         string screenName)
    {
        var card = NewUi(title + "Card", parent);
        var bg = card.AddComponent<Image>();
        Rounded(bg, CardColor);
        var outline = card.AddComponent<Outline>();
        outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f);
        outline.effectDistance = new Vector2(2, -2);
        var button = card.AddComponent<Button>();
        card.AddComponent<LayoutElement>().flexibleWidth = 1;
        var v = card.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(28, 24, 26, 22);
        v.spacing = 10;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var top = Row("Top", card.transform, 60, 12);
        var titleText = Label("Title", top.transform, font, title, 44, Color.white, FontStyles.Bold);
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var chip = NewUi("Badge", top.transform);
        chip.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.16f);
        var h = chip.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(14, 14, 6, 6);
        h.childControlWidth = h.childControlHeight = true;
        h.childAlignment = TextAnchor.MiddleCenter;
        var chipLe = chip.AddComponent<LayoutElement>();
        chipLe.flexibleWidth = 0;
        var badgeText = Label("Text", chip.transform, font, badge, 24, Cyan, FontStyles.Bold);
        badgeText.alignment = TextAlignmentOptions.Center;

        var d = Label("Desc", card.transform, font, desc, 30, SubText, FontStyles.Normal);
        d.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
        d.enableWordWrapping = true;
        d.alignment = TextAlignmentOptions.TopLeft;

        return new ModesSheet.Entry
        {
            card = button, tab = tab, screen = OpenedScreen(tab, screenName), title = titleText,
            badge = badgeText, badgeText = badge, background = bg,
        };
    }

    // タブが表示する画面: onClick でつないである GameObject のうち、名前が合うもの
    private static GameObject OpenedScreen(Button tab, string screenName)
    {
        for (int i = 0; i < tab.onClick.GetPersistentEventCount(); i++)
            if (tab.onClick.GetPersistentTarget(i) is GameObject go && go.name.StartsWith(screenName)) return go;
        return UiStyle.Find(screenName);   // コードで作った画面 (短い答え) はつないでいないので、名前で探す
    }

    // ------------------------------------------------------------
    // UI の部品
    // ------------------------------------------------------------
    internal static GameObject NewUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    internal static void CopyScaler(CanvasScaler from, CanvasScaler to)
    {
        if (from == null) return;
        to.uiScaleMode = from.uiScaleMode;
        to.scaleFactor = from.scaleFactor;
        to.referenceResolution = from.referenceResolution;
        to.screenMatchMode = from.screenMatchMode;
        to.matchWidthOrHeight = from.matchWidthOrHeight;
        to.referencePixelsPerUnit = from.referencePixelsPerUnit;
    }

    internal static void Stretch(GameObject go)
    {
        var r = (RectTransform)go.transform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static GameObject Row(string name, Transform parent, float height, float spacing)
    {
        var go = NewUi(name, parent);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        go.AddComponent<LayoutElement>().preferredHeight = height;
        return go;
    }

    internal static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font, string text, float size,
                                        Color color, FontStyles style)
    {
        var t = NewUi(name, parent).AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
        return t;
    }

    internal static void Rounded(Image image, Color color)
    {
        image.sprite = _rounded;
        image.type = _rounded != null && _rounded.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = color;
    }

    internal static Color Hex(string hex, float alpha = 1f) => UiTheme.Hex(hex, alpha);
}
