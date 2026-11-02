using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Today: 今日の単語。Start → カード (タップでめくる) → 4 つの評価 → … → まとめ (Gemini の発音の採点・連続記録)。
/// 評価するたびに単語帳と履歴を保存する (途中でほかのタブへ移っても、評価した分は残る。録音は捨てる)
/// </summary>
public class VocabTodayView
{
    public struct View
    {
        public GameObject LevelPickRow;
        public Button[] LevelPickButtons;
        public Button StartButton;
        public TMP_Text StartLabel;
        public TMP_Text ProgressText;
        public GameObject CardArea;
        public Button CardButton;
        public TMP_Text CardText;
        public Button SoundButton;
        public Button SayButton;
        public TMP_Text SayLabel;
        public GameObject RatingRow;
        public Button[] RatingButtons;
        public TMP_Text[] RatingLabels;
        public TMP_Text RatingHint;
        public GameObject SummaryScroll;
        public Transform SummaryContent;
        public GameObject SummaryRowTemplate;
        public Button RescoreButton;
        public Button WeakButton;
        public Button LevelUpButton;
        public TMP_Text LevelUpLabel;
    }

    private enum Mode { Idle, Loading, Card, Summary, Weak }

    public const int MaxLookupFailures = 10;
    private static readonly string[] RatingNames = { "Didn't know", "Hard", "Knew it", "Easy" };
    private const string NoDictionaryNote = "Can't reach the dictionary, so today is review only.";

    private readonly VocabContext _ctx;
    private readonly View _v;
    private readonly System.Random _rng = new System.Random();

    private Mode _mode = Mode.Idle;
    private int _token;                     // Leave() で増やす (古い非同期の続きを捨てる)
    private VocabSession _session;
    private readonly Dictionary<string, DictionaryResult> _dicts = new Dictionary<string, DictionaryResult>();
    private readonly Dictionary<string, string> _pron = new Dictionary<string, string>();     // この回の最後の判定
    private readonly Dictionary<string, string> _heard = new Dictionary<string, string>();
    private readonly Dictionary<string, byte[]> _recs = new Dictionary<string, byte[]>();     // まとめを閉じるまでメモリに残す
    private readonly Dictionary<string, GeminiApiClient.WordPronResult> _gemini = new Dictionary<string, GeminiApiClient.WordPronResult>();
    private readonly Dictionary<string, TMP_Text> _rows = new Dictionary<string, TMP_Text>();
    private bool _flipped;
    private bool _finishing;
    private bool _scoring;
    private bool _geminiFailed;
    private string _note = "";
    private List<VocabCard> _weak = new List<VocabCard>();
    private int _weakIndex;
    private VocabCard _weakCard;

    public VocabTodayView(VocabContext ctx, View v)
    {
        _ctx = ctx;
        _v = v;
        for (int i = 0; i < (_v.LevelPickButtons?.Length ?? 0); i++)
        {
            string level = CefrWordList.Levels[i];
            VocabContext.SetLabel(_v.LevelPickButtons[i], level);
            _v.LevelPickButtons[i].onClick.AddListener(() => { VocabSettings.Level = level; ShowIdle(); });
        }
        if (_v.StartButton != null) _v.StartButton.onClick.AddListener(OnStartClicked);
        if (_v.CardButton != null) _v.CardButton.onClick.AddListener(() => { _flipped = !_flipped; Render(); });
        if (_v.SoundButton != null) _v.SoundButton.onClick.AddListener(() => PlayModel(CurrentCard));
        if (_v.SayButton != null) _v.SayButton.onClick.AddListener(OnSayClicked);
        for (int i = 0; i < (_v.RatingButtons?.Length ?? 0); i++)
        {
            int g = i + 1;
            _v.RatingButtons[i].onClick.AddListener(() => Rate(g));
        }
        if (_v.RatingHint != null) _v.RatingHint.text = "If you couldn't remember it, tap \"Didn't know\".";
        if (_v.RescoreButton != null) _v.RescoreButton.onClick.AddListener(() => _ = ScoreAsync());
        if (_v.WeakButton != null) _v.WeakButton.onClick.AddListener(StartWeak);
        if (_v.LevelUpButton != null) _v.LevelUpButton.onClick.AddListener(LevelUp);
        if (_v.SummaryRowTemplate != null) _v.SummaryRowTemplate.SetActive(false);
        _ctx.Capture.AutoEnded += () => { if (_ctx.Capture.IsActive) _ = FinishSayAsync(); };
    }

    private VocabCard CurrentCard => _mode == Mode.Weak ? _weakCard : _session?.Current;

    // ------------------------------------------------------------
    // 画面を開いた・離れた
    // ------------------------------------------------------------
    public void Show()
    {
        if (_mode == Mode.Idle) ShowIdle();
    }

    /// <summary>ほかのタブ・画面へ移った: 回は終わり。録音は捨てる</summary>
    public void Leave()
    {
        _token++;
        _ctx.Capture.Cancel();
        _ctx.Audio.Stop();
        _ctx.SetLoading(false);
        _recs.Clear();
        VocabStore.DeleteAllRecordings();
        _mode = Mode.Idle;
        _session = null;
    }

    private void ShowIdle()
    {
        _mode = Mode.Idle;
        bool hasLevel = VocabSettings.Level.Length > 0;
        if (_v.LevelPickRow != null) _v.LevelPickRow.SetActive(!hasLevel);
        VocabContext.Show(_v.StartButton, hasLevel);
        if (_v.StartLabel != null) _v.StartLabel.text = "Start";
        SetActive(_v.CardArea, false);
        SetActive(_v.RatingRow, false);
        VocabContext.Show(_v.RatingHint, false);
        SetActive(_v.SummaryScroll, false);
        HideSummaryButtons();
        if (!hasLevel)
        {
            SetProgress("Choose your level");
            _ctx.SetStatus("Pick the level of the new words. You can change it later in Settings.");
            return;
        }
        var now = DateTime.UtcNow;
        int learning = _ctx.Deck.Cards.Count(c => c.Status == VocabCard.Learning);
        int due = _ctx.Deck.Cards.Count(c => c.Status == VocabCard.Known && c.DueAt.HasValue && c.DueAt.Value <= now);
        SetProgress("Level " + VocabSettings.Level + "   Learning: " + learning + "   Due: " + due);
        _ctx.SetStatus("Tap \"Start\" for " + VocabSettings.SessionSize + " words.");
    }

    private void OnStartClicked()
    {
        if (_mode == Mode.Weak) { NextWeak(); return; }
        if (_mode == Mode.Idle || _mode == Mode.Summary) _ = StartSessionAsync();
    }

    // ------------------------------------------------------------
    // 回を始める: 学習中 → 期限が来た復習 → 新しい単語 (辞書で引けたものだけ)
    // ------------------------------------------------------------
    private async Task StartSessionAsync()
    {
        if (VocabSettings.Level.Length == 0) { ShowIdle(); return; }
        int token = ++_token;
        _mode = Mode.Loading;
        ResetSessionData();
        SetActive(_v.SummaryScroll, false);
        HideSummaryButtons();
        VocabContext.Show(_v.StartButton, false);
        _ctx.SetLoading(true);
        _ctx.SetStatus("Getting today's words...");

        var now = DateTime.UtcNow;
        var (cards, newCount) = VocabQueue.Plan(_ctx.Deck.Cards, now, VocabSettings.SessionSize);
        bool offline = false;
        bool hasKey = _ctx.Dictionary != null && _ctx.Dictionary.IsConfigured;

        // 前からある単語の辞書 (保存してあれば通信しない)
        foreach (var c in cards)
        {
            try { _dicts[c.Word] = await _ctx.LookupAsync(c.Word); }
            catch (Exception e) { offline = true; Debug.LogWarning("[Vocab] lookup failed for " + c.Word + ": " + e.Message); }
            if (token != _token) return;
        }

        // 新しい単語: CEFR-J の自分のレベルから、単語帳に無いものをランダムに。10 回続けて引けなければ止める
        int added = 0;
        bool noneLeft = false;
        if (newCount > 0 && hasKey)
        {
            var candidates = VocabQueue.NewCandidates(_ctx.Cefr, VocabSettings.Level, _ctx.Deck.Cards.Select(c => c.Word), _rng);
            noneLeft = candidates.Count == 0;
            int fails = 0;
            foreach (string w in candidates)
            {
                if (added >= newCount || fails >= MaxLookupFailures) break;
                DictionaryResult r = null;
                try { r = await _ctx.LookupAsync(w); }
                catch (Exception e) { offline = true; Debug.LogWarning("[Vocab] lookup failed for " + w + ": " + e.Message); }
                if (token != _token) return;
                if (r == null || !r.Found) { fails++; continue; }
                fails = 0;
                _dicts[w] = r;
                // 単語帳に入れるのは評価したとき (途中でやめたら入らない)
                cards.Add(new VocabCard { Word = VocabDeck.Key(w), Source = "auto", Cefr = _ctx.LevelFor(w, r), CreatedAt = now });
                added++;
            }
        }
        _ctx.SetLoading(false);

        _note = newCount > 0 && added == 0
            ? (!hasKey ? VocabContext.NoDictionaryKey : offline ? NoDictionaryNote : noneLeft ? "You have all " + VocabSettings.Level + " words. Try the next level in Settings." : "")
            : "";
        if (cards.Count == 0)
        {
            ShowIdle();
            _ctx.SetStatus(_note.Length > 0 ? _note : "No words yet. Add words or check the dictionary key.");
            return;
        }
        _session = new VocabSession(cards, VocabSettings.Retention / 100.0);
        _mode = Mode.Card;
        ShowCard();
    }

    private void ResetSessionData()
    {
        _session = null;
        _dicts.Clear(); _pron.Clear(); _heard.Clear(); _recs.Clear(); _gemini.Clear(); _rows.Clear();
        _geminiFailed = false;
        _note = "";
        _weak = new List<VocabCard>();
        VocabContext.ClearChildren(_v.SummaryContent, _v.SummaryRowTemplate);
    }

    // ------------------------------------------------------------
    // カード
    // ------------------------------------------------------------
    private void ShowCard()
    {
        _flipped = false;
        if (_v.LevelPickRow != null) _v.LevelPickRow.SetActive(false);
        SetActive(_v.CardArea, true);
        SetActive(_v.SummaryScroll, false);
        HideSummaryButtons();
        if (_mode == Mode.Weak)
        {
            VocabContext.Show(_v.StartButton, true);
            if (_v.StartLabel != null) _v.StartLabel.text = _weakIndex + 1 < _weak.Count ? "Next" : "Back to summary";
            SetProgress("Practice " + (_weakIndex + 1) + " / " + _weak.Count);
            _ctx.SetStatus("Tap \"Say it\" and say the word.");
        }
        else
        {
            VocabContext.Show(_v.StartButton, false);
            SetProgress("Word " + (_session.Index + 1) + " / " + _session.Count);
            _ctx.SetStatus(_note.Length > 0 ? _note : "Tap the card to see the meaning.");
        }
        if (_v.SayLabel != null) _v.SayLabel.text = "Say it";
        Render();
    }

    private void Render()
    {
        var c = CurrentCard;
        if (c == null) return;
        _dicts.TryGetValue(c.Word, out DictionaryResult dict);
        if (_v.CardText != null) _v.CardText.text = _flipped ? BackText(c, dict) : FrontText(c, dict);

        bool rating = _mode == Mode.Card && _flipped;
        SetActive(_v.RatingRow, rating);
        VocabContext.Show(_v.RatingHint, rating);
        if (rating && _session != null)
        {
            int[] days = _session.PreviewDays(DateTime.UtcNow);
            for (int i = 0; i < (_v.RatingLabels?.Length ?? 0) && i < 4; i++)
                if (_v.RatingLabels[i] != null)
                    _v.RatingLabels[i].text = RatingNames[i] + "\n<size=70%>" + Fsrs.IntervalLabel(days[i]) + "</size>";
        }
    }

    private string FrontText(VocabCard c, DictionaryResult dict)
    {
        var sb = new StringBuilder();
        sb.Append("<size=160%><b>").Append(VocabContext.Esc(c.Word)).Append("</b></size>\n");
        sb.Append("<color=#aaaaaa>").Append(c.Cefr).Append("</color>");
        string ipa = dict != null && dict.Found ? dict.Entries.Select(e => e.Ipa).FirstOrDefault(x => !string.IsNullOrEmpty(x)) : null;
        if (!string.IsNullOrEmpty(ipa)) sb.Append("   /").Append(VocabContext.Esc(ipa)).Append('/');
        if (_pron.TryGetValue(c.Word, out string verdict))
        {
            sb.Append("\n\n").Append(VocabContext.PronLabel(verdict));
            _heard.TryGetValue(c.Word, out string heard);
            sb.Append("\n<size=75%>Heard: \"").Append(VocabContext.Esc(heard ?? "")).Append("\"</size>");
        }
        sb.Append("\n\n<size=65%><color=#aaaaaa>Tap the card to ").Append(_mode == Mode.Weak ? "see the meaning" : "see the meaning and rate it").Append(".</color></size>");
        return sb.ToString();
    }

    private string BackText(VocabCard c, DictionaryResult dict)
    {
        var sb = new StringBuilder();
        sb.Append("<b>").Append(VocabContext.Esc(c.Word)).Append("</b>\n\n");
        if (dict != null && dict.Found) sb.Append(VocabAddView.DescribeEntries(dict, _ctx, c.Word)).Append('\n');
        else sb.Append("<color=#aaaaaa>No dictionary entry.</color>\n");
        if (!string.IsNullOrWhiteSpace(c.MyMeaning)) sb.Append("\n<b>My meaning:</b> ").Append(VocabContext.Esc(c.MyMeaning)).Append('\n');
        var examples = dict != null && dict.Found ? dict.Entries.SelectMany(e => e.Examples).Take(2).ToList() : new List<string>();
        if (examples.Count > 0)
        {
            sb.Append("\n<b>Examples</b>\n");
            foreach (var ex in examples) sb.Append("<size=85%><i>").Append(VocabContext.Esc(ex)).Append("</i></size>\n");
        }
        return sb.ToString().TrimEnd();
    }

    private void PlayModel(VocabCard c)
    {
        if (c == null) return;
        _dicts.TryGetValue(c.Word, out DictionaryResult dict);
        if (dict == null) dict = VocabStore.LoadDict(c.Word);
        string url = dict != null ? dict.Entries.Select(e => e.AudioUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? "" : "";
        _ = PlaySafeAsync(() => _ctx.Audio.PlayWordAsync(c.Word, url, CancellationToken.None));
    }

    private static async Task PlaySafeAsync(Func<Task> play)
    {
        try { await play(); }
        catch (Exception e) { Debug.LogWarning("[Vocab] could not play: " + e.Message); }
    }

    // ------------------------------------------------------------
    // 発音 (1 段階目)
    // ------------------------------------------------------------
    private void OnSayClicked()
    {
        if (CurrentCard == null || _finishing) return;
        if (_ctx.Capture.IsActive) { _ = FinishSayAsync(); return; }
        _ = StartSayAsync();
    }

    private async Task StartSayAsync()
    {
        _ctx.Audio.Stop();
        bool ok;
        try { ok = await _ctx.Capture.StartAsync(); }
        catch (Exception e) { Debug.LogWarning("[Vocab] could not start listening: " + e.Message); ok = false; }
        if (!ok) { _ctx.SetStatus("Could not use the microphone."); return; }
        if (_v.SayLabel != null) _v.SayLabel.text = "Stop";
        _ctx.SetStatus("Listening... say the word.");
    }

    private async Task FinishSayAsync()
    {
        if (_finishing) return;
        _finishing = true;
        int token = _token;
        var c = CurrentCard;
        if (_v.SayLabel != null) _v.SayLabel.text = "...";
        try
        {
            var (heard, wav) = await _ctx.Capture.StopAsync();
            if (token != _token || c == null || c != CurrentCard) return;
            string verdict = VocabPronJudge.Judge(c.Word, heard);
            _pron[c.Word] = verdict;
            _heard[c.Word] = heard;
            c.PronBest = VocabPronJudge.Better(c.PronBest, verdict);
            c.PronAttempts++;
            if (wav != null && _mode == Mode.Card)
            {
                _recs[c.Word] = wav;   // 単語ごとに最後の録音だけ
                try { System.IO.File.WriteAllBytes(VocabStore.RecPath(c.Word), wav); }
                catch (Exception e) { Debug.LogWarning("[Vocab] could not save the recording: " + e.Message); }
            }
            if (_ctx.Deck.Find(c.Word) != null) _ctx.SaveDeck();
            _ctx.SetStatus(verdict == VocabPronJudge.Clear ? "Nice." : "Tap \"Sound\" to hear it, then try again.");
            Render();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] pronunciation check failed: " + e.Message);
        }
        finally
        {
            _finishing = false;
            if (_v.SayLabel != null) _v.SayLabel.text = "Say it";
        }
    }

    // ------------------------------------------------------------
    // 評価
    // ------------------------------------------------------------
    private void Rate(int rating)
    {
        if (_mode != Mode.Card || _session == null || _session.Done || !_flipped) return;
        _ctx.Capture.Cancel();
        _ctx.Audio.Stop();
        var card = _session.Current;
        var review = _session.Rate(rating, DateTime.UtcNow);
        if (_ctx.Deck.Find(card.Word) == null) _ctx.Deck.Cards.Add(card);   // 新しい単語は評価したときに単語帳へ
        _ctx.SaveDeck();
        try { VocabStore.AppendReview(review); }
        catch (Exception e) { Debug.LogWarning("[Vocab] could not save the review: " + e.Message); }

        if (_session.Done) ShowSummary(first: true);
        else ShowCard();
    }

    // ------------------------------------------------------------
    // まとめ
    // ------------------------------------------------------------
    private void ShowSummary(bool first)
    {
        _mode = Mode.Summary;
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
            var textT = row.transform.Find("Text");
            TMP_Text text = textT != null ? textT.GetComponent<TMP_Text>() : null;
            if (text == null) text = row.GetComponentInChildren<TMP_Text>(true);
            _rows[c.Word] = text;
            var card = c;
            var myVoiceT = row.transform.Find("MyVoice");
            var myVoice = myVoiceT != null ? myVoiceT.GetComponent<Button>() : null;
            if (myVoice != null)
            {
                myVoice.gameObject.SetActive(_recs.ContainsKey(c.Word));
                myVoice.onClick.AddListener(() => { if (_recs.TryGetValue(card.Word, out byte[] w)) _ = PlaySafeAsync(() => _ctx.Audio.PlayWavAsync(w, CancellationToken.None)); });
            }
            var modelT = row.transform.Find("Model");
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
            _ctx.SetStatus("Done. Tap \"Start\" for more words.");
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
            if (token == _token)
            {
                _scoring = false;
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
        _mode = Mode.Weak;
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

    // ------------------------------------------------------------
    private void SetProgress(string text)
    {
        if (_v.ProgressText != null) _v.ProgressText.text = text;
    }

    private static void SetActive(GameObject go, bool on)
    {
        if (go != null) go.SetActive(on);
    }
}
