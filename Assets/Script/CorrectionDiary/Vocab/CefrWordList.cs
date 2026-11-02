using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// CEFR-J Vocabulary Profile 1.5 (Assets/Resources/CefrjWords.json) を読み、単語のレベルを決める。
/// 辞書の品詞と照合し、一致しなければその語の一番低いレベル。CEFR-J に無い語は "—"
/// </summary>
public class CefrWordList
{
    public const string None = "—";
    public static readonly string[] Levels = { "A1", "A2", "B1", "B2" };

    private readonly Dictionary<string, List<(string pos, string cefr)>> _byWord =
        new Dictionary<string, List<(string pos, string cefr)>>();

    /// <summary>[{w, pos, cefr}]。読めないときは空のリスト</summary>
    public static CefrWordList Parse(string json)
    {
        var list = new CefrWordList();
        try
        {
            if (!(MiniJson.Parse(json ?? "") is List<object> rows)) return list;
            foreach (var r in rows.OfType<Dictionary<string, object>>())
            {
                string w = VocabDeck.Key(TatoebaCatalog.Str(r, "w"));
                string cefr = TatoebaCatalog.Str(r, "cefr").Trim().ToUpperInvariant();
                if (w.Length == 0 || LevelIndex(cefr) < 0) continue;
                if (!list._byWord.TryGetValue(w, out var entries)) list._byWord[w] = entries = new List<(string, string)>();
                entries.Add((TatoebaCatalog.Str(r, "pos").Trim().ToLowerInvariant(), cefr));
            }
        }
        catch (FormatException) { list._byWord.Clear(); }
        return list;
    }

    public int Count => _byWord.Count;

    /// <summary>辞書の品詞 (いくつでも) と照合したレベル</summary>
    public string LevelFor(string word, IEnumerable<string> dictPos)
    {
        if (!_byWord.TryGetValue(VocabDeck.Key(word), out var entries)) return None;
        var matched = (dictPos ?? Enumerable.Empty<string>()).Select(p => LevelForPos(word, p)).Where(l => l != null).ToList();
        return Lowest(matched.Count > 0 ? matched : entries.Select(e => e.cefr));
    }

    /// <summary>辞書の品詞 1 つに当たるレベル。当たらなければ null</summary>
    public string LevelForPos(string word, string dictPos)
    {
        if (!_byWord.TryGetValue(VocabDeck.Key(word), out var entries)) return null;
        var names = CefrPosNames(dictPos);
        var hits = entries.Where(e => names.Contains(e.pos)).Select(e => e.cefr).ToList();
        return hits.Count > 0 ? Lowest(hits) : null;
    }

    /// <summary>一番低いレベルが level の語</summary>
    public IEnumerable<string> WordsAt(string level) =>
        _byWord.Where(kv => Lowest(kv.Value.Select(e => e.cefr)) == level).Select(kv => kv.Key);

    public bool Contains(string word) => _byWord.ContainsKey(VocabDeck.Key(word));

    public static int LevelIndex(string level) => Array.IndexOf(Levels, level);

    private static string Lowest(IEnumerable<string> levels) =>
        levels.Where(l => LevelIndex(l) >= 0).OrderBy(LevelIndex).FirstOrDefault() ?? None;

    // 辞書 (Merriam-Webster) の品詞 → CEFR-J の品詞の名前
    private static HashSet<string> CefrPosNames(string dictPos)
    {
        string p = (dictPos ?? "").Trim().ToLowerInvariant();
        switch (p)
        {
            case "definite article":
            case "indefinite article":
            case "article":
            case "determiner":
                return new HashSet<string> { "determiner" };
            case "auxiliary verb":
            case "modal verb":
                return new HashSet<string> { "modal auxiliary", "verb", "be-verb", "do-verb", "have-verb" };
            case "verb":
                return new HashSet<string> { "verb", "be-verb", "do-verb", "have-verb" };
            case "noun":
            case "adjective":
                return new HashSet<string> { p, "number" };
            default:
                return new HashSet<string> { p };
        }
    }
}
