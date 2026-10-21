using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ログイン / 新規登録 / ログアウトの画面。
/// ・ログインしていないとき: メール・パスワード入力と Sign in / Sign up ボタンを出す
/// ・ログインしているとき:   「Signed in as ...」と Sign out ボタンを出す
/// 画面の出し入れ (この Panel を開く/閉じる) は、既存のUI切り替えのコードで行ってください。
/// </summary>
public class AuthPanel : MonoBehaviour
{
    [Header("Service")]
    [SerializeField] private SupabaseService supabase;

    [Header("Signed-out group (ログインしていないとき表示)")]
    [SerializeField] private GameObject signedOutGroup;
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button signInButton;
    [SerializeField] private Button signUpButton;

    [Header("Signed-in group (ログインしているとき表示)")]
    [SerializeField] private GameObject signedInGroup;
    [SerializeField] private TMP_Text accountText;
    [SerializeField] private Button signOutButton;

    [Header("Status")]
    [Tooltip("エラーや案内を表示する文字 (任意)")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Color statusInfoColor = new Color(0.78f, 0.77f, 0.85f, 1f);
    [SerializeField] private Color statusErrorColor = new Color(1f, 0.71f, 0.67f, 1f);

    [Header("Extras (任意。空欄なら使わない)")]
    [Tooltip("パスワードの表示/非表示を切り替えるボタン (目のアイコン)")]
    [SerializeField] private Button showPasswordButton;
    [Tooltip("パスワードが隠れているときに出すアイコン")]
    [SerializeField] private GameObject passwordHiddenIcon;
    [Tooltip("パスワードが見えているときに出すアイコン")]
    [SerializeField] private GameObject passwordVisibleIcon;
    [Tooltip("Remember this device のチェック。オフだと、アプリを閉じるとログアウトになる")]
    [SerializeField] private Toggle rememberToggle;
    [Tooltip("パスワード再設定メールを送るボタン")]
    [SerializeField] private Button forgotPasswordButton;

    private bool _busy;
    private bool _passwordVisible;

    private void Awake()
    {
        if (passwordInput != null) passwordInput.contentType = TMP_InputField.ContentType.Password;
        if (emailInput != null) emailInput.contentType = TMP_InputField.ContentType.EmailAddress;
        ApplyPasswordVisibility();
    }

    private void OnEnable()
    {
        if (signInButton != null) signInButton.onClick.AddListener(OnSignIn);
        if (signUpButton != null) signUpButton.onClick.AddListener(OnSignUp);
        if (signOutButton != null) signOutButton.onClick.AddListener(OnSignOut);
        // Enter キー (スマホでは、キーボードの「完了/改行」) で、次へ進む / ログインする
        if (emailInput != null) emailInput.onSubmit.AddListener(OnEmailSubmit);
        if (passwordInput != null) passwordInput.onSubmit.AddListener(OnPasswordSubmit);
        if (showPasswordButton != null) showPasswordButton.onClick.AddListener(OnTogglePassword);
        if (forgotPasswordButton != null) forgotPasswordButton.onClick.AddListener(OnForgotPassword);
        if (rememberToggle != null)
        {
            if (supabase != null) rememberToggle.SetIsOnWithoutNotify(supabase.RememberDevice);
            rememberToggle.onValueChanged.AddListener(OnRememberChanged);
        }
        if (supabase != null) supabase.AuthChanged += UpdateView;
        UpdateView();
    }

    private void OnDisable()
    {
        if (signInButton != null) signInButton.onClick.RemoveListener(OnSignIn);
        if (signUpButton != null) signUpButton.onClick.RemoveListener(OnSignUp);
        if (signOutButton != null) signOutButton.onClick.RemoveListener(OnSignOut);
        if (emailInput != null) emailInput.onSubmit.RemoveListener(OnEmailSubmit);
        if (passwordInput != null) passwordInput.onSubmit.RemoveListener(OnPasswordSubmit);
        if (showPasswordButton != null) showPasswordButton.onClick.RemoveListener(OnTogglePassword);
        if (forgotPasswordButton != null) forgotPasswordButton.onClick.RemoveListener(OnForgotPassword);
        if (rememberToggle != null) rememberToggle.onValueChanged.RemoveListener(OnRememberChanged);
        if (supabase != null) supabase.AuthChanged -= UpdateView;
    }

    private async void Start()
    {
        // 前回のログインを復元する (成功すると AuthChanged で画面が切り替わる)
        if (supabase == null) return;
        try { await supabase.TryRestoreSessionAsync(); }
        catch (Exception e) { Debug.LogWarning("[AuthPanel] restore failed: " + e.Message); }
        if (this != null) UpdateView();
    }

    // ------------------------------------------------------------
    private void UpdateView()
    {
        bool signedIn = supabase != null && supabase.IsSignedIn;

        if (signedOutGroup != null) signedOutGroup.SetActive(!signedIn);
        if (signedInGroup != null) signedInGroup.SetActive(signedIn);
        if (accountText != null)
            accountText.text = signedIn ? "Signed in as " + supabase.Email : "";

        if (signedIn && passwordInput != null) passwordInput.text = "";
        // 前回ログインしたメールアドレスを、最初から入れておく
        if (!signedIn && supabase != null && emailInput != null && string.IsNullOrEmpty(emailInput.text))
            emailInput.text = supabase.LastEmail;

        SetButtons(!_busy);
    }

    private void SetButtons(bool interactable)
    {
        if (signInButton != null) signInButton.interactable = interactable;
        if (signUpButton != null) signUpButton.interactable = interactable;
        if (signOutButton != null) signOutButton.interactable = interactable;
        if (forgotPasswordButton != null) forgotPasswordButton.interactable = interactable;
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (statusText == null) return;
        statusText.text = message ?? "";
        statusText.color = isError ? statusErrorColor : statusInfoColor;
    }

    private void SetError(string message) => SetStatus(message, true);

    private bool ValidateInput(out string email, out string password)
    {
        email = emailInput != null ? emailInput.text.Trim() : "";
        password = passwordInput != null ? passwordInput.text : "";

        if (supabase == null || !supabase.IsConfigured)
        {
            SetError("Supabase is not set up yet.");
            return false;
        }
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        {
            SetError("Please enter a valid email address.");
            return false;
        }
        if (password.Length < 6)
        {
            SetError("Password must be at least 6 characters.");
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------
    // メール欄で Enter → パスワード欄へ。パスワード欄で Enter → ログイン (Sign in ボタンと同じ)
    private void OnEmailSubmit(string _)
    {
        if (passwordInput != null) passwordInput.ActivateInputField();
    }

    private void OnPasswordSubmit(string _)
    {
        OnSignIn();
    }

    private async void OnSignIn()
    {
        if (_busy || !ValidateInput(out string email, out string password)) return;
        _busy = true; SetButtons(false); SetStatus("Signing in...");
        try
        {
            await supabase.SignInAsync(email, password);
            SetStatus("");
        }
        catch (Exception e) { SetError(e.Message); }
        finally { _busy = false; if (this != null) UpdateView(); }
    }

    private async void OnSignUp()
    {
        if (_busy || !ValidateInput(out string email, out string password)) return;
        _busy = true; SetButtons(false); SetStatus("Creating your account...");
        try
        {
            var result = await supabase.SignUpAsync(email, password);
            SetStatus(result.NeedsEmailConfirmation
                ? "Almost done! We sent a confirmation email. Open the link, then sign in."
                : "Account created. You are signed in.");
        }
        catch (Exception e) { SetError(e.Message); }
        finally { _busy = false; if (this != null) UpdateView(); }
    }

    // ------------------------------------------------------------
    // Extras
    private void OnTogglePassword()
    {
        _passwordVisible = !_passwordVisible;
        ApplyPasswordVisibility();
    }

    private void ApplyPasswordVisibility()
    {
        if (passwordInput != null)
        {
            passwordInput.contentType = _passwordVisible
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            passwordInput.ForceLabelUpdate();
        }
        if (passwordHiddenIcon != null) passwordHiddenIcon.SetActive(!_passwordVisible);
        if (passwordVisibleIcon != null) passwordVisibleIcon.SetActive(_passwordVisible);
    }

    private void OnRememberChanged(bool on)
    {
        if (supabase != null) supabase.RememberDevice = on;
    }

    private async void OnForgotPassword()
    {
        if (_busy) return;
        string email = emailInput != null ? emailInput.text.Trim() : "";
        if (supabase == null || !supabase.IsConfigured) { SetError("Supabase is not set up yet."); return; }
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        {
            SetError("Enter your email above, then tap \"Forgot password?\" again.");
            if (emailInput != null) emailInput.ActivateInputField();
            return;
        }

        _busy = true; SetButtons(false); SetStatus("Sending a reset link...");
        try
        {
            await supabase.SendPasswordResetAsync(email);
            SetStatus("If an account exists for " + email + ", we sent a link to reset the password. Open it, set a new password, then sign in here.");
        }
        catch (Exception e) { SetError(e.Message); }
        finally { _busy = false; if (this != null) UpdateView(); }
    }

    private async void OnSignOut()
    {
        if (_busy) return;
        _busy = true; SetButtons(false);
        try { await supabase.SignOutAsync(); SetStatus("Signed out."); }
        catch (Exception e) { SetError(e.Message); }
        finally { _busy = false; if (this != null) UpdateView(); }
    }
}
