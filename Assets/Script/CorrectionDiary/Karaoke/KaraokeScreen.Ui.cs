using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// KaraokeScreen の続き: 画面を作り (シーンは変えない)、Modes のシートから開けるようにする。表示は Render でまとめて変える。
/// </summary>
public partial class KaraokeScreen
{
    public const string ScreenName = "karaoke screen";
    public const string TabName = "Karaoke";
    private const string MissColor = "#F87171";   // UiTheme.Danger

    private TMP_FontAsset _font;
    private TMP_InputField _input;
    private TMP_Text _statusText, _prevText, _currentText, _nextText, _offsetText, _mainLabel;
    private GameObject _lyricsCard, _offsetRow, _searchRow;
    private RectTransform _videoArea;
    private Transform _list;
    private Button _mainButton, _newSongButton;
    private string _status = "Type a song title and tap Search.";
    private State _listShownFor = State.Idle;
    private List<Button> _otherTabs = new List<Button>();

    /// <summary>画面と隠れたタブを作る (ModesNav が下のタブを作る前に 1 回)</summary>
    public static void Install()
    {
        if (UiStyle.Find(ScreenName) != null) return;
        var someTab = Object.FindObjectsOfType<Button>(true).FirstOrDefault(b => b.name == "Dairy" && b.transform.parent != null
                                                                                 && b.transform.parent.name == "Panel");
        if (someTab == null) { Debug.LogWarning("[Karaoke] the tab row was not found. The Karaoke mode was not added."); return; }
        Transform panel = someTab.transform.parent;
        TMP_FontAsset font = someTab.GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;

        var go = new GameObject(ScreenName);
        go.SetActive(false);
        var screen = go.AddComponent<KaraokeScreen>();
        screen.Build(panel.parent.GetComponent<CanvasScaler>(), font);

        var tab = Object.Instantiate(someTab.gameObject, panel, false);
        tab.name = TabName;
        var button = tab.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(screen.Open);
        screen._otherTabs = panel.GetComponentsInChildren<Button>(true).Where(b => b != button).ToList();
        foreach (var other in screen._otherTabs) other.onClick.AddListener(() => go.SetActive(false));
    }

    /// <summary>ほかの画面を閉じて、この画面を出す</summary>
    public void Open()
    {
        foreach (var tab in _otherTabs)
            for (int i = 0; i < tab.onClick.GetPersistentEventCount(); i++)
                if (tab.onClick.GetPersistentTarget(i) is GameObject target && ModesNav.IsScreenName(target.name))
                    target.SetActive(false);
        var answer = UiStyle.Find(ShortAnswerScreen.ScreenName);   // 先に作った画面は、このタブを知らないので閉じておく
        if (answer != null) answer.SetActive(false);
        gameObject.SetActive(true);
    }

    private void Build(CanvasScaler scalerFrom, TMP_FontAsset font)
    {
        _font = font;
        var canvasGo = UiStyle.NewUi("Canvas", transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        ModesNav.CopyScaler(scalerFrom, canvasGo.AddComponent<CanvasScaler>());
        canvasGo.AddComponent<GraphicRaycaster>();
        var bg = UiStyle.NewUi("Background", canvasGo.transform);
        UiStyle.Stretch(bg.transform);
        bg.AddComponent<Image>().color = UiTheme.Background;

        var root = UiStyle.ScreenRoot(canvasGo.transform).transform;
        UiStyle.TopRow(root, font, "Karaoke", null);

        // 曲名の入力
        _searchRow = UiStyle.NewUi("SearchRow", root);
        UiStyle.Size(_searchRow, UiTheme.MinTouch);
        UiStyle.Row(_searchRow, UiTheme.Gap);
        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "SongInput";
        inputGo.transform.SetParent(_searchRow.transform, false);
        UiStyle.Size(inputGo, -1, 0, -1, 1);
        UiStyle.Surface(inputGo, UiTheme.Surface, outline: true);
        _input = inputGo.GetComponent<TMP_InputField>();
        _input.lineType = TMP_InputField.LineType.SingleLine;
        foreach (var t in inputGo.GetComponentsInChildren<TMP_Text>(true))
        {
            t.font = font;
            t.fontSize = UiTheme.BodySize;
            t.color = t == _input.placeholder ? UiTheme.SubText : UiTheme.Text;
        }
        if (_input.placeholder is TMP_Text ph) ph.text = "Song title (and artist)";
        _input.onSubmit.AddListener(_ => OnSearch());

        _statusText = UiStyle.NewText("Status", root, font, "", UiTheme.CaptionSize, UiTheme.SubText);
        _statusText.richText = false;

        // 動画 (歌っている間だけ)
        var video = UiStyle.NewUi("VideoArea", root);
        UiStyle.Size(video, 520);
        _videoArea = (RectTransform)video.transform;

        // 歌詞: 前の行 (薄く)・今の行 (大きく)・次の行
        _lyricsCard = UiStyle.NewUi("Lyrics", root);
        UiStyle.Surface(_lyricsCard, UiTheme.Surface, outline: true);
        UiStyle.Column(_lyricsCard, 32, 12);
        _prevText = UiStyle.NewText("Prev", _lyricsCard.transform, font, "", UiTheme.CaptionSize, UiTheme.SubText);
        _currentText = UiStyle.NewText("Current", _lyricsCard.transform, font, "", UiTheme.HeadingSize, UiTheme.Primary, FontStyles.Bold);
        _nextText = UiStyle.NewText("Next", _lyricsCard.transform, font, "", UiTheme.BodySize, UiTheme.SubText);

        // 歌詞のずれを直す
        _offsetRow = UiStyle.NewUi("OffsetRow", root);
        UiStyle.Size(_offsetRow, UiTheme.MinTouch);
        UiStyle.Row(_offsetRow, UiTheme.Gap);
        NewButton("Earlier5", _offsetRow.transform, "−5s", false, 1).onClick.AddListener(() => Nudge(-5));
        NewButton("Earlier", _offsetRow.transform, "−0.5s", false, 1).onClick.AddListener(() => Nudge(-0.5));
        _offsetText = UiStyle.NewText("Offset", _offsetRow.transform, font, "", UiTheme.CaptionSize, UiTheme.SubText);
        _offsetText.alignment = TextAlignmentOptions.Center;
        UiStyle.Size(_offsetText.gameObject, -1, 0, -1, 2);
        NewButton("Later", _offsetRow.transform, "+0.5s", false, 1).onClick.AddListener(() => Nudge(0.5));
        NewButton("Later5", _offsetRow.transform, "+5s", false, 1).onClick.AddListener(() => Nudge(5));

        // 候補の動画・結果の一覧
        _list = UiStyle.Scroll(root, 1f);

        var buttons = UiStyle.NewUi("ButtonRow", root);
        UiStyle.Size(buttons, UiTheme.ButtonHeight);
        UiStyle.Row(buttons, UiTheme.Gap).childForceExpandWidth = true;
        _newSongButton = NewButton("NewSongButton", buttons.transform, "New song", false, 1);
        _newSongButton.onClick.AddListener(OnNewSong);
        _mainButton = NewButton("MainButton", buttons.transform, "Search", true, 2);
        _mainButton.onClick.AddListener(OnMain);
        _mainLabel = _mainButton.GetComponentInChildren<TMP_Text>(true);
    }

    private void OnMain()
    {
        switch (_state)
        {
            case State.Idle: OnSearch(); break;
            case State.Ready: OnStart(); break;
            case State.WaitingToPlay:
            case State.Singing: OnFinish(); break;
            case State.Result:
                if (_pendingWavs != null) OnCheckAgain();
                else { _state = State.Ready; _status = "Tap Start to sing again."; Render(); }
                break;
        }
    }

    private void Render()
    {
        if (_mainButton == null) return;
        bool singing = _state == State.WaitingToPlay || _state == State.Singing;
        _statusText.text = _status ?? "";
        _searchRow.SetActive(_state == State.Idle || _state == State.Searching || _state == State.Choosing);
        _videoArea.gameObject.SetActive(singing);
        _lyricsCard.SetActive(_state == State.Ready || singing);
        _offsetRow.SetActive(_state == State.Ready || singing);
        _offsetText.text = "Timing " + (_offset >= 0 ? "+" : "") + _offset.ToString("0.0") + "s";
        _list.parent.gameObject.SetActive(_state == State.Choosing || _state == State.Result);

        _mainButton.gameObject.SetActive(_state != State.Choosing);
        _mainButton.interactable = _state != State.Searching && _state != State.Checking;
        _mainLabel.text =
            _state == State.Searching ? "Searching..." :
            _state == State.Ready ? "Start" :
            _state == State.WaitingToPlay ? "Cancel" :
            _state == State.Singing ? "Finish & check" :
            _state == State.Checking ? "Checking..." :
            _state == State.Result ? (_pendingWavs != null ? "Check again" : "Sing again") : "Search";
        _newSongButton.gameObject.SetActive(_state != State.Idle && _state != State.Searching);
        _newSongButton.interactable = !IsBusy;

        if (_state == State.Ready) RenderPreview();
        if (_listShownFor != _state || _state == State.Result) RenderList();
    }

    private void RenderPreview()
    {
        _prevText.text = _song == null ? "" : _song.Title + (string.IsNullOrEmpty(_song.Artist) ? "" : " / " + _song.Artist);
        _currentText.text = "♪";
        _nextText.text = _lines.FirstOrDefault(l => !l.IsBlank)?.Text ?? "";
    }

    private void RenderLyrics()
    {
        int i = LrcLyrics.IndexAt(_lines, VideoTime, _offset);
        string Line(int k) => k < 0 || k >= _lines.Count ? "" : (_lines[k].IsBlank ? "♪" : _lines[k].Text);
        _prevText.text = Line(i - 1);
        _currentText.text = i < 0 ? "♪" : Line(i);
        _nextText.text = Line(i + 1);
    }

    private void RenderList()
    {
        _listShownFor = _state;
        foreach (Transform child in _list) Destroy(child.gameObject);
        if (_state == State.Choosing)
        {
            foreach (var v in _videos)
            {
                var video = v;
                NewRow(v.title + "\n<size=80%><color=#9CA3AF>" + YouTubeSearchClient.FormatDuration(v.durationSeconds) + "  ·  " + v.channel + "</color></size>")
                    .onClick.AddListener(() => Choose(video));
            }
        }
        else if (_state == State.Result && _result != null)
        {
            var head = UiStyle.NewText("Score", _list, _font, "", UiTheme.BodySize, UiTheme.Text);
            if (_result.Check == null)
            {
                head.text = "Not checked yet.";
                return;
            }
            head.text = "<b>Score: " + _result.Score + " / 100</b>\n<size=80%><color=#9CA3AF>Checked lines 1–" + _result.Sung.Count +
                        " of " + _result.TotalLines + ". Words in red were hard to hear. Tap a line to hear your voice.</color></size>" +
                        (string.IsNullOrWhiteSpace(_result.Check.Advice) ? "" : "\n\n" + _result.Check.Advice);
            var missed = _result.Check.Lines.ToDictionary(l => l.Line, l => l.Missed);
            foreach (var line in _result.Sung)
            {
                int index = _lines.IndexOf(line);
                string text = missed.TryGetValue(index, out var m) ? KaraokeScorer.Highlight(line.Text, m, MissColor) : line.Text;
                NewRow(text).onClick.AddListener(() => PlayMyVoice(index));
            }
        }
    }

    // 一覧の 1 行 (押せる。文が長ければ折り返して高くなる)
    private Button NewRow(string text)
    {
        var go = UiStyle.NewUi("Row", _list);
        var button = go.AddComponent<Button>();
        UiStyle.Secondary(button);
        var column = UiStyle.Column(go, 24, 0);
        column.childControlHeight = true;
        column.childForceExpandHeight = false;
        var le = UiStyle.Size(go);
        le.minHeight = UiTheme.MinTouch;
        var label = UiStyle.NewText("Label", go.transform, _font, text, UiTheme.BodySize, UiTheme.Text);
        label.alignment = TextAlignmentOptions.Left;
        label.richText = true;
        return button;
    }

    private Button NewButton(string name, Transform parent, string text, bool primary, float flex)
    {
        var go = UiStyle.NewUi(name, parent);
        var button = go.AddComponent<Button>();
        UiStyle.Size(go, -1, 0, -1, flex);
        var label = UiStyle.NewText("Label", go.transform, _font, text, UiTheme.BodySize, UiTheme.Text, FontStyles.Bold);
        UiStyle.Stretch(label.transform);
        if (primary) UiStyle.Primary(button); else UiStyle.Secondary(button);
        return button;
    }
}
