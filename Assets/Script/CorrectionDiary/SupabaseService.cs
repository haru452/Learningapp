using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Supabase のログイン(メール/パスワード)と、クラウド保存のストリークを扱う。
/// Web版 (streak-app) と同じ Supabase プロジェクト・同じ schema.sql を使う。
///
/// ・ログインは任意。ログインしていなければ、これまで通り端末内の記録だけで動く。
/// ・ここに置くのは Project URL と anon キーだけ (service_role キーは絶対に入れない)。
/// ・ログイン状態は、リフレッシュトークンを PlayerPrefs に保存して、次回起動時に復元する。
/// </summary>
public class SupabaseService : MonoBehaviour
{
    // ------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------
    [Header("Supabase (Project Settings > API)")]
    [Tooltip("例: https://xxxx.supabase.co")]
    [SerializeField] private string projectUrl = "";
    [Tooltip("anon public キー (publishable key)。service_role キーは入れない")]
    [SerializeField] private string anonKey = "";

    [Header("Streak")]
    [Tooltip("「今日」を決めるタイムゾーン (IANA名)。例: Asia/Tokyo")]
    [SerializeField] private string timeZone = "Asia/Tokyo";

    // ------------------------------------------------------------
    // Types
    // ------------------------------------------------------------
    public struct StreakInfo
    {
        public int Streak;            // いま有効な連続日数 (途切れていれば 0)
        public string LastDate;       // "yyyy-MM-dd" / なければ空
        public bool StudiedToday;     // 今日すでに記録済みか
        public int Best;              // 最長記録 (クラウドに保存されている値)
    }

    /// <summary>ユーザー名とアバター (12色の番号)</summary>
    public struct ProfileInfo
    {
        public string DisplayName;    // 未設定なら空
        public int AvatarColor;       // 0〜11
    }

    public struct SignUpResult
    {
        public bool NeedsEmailConfirmation;
    }

    /// <summary>画面に出してよい、短いメッセージ付きの例外</summary>
    public class SupabaseException : Exception
    {
        public long StatusCode { get; }
        public SupabaseException(long statusCode, string message) : base(message) { StatusCode = statusCode; }
    }

    // ------------------------------------------------------------
    // State
    // ------------------------------------------------------------
    private const string PrefRefresh = "sb_refresh_token";
    private const string PrefEmail = "sb_email";

    private string _accessToken = "";
    private string _refreshToken = "";
    private DateTime _expiresAtUtc = DateTime.MinValue;
    private Task _refreshing;   // 同時に何度も更新しないため

    public string Email { get; private set; } = "";
    public bool IsSignedIn => !string.IsNullOrEmpty(_accessToken);
    // Project URL / anon キーは Assets/Resources/ApiSecrets.json を優先 (空なら Inspector の値)
    private string ProjectUrl => ApiSecrets.Pick(ApiSecrets.Data.supabaseUrl, projectUrl) ?? "";
    private string AnonKey => ApiSecrets.Pick(ApiSecrets.Data.supabaseAnonKey, anonKey) ?? "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectUrl) && !string.IsNullOrWhiteSpace(AnonKey);

    /// <summary>ログイン/ログアウトのたびに呼ばれる</summary>
    public event Action AuthChanged;

    /// <summary>ユーザー名・アバターが変わったとき (ログイン直後の読み込み、保存、ログアウト) に呼ばれる</summary>
    public event Action ProfileChanged;

    /// <summary>ログイン中のプロフィール (ログアウト中は空)</summary>
    public ProfileInfo Profile { get; private set; } = new ProfileInfo { DisplayName = "" };

    /// <summary>表示に使う名前。ユーザー名が未設定なら、メールアドレスの @ より前を使う。</summary>
    public string DisplayNameOrFallback
    {
        get
        {
            if (!string.IsNullOrEmpty(Profile.DisplayName)) return Profile.DisplayName;
            if (string.IsNullOrEmpty(Email)) return "";
            int at = Email.IndexOf('@');
            return at > 0 ? Email.Substring(0, at) : Email;
        }
    }

    // ------------------------------------------------------------
    // JSON models
    // ------------------------------------------------------------
    [Serializable] private class UserDto { public string id; public string email; }
    [Serializable]
    private class SessionDto
    {
        public string access_token;
        public string refresh_token;
        public int expires_in;
        public UserDto user;
        public string id;       // サインアップで確認メールが必要なとき、ユーザーが直接返る
        public string email;
    }
    [Serializable]
    private class ErrorDto
    {
        public string error_code;
        public string error;
        public string error_description;
        public string msg;
        public string message;
    }
    [Serializable] private class CompleteRow { public int streak; public string last_date; public bool already_done; public int best_streak; }
    [Serializable] private class CompleteRows { public CompleteRow[] rows; }
    [Serializable] private class ImportRow { public int streak; public string last_date; public bool imported; public int best_streak; }
    [Serializable] private class ImportRows { public ImportRow[] rows; }
    [Serializable] private class ProfileRow { public string display_name; public int avatar_color; }
    [Serializable] private class ProfileRows { public ProfileRow[] rows; }
    [Serializable] private class GetRow { public int streak; public string last_date; public bool studied_today; public int best_streak; }
    [Serializable] private class GetRows { public GetRow[] rows; }

    // ------------------------------------------------------------
    // Auth
    // ------------------------------------------------------------

    /// <summary>新規登録。メール確認が ON の場合は、確認メールのリンクを開くまでログインできない。</summary>
    public async Task<SignUpResult> SignUpAsync(string email, string password)
    {
        RequireConfig();
        string json = "{\"email\":\"" + HttpUtil.JsonEscape(email.Trim()) + "\",\"password\":\"" + HttpUtil.JsonEscape(password) + "\"}";
        string body = await PostAsync("/auth/v1/signup", json, null);
        var dto = JsonUtility.FromJson<SessionDto>(body);

        if (dto != null && !string.IsNullOrEmpty(dto.access_token))
        {
            ApplySession(dto, email.Trim());
            return new SignUpResult { NeedsEmailConfirmation = false };
        }
        return new SignUpResult { NeedsEmailConfirmation = true };
    }

    public async Task SignInAsync(string email, string password)
    {
        RequireConfig();
        string json = "{\"email\":\"" + HttpUtil.JsonEscape(email.Trim()) + "\",\"password\":\"" + HttpUtil.JsonEscape(password) + "\"}";
        string body = await PostAsync("/auth/v1/token?grant_type=password", json, null);
        var dto = JsonUtility.FromJson<SessionDto>(body);
        if (dto == null || string.IsNullOrEmpty(dto.access_token))
            throw new SupabaseException(0, "Sign-in failed. Please try again.");
        ApplySession(dto, email.Trim());
    }

    public async Task SignOutAsync()
    {
        string token = _accessToken;
        ClearSession();
        // サーバー側の後始末はベストエフォート (失敗しても端末側は確実にログアウトする)
        if (IsConfigured && !string.IsNullOrEmpty(token))
        {
            try { await PostAsync("/auth/v1/logout", "{}", token); }
            catch (Exception e) { Debug.LogWarning("[Supabase] logout request failed: " + e.Message); }
        }
    }

    /// <summary>起動時に、保存してあるログインを復元する。復元できたら true。</summary>
    public async Task<bool> TryRestoreSessionAsync()
    {
        if (!IsConfigured) return false;
        string saved = PlayerPrefs.GetString(PrefRefresh, "");
        if (string.IsNullOrEmpty(saved)) return false;

        _refreshToken = saved;
        Email = PlayerPrefs.GetString(PrefEmail, "");
        try
        {
            await RefreshTokenAsync();
            return IsSignedIn;
        }
        catch (SupabaseException e) when (e.StatusCode >= 400 && e.StatusCode < 500)
        {
            // トークンが無効 (期限切れ・パスワード変更など)。ログインし直してもらう
            ClearSession();
            return false;
        }
        catch (Exception e)
        {
            // オフラインなど。保存は残し、今回はログインなしで動く
            Debug.LogWarning("[Supabase] could not restore session: " + e.Message);
            _refreshToken = "";
            return false;
        }
    }

    // ------------------------------------------------------------
    // Streak
    // ------------------------------------------------------------
    public async Task<StreakInfo> GetStreakAsync()
    {
        string body = await RpcAsync("get_my_streak");
        var rows = JsonUtility.FromJson<GetRows>("{\"rows\":" + body + "}");
        if (rows == null || rows.rows == null || rows.rows.Length == 0) return default;
        var r = rows.rows[0];
        return new StreakInfo { Streak = r.streak, LastDate = r.last_date ?? "", StudiedToday = r.studied_today, Best = r.best_streak };
    }

    /// <summary>今日の記録を完了にする。同じ日に何回呼んでも、加算されるのは1回だけ(サーバー側で判定)。</summary>
    public async Task<(StreakInfo info, bool alreadyDone)> CompleteStudyAsync()
    {
        string body = await RpcAsync("complete_study");
        var rows = JsonUtility.FromJson<CompleteRows>("{\"rows\":" + body + "}");
        if (rows == null || rows.rows == null || rows.rows.Length == 0)
            throw new SupabaseException(0, "Unexpected response from the server.");
        var r = rows.rows[0];
        var info = new StreakInfo { Streak = r.streak, LastDate = r.last_date ?? "", StudiedToday = true, Best = r.best_streak };
        return (info, r.already_done);
    }

    /// <summary>
    /// 端末内の連続日数をクラウドに引き継ぐ (ログイン後に1回)。
    /// サーバー側で検証され、サーバーの値より大きいときだけ反映される。
    /// lastDate は「今日」か「昨日」であること。反映されたら true。
    /// </summary>
    public async Task<bool> ImportLocalStreakAsync(int streak, DateTime lastDate, int best = 0)
    {
        string extra = ",\"p_streak\":" + streak + ",\"p_best\":" + Math.Max(0, best) +
                       ",\"p_last_date\":\"" + lastDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "\"";
        string body = await RpcAsync("import_local_streak", extra);
        var rows = JsonUtility.FromJson<ImportRows>("{\"rows\":" + body + "}");
        return rows != null && rows.rows != null && rows.rows.Length > 0 && rows.rows[0].imported;
    }

    // ------------------------------------------------------------
    // Profile (ユーザー名・アバターの色)
    // ------------------------------------------------------------

    /// <summary>クラウドから、プロフィールを読み込む (ログイン直後に自動で呼ばれる)。</summary>
    public async Task<ProfileInfo> RefreshProfileAsync()
    {
        string body = await RpcAsync("get_my_profile", "", false);
        return SetProfileFrom(body);
    }

    /// <summary>ユーザー名(20文字まで)とアバターの色(0〜11)を保存する。</summary>
    public async Task<ProfileInfo> SaveProfileAsync(string displayName, int avatarColor)
    {
        string extra =
            ",\"p_display_name\":\"" + HttpUtil.JsonEscape((displayName ?? "").Trim()) + "\"" +
            ",\"p_avatar_color\":" + avatarColor;
        string body = await RpcAsync("set_my_profile", extra, false);
        return SetProfileFrom(body);
    }

    private ProfileInfo SetProfileFrom(string body)
    {
        var rows = JsonUtility.FromJson<ProfileRows>("{\"rows\":" + body + "}");
        var p = new ProfileInfo();
        if (rows != null && rows.rows != null && rows.rows.Length > 0)
        {
            p.DisplayName = rows.rows[0].display_name ?? "";
            p.AvatarColor = Math.Max(0, Math.Min(11, rows.rows[0].avatar_color));
        }
        Profile = p;
        ProfileChanged?.Invoke();
        return p;
    }

    private async void LoadProfileQuietAsync()
    {
        try { await RefreshProfileAsync(); }
        catch (Exception e) { Debug.LogWarning("[Supabase] could not load the profile: " + e.Message); }
    }

    // ------------------------------------------------------------
    // Diary (直近7日分だけクラウドに保存される。古いものはサーバーが自動で消す。見るのはWeb版)
    // ------------------------------------------------------------

    /// <summary>添削した日記を1件保存する。</summary>
    public async Task SaveDiaryAsync(string original, string corrected, string explanation, string engine)
    {
        string extra =
            ",\"p_original\":\"" + HttpUtil.JsonEscape(original ?? "") + "\"" +
            ",\"p_corrected\":\"" + HttpUtil.JsonEscape(corrected ?? "") + "\"" +
            ",\"p_explanation\":\"" + HttpUtil.JsonEscape(explanation ?? "") + "\"" +
            ",\"p_engine\":\"" + HttpUtil.JsonEscape(engine ?? "") + "\"";
        await RpcAsync("save_diary_entry", extra);
    }

    // ------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------
    private void RequireConfig()
    {
        if (!IsConfigured)
            throw new SupabaseException(0, "Supabase is not set up. Fill in the Project URL and anon key on SupabaseService.");
    }

    private string Url(string path) => ProjectUrl.TrimEnd('/') + path;

    private async Task<string> RpcAsync(string function, string extraJsonFields = "", bool withTimezone = true)
    {
        RequireConfig();
        if (!IsSignedIn) throw new SupabaseException(401, "Please sign in first.");

        await EnsureFreshTokenAsync();
        string head = withTimezone ? "\"p_timezone\":\"" + HttpUtil.JsonEscape(timeZone) + "\"" : "";
        if (!withTimezone && extraJsonFields.StartsWith(",")) extraJsonFields = extraJsonFields.Substring(1);
        string json = "{" + head + extraJsonFields + "}";
        try
        {
            return await PostAsync("/rest/v1/rpc/" + function, json, _accessToken);
        }
        catch (SupabaseException e) when (e.StatusCode == 401 && !string.IsNullOrEmpty(_refreshToken))
        {
            // アクセストークンが期限切れだった。更新して1回だけやり直す
            await RefreshTokenAsync();
            return await PostAsync("/rest/v1/rpc/" + function, json, _accessToken);
        }
    }

    private Task EnsureFreshTokenAsync()
    {
        if (DateTime.UtcNow < _expiresAtUtc) return Task.CompletedTask;
        return RefreshTokenAsync();
    }

    private Task RefreshTokenAsync()
    {
        // 更新中なら、それを待つ (リフレッシュトークンは1回しか使えないため、二重に送らない)
        if (_refreshing == null || _refreshing.IsCompleted)
            _refreshing = DoRefreshAsync();
        return _refreshing;
    }

    private async Task DoRefreshAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
            throw new SupabaseException(401, "Please sign in again.");

        string json = "{\"refresh_token\":\"" + HttpUtil.JsonEscape(_refreshToken) + "\"}";
        string body = await PostAsync("/auth/v1/token?grant_type=refresh_token", json, null);
        var dto = JsonUtility.FromJson<SessionDto>(body);
        if (dto == null || string.IsNullOrEmpty(dto.access_token))
            throw new SupabaseException(401, "Please sign in again.");
        ApplySession(dto, Email);
    }

    private void ApplySession(SessionDto dto, string fallbackEmail)
    {
        bool wasSignedIn = IsSignedIn;

        _accessToken = dto.access_token;
        if (!string.IsNullOrEmpty(dto.refresh_token)) _refreshToken = dto.refresh_token;
        // 少し早めに更新する
        int life = dto.expires_in > 0 ? dto.expires_in : 3600;
        _expiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, life - 60));

        string mail = dto.user != null && !string.IsNullOrEmpty(dto.user.email) ? dto.user.email : fallbackEmail;
        Email = mail ?? "";

        PlayerPrefs.SetString(PrefRefresh, _refreshToken);
        PlayerPrefs.SetString(PrefEmail, Email);
        PlayerPrefs.Save();

        if (!wasSignedIn)
        {
            AuthChanged?.Invoke();
            LoadProfileQuietAsync();   // ユーザー名・アバターも読み込む (待たない)
        }
    }

    private void ClearSession()
    {
        bool was = IsSignedIn;
        _accessToken = "";
        _refreshToken = "";
        _expiresAtUtc = DateTime.MinValue;
        Email = "";
        PlayerPrefs.DeleteKey(PrefRefresh);
        PlayerPrefs.DeleteKey(PrefEmail);
        PlayerPrefs.Save();
        Profile = new ProfileInfo { DisplayName = "" };
        if (was)
        {
            AuthChanged?.Invoke();
            ProfileChanged?.Invoke();
        }
    }

    private async Task<string> PostAsync(string path, string json, string bearer)
    {
        var headers = new (string name, string value)[]
        {
            ("apikey", AnonKey),
            ("Authorization", "Bearer " + (string.IsNullOrEmpty(bearer) ? AnonKey : bearer)),
        };

        try
        {
            return await HttpUtil.PostJsonAsync(Url(path), json, headers, 30);
        }
        catch (HttpException e)
        {
            throw new SupabaseException(e.StatusCode, FriendlyMessage(e));
        }
    }

    // ------------------------------------------------------------
    // Error messages
    // ------------------------------------------------------------
    internal static string FriendlyMessage(HttpException e)
    {
        // HttpUtil のメッセージは "HTTP 400 : ...\n{本文}" の形
        string raw = e.Message ?? "";
        int nl = raw.IndexOf('\n');
        string body = nl >= 0 ? raw.Substring(nl + 1).Trim() : "";

        ErrorDto dto = null;
        if (body.StartsWith("{"))
        {
            try { dto = JsonUtility.FromJson<ErrorDto>(body); } catch { }
        }

        string code = dto != null ? (dto.error_code ?? "") : "";
        string text = "";
        if (dto != null)
        {
            text = FirstNonEmpty(dto.msg, dto.message, dto.error_description, dto.error);
        }

        switch (code)
        {
            case "invalid_credentials": return "Email or password is incorrect.";
            case "email_not_confirmed": return "Please confirm your email first. Check your inbox for the link.";
            case "user_already_exists": return "This email is already registered. Try signing in.";
            case "weak_password": return "Password is too weak. Use at least 6 characters.";
            case "over_email_send_rate_limit":
            case "over_request_rate_limit": return "Too many attempts. Please wait a minute and try again.";
            case "signup_disabled": return "Sign-up is turned off for this project.";
        }

        if (e.StatusCode == 0) return "Could not reach the server. Check your internet connection.";
        if (e.StatusCode == 401 && string.IsNullOrEmpty(text)) return "Please sign in again.";
        if (!string.IsNullOrEmpty(text)) return text;
        return "Something went wrong (HTTP " + e.StatusCode + ").";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var v in values) if (!string.IsNullOrEmpty(v)) return v;
        return "";
    }
}
