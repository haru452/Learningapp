using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ShortAnswerScreen の続き: 画面を作り、Modes のシートから開けるようにする (シーンは変えない)。
/// 上のタブ (MainCanvas/Panel) に隠れたボタン "Answer" を足し、ModesNav がそれをカードにする。
/// </summary>
public partial class ShortAnswerScreen
{
    public const string ScreenName = "answer screen";
    public const string TabName = "Answer";

    private TMP_Text _progress, _mainText, _detailText, _mainLabel;
    private Button _mainButton, _replayButton, _checkButton;
    private GameObject _inputRow;
    private TMP_InputField _input;

    /// <summary>
    /// 画面と隠れたタブを作る (ModesNav が下のタブを作る前に 1 回)。元のタブの並び (MainCanvas/Panel) が無ければ何もしない
    /// </summary>
    public static void Install()
    {
        if (UiStyle.Find(ScreenName) != null) return;
        var someTab = Object.FindObjectsOfType<Button>(true).FirstOrDefault(b => b.name == "Dairy" && b.transform.parent != null
                                                                                 && b.transform.parent.name == "Panel");
        if (someTab == null) { Debug.LogWarning("[Answer] the tab row was not found. The Answer mode was not added."); return; }
        Transform panel = someTab.transform.parent;
        Transform mainCanvas = panel.parent;
        TMP_FontAsset font = someTab.GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;

        var go = new GameObject(ScreenName);
        go.SetActive(false);
        var screen = go.AddComponent<ShortAnswerScreen>();
        screen.Build(mainCanvas.GetComponent<CanvasScaler>(), font);

        // 隠れたタブ: 押すとこの画面を開く。ほかのタブを押したら、この画面を閉じる
        var tab = Object.Instantiate(someTab.gameObject, panel, false);
        tab.name = TabName;
        var button = tab.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(screen.Open);
        screen._otherTabs = panel.GetComponentsInChildren<Button>(true).Where(b => b != button).ToList();
        foreach (var other in screen._otherTabs) other.onClick.AddListener(() => go.SetActive(false));

        // アプリを開いたら、問題を 10 問作り置きしておく (Start で待たないように)
        _ = ShortAnswerStock.FillAsync(CreateQuestionClient(), LoadRecent);
    }

    private List<Button> _otherTabs = new List<Button>();

    /// <summary>ほかの画面 (元のタブが出す画面) を閉じて、この画面を出す</summary>
    public void Open()
    {
        foreach (var tab in _otherTabs)
            for (int i = 0; i < tab.onClick.GetPersistentEventCount(); i++)
                if (tab.onClick.GetPersistentTarget(i) is GameObject target && ModesNav.IsScreenName(target.name))
                    target.SetActive(false);
        gameObject.SetActive(true);
    }

    private void Build(CanvasScaler scalerFrom, TMP_FontAsset font)
    {
        var canvasGo = UiStyle.NewUi("Canvas", transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        ModesNav.CopyScaler(scalerFrom, canvasGo.AddComponent<CanvasScaler>());
        canvasGo.AddComponent<GraphicRaycaster>();

        var bg = UiStyle.NewUi("Background", canvasGo.transform);
        UiStyle.Stretch(bg.transform);
        bg.AddComponent<Image>().color = UiTheme.Background;

        var root = UiStyle.ScreenRoot(canvasGo.transform);
        UiStyle.TopRow(root.transform, font, "Answer", null);
        _progress = UiStyle.NewText("Progress", root.transform, font, "", UiTheme.CaptionSize, UiTheme.SubText);
        UiStyle.Size(_progress.gameObject, 44);

        var card = UiStyle.NewUi("Card", root.transform);
        UiStyle.Surface(card, UiTheme.Surface, outline: true);
        UiStyle.Size(card, -1, 1);
        var column = UiStyle.Column(card, 32, 16);
        column.padding = new RectOffset(36, 36, 32, 32);
        var content = UiStyle.Scroll(card.transform, 1f);
        _mainText = UiStyle.NewText("MainText", content, font, "", UiTheme.HeadingSize, UiTheme.Text, FontStyles.Bold);
        _mainText.alignment = TextAlignmentOptions.TopLeft;
        _detailText = UiStyle.NewText("DetailText", content, font, "", UiTheme.BodySize, UiTheme.SubText);
        _detailText.alignment = TextAlignmentOptions.TopLeft;
        _detailText.richText = true;

        // 文字で答える欄 (音声認識が使えないとき: Unity エディターなど)
        _inputRow = UiStyle.NewUi("InputRow", root.transform);
        UiStyle.Size(_inputRow, UiTheme.MinTouch);
        UiStyle.Row(_inputRow, UiTheme.Gap);
        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "AnswerInput";
        inputGo.transform.SetParent(_inputRow.transform, false);
        UiStyle.Size(inputGo, -1, 0, -1, 3);
        UiStyle.Surface(inputGo, UiTheme.Surface, outline: true);
        _input = inputGo.GetComponent<TMP_InputField>();
        _input.lineType = TMP_InputField.LineType.SingleLine;
        foreach (var t in inputGo.GetComponentsInChildren<TMP_Text>(true))
        {
            t.font = font;
            t.fontSize = 32;
            t.color = t == _input.placeholder ? UiTheme.SubText : UiTheme.Text;
        }
        if (_input.placeholder is TMP_Text ph) ph.text = "Type your answer";
        _input.onSubmit.AddListener(_ => OnCheckTyped());
        _checkButton = NewButton("CheckButton", _inputRow.transform, font, "Check", primary: false, flex: 1);
        _checkButton.onClick.AddListener(OnCheckTyped);
        _inputRow.SetActive(false);

        var buttons = UiStyle.NewUi("ButtonRow", root.transform);
        UiStyle.Size(buttons, UiTheme.ButtonHeight);
        UiStyle.Row(buttons, UiTheme.Gap).childForceExpandWidth = true;
        _replayButton = NewButton("ReplayButton", buttons.transform, font, "Replay", primary: false, flex: 1);
        _replayButton.onClick.AddListener(OnReplay);
        _mainButton = NewButton("MainButton", buttons.transform, font, "Start", primary: true, flex: 2);
        _mainButton.onClick.AddListener(OnMain);
        _mainLabel = _mainButton.GetComponentInChildren<TMP_Text>(true);
    }

    private static Button NewButton(string name, Transform parent, TMP_FontAsset font, string text, bool primary, float flex)
    {
        var go = UiStyle.NewUi(name, parent);
        var button = go.AddComponent<Button>();
        UiStyle.Size(go, -1, 0, -1, flex);
        var label = UiStyle.NewText("Label", go.transform, font, text, UiTheme.BodySize, UiTheme.Text, FontStyles.Bold);
        UiStyle.Stretch(label.transform);
        if (primary) UiStyle.Primary(button); else UiStyle.Secondary(button);
        return button;
    }
}
