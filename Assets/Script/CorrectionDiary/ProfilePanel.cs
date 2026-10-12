using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユーザー名とアバター(12色)を設定する画面。ログイン中だけ使える。
///   ・名前を入力 (20文字まで)
///   ・◀ ▶ ボタンで、アバターの色を選ぶ (プレビューに反映される)
///   ・Save で、クラウドに保存 (Web版でも同じ名前・色になる)
/// 画面の出し入れは、既存のUI切り替えのコードで行ってください。
/// </summary>
public class ProfilePanel : MonoBehaviour
{
    [SerializeField] private SupabaseService supabase;

    [Header("Inputs")]
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private Button prevColorButton;
    [SerializeField] private Button nextColorButton;
    [SerializeField] private Button saveButton;

    [Header("Preview")]
    [Tooltip("プレビュー用の ProfileAvatarView。Follow Account は OFF にしておく")]
    [SerializeField] private ProfileAvatarView preview;

    [Header("Status")]
    [Tooltip("任意: 「Saved」やエラーを表示する文字")]
    [SerializeField] private TMP_Text statusText;
    [Tooltip("任意: ログインしていないときに、この画面の中身を隠すためのグループ")]
    [SerializeField] private GameObject contentGroup;
    [Tooltip("任意: ログインしていないときに出す案内の文字")]
    [SerializeField] private TMP_Text signedOutHint;

    private int _color;
    private bool _busy;

    private void Awake()
    {
        if (nameInput != null)
        {
            nameInput.characterLimit = 20;
            nameInput.lineType = TMP_InputField.LineType.SingleLine;
        }
    }

    private void OnEnable()
    {
        if (prevColorButton != null) prevColorButton.onClick.AddListener(OnPrev);
        if (nextColorButton != null) nextColorButton.onClick.AddListener(OnNext);
        if (saveButton != null) saveButton.onClick.AddListener(OnSave);
        if (nameInput != null) nameInput.onValueChanged.AddListener(OnNameChanged);
        if (supabase != null)
        {
            supabase.AuthChanged += OnAuthChanged;
            supabase.ProfileChanged += OnProfileChanged;
        }
        LoadFromAccount();
    }

    private void OnDisable()
    {
        if (prevColorButton != null) prevColorButton.onClick.RemoveListener(OnPrev);
        if (nextColorButton != null) nextColorButton.onClick.RemoveListener(OnNext);
        if (saveButton != null) saveButton.onClick.RemoveListener(OnSave);
        if (nameInput != null) nameInput.onValueChanged.RemoveListener(OnNameChanged);
        if (supabase != null)
        {
            supabase.AuthChanged -= OnAuthChanged;
            supabase.ProfileChanged -= OnProfileChanged;
        }
    }

    // ------------------------------------------------------------
    private bool _dirty;     // 利用者が、名前や色を変更している途中か
    private bool _loading;   // コードから入力欄を書き換えているところ (変更として扱わない)

    private void OnAuthChanged()
    {
        _dirty = false;
        LoadFromAccount();
    }

    // ログイン直後にプロフィールが読み込まれたときや、保存が終わったとき。入力の途中なら、入力を消さない
    private void OnProfileChanged()
    {
        if (_dirty) { ApplyVisibility(); UpdatePreview(); return; }
        LoadFromAccount();
    }

    private void LoadFromAccount()
    {
        bool signedIn = supabase != null && supabase.IsSignedIn;
        ApplyVisibility();

        _loading = true;
        if (nameInput != null) nameInput.text = signedIn ? supabase.Profile.DisplayName : "";
        _loading = false;

        _color = signedIn ? supabase.Profile.AvatarColor : 0;
        UpdatePreview();
    }

    private void ApplyVisibility()
    {
        bool signedIn = supabase != null && supabase.IsSignedIn;
        if (contentGroup != null) contentGroup.SetActive(signedIn);
        if (signedOutHint != null)
        {
            signedOutHint.gameObject.SetActive(!signedIn);
            signedOutHint.text = "Sign in to set your name and avatar.";
        }
    }

    private void OnNameChanged(string _)
    {
        if (!_loading) _dirty = true;
        UpdatePreview();
    }

    private void OnPrev() { _color = (_color + AvatarPalette.Count - 1) % AvatarPalette.Count; _dirty = true; UpdatePreview(); }
    private void OnNext() { _color = (_color + 1) % AvatarPalette.Count; _dirty = true; UpdatePreview(); }

    private void UpdatePreview()
    {
        if (preview == null) return;
        string name = nameInput != null ? nameInput.text : "";
        if (string.IsNullOrWhiteSpace(name) && supabase != null) name = supabase.DisplayNameOrFallback;
        preview.SetPreview(name, supabase != null && supabase.IsSignedIn ? _color : -1);
    }

    private async void OnSave()
    {
        if (_busy || supabase == null || !supabase.IsSignedIn) return;
        _busy = true;
        if (saveButton != null) saveButton.interactable = false;
        SetStatus("Saving...");
        try
        {
            string name = nameInput != null ? nameInput.text.Trim() : "";
            _dirty = false;   // 保存の完了で ProfileChanged が来たとき、保存した内容で入れ直してよい
            await supabase.SaveProfileAsync(name, _color);
            SetStatus("Saved!");
        }
        catch (Exception e)
        {
            _dirty = true;    // 保存できなかったので、入力は残す
            SetStatus(e.Message);
        }
        finally
        {
            _busy = false;
            if (this != null && saveButton != null) saveButton.interactable = true;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message ?? "";
    }
}
