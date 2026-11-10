using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// GeminiApiClient の続き: シャドーイング (YouTube) と、単語帳の発音のまとめ採点。
/// </summary>
public partial class GeminiApiClient
{
    // ---------------- シャドーイング (YouTube) ----------------
    private const string VideoScriptPrompt =
        "Transcribe the English speech in this video, one sentence per item, exactly as it is spoken. " +
        "Leave out music, the fixed greeting at the start and the fixed closing lines at the end, and any speech that is not English.";

    public const string VideoScriptSchema =
        "{\"type\":\"OBJECT\",\"properties\":{\"sentences\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\"," +
        "\"properties\":{\"text\":{\"type\":\"STRING\"}},\"required\":[\"text\"]}}},\"required\":[\"sentences\"]}";

    /// <summary>YouTube の動画のお手本 (文ごとの文字起こし)。{"sentences":[{"text"}]} の JSON をそのまま返す</summary>
    public async Task<string> TranscribeVideoAsync(string youtubeUrl)
    {
        if (string.IsNullOrWhiteSpace(youtubeUrl)) throw new ArgumentException("No video.", nameof(youtubeUrl));
        // 文字起こしに必要なのは音声だけ: 映像は少ないコマ・低い解像度にして、使う量 (トークン) を抑える
        return await GeneratePartsAsync(YouTubePart(youtubeUrl, VideoFps) + "," + TextPart(VideoScriptPrompt), VideoScriptSchema,
                                        "\"mediaResolution\":\"MEDIA_RESOLUTION_LOW\",\"temperature\":0.1", AudioGradeTimeoutSeconds);
    }

    private const string ShadowTranscriptPrompt =
        "The attached audio is an English learner shadowing a video (speaking along with it while listening on earphones). " +
        "1) transcript: write exactly what the learner said, including mistakes. Do not correct it, and do not copy the reference. " +
        "2) notes: 0 to 3 short notes in English about pronunciation, rhythm or falling behind (for example: weak or missing small words, " +
        "unclear sounds, sentence endings dropped). The reference script is only a hint for hard-to-hear words:\n";

    public const string ShadowTranscriptSchema =
        "{\"type\":\"OBJECT\",\"properties\":{\"transcript\":{\"type\":\"STRING\"}," +
        "\"notes\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\"}}},\"required\":[\"transcript\",\"notes\"]}";

    /// <summary>シャドーイングの録音 (60 秒ほど) を書き起こし、発音などの気付きをもらう</summary>
    public async Task<(string transcript, List<string> notes)> TranscribeShadowingAsync(byte[] wav, string referenceExcerpt)
    {
        if (wav == null || wav.Length == 0) throw new ArgumentException("No recording.", nameof(wav));
        string reference = referenceExcerpt ?? "";
        if (reference.Length > 6000) reference = reference.Substring(0, 6000);
        string json = await GeneratePartsAsync(TextPart(ShadowTranscriptPrompt + reference) + "," + AudioPart(wav),
                                               ShadowTranscriptSchema, "\"temperature\":0.1", AudioGradeTimeoutSeconds);
        var notes = new List<string>();
        string transcript = "";
        if (MiniJson.Parse(json) is Dictionary<string, object> d)
        {
            transcript = TatoebaCatalog.Str(d, "transcript").Trim();
            if (d.TryGetValue("notes", out object n) && n is List<object> list)
                foreach (var o in list) if (o is string t && !string.IsNullOrWhiteSpace(t)) notes.Add(t.Trim());
        }
        else throw new Exception("Could not parse Gemini response: " + json);
        return (transcript, notes);
    }

    private const string ShadowAdvicePrompt =
        "日本人の英語学習者が YouTube の動画でシャドーイングをしました。下の「気付き」(英語) と「言えなかった単語」をもとに、" +
        "次に練習するときのアドバイスを日本語で 3 つほど、それぞれ 1〜2 文で書いてください。具体的な単語や音を挙げてください。\n";

    public const string ShadowAdviceSchema =
        "{\"type\":\"OBJECT\",\"properties\":{\"advice\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\"}}},\"required\":[\"advice\"]}";

    /// <summary>気付きと言えなかった単語から、日本語のアドバイス ("- …" の行) を作る</summary>
    public async Task<string> ShadowAdviceAsync(IList<string> notes, IList<string> missedWords)
    {
        string prompt = ShadowAdvicePrompt +
            "気付き:\n" + string.Join("\n", (notes ?? new List<string>()).Take(12).Select(x => "- " + x)) + "\n" +
            "言えなかった単語 (多い順): " + string.Join(", ", (missedWords ?? new List<string>()).Take(40));
        string json = await GeneratePartsAsync(TextPart(prompt), ShadowAdviceSchema, "\"temperature\":0.3");
        var lines = new List<string>();
        if (MiniJson.Parse(json) is Dictionary<string, object> d && d.TryGetValue("advice", out object a) && a is List<object> list)
            foreach (var o in list) if (o is string t && !string.IsNullOrWhiteSpace(t)) lines.Add("- " + t.Trim());
        if (lines.Count == 0) throw new Exception("Could not parse Gemini response: " + json);
        return string.Join("\n", lines);
    }

    /// <summary>お手本の文を読んだ録音を聞いて、発音について短くコメントし、苦手な音 (最大 2) も返す</summary>
    public async Task<PronunciationFeedback> CheckPronunciationAsync(string expectedText, byte[] wav)
    {
        if (wav == null || wav.Length == 0) throw new ArgumentException("No recording.", nameof(wav));
        string prompt = PronunciationPrompt.Replace("{0}", expectedText ?? "");
        string innerJson = await GeneratePartsAsync(TextPart(prompt) + "," + AudioPart(wav), PronunciationSchema);
        PronunciationFeedback fb;
        try { fb = ParsePronunciation(innerJson, expectedText); }
        catch (Exception) { fb = new PronunciationFeedback(); }
        if (string.IsNullOrWhiteSpace(fb.Comment))
            throw new Exception("Could not parse Gemini response: " + innerJson);
        return fb;
    }

    // ------------------------------------------------------------
    // 単語帳: 1 回分の単語の発音をまとめて採点する (最大 20 語を 1 リクエストで)
    // ------------------------------------------------------------
    public const int MaxVocabWords = 20;

    private const string VocabPronPrompt =
        "You are an English pronunciation coach for a Japanese learner. Each attached audio clip is the learner saying " +
        "one English word, labeled \"Word N: <word>\" just before it. For every word, return:\n" +
        "- word: the word exactly as labeled\n" +
        "- rating: good (clear), close (understandable but a sound is off), or practice (hard to understand or a different word)\n" +
        "- advice: 1-2 short sentences in Japanese about how to say it better (if good, one small tip)\n" +
        "- sounds: 0 to 2 codes for the sounds the learner had trouble with: " +
        "th (think/this), r_l (R vs L), v_b (V vs B), f_h (F vs H), s_sh (S vs SH), vowel (cat/cut/cot), " +
        "long_short (ship/sheep), final_consonant (dropped final consonants), extra_vowel (adding vowels after consonants), " +
        "stress (word stress), other (anything else). Empty if it was clear.\n" +
        "Return one item per word, in the same order.";

    public const string VocabPronSchema =
        "{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\",\"properties\":{" +
        "\"word\":{\"type\":\"STRING\"}," +
        "\"rating\":{\"type\":\"STRING\",\"enum\":[\"good\",\"close\",\"practice\"]}," +
        "\"advice\":{\"type\":\"STRING\"}," +
        "\"sounds\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\",\"enum\":[\"th\",\"r_l\",\"v_b\",\"f_h\",\"s_sh\",\"vowel\"," +
        "\"long_short\",\"final_consonant\",\"extra_vowel\",\"stress\",\"other\"]}}}," +
        "\"required\":[\"word\",\"rating\",\"advice\"]}}";

    /// <summary>単語ごとの Gemini の判定 (good / close / practice) と日本語のアドバイス</summary>
    public class WordPronResult
    {
        public string Word = "";
        public string Rating = "";
        public string Advice = "";
        public List<string> Sounds = new List<string>();
    }

    /// <summary>まとめ採点の返事を読む。送っていない語・知らない判定・知らない音は捨てる。同じ語は最初だけ</summary>
    public static List<WordPronResult> ParseWordPronunciations(string json, IList<string> words)
    {
        var result = new List<WordPronResult>();
        object parsed;
        try { parsed = MiniJson.Parse(json ?? ""); }
        catch (FormatException) { return result; }
        if (!(parsed is List<object> list)) return result;
        var asked = new HashSet<string>((words ?? new string[0]).Select(VocabDeck.Key));
        foreach (var o in list.OfType<Dictionary<string, object>>())
        {
            string word = VocabDeck.Key(JsonGet.Str(o, "word"));
            string rating = JsonGet.Str(o, "rating").Trim().ToLowerInvariant();
            if (!asked.Contains(word) || result.Any(r => r.Word == word)) continue;
            if (rating != "good" && rating != "close" && rating != "practice") continue;
            var sounds = o.TryGetValue("sounds", out object s) && s is List<object> sl
                ? sl.OfType<string>().Select(x => x.Trim()).Where(SoundTypes.IsKnown).Distinct().Take(2).ToList()
                : new List<string>();
            result.Add(new WordPronResult { Word = word, Rating = rating, Advice = JsonGet.Str(o, "advice").Trim(), Sounds = sounds });
        }
        return result;
    }

    /// <summary>単語ごとの録音 (words と同じ順) をまとめて採点する。最大 20 語</summary>
    public async Task<List<WordPronResult>> CheckWordPronunciationsAsync(IList<string> words, IList<byte[]> wavs)
    {
        if (words == null || wavs == null || words.Count != wavs.Count) throw new ArgumentException("words and wavs must match");
        var sb = new System.Text.StringBuilder(TextPart(VocabPronPrompt));
        int n = 0;
        var sent = new List<string>();
        for (int i = 0; i < words.Count && n < MaxVocabWords; i++)
        {
            if (wavs[i] == null || wavs[i].Length == 0) continue;
            n++;
            sent.Add(words[i]);
            sb.Append(',').Append(TextPart("Word " + n + ": " + words[i])).Append(',').Append(AudioPart(wavs[i]));
        }
        if (n == 0) return new List<WordPronResult>();
        string innerJson = await GeneratePartsAsync(sb.ToString(), VocabPronSchema, timeoutSeconds: AudioGradeTimeoutSeconds);
        var result = ParseWordPronunciations(innerJson, sent);
        if (result.Count == 0) throw new Exception("Could not parse Gemini response: " + innerJson);
        return result;
    }
}
