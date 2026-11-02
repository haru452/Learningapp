using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Merriam-Webster Learner's Dictionary (dictionaryapi.com) の返事を読む。純粋な関数だけ。
///  - 見つからないときは文字列の配列 (似た単語) が返る
///  - 見出し (meta.id の ':' より前) が単語と同じエントリーだけ使う (無ければ最初のエントリー)
/// </summary>
public static class MerriamWebsterParser
{
    public const string AudioBase = "https://media.merriam-webster.com/audio/prons/en/us/mp3/";
    private const int MaxExamples = 2;

    /// <summary>JSON として読めないときは FormatException</summary>
    public static DictionaryResult Parse(string json, string word)
    {
        if (!(MiniJson.Parse(json ?? "") is List<object> list)) throw new FormatException("not an array");
        var result = new DictionaryResult();
        var entries = list.OfType<Dictionary<string, object>>().ToList();
        if (entries.Count == 0)
        {
            result.Suggestions = list.OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            return result;
        }

        string key = (word ?? "").Trim().ToLowerInvariant();
        var same = entries.Where(e => IdWord(e).ToLowerInvariant() == key).ToList();
        if (same.Count == 0) same.Add(entries[0]);
        result.Entries = same.Select(ReadEntry).ToList();
        result.Found = result.Entries.Count > 0;
        return result;
    }

    public const string KeyProblem = "The dictionary did not accept the request. Check merriamWebsterApiKey in ApiSecrets.json.";

    /// <summary>返事が JSON の配列でない (キーの間違い・HTML のエラーページなど) ときの画面の文。問題なければ null</summary>
    public static string ProblemMessage(string body)
    {
        try { return MiniJson.Parse(body ?? "") is List<object> ? null : KeyProblem; }
        catch (FormatException) { return KeyProblem; }
    }

    /// <summary>Parse と同じ。ただし JSON でない返事は DictionaryKeyException (通信の失敗と分けるため)</summary>
    public static DictionaryResult ParseOrThrow(string body, string word)
    {
        string problem = ProblemMessage(body);
        if (problem != null) throw new DictionaryKeyException(problem);
        return Parse(body, word);
    }

    /// <summary>音声ファイル名 → URL。フォルダ: bix / gg / number (数字・記号で始まる) / 先頭の 1 文字</summary>
    public static string AudioUrl(string audio)
    {
        if (string.IsNullOrWhiteSpace(audio)) return "";
        audio = audio.Trim();
        string folder = audio.StartsWith("bix", StringComparison.Ordinal) ? "bix"
            : audio.StartsWith("gg", StringComparison.Ordinal) ? "gg"
            : !char.IsLetter(audio[0]) ? "number"
            : audio.Substring(0, 1).ToLowerInvariant();
        return AudioBase + folder + "/" + audio + ".mp3";
    }

    /// <summary>{it} {bc} {wi} などの記号を取る。リンク ({sx|w||} {a_link|w} {d_link|w|id}) は語を残す</summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string s = text.Replace("{ldquo}", "“").Replace("{rdquo}", "”");
        s = Regex.Replace(s, @"\{(?:sx|a_link|d_link|i_link|et_link|mat|dxt)\|([^|}]*)[^}]*\}", "$1");
        s = Regex.Replace(s, @"\{[^}]*\}", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static string IdWord(Dictionary<string, object> e)
    {
        string id = e.TryGetValue("meta", out object m) && m is Dictionary<string, object> meta ? JsonGet.Str(meta, "id") : "";
        int colon = id.IndexOf(':');
        return (colon >= 0 ? id.Substring(0, colon) : id).Trim();
    }

    private static DictEntry ReadEntry(Dictionary<string, object> e)
    {
        var entry = new DictEntry { Pos = JsonGet.Str(e, "fl").Trim() };
        var hwi = e.TryGetValue("hwi", out object h) ? h as Dictionary<string, object> : null;
        string hw = hwi != null ? JsonGet.Str(hwi, "hw").Replace("*", "").Trim() : "";
        entry.Headword = hw.Length > 0 ? Clean(hw) : IdWord(e);

        if (hwi != null && hwi.TryGetValue("prs", out object p) && p is List<object> prs)
        {
            foreach (var pr in prs.OfType<Dictionary<string, object>>())
            {
                if (entry.Ipa.Length == 0) entry.Ipa = JsonGet.Str(pr, "ipa").Trim();
                if (entry.AudioUrl.Length == 0 && pr.TryGetValue("sound", out object so) && so is Dictionary<string, object> sound)
                    entry.AudioUrl = AudioUrl(JsonGet.Str(sound, "audio"));
            }
        }

        if (e.TryGetValue("shortdef", out object sd) && sd is List<object> defs)
            entry.Definitions = defs.OfType<string>().Select(Clean).Where(d => d.Length > 0).ToList();

        if (e.TryGetValue("def", out object def)) CollectExamples(def, entry.Examples);
        return entry;
    }

    // def の中を探して ["vis", [{t: ...}]] の t を集める (最大 2)
    private static void CollectExamples(object node, List<string> into)
    {
        if (into.Count >= MaxExamples || node == null) return;
        if (node is List<object> arr)
        {
            if (arr.Count == 2 && arr[0] is string tag && tag == "vis" && arr[1] is List<object> vis)
            {
                foreach (var v in vis.OfType<Dictionary<string, object>>())
                {
                    if (into.Count >= MaxExamples) return;
                    string t = Clean(JsonGet.Str(v, "t"));
                    if (t.Length > 0) into.Add(t);
                }
                return;
            }
            foreach (var x in arr) CollectExamples(x, into);
        }
        else if (node is Dictionary<string, object> obj)
        {
            foreach (var x in obj.Values) CollectExamples(x, into);
        }
    }
}
