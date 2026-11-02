using System;
using System.Linq;
using System.Text;

/// <summary>
/// 発音チェックの 1 段階目: 端末が聞き取った文字と単語を比べる。
/// clear = 同じ語がある (大文字・記号は無視。複数形・-ing は別の語)、close = 1/3 くらいまでの違い、again = それ以外
/// </summary>
public static class VocabPronJudge
{
    public const string Clear = "clear";
    public const string Close = "close";
    public const string Again = "again";

    public static string Judge(string target, string heard)
    {
        var t = Words(target);
        var h = Words(heard);
        if (t.Length == 0 || h.Length < t.Length) return h.Length == 0 || t.Length == 0 ? Again : CompareWhole(t, h);
        string want = string.Join(" ", t);
        int limit = Math.Max(1, want.Length / 3);
        string best = Again;
        for (int i = 0; i + t.Length <= h.Length; i++)
        {
            string got = string.Join(" ", h.Skip(i).Take(t.Length));
            if (got == want) return Clear;
            if (Distance(want, got) <= limit) best = Close;
        }
        return best;
    }

    /// <summary>良い方の判定 (clear > close > again > "")</summary>
    public static string Better(string a, string b) => Rank(a) >= Rank(b) ? (a ?? "") : (b ?? "");

    private static int Rank(string v) => v == Clear ? 3 : v == Close ? 2 : v == Again ? 1 : 0;

    // 聞き取れた語が単語より少ないとき (2 語の単語を 1 語に聞き取ったなど): 全体で比べる
    private static string CompareWhole(string[] t, string[] h)
    {
        string want = string.Join(" ", t), got = string.Join(" ", h);
        return Distance(want.Replace(" ", ""), got.Replace(" ", "")) <= Math.Max(1, want.Length / 3) ? Close : Again;
    }

    private static string[] Words(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in (s ?? "").ToLowerInvariant())
            sb.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : char.IsWhiteSpace(c) || c == '-' ? ' ' : '\0');
        return sb.ToString().Replace("\0", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
