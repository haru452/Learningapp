using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 下のタブの「Modes」で開く、全部の機能のシート (2 列のカード)。
/// カードを押すと、隠してある元のタブボタン (Summary・Topic など) を押したのと同じ動きをする。
/// 画面の切り替え方は元のタブのまま (ここでは変えない)。
/// 起動時に ModesNav が作る (シーンには置かない)
/// </summary>
public class ModesSheet : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public Button card;
        public Button tab;            // 元のタブボタン (押すと画面が切り替わる)
        public GameObject screen;     // そのタブで開く画面 (今いる画面に「Now」を出す)
        public TMP_Text title;
        public TMP_Text badge;
        public string badgeText;
        public Image background;
        public bool inBottomBar;      // 下のタブにもある (● は Modes ではなく下のタブに付ける)
    }

    private GameObject sheet;          // 暗い背景ごと出し入れする
    private RectTransform panel;       // 下から出てくる板
    private Button dimButton;          // 背景を押すと閉じる
    private Button closeButton;
    private TMP_Text modesLabel;       // 下のタブの「Modes」の文字
    private RectTransform modesTab;    // 下のタブの「Modes」のボタン (Close を同じ場所に出す)
    private List<Entry> entries = new List<Entry>();

    private static readonly Color CardColor = new Color32(0x11, 0x18, 0x27, 0xF2);
    private static readonly Color CurrentColor = new Color32(0x1E, 0x29, 0x3B, 0xFF);
    private const float SlideSeconds = 0.18f;

    private readonly Dictionary<Entry, string> _titles = new Dictionary<Entry, string>();
    private string _modesText;
    private float _nextBadgeCheck;
    private Coroutine _slide;
    private float _panelBottom;        // 板の下の端 (下のタブの上)

    public bool IsOpen => sheet != null && sheet.activeSelf;

    /// <summary>ModesNav から部品をつなぐ (起動時に 1 回)</summary>
    public void Setup(GameObject sheet, RectTransform panel, Button dimButton, Button closeButton,
                      TMP_Text modesLabel, RectTransform modesTab, List<Entry> entries)
    {
        this.sheet = sheet; this.panel = panel; this.dimButton = dimButton; this.closeButton = closeButton;
        this.modesLabel = modesLabel; this.modesTab = modesTab; this.entries = entries;
        Wire();
    }

    private void Wire()
    {
        foreach (var e in entries)
        {
            if (e.title != null) _titles[e] = e.title.text;
            var entry = e;
            if (e.card != null) e.card.onClick.AddListener(() => Choose(entry));
        }
        if (modesLabel != null) _modesText = TabBadge.Remove(modesLabel.text);
        if (dimButton != null) dimButton.onClick.AddListener(Close);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (sheet != null) sheet.SetActive(false);
    }

    /// <summary>下のタブの「Modes」から呼ぶ</summary>
    public void Open()
    {
        if (sheet == null) return;
        RefreshCards();
        sheet.SetActive(true);
        PlaceOverModesTab();
        if (_slide != null) StopCoroutine(_slide);
        _slide = StartCoroutine(SlideIn());
    }

    public void Close()
    {
        if (_slide != null) StopCoroutine(_slide);
        _slide = null;
        if (panel != null) panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, _panelBottom);
        if (sheet != null) sheet.SetActive(false);
    }

    /// <summary>
    /// Close を下のタブの「Modes」とまったく同じ場所・大きさにし (開いた指のまま押せる)、板はタブの上から出す。
    /// どちらの Canvas も Overlay なので、ワールド座標 = 画面の画素
    /// </summary>
    private void PlaceOverModesTab()
    {
        _panelBottom = 0f;
        var close = closeButton != null ? (RectTransform)closeButton.transform : null;
        if (modesTab == null || close == null || !modesTab.gameObject.activeInHierarchy)
        {
            if (close != null) close.gameObject.SetActive(false);   // 下のタブが無い画面: 背景と戻るボタンで閉じる
            return;
        }
        close.gameObject.SetActive(true);
        var c = new Vector3[4];
        modesTab.GetWorldCorners(c);
        float scale = Mathf.Max(0.0001f, close.lossyScale.x);
        close.anchorMin = close.anchorMax = Vector2.zero;
        close.pivot = new Vector2(0.5f, 0.5f);
        close.position = (c[0] + c[2]) * 0.5f;
        close.sizeDelta = new Vector2(c[2].x - c[0].x, c[2].y - c[0].y) / scale;

        // 板の下の端を、下のタブ (バー) の上にする
        _panelBottom = BottomNavBar.ReservedPixels / scale;
    }

    private void Choose(Entry e)
    {
        Close();
        if (e.tab != null) e.tab.onClick.Invoke();   // 隠れていても、元のタブと同じ切り替えをする
    }

    private void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();   // Android の戻るボタン
        if (Time.unscaledTime < _nextBadgeCheck) return;
        _nextBadgeCheck = Time.unscaledTime + 0.5f;
        RefreshBadgeMarks();
    }

    // 隠したタブに付いた「●」(Topic の採点が終わった など) を、カードと「Modes」に写す
    private void RefreshBadgeMarks()
    {
        bool any = false;
        foreach (var e in entries)
        {
            bool marked = HasMark(e.tab);
            any |= marked && !e.inBottomBar;
            if (e.title != null && _titles.TryGetValue(e, out var plain))
            {
                string text = marked ? TabBadge.Add(plain) : plain;
                if (e.title.text != text) e.title.text = text;
            }
        }
        if (modesLabel != null && _modesText != null)
        {
            string text = any ? TabBadge.Add(_modesText) : _modesText;
            if (modesLabel.text != text) modesLabel.text = text;
        }
    }

    private static bool HasMark(Button tab)
    {
        if (tab == null) return false;
        var label = tab.GetComponentInChildren<TMP_Text>(true);
        return label != null && TabBadge.Has(label.text);
    }

    // 今いる画面のカードに「Now」を出す
    private void RefreshCards()
    {
        foreach (var e in entries)
        {
            bool current = e.screen != null && e.screen.activeInHierarchy;
            if (e.badge != null) e.badge.text = current ? "Now" : e.badgeText;
            if (e.background != null) e.background.color = current ? CurrentColor : CardColor;
        }
        RefreshBadgeMarks();
    }

    private IEnumerator SlideIn()
    {
        if (panel == null) yield break;
        Canvas.ForceUpdateCanvases();
        float height = Mathf.Max(1f, panel.rect.height);
        float t = 0f;
        while (t < SlideSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / SlideSeconds), 3f);   // ease-out
            panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, _panelBottom - (height + _panelBottom) * (1f - k));
            yield return null;
        }
        panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, _panelBottom);
        _slide = null;
    }
}
