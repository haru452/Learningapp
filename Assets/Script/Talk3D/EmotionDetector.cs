using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// LLM の返事やユーザーの発話から「感情」を読み取る。
///
///  1. 感情タグ:  [happy] / (happy) / *happy* / &lt;happy&gt; のような書き方を読み取る（大文字小文字は区別しない）
///  2. タグが無いとき: 文中の言葉（sorry, wow, great など）から推測する
///
/// 返す感情名は CharacterAnimator.SetEmotion で使える
/// happy / sad / angry / surprised / relaxed / neutral のどれか。分からなければ null。
/// </summary>
public static class EmotionDetector
{
    public const string Happy = "happy";
    public const string Sad = "sad";
    public const string Angry = "angry";
    public const string Surprised = "surprised";
    public const string Relaxed = "relaxed";
    public const string Neutral = "neutral";

    /// <summary>LLM のシステムプロンプトに付け足す指示（英語の方が小さいモデルでも守られやすい）</summary>
    public const string PromptInstruction =
        "Start every reply with exactly one emotion tag that matches how you feel, " +
        "chosen from: [happy], [sad], [angry], [surprised], [relaxed], [neutral]. " +
        "For example: \"[happy] That sounds like a lot of fun!\" or \"[sad] I'm sorry to hear that.\" " +
        "Do not use any other tags.";

    // 括弧やアスタリスクで囲まれた1〜2語（例: [happy] (big smile) *laughs*）
    static readonly Regex Bracketed = new Regex(
        @"\[\s*([A-Za-z]+(?:[ \-][A-Za-z]+)?)\s*\]|\(\s*([A-Za-z]+(?:[ \-][A-Za-z]+)?)\s*\)|\*\s*([A-Za-z]+(?:[ \-][A-Za-z]+)?)\s*\*|<\s*([A-Za-z]+)\s*>",
        RegexOptions.Compiled);

    // 角括弧の中身は、感情でなくても読み上げない（[laughs] など）
    static readonly Regex AnySquareTag = new Regex(@"\[[^\]\n]{1,30}\]", RegexOptions.Compiled);

    static readonly Dictionary<string, string> Synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "happy", Happy }, { "joy", Happy }, { "joyful", Happy }, { "smile", Happy }, { "smiling", Happy },
        { "big smile", Happy }, { "excited", Happy }, { "laugh", Happy }, { "laughs", Happy }, { "laughing", Happy },
        { "cheerful", Happy }, { "glad", Happy }, { "delighted", Happy }, { "giggle", Happy }, { "giggles", Happy },
        { "smiles", Happy }, { "grin", Happy }, { "grins", Happy }, { "chuckle", Happy }, { "chuckles", Happy },
        { "beams", Happy }, { "blushes", Happy }, { "cheers", Happy },

        { "sad", Sad }, { "sorry", Sad }, { "upset", Sad }, { "unhappy", Sad }, { "disappointed", Sad },
        { "sigh", Sad }, { "sighs", Sad }, { "worried", Sad }, { "concerned", Sad },
        { "frown", Sad }, { "frowns", Sad }, { "cries", Sad }, { "sniff", Sad }, { "sniffs", Sad },

        { "angry", Angry }, { "mad", Angry }, { "annoyed", Angry }, { "frustrated", Angry }, { "pout", Angry }, { "pouts", Angry },

        { "surprised", Surprised }, { "surprise", Surprised }, { "shocked", Surprised }, { "amazed", Surprised },
        { "gasp", Surprised }, { "gasps", Surprised }, { "wow", Surprised }, { "curious", Surprised },

        { "relaxed", Relaxed }, { "calm", Relaxed }, { "gentle", Relaxed }, { "warm", Relaxed },
        { "relieved", Relaxed }, { "peaceful", Relaxed }, { "soft smile", Relaxed }, { "kind", Relaxed },
        { "nods", Relaxed }, { "smiles softly", Relaxed },

        { "neutral", Neutral }, { "normal", Neutral }, { "none", Neutral },
    };

    /// <summary>感情名を正規化する（例: "Smile" → "happy"）。感情でなければ null。</summary>
    public static string Normalize(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        string key = word.Trim().Replace('-', ' ');
        return Synonyms.TryGetValue(key, out string e) ? e : null;
    }

    /// <summary>文字列の中の最初の感情タグを返す。無ければ null。</summary>
    public static string FindTag(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (Match m in Bracketed.Matches(text))
        {
            string e = Normalize(GroupValue(m));
            if (e != null) return e;
        }
        return null;
    }

    /// <summary>読み上げ・表示用に、感情タグ（と角括弧のタグ）を取り除く。</summary>
    public static string StripTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string s = Bracketed.Replace(text, m => Normalize(GroupValue(m)) != null ? "" : m.Value);
        s = AnySquareTag.Replace(s, "");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    // ------------------------------------------------------------ 言葉から推測

    sealed class KeywordRule
    {
        public readonly string Emotion;
        public readonly Regex Pattern;
        public KeywordRule(string emotion, string pattern)
        {
            Emotion = emotion;
            Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }

    static readonly KeywordRule[] Keywords =
    {
        new KeywordRule(Sad, @"\b(sorry|sad|unfortunately|miss (you|it|them)|lonely|tough|hard time|that'?s too bad|oh no|tired|sick|hurt|cried|cry)\b"),
        new KeywordRule(Surprised, @"\b(wow|whoa|really\?|no way|amazing|incredible|unbelievable|surprised|seriously\?|oh!)"),
        new KeywordRule(Angry, @"\b(angry|annoyed|annoying|frustrat\w*|unfair|mad at|hate)\b"),
        new KeywordRule(Happy, @"\b(great|awesome|glad|happy|love|wonderful|fantastic|nice|yay|haha|fun|excited|congrat\w*|perfect|well done|good job|cool)\b"),
        new KeywordRule(Relaxed, @"\b(relax\w*|calm|take it easy|peaceful|rest|cozy|comfortable|no worries|it'?s okay|don'?t worry)\b"),
    };

    /// <summary>
    /// タグが無いときに、文中の言葉から感情を推測する。見つからなければ null。
    /// （悲しい言葉を優先する。例: "Sorry, that's great" は「悲しい」に近い文脈が多いため）
    /// </summary>
    public static string FromWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (KeywordRule rule in Keywords)
        {
            if (rule.Pattern.IsMatch(text)) return rule.Emotion;
        }
        return null;
    }

    /// <summary>タグを優先し、無ければ言葉から推測する。</summary>
    public static string Detect(string text) => FindTag(text) ?? FromWords(StripTags(text));

    static string GroupValue(Match m)
    {
        for (int i = 1; i < m.Groups.Count; i++)
            if (m.Groups[i].Success) return m.Groups[i].Value;
        return null;
    }
}
