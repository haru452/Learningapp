using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Repeat 画面に、実行時に「Shadowing: On/Off」「Slow: On/Off」のボタンと、イヤホンのポップアップを足す
/// (エディタのツールで画面を作り直さなくてよいように、Start ボタンを複製して作る)。選んだ状態は端末に覚える。
/// </summary>
public class ShadowUi
{
    private const string ShadowPrefsKey = "shadow_mode_on";
    private const string SlowPrefsKey = "shadow_slow_on";
    public const string PopupMessage =
        "Please use earphones for shadowing. If the phone speaker is used, the microphone hears it and the result will be wrong.";

    public bool ShadowOn { get; private set; }
    public bool SlowOn { get; private set; }
    public event Action Changed;

    private readonly Button _shadowButton;
    private readonly Button _slowButton;
    private readonly TMP_Text _shadowLabel;
    private readonly TMP_Text _slowLabel;
    private readonly Button _template;
    private readonly TMP_Text _textTemplate;
    private GameObject _popup;
    private TaskCompletionSource<bool> _answer;

    public ShadowUi(Button template, TMP_Text textTemplate)
    {
        _template = template;
        _textTemplate = textTemplate;
        ShadowOn = PlayerPrefs.GetInt(ShadowPrefsKey, 0) == 1;
        SlowOn = PlayerPrefs.GetInt(SlowPrefsKey, 0) == 1;

        // Root (Start ボタンの親) の先頭に 1 行足す
        Transform root = template.transform.parent;
        var row = new GameObject("ShadowRow", typeof(RectTransform));
        row.transform.SetParent(root, false);
        row.transform.SetSiblingIndex(0);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 90;

        (_shadowButton, _shadowLabel) = CloneButton(row.transform, "ShadowingButton");
        (_slowButton, _slowLabel) = CloneButton(row.transform, "ShadowSlowButton");
        _shadowButton.onClick.AddListener(() => { ShadowOn = !ShadowOn; Save(); Refresh(); Changed?.Invoke(); });
        _slowButton.onClick.AddListener(() => { SlowOn = !SlowOn; Save(); Refresh(); Changed?.Invoke(); });
        Refresh();
    }

    /// <summary>切り替えられるのは練習していないとき (Idle・まとめ) だけ</summary>
    public void SetInteractable(bool on)
    {
        _shadowButton.interactable = on;
        _slowButton.interactable = on;
    }

    /// <summary>イヤホンを使うよう促す。Start anyway → true、Cancel → false</summary>
    public Task<bool> ConfirmEarphonesAsync()
    {
        CloseEarphonePopup(false);
        _answer = new TaskCompletionSource<bool>();
        _popup = CreatePopup();
        return _answer.Task;
    }

    /// <summary>ポップアップを閉じる (タブの移動など)。待っている側には answer を返す</summary>
    public void CloseEarphonePopup(bool answer)
    {
        if (_popup != null) UnityEngine.Object.Destroy(_popup);
        _popup = null;
        _answer?.TrySetResult(answer);
        _answer = null;
    }

    // ------------------------------------------------------------
    private void Refresh()
    {
        _shadowLabel.text = "Shadowing: " + (ShadowOn ? "On" : "Off");
        _slowLabel.text = "Slow: " + (SlowOn ? "On" : "Off");
        _slowButton.gameObject.SetActive(ShadowOn);   // Slow はシャドーイングのときだけ
    }

    private void Save()
    {
        PlayerPrefs.SetInt(ShadowPrefsKey, ShadowOn ? 1 : 0);
        PlayerPrefs.SetInt(SlowPrefsKey, SlowOn ? 1 : 0);
        PlayerPrefs.Save();
    }

    private (Button, TMP_Text) CloneButton(Transform parent, string name)
    {
        var go = UnityEngine.Object.Instantiate(_template.gameObject, parent, false);
        go.name = name;
        go.SetActive(true);
        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();   // 複製元のボタンの動きは引き継がない
        button.interactable = true;
        var le = go.GetComponent<LayoutElement>();
        if (le != null) { le.preferredHeight = -1; le.minHeight = -1; }
        return (button, go.GetComponentInChildren<TMP_Text>(true));
    }

    private GameObject CreatePopup()
    {
        var canvas = _template.GetComponentInParent<Canvas>().rootCanvas.transform;
        var overlay = new GameObject("EarphonePopup", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)overlay.transform;
        rt.SetParent(canvas, false);
        rt.SetAsLastSibling();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);   // 後ろを押せないようにする

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        var prt = (RectTransform)panel.transform;
        prt.SetParent(rt, false);
        prt.anchorMin = new Vector2(0.07f, 0.33f); prt.anchorMax = new Vector2(0.93f, 0.67f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.16f, 0.18f, 0.23f, 1f);
        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(32, 32, 32, 32);
        vlg.spacing = 24;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var textGo = new GameObject("Message", typeof(RectTransform));
        textGo.transform.SetParent(prt, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        if (_textTemplate != null) text.font = _textTemplate.font;
        text.text = "\U0001F3A7 " + PopupMessage;
        text.fontSize = 34;
        text.enableAutoSizing = true;
        text.fontSizeMin = 20;
        text.fontSizeMax = 36;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.enableWordWrapping = true;
        textGo.AddComponent<LayoutElement>().flexibleHeight = 1;

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(prt, false);
        var hlg = buttons.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;
        buttons.AddComponent<LayoutElement>().preferredHeight = 100;
        var (start, startLabel) = CloneButton(buttons.transform, "StartAnywayButton");
        var (cancel, cancelLabel) = CloneButton(buttons.transform, "CancelButton");
        startLabel.text = "Start anyway";
        cancelLabel.text = "Cancel";
        start.onClick.AddListener(() => CloseEarphonePopup(true));
        cancel.onClick.AddListener(() => CloseEarphonePopup(false));
        return overlay;
    }
}
