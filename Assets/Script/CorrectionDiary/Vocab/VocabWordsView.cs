using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Words: 学習中 / わかった の一覧とレベルの絞り込み。行をタップ → Back to learning / Delete (確認つき)</summary>
public class VocabWordsView
{
    public struct View
    {
        public Button LearningToggle, KnownToggle;
        public Button[] FilterButtons;     // All A1 A2 B1 B2 —
        public Transform Content;
        public GameObject RowTemplate;
        public GameObject DialogPanel;
        public TMP_Text DialogText;
        public Button[] DialogButtons;
        public TMP_Text[] DialogLabels;
    }

    private static readonly string[] Filters = { VocabDeck.FilterAll, "A1", "A2", "B1", "B2", CefrWordList.None };

    private readonly VocabContext _ctx;
    private readonly View _v;
    private string _status = VocabCard.Learning;
    private string _filter = VocabDeck.FilterAll;

    public VocabWordsView(VocabContext ctx, View v)
    {
        _ctx = ctx;
        _v = v;
        if (_v.RowTemplate != null) _v.RowTemplate.SetActive(false);
        if (_v.LearningToggle != null) _v.LearningToggle.onClick.AddListener(() => { _status = VocabCard.Learning; Refresh(); });
        if (_v.KnownToggle != null) _v.KnownToggle.onClick.AddListener(() => { _status = VocabCard.Known; Refresh(); });
        for (int i = 0; i < (_v.FilterButtons?.Length ?? 0) && i < Filters.Length; i++)
        {
            string f = Filters[i];
            VocabContext.SetLabel(_v.FilterButtons[i], f == VocabDeck.FilterAll ? "All" : f);
            _v.FilterButtons[i].onClick.AddListener(() => { _filter = f; Refresh(); });
        }
        CloseDialog();
    }

    public void Show()
    {
        CloseDialog();
        Refresh();
    }

    private void Refresh()
    {
        _ctx.Highlight(_v.LearningToggle, _status == VocabCard.Learning);
        _ctx.Highlight(_v.KnownToggle, _status == VocabCard.Known);
        for (int i = 0; i < (_v.FilterButtons?.Length ?? 0) && i < Filters.Length; i++)
            _ctx.Highlight(_v.FilterButtons[i], Filters[i] == _filter);

        VocabContext.ClearChildren(_v.Content, _v.RowTemplate);
        if (_v.RowTemplate == null) return;
        var cards = _ctx.Deck.List(_status, _filter);
        foreach (var c in cards)
        {
            var row = VocabContext.CloneRow(_v.RowTemplate, _v.Content);
            var text = row.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = RowText(c);
            var button = row.GetComponent<Button>();
            var card = c;
            if (button != null) button.onClick.AddListener(() => OpenActions(card));
        }
        _ctx.SetStatus(cards.Count == 0
            ? (_status == VocabCard.Learning ? "No words in Learning." : "No known words yet.")
            : cards.Count + (cards.Count == 1 ? " word" : " words"));
    }

    private static string RowText(VocabCard c)
    {
        string line2 = c.Status == VocabCard.Known && c.DueAt.HasValue
            ? "Next: " + c.DueAt.Value.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture)
            : "Learning";
        line2 += "   Missed: " + c.MissCount;
        if (!string.IsNullOrEmpty(c.PronBest)) line2 += "   Best: " + VocabContext.PronLabel(c.PronBest);
        return "<b>" + VocabContext.Esc(c.Word) + "</b>  <color=#aaaaaa>" + c.Cefr + "</color>\n<size=75%>" + line2 + "</size>";
    }

    // 行の操作: [Back to learning] (わかったのときだけ) [Delete] [Close]
    private void OpenActions(VocabCard c)
    {
        bool known = c.Status == VocabCard.Known;
        OpenDialog("<b>" + VocabContext.Esc(c.Word) + "</b>",
            known ? ("Back to learning", (Action)(() => { _ctx.Deck.MoveToLearning(c.Word); _ctx.SaveDeck(); CloseDialog(); Refresh(); _ctx.SetStatus("Moved \"" + c.Word + "\" to Learning."); })) : (null, null),
            ("Delete", () => ConfirmDelete(c)),
            ("Close", CloseDialog));
    }

    private void ConfirmDelete(VocabCard c)
    {
        OpenDialog("Delete \"" + VocabContext.Esc(c.Word) + "\"?",
            ("Delete", () => { _ctx.Deck.Remove(c.Word); _ctx.SaveDeck(); CloseDialog(); Refresh(); _ctx.SetStatus("Deleted \"" + c.Word + "\"."); }),
            ("Cancel", CloseDialog),
            (null, null));
    }

    private void OpenDialog(string message, params (string label, Action action)[] buttons)
    {
        if (_v.DialogPanel == null) return;
        _v.DialogPanel.SetActive(true);
        if (_v.DialogText != null) _v.DialogText.text = message;
        for (int i = 0; i < (_v.DialogButtons?.Length ?? 0); i++)
        {
            var b = _v.DialogButtons[i];
            b.onClick.RemoveAllListeners();
            bool used = i < buttons.Length && buttons[i].label != null;
            b.gameObject.SetActive(used);
            if (!used) continue;
            var action = buttons[i].action;
            b.onClick.AddListener(() => action());
            if (_v.DialogLabels != null && i < _v.DialogLabels.Length && _v.DialogLabels[i] != null) _v.DialogLabels[i].text = buttons[i].label;
        }
    }

    private void CloseDialog()
    {
        if (_v.DialogPanel != null) _v.DialogPanel.SetActive(false);
    }
}
