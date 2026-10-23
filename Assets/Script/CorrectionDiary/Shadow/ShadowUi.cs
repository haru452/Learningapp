using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Repeat 画面に、実行時に「Shadowing: On/Off」のボタン、動画の検索欄と候補、動画の表示枠、イヤホンのポップアップを足す
/// (エディタのツールで画面を作り直さなくてよいように、Start ボタンを複製して作る)。選んだ状態・動画は端末に覚える。
/// </summary>
public class ShadowUi
{
    private const string ShadowPrefsKey = "shadow_mode_on";
    private const string VideoPrefsKey = "shadow_video_v1";
    public const string PopupMessage =
        "Please use earphones for shadowing. If the phone speaker is used, the microphone hears it and the result will be wrong.";

    public bool ShadowOn { get; private set; }
    public YouTubeSearchClient.Video Video { get; private set; }
    public RectTransform VideoArea { get; }

    public event Action Changed;                                   // Shadowing のオン・オフ
    public event Action<string> SearchRequested;
    public event Action<YouTubeSearchClient.Video> VideoChosen;

    private readonly Button _template;
    private readonly TMP_Text _textTemplate;
    private readonly Button _shadowButton;
    private readonly TMP_Text _shadowLabel;
    private readonly GameObject _searchRow;
    private readonly TMP_InputField _input;
    private readonly Button _searchButton;
    private readonly GameObject _candidates;
    private readonly LayoutElement _videoLayout;
    private GameObject _popup;
    private TaskCompletionSource<bool> _answer;

    [Serializable] private class SavedVideo { public string id; public string title; public string channel; public int durationSeconds; }

    public ShadowUi(Button template, TMP_Text textTemplate)
    {
        _template = template;
        _textTemplate = textTemplate;
        ShadowOn = PlayerPrefs.GetInt(ShadowPrefsKey, 0) == 1;
        Video = LoadVideo();
        Transform root = template.transform.parent;

        // 1 行目: Shadowing: On/Off
        var row = NewRow(root, "ShadowRow", 90);
        row.transform.SetSiblingIndex(0);
        (_shadowButton, _shadowLabel) = CloneButton(row.transform, "ShadowingButton");
        _shadowButton.onClick.AddListener(() =>
        {
            ShadowOn = !ShadowOn;
            PlayerPrefs.SetInt(ShadowPrefsKey, ShadowOn ? 1 : 0);
            PlayerPrefs.Save();
            Refresh();
            Changed?.Invoke();
        });

        // 2 行目: 検索欄 + Search
        _searchRow = NewRow(root, "ShadowSearchRow", 90);
        _searchRow.transform.SetSiblingIndex(1);
        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "ShadowSearchInput";
        inputGo.transform.SetParent(_searchRow.transform, false);
        inputGo.AddComponent<LayoutElement>().flexibleWidth = 3;
        _input = inputGo.GetComponent<TMP_InputField>();
        _input.lineType = TMP_InputField.LineType.SingleLine;
        foreach (var t in inputGo.GetComponentsInChildren<TMP_Text>(true))
        {
            if (_textTemplate != null) t.font = _textTemplate.font;
            t.fontSize = 30;
        }
        if (_input.placeholder is TMP_Text ph) ph.text = "Video title";
        _input.onSubmit.AddListener(_ => RequestSearch());
        TMP_Text searchLabel;
        (_searchButton, searchLabel) = CloneButton(_searchRow.transform, "ShadowSearchButton");
        searchLabel.text = "Search";
        _searchButton.onClick.AddListener(RequestSearch);

        // 候補 (最大 5)
        _candidates = new GameObject("ShadowCandidates", typeof(RectTransform));
        _candidates.transform.SetParent(root, false);
        _candidates.transform.SetSiblingIndex(2);
        var vlg = _candidates.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        _candidates.SetActive(false);

        // 動画の表示枠 (WebView はこの位置に重なる)
        var area = new GameObject("ShadowVideoArea", typeof(RectTransform));
        area.transform.SetParent(root, false);
        area.transform.SetSiblingIndex(3);
        VideoArea = (RectTransform)area.transform;
        _videoLayout = area.AddComponent<LayoutElement>();
        ShowVideo(false);

        Refresh();
    }

    /// <summary>練習していないとき (Idle・まとめ) だけ切り替え・検索できる</summary>
    public void SetInteractable(bool on)
    {
        _shadowButton.interactable = on;
        _searchButton.interactable = on;
        _input.interactable = on;
        foreach (var b in _candidates.GetComponentsInChildren<Button>(true)) b.interactable = on;
    }

    public void ShowCandidates(IList<YouTubeSearchClient.Video> videos)
    {
        foreach (Transform c in _candidates.transform) UnityEngine.Object.Destroy(c.gameObject);
        foreach (var v in videos ?? new List<YouTubeSearchClient.Video>())
        {
            var (b, label) = CloneButton(_candidates.transform, "Candidate");
            var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 80;
            label.text = Escape(v.title) + " — " + Escape(v.channel) + " (" + YouTubeSearchClient.FormatDuration(v.durationSeconds) + ")";
            var chosen = v;
            b.onClick.AddListener(() => Choose(chosen));
        }
        _candidates.SetActive(videos != null && videos.Count > 0);
    }

    public void HideCandidates() => ShowCandidates(null);

    /// <summary>練習中は検索欄を隠す (動画とボタンの場所を空ける)</summary>
    public void SetSearchVisible(bool on) => _searchRow.SetActive(ShadowOn && on);

    /// <summary>動画を出す枠を広げる / 閉じる (16:9)</summary>
    public void ShowVideo(bool on)
    {
        float width = ((RectTransform)_template.transform.parent).rect.width - 48;
        _videoLayout.preferredHeight = on ? Mathf.Max(300, width * 9f / 16f) : 0;
        _videoLayout.minHeight = on ? 300 : 0;
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_template.transform.parent);
    }

    /// <summary>イヤホンを使うよう促す。Start anyway → true、Cancel → false</summary>
    public Task<bool> ConfirmEarphonesAsync()
    {
        CloseEarphonePopup(false);
        _answer = new TaskCompletionSource<bool>();
        _popup = CreatePopup();
        return _answer.Task;
    }

    public void CloseEarphonePopup(bool answer)
    {
        if (_popup != null) UnityEngine.Object.Destroy(_popup);
        _popup = null;
        _answer?.TrySetResult(answer);
        _answer = null;
    }

    // ------------------------------------------------------------
    private void RequestSearch()
    {
        string q = (_input.text ?? "").Trim();
        if (q.Length > 0) SearchRequested?.Invoke(q);
    }

    private void Choose(YouTubeSearchClient.Video v)
    {
        Video = v;
        PlayerPrefs.SetString(VideoPrefsKey, JsonUtility.ToJson(new SavedVideo
            { id = v.id, title = v.title, channel = v.channel, durationSeconds = v.durationSeconds }));
        PlayerPrefs.Save();
        HideCandidates();
        VideoChosen?.Invoke(v);
    }

    private static YouTubeSearchClient.Video LoadVideo()
    {
        try
        {
            var s = JsonUtility.FromJson<SavedVideo>(PlayerPrefs.GetString(VideoPrefsKey, ""));
            if (s != null && !string.IsNullOrEmpty(s.id) && s.durationSeconds > 0)
                return new YouTubeSearchClient.Video { id = s.id, title = s.title, channel = s.channel, durationSeconds = s.durationSeconds };
        }
        catch (Exception) { }
        return null;
    }

    private void Refresh()
    {
        _shadowLabel.text = "Shadowing: " + (ShadowOn ? "On" : "Off");
        _searchRow.SetActive(ShadowOn);
        if (!ShadowOn) { HideCandidates(); ShowVideo(false); }
    }

    private static GameObject NewRow(Transform parent, string name, float height)
    {
        var row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;
        row.AddComponent<LayoutElement>().preferredHeight = height;
        return row;
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

    private static string Escape(string s) => (s ?? "").Replace("<", "<​");

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
