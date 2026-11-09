using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Today の後半: まとめ (発音の採点・連続記録・レベルアップ) と、苦手な単語だけの練習。VocabTodayView.cs の続き</summary>
public partial class VocabTodayView
{
    // ------------------------------------------------------------
    // まとめ
    // ------------------------------------------------------------
    private void ShowSummary(bool first)
    {
        SetMode(Mode.Summary);
        SetActive(_v.CardArea, false);
        SetActive(_v.RatingRow, false);
        VocabContext.Show(_v.RatingHint, false);
        SetActive(_v.SummaryScroll, true);
        VocabContext.Show(_v.StartButton, true);
        if (_v.StartLabel != null) _v.StartLabel.text = "Start";
        SetProgress("Summary");
        if (first) BuildRows();
        RefreshSummaryButtons();
        if (!first) { _ctx.SetStatus("Tap \"Start\" for more words."); return; }

        _ctx.SetStatus("Done. Tap \"Start\" for more words.");
        _ = CountStudyDayAsync();
        if (_recs.Count > 0) _ = ScoreAsync();
    }

    private void BuildRows()
    {
        VocabContext.ClearChildren(_v.SummaryContent, _v.SummaryRowTemplate);
        _rows.Clear();
        if (_v.SummaryRowTemplate == null || _session == null) return;
        foreach (var c in _session.Cards)
        {
            var row = VocabContext.CloneRow(_v.SummaryRowTemplate, _v.SummaryContent);
            var textT = row.transform.Find(RowTextName);
            TMP_Text text = textT != null ? textT.GetComponent<TMP_Text>() : null;
            if (text == null) text = row.GetComponentInChildren<TMP_Text>(true);
            _rows[c.Word] = text;
            var card = c;
            var myVoiceT = row.transform.Find(MyVoicePath);
            var myVoice = myVoiceT != null ? myVoiceT.GetComponent<Button>() : null;
            if (myVoice != null)
            {
                myVoice.gameObject.SetActive(_recs.ContainsKey(c.Word));
                myVoice.onClick.AddListener(() => { if (_recs.TryGetValue(card.Word, out byte[] w)) _ = PlaySafeAsync(() => _ctx.Audio.PlayWavAsync(w, CancellationToken.None)); });
            }
            var modelT = row.transform.Find(ModelPath);
            var model = modelT != null ? modelT.GetComponent<Button>() : null;
            if (model != null) model.onClick.AddListener(() => PlayModel(card));
            UpdateRow(c);
        }
    }

    private void UpdateRow(VocabCard c)
    {
        if (!_rows.TryGetValue(c.Word, out TMP_Text text) || text == null) return;
        var sb = new StringBuilder();
        sb.Append("<b>").Append(VocabContext.Esc(c.Word)).Append("</b>  <color=#aaaaaa>").Append(c.Cefr).Append("</color>");
        sb.Append("\n<size=80%>Say: ").Append(_pron.TryGetValue(c.Word, out string p) ? VocabContext.PronLabel(p) : "<color=#aaaaaa>-</color>");
        if (_recs.ContainsKey(c.Word) || _gemini.ContainsKey(c.Word))
        {
            sb.Append("   Gemini: ");
            if (_gemini.TryGetValue(c.Word, out var g))
            {
                sb.Append(VocabContext.GeminiLabel(g.Rating));
                if (g.Advice.Length > 0) sb.Append('\n').Append(VocabContext.Esc(g.Advice));
                if (g.Sounds.Count > 0) sb.Append("\nSounds: ").Append(string.Join(", ", g.Sounds.Select(s => SoundTypes.Label(s, "other"))));
            }
            else sb.Append(_scoring ? "<color=#aaaaaa>...</color>" : "<color=#aaaaaa>-</color>");
        }
        sb.Append("</size>");
        text.text = sb.ToString();
    }

    // 1 回分の録音をまとめて Gemini に送る。失敗したら [Score again]
    private async Task ScoreAsync()
    {
        if (_scoring || _session == null) return;
        var words = _recs.Keys.Where(w => !_gemini.ContainsKey(w)).Take(GeminiApiClient.MaxVocabWords).ToList();
        if (words.Count == 0) return;
        if (_ctx.Gemini == null) { _ctx.SetStatus("No Gemini key, so no pronunciation advice."); return; }
        int token = _token;
        _scoring = true;
        _geminiFailed = false;
        RefreshSummaryButtons();
        foreach (var c in _session.Cards) UpdateRow(c);
        _ctx.SetStatus("Checking your pronunciation...");
        try
        {
            var results = await _ctx.Gemini.CheckWordPronunciationsAsync(words, words.Select(w => _recs[w]).ToList());
            if (token != _token) return;
            foreach (var r in results)
            {
                _gemini[r.Word] = r;
                var card = _ctx.Deck.Find(r.Word);
                if (card != null) card.GeminiPron = r.Rating;
                VocabStore.DeleteRecording(r.Word);   // 採点が済んだ録音は消す (My voice 用にメモリには残す)
            }
            _ctx.SaveDeck();
            // Gemini が返さなかった単語があれば、[Score again] でその単語だけもう一度
            int missing = words.Count(w => !_gemini.ContainsKey(w));
            _geminiFailed = missing > 0;
            _ctx.SetStatus(missing > 0
                ? missing + (missing == 1 ? " word was" : " words were") + " not checked. Tap \"Score again\"."
                : "Done. Tap \"Start\" for more words.");
        }
        catch (Exception e)
        {
            if (token != _token) return;
            Debug.LogWarning("[Vocab] Gemini pronunciation check failed: " + e.Message);
            _geminiFailed = true;
            _ctx.SetStatus("Could not check the pronunciation. Tap \"Score again\".");
        }
        finally
        {
            _scoring = false;   // 回が変わっていても必ず戻す (戻さないと次の回の採点が始まらない)
            if (token == _token)
            {
                if (_mode == Mode.Summary)
                {
                    foreach (var c in _session.Cards) UpdateRow(c);
                    RefreshSummaryButtons();
                }
            }
        }
    }

    // 連続記録: 端末の連続記録とリマインダー + ログイン中ならクラウド (失敗は Console だけ)
    private async Task CountStudyDayAsync()
    {
        var diary = UnityEngine.Object.FindObjectOfType<DiaryManager>(true);
        if (diary != null) diary.CountOtherPracticeAsStudyDay();
        var supabase = _ctx.Supabase;
        if (supabase == null || !supabase.IsConfigured) return;
        try
        {
            if (!await supabase.EnsureSignedInAsync()) return;
            await supabase.CompleteVocabSessionAsync(DateTime.UtcNow);
            if (diary != null) diary.RefreshStreakFromCloud();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] could not count the study day in the cloud: " + e.Message);
        }
    }

    private void RefreshSummaryButtons()
    {
        bool summary = _mode == Mode.Summary;
        VocabContext.Show(_v.RescoreButton, summary && _geminiFailed && !_scoring);
        _weak = _session == null ? new List<VocabCard>() : _session.Cards.Where(c =>
            (_pron.TryGetValue(c.Word, out string p) && p != VocabPronJudge.Clear)
            || (_gemini.TryGetValue(c.Word, out var g) && g.Rating == "practice")).ToList();
        VocabContext.Show(_v.WeakButton, summary && _weak.Count > 0);

        string next = VocabLevel.Next(VocabSettings.Level);
        bool suggest = summary && next != null && VocabLevel.ShouldSuggestUp(VocabStore.LoadReviews(), VocabSettings.LevelSetAt);
        VocabContext.Show(_v.LevelUpButton, suggest);
        if (suggest && _v.LevelUpLabel != null) _v.LevelUpLabel.text = "Level up to " + next + "?";
    }

    private void HideSummaryButtons()
    {
        VocabContext.Show(_v.RescoreButton, false);
        VocabContext.Show(_v.WeakButton, false);
        VocabContext.Show(_v.LevelUpButton, false);
    }

    private void LevelUp()
    {
        string next = VocabLevel.Next(VocabSettings.Level);
        if (next == null) return;
        VocabSettings.Level = next;
        VocabContext.Show(_v.LevelUpButton, false);
        _ctx.SetStatus("Level set to " + next + ". New words come from " + next + ".");
    }

    // ------------------------------------------------------------
    // 苦手な単語の練習 (発音だけ。評価ボタンなし、Gemini は呼ばない)
    // ------------------------------------------------------------
    private void StartWeak()
    {
        if (_weak.Count == 0) return;
        SetMode(Mode.Weak);
        _weakIndex = 0;
        _weakCard = _weak[0];
        ShowCard();
    }

    private void NextWeak()
    {
        _ctx.Capture.Cancel();
        _weakIndex++;
        if (_weakIndex >= _weak.Count)
        {
            _weakCard = null;
            ShowSummary(first: false);
            foreach (var c in _session.Cards) UpdateRow(c);
            return;
        }
        _weakCard = _weak[_weakIndex];
        ShowCard();
    }
}
