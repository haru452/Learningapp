// 次に出てくる 20 単語の動画の場面を、裏でいつも準備しておく。
// 1 つでも準備できていなければ、Supabase の一覧 → その場の YouTube 検索の順で埋める。
// Unity の API は使わず、単語帳・next_new.json・時刻は関数で受け取る (テストで偽物に差し替える)。
// Kick はメインのスレッドからだけ呼ぶ前提なので、スレッドもロックも使わない。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class VocabClipPrefetcher
{
    /// <summary>準備しておく単語の数 (共有の一覧から埋める)</summary>
    public const int Target = 20;

    /// <summary>
    /// その場の YouTube 検索までするのは、次に出る単語のうちこの数まで (1 日の検索の回数を先読みで使い切らないため)
    /// </summary>
    public const int SearchAhead = 5;

    private readonly VocabClipSource _source;
    private readonly IVocabClipStorage _storage;
    private readonly Func<IList<string>> _sessionRemaining;
    private readonly Func<IList<VocabCard>> _deck;
    private readonly Func<List<string>> _loadNextNew;
    private readonly Action<List<string>> _saveNextNew;
    private readonly Func<List<string>> _randomCandidates;
    private readonly Func<DateTime> _nowUtc;
    private readonly Action<string> _log;

    private bool _again;   // 動いている間に Kick された
    private bool _stopped; // Stop された (以後は動かない)

    /// <summary>今動いているか</summary>
    public bool IsRunning { get; private set; }

    /// <summary>今の (または最後の) 準備の Task。動いたことがなければ終わった Task</summary>
    public Task RunningTask { get; private set; } = Task.CompletedTask;

    /// <param name="sessionRemaining">今の回でまだ答えていない単語 (回が無ければ空)</param>
    /// <param name="loadNextNew">VocabStore.LoadNextNew</param>
    /// <param name="saveNextNew">VocabStore.SaveNextNew</param>
    /// <param name="randomCandidates">新しい単語の候補 (単語帳の単語は除いたもの)。レベル未選択なら空</param>
    /// <param name="log">"[VocabClips] ..." の出し先。null なら何もしない</param>
    public VocabClipPrefetcher(
        VocabClipSource source,
        IVocabClipStorage storage,
        Func<IList<string>> sessionRemaining,
        Func<IList<VocabCard>> deck,
        Func<List<string>> loadNextNew, Action<List<string>> saveNextNew,
        Func<List<string>> randomCandidates,
        Func<DateTime> nowUtc,
        Action<string> log = null)
    {
        _source = source;
        _storage = storage;
        _sessionRemaining = sessionRemaining;
        _deck = deck;
        _loadNextNew = loadNextNew;
        _saveNextNew = saveNextNew;
        _randomCandidates = randomCandidates;
        _nowUtc = nowUtc;
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// 次に出てくる単語を順に返す: 今の回の残り → 学習中 (単語帳の順) → known で復習の早い順 → nextNew。
    /// 正規化して空と重なりを除き、Target 個まで。
    /// </summary>
    public static List<string> UpcomingWords(IList<string> sessionRemaining, IList<VocabCard> deck, IList<string> nextNew, DateTime nowUtc)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        void Add(string w)
        {
            if (result.Count >= Target) return;
            string k = VocabDeck.Key(w);
            if (k.Length == 0 || !seen.Add(k)) return;
            result.Add(k);
        }

        foreach (var w in sessionRemaining ?? new List<string>()) Add(w);
        var cards = (deck ?? new List<VocabCard>()).Where(c => c != null).ToList();
        foreach (var c in cards.Where(c => c.Status == VocabCard.Learning)) Add(c.Word);
        // 期限前のものも含めて、近い順 (OrderBy は同じ値の順番を保つ)
        foreach (var c in cards.Where(c => c.Status == VocabCard.Known && c.DueAt.HasValue).OrderBy(c => c.DueAt.Value)) Add(c.Word);
        foreach (var w in nextNew ?? new List<string>()) Add(w);
        return result;
    }

    /// <summary>
    /// nextNew に、候補から重ならない単語を need 個まで足した新しいリストを返す。need が 0 以下ならそのままの写し。
    /// (単語帳との重なりは候補の側で除いてある)
    /// </summary>
    public static List<string> TopUpNextNew(IList<string> nextNew, IList<string> randomCandidates, int need)
    {
        var result = new List<string>(nextNew ?? new List<string>());
        if (need <= 0) return result;
        var seen = new HashSet<string>(result.Select(VocabDeck.Key));
        int added = 0;
        foreach (var w in randomCandidates ?? new List<string>())
        {
            if (added >= need) break;
            string k = VocabDeck.Key(w);
            if (k.Length == 0 || !seen.Add(k)) continue;
            result.Add(k);
            added++;
        }
        return result;
    }

    /// <summary>
    /// 準備を始める。動いていれば「終わったらもう 1 回」の印だけ付ける (同時に動くのは 1 つ)。
    /// Stop の後は何もしない。
    /// </summary>
    public void Kick()
    {
        if (_stopped) return;
        if (IsRunning) { _again = true; return; }
        IsRunning = true;
        _again = false;
        RunningTask = LoopAsync();
    }

    /// <summary>次の単語の前で止める (VocabManager.OnDestroy から呼ぶ)</summary>
    public void Stop() => _stopped = true;

    private async Task LoopAsync()
    {
        try
        {
            bool progress;
            do
            {
                _again = false;
                progress = await RunOnceAsync();
            } while (_again && !_stopped && progress);
        }
        catch (Exception e)
        {
            _log("[VocabClips] prefetch failed: " + e.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>1 回分の準備。上限・通信なし・キーなし・わからない失敗で止まったとき、または失敗したときは false</summary>
    private async Task<bool> RunOnceAsync()
    {
        try
        {
            var now = _nowUtc();
            var session = _sessionRemaining?.Invoke() ?? new List<string>();
            var deck = _deck?.Invoke() ?? new List<VocabCard>();

            // 1. 足りない新しい単語を next_new.json に足す
            int others = UpcomingWords(session, deck, new List<string>(), now).Count;
            var nextNew = _loadNextNew() ?? new List<string>();
            int need = Target - others - nextNew.Count;
            if (need > 0)
            {
                var topped = TopUpNextNew(nextNew, _randomCandidates?.Invoke() ?? new List<string>(), need);
                if (topped.Count != nextNew.Count)
                {
                    _saveNextNew(topped);
                    nextNew = topped;
                }
            }

            // 2. 準備できていない単語
            var upcoming = UpcomingWords(session, deck, nextNew, now);
            var missing = Missing(upcoming, now);
            if (missing.Count == 0) return true;

            // 3. Supabase の一覧からまとめて埋める
            var filled = await _source.FillFromCloudAsync(missing);
            if (filled.Values.Contains(ClipStatus.Offline))
            {
                _log("[VocabClips] prefetch paused: " + ClipStatus.Offline);
                return false;
            }
            missing = Missing(missing, _nowUtc());

            // 4. 残りのうち、すぐ出る単語だけその場で検索する (それより先は、出る順が近づいてから)
            foreach (var w in missing.Where(w => upcoming.IndexOf(w) < SearchAhead))
            {
                if (_stopped) return false;
                var (status, _) = await _source.GetAsync(w, allowSearch: true);
                if (status == ClipStatus.Limit || status == ClipStatus.Offline || status == ClipStatus.NoKey
                    || status == ClipStatus.Error)
                {
                    _log("[VocabClips] prefetch paused: " + status);
                    return false;
                }
            }
            return true;
        }
        catch (Exception e)
        {
            _log("[VocabClips] prefetch failed: " + e.Message);
            return false;
        }
    }

    private List<string> Missing(IEnumerable<string> words, DateTime now)
        => words.Where(w => !VocabClipStore.IsReady(_storage.Load(w), now)).ToList();
}
