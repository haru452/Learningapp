using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 日記の連続日数カード。
///   「N Days Streak」「Best: Nd」「Keep it up! ...」のテキストと、今週(月〜日)のチェック丸を更新する。
///
/// DiaryManager が日記の添削に成功したときに MarkTodayDoneAsync() を呼ぶ。
///
/// Supabase にログインしているときは、連続日数と週の丸をクラウドの値で表示する
/// (Web版と同じ記録になる)。ログインしていない/通信できないときは、端末内の記録を使う。
/// </summary>
public class DiaryStreakView : MonoBehaviour
{
    [Serializable]
    public class DayCell
    {
        [Tooltip("丸い背景 (Image)")]
        public Image circle;
        [Tooltip("チェックマーク (達成した日だけ表示される)")]
        public GameObject check;
        [Tooltip("曜日の文字 (任意)")]
        public TMP_Text label;
    }

    [Header("Texts (使わないものは空欄でOK)")]
    [Tooltip("例: 7 Days Streak")]
    [SerializeField] private TMP_Text titleText;
    [Tooltip("例: 7日連続達成中 (連続日数が0のときは非表示)")]
    [SerializeField] private TMP_Text badgeText;
    [Tooltip("例: Keep it up! 7 days of daily English voice logs")]
    [SerializeField] private TMP_Text messageText;
    [Tooltip("例: Best: 14d")]
    [SerializeField] private TMP_Text bestText;

    [Header("Week row (月, 火, 水, 木, 金, 土, 日 の順に7個)")]
    [SerializeField] private DayCell[] dayCells = new DayCell[7];

    [Header("Text formats ({0} が数字に置き換わる)")]
    [SerializeField] private string titleFormat = "{0} Days Streak";
    [SerializeField] private string titleFormatSingular = "{0} Day Streak";
    [Tooltip("※ 日本語を表示するには、TextMeshPro の日本語フォントが必要。四角に化ける場合は英語に変える")]
    [SerializeField] private string badgeFormat = "{0}日連続達成中";
    [SerializeField] private string bestFormat = "Best: {0}d";
    [SerializeField] private string messageDone = "Keep it up! {0} days of daily English voice logs";
    [SerializeField] private string messageDoneSingular = "Great start! {0} day of daily English voice logs";
    [SerializeField] private string messagePending = "Record today's diary to keep your {0}-day streak!";
    [SerializeField] private string messageNoStreak = "Record a diary today to start your streak!";

    [Header("Week row colors")]
    [SerializeField] private Color doneColor = new Color(0.16f, 0.36f, 0.30f, 1f);
    [SerializeField] private Color todayDoneColor = new Color(0.30f, 0.82f, 0.96f, 1f);
    [SerializeField] private Color todayPendingColor = new Color(0.30f, 0.82f, 0.96f, 0.25f);
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.06f);
    [SerializeField] private Color labelColor = new Color(0.65f, 0.70f, 0.75f, 1f);
    [SerializeField] private Color todayLabelColor = new Color(0.30f, 0.82f, 0.96f, 1f);

    [Header("Day boundary")]
    [Tooltip("1日の区切りの時刻。0 = 午前0時。例えば 4 にすると、午前4時までは前の日として数える")]
    [Range(0, 12)]
    [SerializeField] private int dayBoundaryHour = 0;

    [Header("Cloud (任意。空欄ならこれまで通り端末内だけで動く)")]
    [SerializeField] private SupabaseService supabase;

    [Header("Debug")]
    [Tooltip("ONにすると、メッセージ欄に、ログイン状態・クラウドの値・端末内の値・エラーを表示する (原因調べ用。終わったらOFF)")]
    [SerializeField] private bool showDebug = false;

    private string _debugNote = "";

    private DiaryStreakTracker _tracker;

    // クラウドから取得した値 (ログイン中だけ使う)
    private bool _cloudValid;
    private int _cloudStreak;
    private bool _cloudDoneToday;
    private int _cloudBest;
    private DateTime? _cloudLastDate;

    private DateTime Today => DateTime.Now.AddHours(-dayBoundaryHour).Date;

    /// <summary>「今日」の日付 (Day Boundary Hour を考慮した、日記の記録上の日付)</summary>
    public DateTime TodayDate => Today;

    // ------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------
    private void OnEnable()
    {
        if (supabase != null) supabase.AuthChanged += OnAuthChanged;
        Refresh();
        _ = RefreshFromCloudAsync();
    }

    private void OnDisable()
    {
        if (supabase != null) supabase.AuthChanged -= OnAuthChanged;
    }

    private void OnAuthChanged()
    {
        if (supabase == null || !supabase.IsSignedIn) _cloudValid = false;
        Refresh();
        _ = RefreshFromCloudAsync();
    }

    // アプリに戻ってきたとき、日付が変わっていても表示が合うようにする
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            Refresh();
            _ = RefreshFromCloudAsync();
        }
    }

    // ------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------

    /// <summary>今日を「日記を書いた日」として記録し、表示を更新する。今日初めての記録なら true。</summary>
    public bool MarkTodayDone()
    {
        EnsureTracker();
        bool added = _tracker.MarkDone(Today);
        Refresh();
        return added;
    }

    /// <summary>
    /// 今日を記録する。端末内には必ず記録し、ログイン中ならクラウドにも保存する
    /// (クラウドが失敗しても端末内の記録は残る)。今日初めての記録なら true。
    /// </summary>
    public async Task<bool> MarkTodayDoneAsync()
    {
        bool added = MarkTodayDone();

        if (CloudActive)
        {
            try
            {
                await ImportLocalOnceAsync();
                var (info, alreadyDone) = await supabase.CompleteStudyAsync();
                if (this == null) return added;
                ApplyCloud(info);
                Refresh();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DiaryStreakView] cloud save failed (kept locally): " + e.Message);
                _debugNote = "save FAILED: " + Short(e.Message);
                Refresh();
            }
        }
        return added;
    }

    /// <summary>ログイン中なら、クラウドの最新の値を取得して表示を更新する。</summary>
    public async Task RefreshFromCloudAsync()
    {
        if (!CloudActive) return;
        try
        {
            await ImportLocalOnceAsync();
            var info = await supabase.GetStreakAsync();
            if (this == null) return;
            ApplyCloud(info);
            Refresh();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DiaryStreakView] cloud fetch failed (showing local): " + e.Message);
            _debugNote = "fetch FAILED: " + Short(e.Message);
            Refresh();
        }
    }

    /// <summary>現在の連続日数</summary>
    public int GetCurrentStreak()
    {
        if (CloudActive && _cloudValid) return _cloudStreak;
        EnsureTracker();
        return _tracker.GetCurrentStreak(Today);
    }

    /// <summary>保存されている記録をもとに、表示を更新する。</summary>
    public void Refresh()
    {
        EnsureTracker();

        DateTime today = Today;
        bool cloud = CloudActive && _cloudValid;
        bool doneToday = cloud ? _cloudDoneToday : _tracker.IsDone(today);
        int streak = cloud ? _cloudStreak : _tracker.GetCurrentStreak(today);
        int best = Math.Max(_tracker.GetBestStreak(), streak);
        if (cloud) best = Math.Max(best, _cloudBest);   // 端末をまたいだ最長記録

        if (titleText != null)
            titleText.text = Fmt(streak == 1 ? titleFormatSingular : titleFormat, streak);

        if (badgeText != null)
        {
            badgeText.gameObject.SetActive(streak > 0);
            badgeText.text = Fmt(badgeFormat, streak);
        }

        if (bestText != null)
            bestText.text = Fmt(bestFormat, best);

        if (messageText != null)
            messageText.text = showDebug ? BuildDebug() : BuildMessage(streak, doneToday);

        RenderWeek(today);
    }

    // ------------------------------------------------------------
    // Internal
    // ------------------------------------------------------------
    private void EnsureTracker()
    {
        if (_tracker == null) _tracker = new DiaryStreakTracker();
    }

    // ログイン後、そのアカウントで1回だけ、端末内の連続日数をクラウドに引き継ぐ
    private Task _importTask;

    private Task ImportLocalOnceAsync()
    {
        if (_importTask == null || _importTask.IsCompleted) _importTask = DoImportAsync();
        return _importTask;
    }

    private async Task DoImportAsync()
    {
        string key = "diary_cloud_imported_v2_" + supabase.Email;
        if (PlayerPrefs.GetInt(key, 0) == 1) return;

        // 別のアカウントの記録が残っている端末では、その記録を、このアカウントに送らない
        if (!LocalBelongsToCurrentAccount())
        {
            _debugNote = "import skipped: this device's local record belongs to another account";
            return;
        }

        EnsureTracker();
        DateTime today = Today;
        int streak = _tracker.GetCurrentStreak(today);
        if (streak > 0)
        {
            DateTime last = _tracker.IsDone(today) ? today : today.AddDays(-1);
            try
            {
                bool imported = await supabase.ImportLocalStreakAsync(streak, last, _tracker.GetBestStreak());
                _debugNote = "import: sent " + streak + "d -> " + (imported ? "applied" : "not needed (cloud already >= local)");
            }
            catch (Exception e)
            {
                // 失敗しても、日記の記録や表示は止めない。印を付けないので、次回また試す
                Debug.LogWarning("[DiaryStreakView] import of local streak failed (will retry): " + e.Message);
                _debugNote = "import FAILED: " + Short(e.Message);
                return;
            }
        }
        PlayerPrefs.SetInt(key, 1);
        ClaimLocalForCurrentAccount();
        PlayerPrefs.Save();
    }

    private string BuildDebug()
    {
        string account = supabase == null ? "Supabase field EMPTY"
                       : supabase.IsSignedIn ? "signed in: " + supabase.Email
                       : "NOT signed in";
        int local = _tracker.GetCurrentStreak(Today);
        string cloud = (CloudActive && _cloudValid) ? _cloudStreak + "d (last " + (_cloudLastDate.HasValue ? _cloudLastDate.Value.ToString("MM-dd") : "-") + ")" : "-";
        return account + " | cloud " + cloud + " | local " + local + "d | " + (_debugNote == "" ? "no cloud call yet" : _debugNote);
    }

    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\n", " ");
        return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
    }

    private bool CloudActive => supabase != null && supabase.IsSignedIn;

    // この端末の記録(端末内)が、どのアカウントのものか。別のアカウントでログインしたときに、
    // 他人の記録を混ぜたり、クラウドに送ったりしないために使う
    private const string OwnerKey = "diary_local_owner";

    private bool LocalBelongsToCurrentAccount()
    {
        string owner = PlayerPrefs.GetString(OwnerKey, "");
        return string.IsNullOrEmpty(owner) || owner == supabase.Email;
    }

    private void ClaimLocalForCurrentAccount()
    {
        if (PlayerPrefs.GetString(OwnerKey, "") == supabase.Email) return;
        PlayerPrefs.SetString(OwnerKey, supabase.Email);
        PlayerPrefs.Save();
    }

    // クラウドの連続記録を、この端末の記録にも写す。
    // 別の端末で同じアカウントにログインしたときに、この端末も、クラウドと同じになる。
    private void MirrorCloudToLocal()
    {
        if (!_cloudLastDate.HasValue || _cloudStreak <= 0) return;
        if (!LocalBelongsToCurrentAccount()) return;

        EnsureTracker();
        _tracker.MarkRangeDone(_cloudLastDate.Value, Math.Min(_cloudStreak, 3650));
        ClaimLocalForCurrentAccount();
    }

    private void ApplyCloud(SupabaseService.StreakInfo info)
    {
        _cloudValid = true;
        _cloudStreak = info.Streak;
        _cloudDoneToday = info.StudiedToday;
        _cloudBest = info.Best;
        _cloudLastDate = null;
        if (!string.IsNullOrEmpty(info.LastDate) &&
            DateTime.TryParseExact(info.LastDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime d))
            _cloudLastDate = d.Date;

        MirrorCloudToLocal();
    }

    // 指定した日が「記録済み」か。クラウド表示中は、最後の記録日から連続日数ぶん遡った範囲を記録済みとする
    private bool IsDoneOn(DateTime day, DateTime today)
    {
        if (CloudActive && _cloudValid)
        {
            if (day == today) return _cloudDoneToday;
            if (_cloudStreak <= 0 || !_cloudLastDate.HasValue) return false;
            DateTime last = _cloudLastDate.Value;
            return day <= last && day > last.AddDays(-_cloudStreak);
        }
        return _tracker.IsDone(day);
    }

    private string BuildMessage(int streak, bool doneToday)
    {
        if (streak <= 0) return messageNoStreak;
        if (doneToday) return Fmt(streak == 1 ? messageDoneSingular : messageDone, streak);
        return Fmt(messagePending, streak);
    }

    private void RenderWeek(DateTime today)
    {
        if (dayCells == null) return;

        DateTime monday = DiaryStreakTracker.StartOfWeek(today);

        for (int i = 0; i < dayCells.Length && i < 7; i++)
        {
            DayCell cell = dayCells[i];
            if (cell == null) continue;

            DateTime day = monday.AddDays(i);
            bool done = IsDoneOn(day, today);
            bool isToday = day == today;

            if (cell.circle != null)
            {
                cell.circle.color = isToday
                    ? (done ? todayDoneColor : todayPendingColor)
                    : (done ? doneColor : emptyColor);
            }

            if (cell.check != null)
                cell.check.SetActive(done);

            if (cell.label != null)
                cell.label.color = isToday ? todayLabelColor : labelColor;
        }
    }

    // Inspector の書式に誤りがあっても、画面が止まらないようにする
    private static string Fmt(string format, int number)
    {
        try { return string.Format(format, number); }
        catch (FormatException) { return format; }
    }
}