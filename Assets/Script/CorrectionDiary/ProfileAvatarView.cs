using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>アバターに使う12色 (Web版と同じ並び)。番号は 0〜11。</summary>
public static class AvatarPalette
{
    public const int Count = 12;

    private static readonly int[] Rgb =
    {
        0xE8590C, 0xF59F00, 0x94D82D, 0x2B8A3E, 0x12B886, 0x15AABF,
        0x228BE6, 0x4C6EF5, 0x7950F2, 0xBE4BDB, 0xE64980, 0x868E96,
    };

    public static Color Get(int index)
    {
        int rgb = Rgb[((index % Count) + Count) % Count];
        return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}

/// <summary>
/// ユーザーのアバター (色の丸 + 名前の頭文字) と、名前を表示する。
/// ヘッダー右上の丸など、好きな場所に置ける。ログアウト中は、灰色の丸と「?」を表示する。
/// ※ 日本語の名前の頭文字を出すには、TextMeshPro の日本語フォントが必要。四角に化ける場合は、英数字の名前にする。
/// </summary>
public class ProfileAvatarView : MonoBehaviour
{
    [SerializeField] private SupabaseService supabase;

    [Header("UI (使わないものは空欄でOK)")]
    [Tooltip("色をつける丸い Image")]
    [SerializeField] private Image circle;
    [Tooltip("丸の中に出す頭文字")]
    [SerializeField] private TMP_Text initialText;
    [Tooltip("名前を出す文字 (任意)")]
    [SerializeField] private TMP_Text nameText;

    [Header("Options")]
    [Tooltip("ONにすると、ログイン中のアカウントに合わせて自動で更新する。OFFにすると、SetPreview() で指定した内容だけを出す (編集画面のプレビュー用)")]
    [SerializeField] private bool followAccount = true;
    [SerializeField] private Color signedOutColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    private void OnEnable()
    {
        if (supabase != null)
        {
            supabase.AuthChanged += Refresh;
            supabase.ProfileChanged += Refresh;
        }
        Refresh();
    }

    private void OnDisable()
    {
        if (supabase != null)
        {
            supabase.AuthChanged -= Refresh;
            supabase.ProfileChanged -= Refresh;
        }
    }

    /// <summary>ログイン中のアカウントに合わせて、表示を更新する。</summary>
    public void Refresh()
    {
        if (!followAccount) return;

        if (supabase == null || !supabase.IsSignedIn)
        {
            Show("", -1);
            return;
        }
        Show(supabase.DisplayNameOrFallback, supabase.Profile.AvatarColor);
    }

    /// <summary>指定した名前と色を、そのまま表示する (編集画面のプレビュー用)。colorIndex が負ならログアウト表示。</summary>
    public void SetPreview(string displayName, int colorIndex)
    {
        Show(displayName, colorIndex);
    }

    private void Show(string displayName, int colorIndex)
    {
        if (circle != null)
            circle.color = colorIndex < 0 ? signedOutColor : AvatarPalette.Get(colorIndex);
        if (initialText != null)
            initialText.text = colorIndex < 0 ? "?" : InitialOf(displayName);
        if (nameText != null)
            nameText.text = displayName ?? "";
    }

    // 絵文字などの2文字ぶんの文字も、1文字として取り出す
    internal static string InitialOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        name = name.Trim();
        string first = new StringInfo(name).SubstringByTextElements(0, 1);
        return first.ToUpperInvariant();
    }
}
