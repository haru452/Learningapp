using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>短い答えの練習の 1 問 (Gemini が作る)</summary>
public class ShortAnswerQuestion
{
    public string Question = "";
    public List<string> Answers = new List<string>();   // 正しい答えの言い方 (key / keys / a key)
}

/// <summary>1 問の結果</summary>
public class ShortAnswerResult
{
    public string Question = "";
    public List<string> Answers = new List<string>();
    public string Heard = "";
    public bool Correct;
}

/// <summary>
/// 聞き取った答えが正しいかを決める (Unity に依存しない)。
/// 小文字にして記号を除き、a / an / the を無視し、数字は英語の数に直し、語尾の s / es をそろえてから、
/// 正しい答えのどれかが聞き取った文の中に「単語の並び」として入っていれば正解。
/// </summary>
public static class ShortAnswerJudge
{
    private static readonly HashSet<string> Articles = new HashSet<string> { "a", "an", "the", "some" };
    private static readonly string[] Numbers =
        { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
          "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" };

    public static bool IsCorrect(string heard, IEnumerable<string> answers)
    {
        var said = Tokens(heard);
        if (said.Count == 0) return false;
        foreach (var a in answers ?? Enumerable.Empty<string>())
        {
            var target = Tokens(a);
            if (target.Count > 0 && ContainsRun(said, target)) return true;
        }
        return false;
    }

    /// <summary>比べるための単語の並び</summary>
    public static List<string> Tokens(string text)
    {
        var result = new List<string>();
        foreach (Match m in Regex.Matches((text ?? "").ToLowerInvariant(), "[a-z0-9']+"))
        {
            string t = m.Value.Trim('\'');
            if (t.Length == 0 || Articles.Contains(t)) continue;
            if (int.TryParse(t, out int n) && n >= 0 && n < Numbers.Length) t = Numbers[n];
            result.Add(Stem(t));
        }
        return result;
    }

    // keys → key, boxes → box, glasses → glass (短い語・ss で終わる語はそのまま)
    private static string Stem(string t)
    {
        if (t.Length <= 3 || t.EndsWith("ss")) return t;
        if (t.EndsWith("ies") && t.Length > 4) return t.Substring(0, t.Length - 3) + "y";
        if (t.EndsWith("ches") || t.EndsWith("shes") || t.EndsWith("xes") || t.EndsWith("sses")) return t.Substring(0, t.Length - 2);
        if (t.EndsWith("s")) return t.Substring(0, t.Length - 1);
        return t;
    }

    private static bool ContainsRun(List<string> said, List<string> target)
    {
        for (int i = 0; i + target.Count <= said.Count; i++)
        {
            bool ok = true;
            for (int j = 0; j < target.Count && ok; j++) ok = said[i + j] == target[j];
            if (ok) return true;
        }
        return false;
    }
}
