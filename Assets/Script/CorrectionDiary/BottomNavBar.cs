using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面の下のタブ (Diary / Topic / Vocab / Modes)。起動時に ModesNav が作る。
/// ・MainCanvas (元のタブがあった画面) が出ている間だけ表示する
/// ・今いる画面のタブを、丸い背景で目立たせる
/// ・最初に表示したとき、Summary などの画面の中身 (Root) をバーの上までにし、元のタブがあった上の空きを使う
/// </summary>
public class BottomNavBar : MonoBehaviour
{
    public class Item
    {
        public string label;
        public Action onClick;
        public GameObject[] screens;   // このタブで開く画面 (どれかが出ていたら選ばれている)
        public Button markFrom;        // 隠した元のタブ。その名前に ● (採点が終わった など) が付いたら、こちらにも付ける
        internal Image bg;
        internal TMP_Text text;
    }

    private const float BarHeight = 150f;
    private static readonly Color BarColor = new Color(UiTheme.Background.r, UiTheme.Background.g, UiTheme.Background.b, 0.98f);
    private static readonly Color LineColor = UiTheme.Divider;
    private static readonly Color SelectedBg = UiTheme.SurfaceRaised;
    private static readonly Color SelectedText = Color.white;
    private static readonly Color NormalText = UiTheme.SubText;

    private GameObject _bar;
    private GameObject _mainCanvas;
    private RectTransform _oldTab;          // 元のタブ (上の空きを測る)
    private List<RectTransform> _roots;     // 画面の中身 (下の端をバーの上にする)
    private List<Item> _items;
    private float _height;
    private bool _fitted;
    private float _nextRefresh;

    /// <summary>下のタブが画面の下で使っている高さ (画素)。出ていなければ 0。OnGUI で描く画面 (Talk) が避けるのに使う</summary>
    public static float ReservedPixels { get; private set; }

    public TMP_Text LabelOf(string label) => _items.FirstOrDefault(i => i.label == label)?.text;

    /// <summary>タブのボタンの場所 (Modes のシートの Close を同じ場所に出すのに使う)</summary>
    public RectTransform TabOf(string label) => _items.FirstOrDefault(i => i.label == label)?.bg?.rectTransform;

    public static BottomNavBar Create(Transform mainCanvas, RectTransform oldTab, IEnumerable<RectTransform> roots,
                                      TMP_FontAsset font, List<Item> items)
    {
        var root = new GameObject("BottomNavBar", typeof(RectTransform));
        root.layer = LayerMask.NameToLayer("UI");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;   // 画面 (0) より手前、Modes のシート (100) より奥
        ModesNav.CopyScaler(mainCanvas.GetComponent<CanvasScaler>(), root.AddComponent<CanvasScaler>());
        root.AddComponent<GraphicRaycaster>();
        var nav = root.AddComponent<BottomNavBar>();
        nav._mainCanvas = mainCanvas.gameObject;
        nav._oldTab = oldTab;
        nav._roots = roots.Where(r => r != null).ToList();
        nav._items = items;

        // Android のジェスチャーバーなどの分だけ高くする (Constant Pixel Size なので 1 単位 = 1 画素)
        float scale = Mathf.Max(0.01f, root.GetComponent<CanvasScaler>().scaleFactor);
        float inset = Mathf.Max(0f, Screen.safeArea.yMin) / scale;
        nav._height = BarHeight + inset;

        var bar = ModesNav.NewUi("Bar", root.transform);
        nav._bar = bar;
        var r = (RectTransform)bar.transform;
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(1f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta = new Vector2(0f, nav._height);
        bar.AddComponent<Image>().color = BarColor;

        var line = ModesNav.NewUi("Line", bar.transform);
        var lr = (RectTransform)line.transform;
        lr.anchorMin = new Vector2(0f, 1f);
        lr.anchorMax = new Vector2(1f, 1f);
        lr.pivot = new Vector2(0.5f, 1f);
        lr.sizeDelta = new Vector2(0f, 2f);
        line.AddComponent<Image>().color = LineColor;

        var row = ModesNav.NewUi("Items", bar.transform);
        var rr = (RectTransform)row.transform;
        rr.anchorMin = Vector2.zero;
        rr.anchorMax = Vector2.one;
        rr.offsetMin = new Vector2(24f, inset + 18f);
        rr.offsetMax = new Vector2(-24f, -18f);
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 16;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;

        foreach (var item in items)
        {
            var go = ModesNav.NewUi(item.label + "Tab", row.transform);
            item.bg = go.AddComponent<Image>();
            ModesNav.Rounded(item.bg, Color.clear);
            var button = go.AddComponent<Button>();
            var it = item;
            button.onClick.AddListener(() => it.onClick?.Invoke());
            item.text = ModesNav.Label("Label", go.transform, font, item.label, 32, NormalText, FontStyles.Bold);
            item.text.alignment = TextAlignmentOptions.Center;
            ModesNav.Stretch(item.text.gameObject);
        }
        bar.SetActive(false);
        return nav;
    }

    private void Update()
    {
        bool show = _mainCanvas != null && _mainCanvas.activeInHierarchy;
        if (_bar.activeSelf != show) _bar.SetActive(show);
        ReservedPixels = show ? _height * Mathf.Max(0.01f, GetComponent<CanvasScaler>().scaleFactor) : 0f;
        if (!show) return;
        if (!_fitted) { _fitted = true; FitScreens(); }
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.2f;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        foreach (var item in _items)
        {
            bool selected = item.screens != null && item.screens.Any(s => s != null && s.activeInHierarchy);
            item.bg.color = selected ? SelectedBg : Color.clear;
            item.text.color = selected ? SelectedText : NormalText;
            if (item.markFrom == null) continue;   // Modes の ● は ModesSheet が付ける
            var source = item.markFrom.GetComponentInChildren<TMP_Text>(true);
            string text = source != null && TabBadge.Has(source.text) ? TabBadge.Add(item.label) : item.label;
            if (item.text.text != text) item.text.text = text;
        }
    }

    // 画面の中身 (アンカーで高さを決めている Root) を、バーの上から元のタブの上の端までにする
    private void FitScreens()
    {
        float screenH = Mathf.Max(1f, Screen.height);
        float scale = Mathf.Max(0.01f, GetComponent<CanvasScaler>().scaleFactor);
        float bottom = (_height * scale) / screenH + 0.01f;

        float top = -1f;
        if (_oldTab != null)
        {
            var c = new Vector3[4];
            _oldTab.GetWorldCorners(c);   // Overlay なので、ワールド座標 = 画面の画素
            float tabTop = Mathf.Max(c[1].y, c[2].y) / screenH;
            float tabBottom = Mathf.Min(c[0].y, c[3].y) / screenH;
            if (tabTop > tabBottom && tabTop - tabBottom < 0.15f && tabBottom > 0.5f) top = tabTop;
            else Debug.LogWarning("[ModesNav] could not measure the old tabs. Kept the top of the screens.");
        }

        foreach (var root in _roots)
        {
            if (root.anchorMin.y > bottom) continue;   // もう十分に上
            root.anchorMin = new Vector2(root.anchorMin.x, bottom);
            if (top > root.anchorMax.y && top - root.anchorMax.y < 0.15f)
                root.anchorMax = new Vector2(root.anchorMax.x, top);
        }
    }
}
