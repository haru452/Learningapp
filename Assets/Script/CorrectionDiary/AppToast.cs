using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// どの画面の上にも出す短いお知らせ (上の方のカード)。8 秒で消える。押すとすぐ消える。
/// 続けて来たものは順に出す。あとで送った添削・採点が終わったときに使う。
/// </summary>
public class AppToast : MonoBehaviour
{
    private const float ShowSeconds = 8f;

    private static AppToast _instance;
    private readonly Queue<string> _queue = new Queue<string>();
    private GameObject _card;
    private TMP_Text _text;
    private float _hideAt;

    public static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !Application.isPlaying) return;
        if (_instance == null) _instance = Create();
        _instance._queue.Enqueue(message.Trim());
        if (!_instance._card.activeSelf) _instance.Next();
    }

    private static AppToast Create()
    {
        var go = new GameObject("AppToast");
        DontDestroyOnLoad(go);
        var toast = go.AddComponent<AppToast>();
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // どの画面よりも上
        var scaler = go.AddComponent<CanvasScaler>();
        var from = FindObjectOfType<CanvasScaler>(true);
        if (from != null) ModesNav.CopyScaler(from, scaler);
        go.AddComponent<GraphicRaycaster>();

        var font = FindObjectOfType<TMP_Text>(true)?.font;
        if (font == null) font = TMP_Settings.defaultFontAsset;

        var card = UiStyle.NewUi("Card", go.transform);
        var rt = (RectTransform)card.transform;
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(UiTheme.Gutter, 0);
        rt.offsetMax = new Vector2(-UiTheme.Gutter, -UiTheme.Gutter * 2);
        UiStyle.Surface(card, UiTheme.SurfaceRaised, outline: true);
        var fit = card.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var column = card.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(40, 40, 32, 32);
        column.childControlHeight = true;
        column.childControlWidth = true;
        column.childForceExpandHeight = false;

        toast._text = UiStyle.NewText("Message", card.transform, font, "", UiTheme.BodySize, UiTheme.Text);
        toast._text.alignment = TextAlignmentOptions.Left;
        toast._text.enableWordWrapping = true;

        var button = card.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(toast.Next);   // 押したら次へ (無ければ消える)

        toast._card = card;
        card.SetActive(false);
        return toast;
    }

    private void Next()
    {
        if (_queue.Count == 0) { _card.SetActive(false); return; }
        _text.text = _queue.Dequeue();
        _card.SetActive(true);
        _hideAt = Time.realtimeSinceStartup + ShowSeconds;
    }

    private void Update()
    {
        if (_card != null && _card.activeSelf && Time.realtimeSinceStartup >= _hideAt) Next();
    }
}
