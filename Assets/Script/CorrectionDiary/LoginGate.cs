using System;
using UnityEngine;

/// <summary>
/// ログインできたら、指定したオブジェクトを SetActive で「出す / 消す」。ログアウトすると、逆になる。
///
///   Show When Signed In   : ログイン中だけ表示する (ログアウト中は消える)
///   Hide When Signed In   : ログイン中は消す       (ログアウト中は表示する。例: ログイン画面)
///
/// ・前回のログインが残っているときは、アプリを開いたときにも、自動で切り替わる。
/// ・このスクリプトは、上の2つのリストに入れたオブジェクトとは別の、いつも表示されているオブジェクト
///   (Canvas や Supabase のオブジェクトなど) に付けること。消えてしまうと、元に戻せなくなる。
/// </summary>
public class LoginGate : MonoBehaviour
{
    [SerializeField] private SupabaseService supabase;

    [Header("ログインしたら 表示する (ログアウトしたら消す)")]
    [SerializeField] private GameObject[] showWhenSignedIn;

    [Header("ログインしたら 消す (ログアウトしたら表示する)")]
    [SerializeField] private GameObject[] hideWhenSignedIn;

    [Header("Options")]
    [Tooltip("アプリを開いたとき、前回のログインを復元する (AuthPanel と一緒に使っても、通信は1回だけ)")]
    [SerializeField] private bool restoreOnStart = true;

    private void OnEnable()
    {
        if (supabase != null) supabase.AuthChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        if (supabase != null) supabase.AuthChanged -= Apply;
    }

    private async void Start()
    {
        if (!restoreOnStart || supabase == null) return;
        try { await supabase.TryRestoreSessionAsync(); }
        catch (Exception e) { Debug.LogWarning("[LoginGate] restore failed: " + e.Message); }
        if (this != null) Apply();
    }

    /// <summary>いまのログイン状態に合わせて、オブジェクトを出し入れする。</summary>
    public void Apply()
    {
        bool signedIn = supabase != null && supabase.IsSignedIn;
        SetAll(showWhenSignedIn, signedIn);
        SetAll(hideWhenSignedIn, !signedIn);
    }

    private void SetAll(GameObject[] objects, bool active)
    {
        if (objects == null) return;
        foreach (var go in objects)
        {
            if (go == null) continue;
            if (go == gameObject)
            {
                Debug.LogWarning("[LoginGate] This script's own object is in a list; skipped. Put LoginGate on an object that is always active.");
                continue;
            }
            if (go.activeSelf != active) go.SetActive(active);
        }
    }
}
