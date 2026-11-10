using UnityEngine;

/// <summary>
/// アプリ全体の見た目 (濃い紺 + 水色) の決まりを 1 か所にまとめる。画面を作り直すときは、ここの値だけを使う。
/// Canvas は Constant Pixel Size なので、大きさの単位は画素 (48dp ≒ 120 画素)。
/// </summary>
public static class UiTheme
{
    // ---------------- 色 ----------------
    public static readonly Color Background = Hex("#0B0F1A");      // 画面の背景・下のタブ
    public static readonly Color Surface = Hex("#111827");         // カード・入力欄
    public static readonly Color SurfaceRaised = Hex("#1F2440");   // 2 番目のボタン・選ばれたタブ
    public static readonly Color Sheet = Hex("#1E2235");           // 下から出るシート
    public static readonly Color Primary = Hex("#22D3EE");         // 主なボタン・強調 (水色)
    public static readonly Color OnPrimary = Hex("#0B0F1A");       // 水色の上の文字
    public static readonly Color Text = Color.white;
    public static readonly Color SubText = Hex("#9CA3AF");         // 説明・補足 (紺の上で 7:1 以上)
    public static readonly Color Border = Hex("#22D3EE", 0.25f);   // カードの縁
    public static readonly Color Divider = Hex("#22D3EE", 0.18f);
    public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.6f);
    public static readonly Color Success = Hex("#4ADE80");
    public static readonly Color Warning = Hex("#FBBF24");
    public static readonly Color Danger = Hex("#F87171");

    // ---------------- 文字の大きさ ----------------
    public const float TitleSize = 52f;     // 画面の題
    public const float HeadingSize = 40f;   // まとまりの見出し
    public const float BodySize = 32f;      // 本文・ボタン
    public const float CaptionSize = 26f;   // 補足 (これより小さくしない)

    // ---------------- 大きさ・間隔 ----------------
    public const float MinTouch = 120f;     // 押せる物の最小の高さ (48dp)
    public const float ButtonHeight = 128f;
    public const float Gutter = 40f;        // 画面の左右の余白
    public const float Gap = 24f;           // 部品の間
    public const float SectionGap = 48f;    // まとまりの間

    public static Color Hex(string hex, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        c.a = alpha;
        return c;
    }
}
