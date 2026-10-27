using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>苦手な発音の種類 (固定 10 + その他)。表示・例・YouTube の検索語</summary>
public static class SoundTypes
{
    public const string Other = "other";

    public static readonly string[] All =
        { "th", "r_l", "v_b", "f_h", "s_sh", "vowel", "long_short", "final_consonant", "extra_vowel", "stress" };

    private static readonly string[] Labels =
        { "TH", "R / L", "V / B", "F / H", "S / SH", "Vowels (æ ʌ ɑ ə)", "Long / short vowels",
          "Final consonants", "Extra vowels", "Word stress" };

    private static readonly string[] Examples =
        { "think / this", "right / light", "very / berry", "food / who'd", "see / she", "cat / cut / cot", "ship / sheep",
          "walked / hand", "stop → ストップ", "PHO-to-graph" };

    private static readonly string[] Queries =
    {
        "how to pronounce TH sound English pronunciation",
        "R and L sounds English pronunciation lesson",
        "V and B sounds English pronunciation lesson",
        "F sound English pronunciation lesson",
        "S and SH sounds English pronunciation lesson",
        "short vowels cat cut cot English pronunciation",
        "ship sheep long short vowels English pronunciation",
        "final consonant sounds English pronunciation",
        "stop adding vowels English pronunciation consonant clusters",
        "word stress English pronunciation lesson",
    };

    public static bool IsKnown(string code) => code == Other || Array.IndexOf(All, code) >= 0;

    /// <summary>アプリの表示。その他は "Other: z"</summary>
    public static string Label(string code, string otherName)
    {
        if (code == Other) return "Other: " + (otherName ?? "");
        int i = Array.IndexOf(All, code);
        return i >= 0 ? Labels[i] : code ?? "";
    }

    public static string Example(string code)
    {
        int i = Array.IndexOf(All, code);
        return i >= 0 ? Examples[i] : "";
    }

    public static string SearchQuery(string code, string otherName)
    {
        if (code == Other) return "how to pronounce " + (otherName ?? "") + " sound English pronunciation";
        int i = Array.IndexOf(All, code);
        return i >= 0 ? Queries[i] : "";
    }

    /// <summary>
    /// その他の名前をそろえる (SQL の _sound_label と同じ): 小文字 → カッコとその中身を取る → sound / sounds を取る →
    /// 英字・数字・空白・/ - だけ → 空白 1 つ → 前後の空白 → 30 文字まで
    /// </summary>
    public static string NormalizeOtherName(string name)
    {
        string s = (name ?? "").ToLowerInvariant();
        s = Regex.Replace(s, @"\([^)]*\)", " ");
        s = Regex.Replace(s, @"\bsounds?\b", " ");
        var sb = new StringBuilder();
        foreach (char c in s)
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ' || c == '/' || c == '-' || char.IsWhiteSpace(c))
                sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        if (s.Length > 30) s = s.Substring(0, 30).Trim();
        return s;
    }
}

/// <summary>苦手な音の 1 件 (どの音か・その他の名前・例の単語)</summary>
public class SoundHit
{
    public string Code;
    public string Label;   // その他のときのそろえた名前
    public string Word;
}

/// <summary>get_my_weak_sounds の 1 行</summary>
public class WeakSound
{
    public string Sound;
    public string Label;
    public int Points;
    public int? PreviousPoints;
    public System.Collections.Generic.List<string> Words = new System.Collections.Generic.List<string>();
}
