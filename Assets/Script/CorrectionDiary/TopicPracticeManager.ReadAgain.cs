using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TopicPracticeManager の続き: 言い直しで 1 文読んだあと、「Next」の横に「Again」を出し、同じ文をもう一度読めるようにする。
/// ボタンは Read aloud のボタンを複製して作る (シーンは変えない)。もう一度読んだら、その文の一致率は新しい方にする。
/// </summary>
public partial class TopicPracticeManager
{
    private Button _readAgainButton;

    private void EnsureReadAgainButton()
    {
        if (_readAgainButton != null || readAloudButton == null) return;
        var go = Instantiate(readAloudButton.gameObject, readAloudButton.transform.parent, false);
        go.name = "ReadAgainButton";
        go.transform.SetSiblingIndex(readAloudButton.transform.GetSiblingIndex());   // Next の前 (左) に置く
        _readAgainButton = go.GetComponent<Button>();
        _readAgainButton.onClick = new Button.ButtonClickedEvent();   // 複製元の Read aloud の動きは外す
        _readAgainButton.onClick.AddListener(OnReadAgainClicked);
        UiStyle.Secondary(_readAgainButton);
        var label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "Again";
        go.SetActive(false);
    }

    /// <summary>1 文読み終わって、まだ次へ進んでいないときだけ出す (RefreshReadAloud から)</summary>
    private void RefreshReadAgainButton()
    {
        bool show = _readSentences.Length > 0 && !ReadAloudFinished && !_reading && !string.IsNullOrEmpty(_lastReadResult);
        if (show) EnsureReadAgainButton();
        if (_readAgainButton == null) return;
        _readAgainButton.gameObject.SetActive(show);
        _readAgainButton.interactable = !_busy && !_recordingPron;
    }

    // 同じ文をもう一度読む: さっきの一致率は取り消して、すぐ聞き始める
    private async void OnReadAgainClicked()
    {
        if (_busy || _state != State.Idle || _recordingPron || _reading || ReadAloudFinished) return;
        if (string.IsNullOrEmpty(_lastReadResult)) return;

        if (_readScores.Count > 0) _readScores.RemoveAt(_readScores.Count - 1);
        _lastReadResult = "";
        _lastPronComment = "";
        RefreshReadAloud();
        if (!await StartListeningAsync()) return;
        _reading = true;
        SetStatus("Read the sentence aloud again, then tap Stop.");
        RefreshReadAloud();
    }
}
