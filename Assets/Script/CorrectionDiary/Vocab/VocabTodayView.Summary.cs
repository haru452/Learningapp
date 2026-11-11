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
        if (first) _asking.Clear();
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

        _ctx.SetStatus(_recs.Count > 0 ? "Done. Tap \"Ask Gemini\" on a word for advice." : "Done. Tap \"Start\" for more words.");
        _ = CountStudyDayAsync();
    }

    private void BuildRows()
    {
        VocabContext.ClearChildren(_v.SummaryContent, _v.SummaryRowTemplate);
        _rows.Clear();
        _askButtons.Clear();
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
            var ask = CreateAskButton(model);
            if (model != null) model.onClick.AddListener(() => PlayModel(card));
            if (ask != null)
            {
                ask.onClick.AddListener(() => _ = AskGeminiAsync(card.Word));
                _askButtons[c.Word] = ask;
            }
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
            else sb.Append("<color=#aaaaaa>-</color>");
            if (_asking.Contains(c.Word)) sb.Append("\n<color=#aaaaaa>Asking Gemini...</color>");
        }
        sb.Append("</size>");
        text.text = sb.ToString();
        if (_askButtons.TryGetValue(c.Word, out Button ask) && ask != null)
        {
            ask.gameObject.SetActive(_recs.ContainsKey(c.Word) && !_asking.Contains(c.Word));
            var label = ask.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = _gemini.ContainsKey(c.Word) ? "Ask again" : "Ask Gemini";
        }
    }

    // ------------------------------------------------------------
    // 1 語ずつ Gemini に聞く (まとめての採点はしない)。メモリに残っている録音を送る。何度でも聞き直せる
    // ------------------------------------------------------------
    private const string AskGeminiName = "AskGemini";
    private readonly Dictionary<string, Button> _askButtons = new Dictionary<string, Button>();
    private readonly HashSet<string> _asking = new HashSet<string>();   // いま Gemini に聞いている単語

    // 行の Model ボタンを複製して「Ask Gemini」ボタンを作る (シーンを作り直さなくても出るように、実行時に作る)
    private static Button CreateAskButton(Button model)
    {
        if (model == null) return null;
        var go = UnityEngine.Object.Instantiate(model.gameObject, model.transform.parent);
        go.name = AskGeminiName;
        var button = go.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        var label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = "Ask Gemini";
        go.SetActive(false);
        return button;
    }

    private async Task AskGeminiAsync(string word)
    {
        if (_asking.Contains(word) || !_recs.TryGetValue(word, out byte[] wav)) return;
        if (_ctx.Gemini == null) { _ctx.SetStatus("No Gemini key, so no pronunciation advice."); return; }
        int token = _token;
        var card = _session?.Cards.FirstOrDefault(c => c.Word == word);
        _asking.Add(word);
        if (card != null) UpdateRow(card);
        bool first = !_gemini.ContainsKey(word);
        _ctx.SetStatus("Asking Gemini about \"" + word + "\"...");
        try
        {
            var results = await _ctx.Gemini.CheckWordPronunciationsAsync(new[] { word }, new[] { wav });
            if (token != _token) return;
            if (results.Count == 0) throw new Exception("Gemini did not return the word");
            foreach (var r in results) ApplyResult(r);
            _ctx.SaveDeck();
            VocabStore.DeleteRecording(word);   // 端末のファイルは消す (聞き直し・My voice 用にメモリには残す)
            if (first) _ = SavePronLogsAsync(results);   // Web の苦手な発音は 1 回目だけ (聞き直しで二重に数えない)
            _ctx.SetStatus("Gemini checked \"" + word + "\".");
        }
        catch (Exception e)
        {
            if (token != _token) return;
            Debug.LogWarning("[Vocab] asking Gemini failed: " + e.Message);
            _ctx.SetStatus("Could not ask Gemini. Try again in a moment.");
        }
        finally
        {
            _asking.Remove(word);
            if (token == _token && _mode == Mode.Summary)
            {
                if (card != null) UpdateRow(card);
                RefreshSummaryButtons();   // 判定が変わると「苦手な単語の練習」に入る単語も変わる
            }
        }
    }

    // Gemini の 1 語分の判定を、この回の結果と単語帳に入れる
    private void ApplyResult(GeminiApiClient.WordPronResult r)
    {
        // 端末の音声認識が単語どおりに聞き取れたなら、L と R は言い分けられている (Gemini の思い込みを出さない)
        if (_pron.TryGetValue(r.Word, out string heard) && heard == VocabPronJudge.Clear) r.Sounds.Remove("r_l");
        _gemini[r.Word] = r;
        var card = _ctx.Deck.Find(r.Word);
        if (card != null) card.GeminiPron = r.Rating;
    }

    // 苦手な音 (th・R と L など) があった単語を 1 回ずつクラウドに残す (失敗は Console だけ)
    private async Task SavePronLogsAsync(List<GeminiApiClient.WordPronResult> results)
    {
        var supabase = _ctx.Supabase;
        if (supabase == null || !supabase.IsConfigured) return;
        try
        {
            if (!await supabase.EnsureSignedInAsync()) return;
            await supabase.SaveVocabPronLogsAsync(results, new Dictionary<string, string>(_heard), DateTime.UtcNow);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] could not save the pronunciation logs: " + e.Message);
        }
    }

    // その日にもう数えた (端末・クラウド)。クラウドは送れたときだけ印を付ける (失敗したら次の評価でもう一度)
    private static DateTime _localCountedOn = DateTime.MinValue;
    private static DateTime _cloudCountedOn = DateTime.MinValue;
    private static bool _cloudSending;

    // 連続記録: 端末の連続記録とリマインダー + ログイン中ならクラウド (失敗は Console だけ)。
    // 1 枚目を評価したとき・まとめのときに呼ぶ。同じ日は 1 回だけ
    private async Task CountStudyDayAsync()
    {
        DateTime today = DateTime.Now.Date;
        var diary = UnityEngine.Object.FindObjectOfType<DiaryManager>(true);
        if (_localCountedOn != today)
        {
            _localCountedOn = today;
            if (diary != null) diary.CountOtherPracticeAsStudyDay();
        }
        var supabase = _ctx.Supabase;
        if (supabase == null || !supabase.IsConfigured) return;
        if (_cloudCountedOn == today || _cloudSending) return;
        _cloudSending = true;
        try
        {
            if (!await supabase.EnsureSignedInAsync()) return;
            await supabase.CompleteVocabSessionAsync(DateTime.UtcNow);
            _cloudCountedOn = today;
            if (diary != null) diary.RefreshStreakFromCloud();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] could not count the study day in the cloud: " + e.Message);
        }
        finally { _cloudSending = false; }
    }

    private void RefreshSummaryButtons()
    {
        bool summary = _mode == Mode.Summary;
        VocabContext.Show(_v.RescoreButton, false);   // まとめての採点はやめた (1 語ずつ Ask Gemini)
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
