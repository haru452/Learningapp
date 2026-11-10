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
/// 評価するたびに単語帳と履歴を保存する。途中でほかのタブへ移っても回はそのまま残り、戻ると続きから
/// </summary>
public partial class VocabTodayView
{
    public struct View
    {
        public GameObject LevelPickRow;
        public Button[] LevelPickButtons;
        public Button StartButton;
        public TMP_Text StartLabel;
        public TMP_Text ProgressText;
        public Image ProgressFill;      // 進み具合の細い棒 (無くてもよい)
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
        public Button VideosButton;     // カードの Sound / Say it の行 (無ければ動画の機能は無し)
    }

    private enum Mode { Idle, Loading, Card, Summary, Weak }

    public const int MaxLookupFailures = 10;
    // まとめの 1 行の中の部品 (VocabScreenBuilder が作る名前と同じにする)
    public const string RowTextName = "Text";
    public const string RowButtonsName = "Buttons";
    public const string MyVoiceName = "MyVoice";
    public const string ModelName = "Model";
    public const string MyVoicePath = RowButtonsName + "/" + MyVoiceName;
    public const string ModelPath = RowButtonsName + "/" + ModelName;
    private static readonly string[] RatingNames = { "Didn't know", "Hard", "Knew it", "Easy" };
    private const string NoDictionaryNote = "Can't reach the dictionary, so today is review only.";

    private readonly VocabContext _ctx;
    private readonly View _v;
    private readonly System.Random _rng = new System.Random();

    private Mode _mode = Mode.Idle;
    private int _token;                     // 新しい回を始めたら増やす (古い非同期の続きを捨てる)
    private int _sayToken;                  // Pause() で増やす (止めた聞き取りの続きを捨てる)
    private VocabSession _session;
    private readonly Dictionary<string, DictionaryResult> _dicts = new Dictionary<string, DictionaryResult>();
    private readonly Dictionary<string, string> _pron = new Dictionary<string, string>();     // この回の最後の判定
    private readonly Dictionary<string, string> _heard = new Dictionary<string, string>();
    private readonly Dictionary<string, byte[]> _recs = new Dictionary<string, byte[]>();     // まとめを閉じるまでメモリに残す
    private readonly Dictionary<string, GeminiApiClient.WordPronResult> _gemini = new Dictionary<string, GeminiApiClient.WordPronResult>();
    private readonly Dictionary<string, TMP_Text> _rows = new Dictionary<string, TMP_Text>();
    private bool _flipped;
    private bool _finishing;
    private bool _starting;
    private bool _scoring;
    private bool _geminiFailed;
    private string _note = "";
    private List<VocabCard> _weak = new List<VocabCard>();
    private int _weakIndex;
    private VocabCard _weakCard;
    private readonly VocabVideosView _videos;   // 動画の画面 (無ければ null)

    public VocabTodayView(VocabContext ctx, View v, VocabVideosView videos = null)
    {
        _ctx = ctx;
        _v = v;
        _videos = _v.VideosButton != null ? videos : null;
        for (int i = 0; i < (_v.LevelPickButtons?.Length ?? 0); i++)
        {
            string level = CefrWordList.Levels[i];
            VocabContext.SetLabel(_v.LevelPickButtons[i], level);
            _v.LevelPickButtons[i].onClick.AddListener(() => { VocabSettings.Level = level; ShowIdle(); });
        }
        if (_v.StartButton != null) _v.StartButton.onClick.AddListener(OnStartClicked);
        if (_v.CardButton != null) _v.CardButton.onClick.AddListener(() => { if (VideosOpen) return; _flipped = !_flipped; Render(); });
        if (_v.SoundButton != null) _v.SoundButton.onClick.AddListener(() => PlayModel(CurrentCard));
        if (_v.SayButton != null) _v.SayButton.onClick.AddListener(OnSayClicked);
        if (_v.VideosButton != null)
        {
            VocabContext.SetLabel(_v.VideosButton, "Videos");
            _v.VideosButton.onClick.AddListener(OpenVideos);
            _v.VideosButton.gameObject.SetActive(_videos != null);
        }
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
    private bool VideosOpen => _videos != null && _videos.IsOpen;

    /// <summary>今の回でまだ答えていない単語 (今のカードから最後まで)。回が無い・まとめなどでは空</summary>
    public List<string> RemainingWords()
    {
        if (_mode != Mode.Card || _session == null || _session.Done) return new List<string>();
        return _session.Cards.Skip(_session.Index).Select(c => c.Word).ToList();
    }

    // ------------------------------------------------------------
    // 画面を開いた・離れた
    // ------------------------------------------------------------
    public void Show()
    {
        if (_mode == Mode.Idle) ShowIdle();
        else if (_mode == Mode.Summary) _ctx.SetStatus(_scoring ? "Checking your pronunciation..." : "Tap \"Start\" for more words.");
        else if (VideosOpen) _videos.ResumeWithoutPlaying();   // 同じ場面の文だけ (流し直さない)
        else if (_mode == Mode.Card || _mode == Mode.Weak) ShowStatusForCard();
    }

    /// <summary>
    /// ほかのタブ・画面へ移った: マイクと音声だけ止める。回 (今のカード・まとめ・録音) はそのまま残し、
    /// 戻ってきたら続きから。読み込みや採点も裏で続ける
    /// </summary>
    public void Pause()
    {
        _sayToken++;
        _ctx.Capture.Cancel();
        _ctx.Audio.Stop();
        _starting = false;
        if (_v.SayLabel != null) _v.SayLabel.text = "Say it";
        _videos?.Pause();
        if ((_mode == Mode.Card || _mode == Mode.Weak) && !VideosOpen) ShowStatusForCard();
    }

    // カードの「Videos」: カードと評価の行を隠して動画の画面を出す。Close で戻る (めくった状態はそのまま)
    private void OpenVideos()
    {
        var card = CurrentCard;
        if (_videos == null || card == null || VideosOpen) return;
        if (_mode != Mode.Card && _mode != Mode.Weak) return;
        _sayToken++;
        _ctx.Capture.Cancel();
        _ctx.Audio.Stop();
        if (_v.SayLabel != null) _v.SayLabel.text = "Say it";
        SetActive(_v.CardArea, false);
        SetActive(_v.RatingRow, false);
        VocabContext.Show(_v.RatingHint, false);
        _ctx.SetStatus("");
        _videos.Open(card, () =>
        {
            SetActive(_v.CardArea, true);
            Render();
            ShowStatusForCard();
        });
    }

    private void ShowStatusForCard()
    {
        if (_mode == Mode.Weak) _ctx.SetStatus("Tap \"Say it\" and say the word.");
        else _ctx.SetStatus(_note.Length > 0 ? _note : "Tap the card to see the meaning.");
    }

    private void ShowIdle()
    {
        SetMode(Mode.Idle);
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
        SetMode(Mode.Loading);
        ResetSessionData();
        SetActive(_v.SummaryScroll, false);
        HideSummaryButtons();
        VocabContext.Show(_v.StartButton, false);
        _ctx.SetLoading(true);
        _ctx.SetStatus("Getting today's words...");

        var now = DateTime.UtcNow;
        var (cards, newCount) = VocabQueue.Plan(_ctx.Deck.Cards, now, VocabSettings.SessionSize);
        bool offline = false;                 // 1 回でも通信に失敗したら、そのあとは保存した結果だけ使う (待たせない)
        string keyProblem = null;             // 辞書が JSON 以外を返した (キーの間違い)
        bool hasKey = _ctx.Dictionary != null && _ctx.Dictionary.IsConfigured;

        async Task<DictionaryResult> Lookup(string word)
        {
            if (offline || keyProblem != null) return VocabStore.LoadDict(word);
            try { return await _ctx.LookupAsync(word); }
            catch (DictionaryKeyException e) { keyProblem = e.Message; }
            catch (Exception e) { offline = true; Debug.LogWarning("[Vocab] lookup failed for " + word + ": " + e.Message); }
            return VocabStore.LoadDict(word);
        }

        // 前からある単語の辞書 (保存してあれば通信しない)
        foreach (var c in cards)
        {
            _dicts[c.Word] = await Lookup(c.Word);
            if (token != _token) return;
        }

        // 新しい単語: CEFR-J の自分のレベルから、単語帳に無いものをランダムに。10 回続けて引けなければ止める
        int added = 0;
        bool noneLeft = false;
        if (newCount > 0 && hasKey)
        {
            var deckWords = _ctx.Deck.Cards.Select(c => c.Word).ToList();
            var randomCandidates = VocabQueue.NewCandidates(_ctx.Cefr, VocabSettings.Level, deckWords, _rng);
            var nextNew = VocabStore.LoadNextNew();   // 先に決めておいた単語 (動画の場面を先に探してある)
            var candidates = VocabQueue.OrderCandidates(nextNew, randomCandidates, deckWords);
            noneLeft = candidates.Count == 0;
            int fails = 0;
            var notFound = new List<string>();   // 辞書に無かった語 (次からも出さないので next_new.json から除く)
            foreach (string w in candidates)
            {
                if (added >= newCount || fails >= MaxLookupFailures) break;
                if (offline || keyProblem != null) break;
                DictionaryResult r = await Lookup(w);
                if (token != _token) return;
                if (r != null && !r.Found) notFound.Add(w);
                if (r == null || !r.Found) { fails++; continue; }
                fails = 0;
                _dicts[w] = r;
                // 単語帳に入れるのは評価したとき (途中でやめたら入らない)
                cards.Add(new VocabCard { Word = VocabDeck.Key(w), Source = "auto", Cefr = _ctx.LevelFor(w, r), CreatedAt = now });
                added++;
            }
            // 使った単語と辞書に無かった単語は先に決めた一覧から除く
            if (nextNew.Count > 0)
                VocabStore.SaveNextNew(VocabQueue.RemainingNextNew(nextNew, cards.Skip(cards.Count - added).Select(c => c.Word), randomCandidates, notFound));
        }
        _ctx.SetLoading(false);

        _note = newCount > 0 && added == 0
            ? (!hasKey ? VocabContext.NoDictionaryKey : keyProblem ?? (offline ? NoDictionaryNote : _ctx.Cefr.Count == 0 ? "The word list (CefrjWords.json) is missing." : noneLeft ? "You have all " + VocabSettings.Level + " words. Try the next level in Settings." : ""))
            : "";
        if (cards.Count == 0)
        {
            ShowIdle();
            _ctx.SetStatus(_note.Length > 0 ? _note : "No words yet. Add words or check the dictionary key.");
            return;
        }
        _session = new VocabSession(cards, VocabSettings.Retention / 100.0);
        SetMode(Mode.Card);
        ShowCard();
        _ctx.KickPrefetch?.Invoke();   // 今の回の単語から動画の場面を準備する
    }

    private void ResetSessionData()
    {
        _session = null;
        _dicts.Clear(); _pron.Clear(); _heard.Clear(); _recs.Clear(); _gemini.Clear(); _rows.Clear();
        _geminiFailed = false;
        _scoring = false;
        _note = "";
        _weak = new List<VocabCard>();
        VocabContext.ClearChildren(_v.SummaryContent, _v.SummaryRowTemplate);
    }

    // ------------------------------------------------------------
    // カード
    // ------------------------------------------------------------
    private void ShowCard()
    {
        _videos?.Dismiss();
        _flipped = false;
        if (_v.LevelPickRow != null) _v.LevelPickRow.SetActive(false);
        SetActive(_v.CardArea, true);
        SetActive(_v.SummaryScroll, false);
        HideSummaryButtons();
        if (_mode == Mode.Weak)
        {
            VocabContext.Show(_v.StartButton, true);
            if (_v.StartLabel != null) _v.StartLabel.text = _weakIndex + 1 < _weak.Count ? "Next" : "Back to summary";
            SetProgress("Practice " + (_weakIndex + 1) + " / " + _weak.Count, (_weakIndex + 1f) / _weak.Count);
            _ctx.SetStatus("Tap \"Say it\" and say the word.");
        }
        else
        {
            VocabContext.Show(_v.StartButton, false);
            SetProgress("Word " + (_session.Index + 1) + " / " + _session.Count, (_session.Index + 1f) / _session.Count);
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
        sb.Append("<size=220%><b>").Append(VocabContext.Esc(c.Word)).Append("</b></size>\n");
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
        sb.Append("<size=150%><b>").Append(VocabContext.Esc(c.Word)).Append("</b></size>\n\n");
        if (dict != null && dict.Found) sb.Append(_ctx.DescribeEntries(dict, c.Word)).Append('\n');
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
        if (_starting) return;
        if (_ctx.Capture.IsActive) { _ = FinishSayAsync(); return; }
        _ = StartSayAsync();
    }

    private async Task StartSayAsync()
    {
        _ctx.Audio.Stop();
        int token = _token, sayToken = _sayToken;
        var card = CurrentCard;
        _starting = true;
        bool ok;
        try { ok = await _ctx.Capture.StartAsync(); }
        catch (Exception e) { Debug.LogWarning("[Vocab] could not start listening: " + e.Message); ok = false; }
        finally { _starting = false; }
        // 権限を待つ間に評価した・タブを移った: 始めた聞き取りは捨てる (別のカードの結果にしない)
        if (ok && (token != _token || sayToken != _sayToken || card != CurrentCard)) { _ctx.Capture.Cancel(); return; }
        if (sayToken != _sayToken) return;
        if (!ok) { _ctx.SetStatus("Could not use the microphone."); return; }
        if (_v.SayLabel != null) _v.SayLabel.text = "Stop";
        _ctx.SetStatus("Listening... say the word.");
    }

    private async Task FinishSayAsync()
    {
        if (_finishing) return;
        _finishing = true;
        int token = _token, sayToken = _sayToken;
        var c = CurrentCard;
        if (_v.SayLabel != null) _v.SayLabel.text = "...";
        try
        {
            var (heard, wav) = await _ctx.Capture.StopAsync();
            if (token != _token || sayToken != _sayToken || c == null || c != CurrentCard) return;
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
        if (_mode != Mode.Card || _session == null || _session.Done || !_flipped || VideosOpen) return;
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
        _ctx.KickPrefetch?.Invoke();
    }

    // ------------------------------------------------------------
    // fraction: 0〜1 で棒の長さ。省略 (負の数) なら棒を隠す
    private void SetProgress(string text, float fraction = -1f)
    {
        if (_v.ProgressText != null) _v.ProgressText.text = text;
        if (_v.ProgressFill == null) return;
        _v.ProgressFill.fillAmount = Mathf.Clamp01(fraction);
        var track = _v.ProgressFill.transform.parent;
        if (track != null) track.gameObject.SetActive(fraction >= 0f);
    }

    // 学習中 (読み込み・カード・苦手の練習) は上の Today / Words / Add / Settings を隠して、カードを広くする
    private void SetMode(Mode mode)
    {
        if (mode != Mode.Card && mode != Mode.Weak) _videos?.Dismiss();   // 回を離れたら動画の画面も閉じる
        _mode = mode;
        _ctx.ShowTabs?.Invoke(mode == Mode.Idle || mode == Mode.Summary);
    }

    private static void SetActive(GameObject go, bool on)
    {
        if (go != null) go.SetActive(on);
    }
}
