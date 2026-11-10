using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// GeminiApiClient の続き: カラオケ。歌った録音 (伴奏なし) と歌詞の行を送り、行ごとにうまく発音できなかった単語と原因の音を返してもらう。
/// リズムや音程は見ない。
/// </summary>
public partial class GeminiApiClient
{
    private const string KaraokePrompt =
        "The attached audio is a Japanese learner singing an English song along with earphones (no music in the recording). " +
        "Below are the lyric lines they sang, with line numbers. Judge pronunciation only: ignore rhythm, pitch and melody, " +
        "and ignore small changes that singers normally make (stretched vowels, dropped final g in -ing).\n" +
        "For each line, list the words that were mispronounced or not sung at all. Use the words exactly as written in the lyrics. " +
        "For each word give the sound that caused the problem as one of these codes: {0}, or \"other\".\n" +
        "Only list clear problems; if a line was fine, leave it out.\n" +
        "advice: 2 or 3 short sentences in Japanese about the most important sounds to practice in this song.\n\nLyrics:\n";

    public const string KaraokeSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"lines\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\",\"properties\":{" +
        "\"line\":{\"type\":\"INTEGER\"}," +
        "\"missed\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\",\"properties\":{" +
        "\"word\":{\"type\":\"STRING\"},\"sound\":{\"type\":\"STRING\"}},\"required\":[\"word\",\"sound\"]}}}," +
        "\"required\":[\"line\",\"missed\"]}}," +
        "\"advice\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"lines\",\"advice\"]}";

    /// <summary>1 回分の録音 (最大 3 分) とその部分の歌詞の行 (index = 歌詞の行番号) を採点する</summary>
    public async Task<KaraokeCheck> CheckKaraokeAsync(IList<(int index, string text)> lines, byte[] wav)
    {
        if (wav == null || wav.Length == 0) throw new ArgumentException("no recording");
        string lyrics = string.Join("\n", lines.Select(l => l.index + ": " + l.text));
        string prompt = string.Format(KaraokePrompt, string.Join(", ", SoundTypes.All)) + lyrics;
        string json = await GeneratePartsAsync(TextPart(prompt) + "," + AudioPart(wav), KaraokeSchema, null, AudioGradeTimeoutSeconds);
        // 壊れた返事を「全部言えた (100 点)」にしないよう、読めなければ失敗にする (Check again で送り直せる)
        return ParseKaraokeCheck(json, lines) ?? throw new Exception("Could not read Gemini's karaoke check: " + json);
    }

    /// <summary>
    /// 採点の返事を読む。送っていない行・その行に無い単語は捨てる。知らない音は "other"。単語は小文字。
    /// 壊れている・lines が無いときは null (lines が空の正しい返事は、空の結果)
    /// </summary>
    public static KaraokeCheck ParseKaraokeCheck(string json, IList<(int index, string text)> lines)
    {
        var result = new KaraokeCheck();
        object root;
        try { root = MiniJson.Parse(json); }
        catch (Exception) { return null; }
        if (!(root is Dictionary<string, object> d) || !(d.TryGetValue("lines", out var linesValue) && linesValue is List<object>)) return null;
        result.Advice = JsonGet.Str(d, "advice").Trim();

        var textOf = new Dictionary<int, string>();
        foreach (var (index, text) in lines ?? new List<(int, string)>()) textOf[index] = text ?? "";
        foreach (var item in (List<object>)linesValue)
        {
            if (!(item is Dictionary<string, object> li) || !(li.TryGetValue("line", out var n) && n is double num)) continue;
            int index = (int)num;
            if (!textOf.TryGetValue(index, out string text) || result.Lines.Any(r => r.Line == index)) continue;
            var words = new HashSet<string>(LrcLyrics.Words(text));
            var line = new KaraokeLineResult { Line = index, Text = text };
            foreach (var m in (li.TryGetValue("missed", out var ms) ? ms as List<object> : null) ?? new List<object>())
            {
                if (!(m is Dictionary<string, object> md)) continue;
                string word = LrcLyrics.Clean(JsonGet.Str(md, "word"));
                if (!words.Contains(word) || line.Missed.Any(x => x.Word == word)) continue;
                string sound = JsonGet.Str(md, "sound").Trim().ToLowerInvariant();
                line.Missed.Add(new KaraokeMiss { Word = word, Sound = Array.IndexOf(SoundTypes.All, sound) >= 0 ? sound : SoundTypes.Other });
            }
            if (line.Missed.Count > 0) result.Lines.Add(line);
        }
        return result;
    }
}
