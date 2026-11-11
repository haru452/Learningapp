using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Talk で、今日の単語帳の単語とレベルを Lufka への指示に足す。あなたが単語を使えたかも判定する。
/// Unity に依存しない (テストで確かめる)。
/// </summary>
public static class TalkPlan
{
    public const int MaxWords = 5;
    public const string DefaultLevel = "B1";

    /// <summary>元の指示の後ろに足す部分の目印 (会話を始めるたびに、ここから後ろを作り直す)</summary>
    public const string Marker = "\n\n[Practice]\n";

    /// <summary>
    /// 会話に混ぜる単語を選ぶ: (1) 今日 (地元の日付) 評価した語を新しい順 (2) 学習中の語を最後に評価した新しい順
    /// (3) 覚えた語を次の復習が早い順。重なりなし・最大 max 語
    /// </summary>
    public static List<string> PickWords(IList<VocabReview> reviews, IList<VocabCard> cards, DateTime nowLocal, int max = MaxWords)
    {
        var result = new List<string>();
        void Add(string w)
        {
            w = (w ?? "").Trim().ToLowerInvariant();
            if (w.Length > 0 && result.Count < max && !result.Contains(w)) result.Add(w);
        }
        foreach (var r in (reviews ?? new VocabReview[0]).Where(r => r != null && r.At.ToLocalTime().Date == nowLocal.Date)
                                                          .OrderByDescending(r => r.At))
            Add(r.Word);
        var deck = (cards ?? new VocabCard[0]).Where(c => c != null).ToList();
        foreach (var c in deck.Where(c => c.Status == VocabCard.Learning).OrderByDescending(c => c.LastReviewAt ?? DateTime.MinValue))
            Add(c.Word);
        foreach (var c in deck.Where(c => c.Status == VocabCard.Known).OrderBy(c => c.DueAt ?? DateTime.MaxValue))
            Add(c.Word);
        return result;
    }

    /// <summary>Lufka への指示 (英語): レベルに合わせた言葉づかいと、今日の単語を混ぜること</summary>
    public static string Instruction(string level, IList<string> words)
    {
        string lv = string.IsNullOrWhiteSpace(level) ? DefaultLevel : level.Trim();
        string s = "The user is an " + lv + " English learner. Use short, simple words and sentences.";
        if (words != null && words.Count > 0)
            s += " Today the user is learning these words: " + string.Join(", ", words) +
                 ". Naturally use one of them in some replies, and ask questions that make the user want to use them.";
        return s;
    }

    /// <summary>元の指示に練習の指示を足す。前に足した分 (Marker から後ろ) は消してから足す</summary>
    public static string WithInstruction(string basePrompt, string instruction)
    {
        string b = basePrompt ?? "";
        int i = b.IndexOf(Marker, StringComparison.Ordinal);
        if (i >= 0) b = b.Substring(0, i);
        return b + Marker + instruction;
    }

    /// <summary>発話の中で使えた単語 (words の順)。大文字小文字と、よくある形の変化 (went, studies, running など) を同じ語とみなす</summary>
    public static List<string> UsedWords(string utterance, IList<string> words)
    {
        var forms = new HashSet<string>();
        foreach (string raw in (utterance ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            string w = new string(raw.ToLowerInvariant().Where(c => char.IsLetter(c) || c == '\'').ToArray()).Trim('\'');
            if (w.Length == 0) continue;
            foreach (string f in BaseForms(w)) forms.Add(f);
        }
        return (words ?? new string[0]).Where(x => x != null && forms.Contains(x.Trim().ToLowerInvariant())).ToList();
    }

    // 語から考えられる元の形 (その語自身も含む)
    private static IEnumerable<string> BaseForms(string w)
    {
        yield return w;
        if (Irregular.TryGetValue(w, out string irr)) yield return irr;
        if (w.EndsWith("ies") && w.Length > 4) yield return w.Substring(0, w.Length - 3) + "y";
        if (w.EndsWith("ied") && w.Length > 4) yield return w.Substring(0, w.Length - 3) + "y";
        if (w.EndsWith("es") && w.Length > 3) yield return w.Substring(0, w.Length - 2);
        if (w.EndsWith("s") && w.Length > 3) yield return w.Substring(0, w.Length - 1);
        foreach (string suffix in new[] { "ed", "ing" })
        {
            if (!w.EndsWith(suffix) || w.Length <= suffix.Length + 2) continue;
            string stem = w.Substring(0, w.Length - suffix.Length);
            yield return stem;                 // played → play
            yield return stem + "e";           // making → make
            if (stem.Length >= 3 && stem[stem.Length - 1] == stem[stem.Length - 2])
                yield return stem.Substring(0, stem.Length - 1);   // running → run
        }
    }

    // ------------------------------------------------------------
    // 黙っているとき: 今日の単語を使いたくなる質問を Lufka にさせる
    // ------------------------------------------------------------
    /// <summary>この秒数だけ黙っていたら、Lufka から質問する</summary>
    public const float SilenceSeconds = 5f;
    /// <summary>あなたが話さないまま続けて質問するのは、この回数まで (話したら数え直す)</summary>
    public const int MaxNudgesInRow = 2;

    /// <summary>
    /// 次に使わせたい単語: まだ使っていない語のうち、まだ質問に使っていない最初の語。
    /// 全部質問に使ったなら、使っていない語の最初から。使っていない語が無ければ null
    /// </summary>
    public static string NextNudgeWord(IEnumerable<string> words, IEnumerable<string> used, IEnumerable<string> nudged)
    {
        var usedSet = new HashSet<string>(used ?? new string[0]);
        var nudgedSet = new HashSet<string>(nudged ?? new string[0]);
        var unused = (words ?? new string[0]).Where(w => !string.IsNullOrWhiteSpace(w) && !usedSet.Contains(w)).ToList();
        return unused.FirstOrDefault(w => !nudgedSet.Contains(w)) ?? unused.FirstOrDefault();
    }

    /// <summary>
    /// Lufka への頼み (あなたの発話の代わりに送る。画面には出さない)。
    /// 答えるとその単語を自然に使いたくなる、短い質問を 1 つだけ
    /// </summary>
    public static string NudgeQuery(string word) =>
        "(The user has been quiet for a few seconds. Do not mention that they are quiet. " +
        "Ask one short, friendly, easy question about their life or opinions, so that the most natural answer uses the word \"" +
        (word ?? "").Trim() + "\". You may use the word in your question as a hint. Only ask the question.)";

    // 不規則動詞の過去形・過去分詞 → 元の形
    private static readonly Dictionary<string, string> Irregular = BuildIrregular(
        "go:went,gone", "be:was,were,been,is,are,am", "have:had,has", "do:did,done,does", "make:made", "take:took,taken",
        "see:saw,seen", "come:came", "get:got,gotten", "know:knew,known", "think:thought", "say:said", "tell:told",
        "find:found", "give:gave,given", "buy:bought", "eat:ate,eaten", "write:wrote,written", "run:ran",
        "begin:began,begun", "feel:felt", "leave:left", "meet:met", "bring:brought", "teach:taught");

    private static Dictionary<string, string> BuildIrregular(params string[] rows)
    {
        var d = new Dictionary<string, string>();
        foreach (string row in rows)
        {
            string[] p = row.Split(':');
            foreach (string form in p[1].Split(',')) d[form] = p[0];
        }
        return d;
    }
}
