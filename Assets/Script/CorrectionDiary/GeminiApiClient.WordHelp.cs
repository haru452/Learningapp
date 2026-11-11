using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// GeminiApiClient の続き: 単語帳のカードで「うまく言えなかった」を押したとき。
/// 1 語の録音を聞いて、どの音ができていないか・どう直すか (日本語) と、練習する音 (最大 2) を返す
/// </summary>
public partial class GeminiApiClient
{
    private const string WordHelpPrompt =
        "You are an English pronunciation coach for a Japanese learner. The attached audio is the learner trying to say " +
        "the English word \"{0}\"{1}. The learner felt it did not sound right.\n" +
        "Listen carefully and return:\n" +
        "- problem: 1-2 short sentences in Japanese. Say exactly which sound in the word was off and what it sounded like " +
        "(for example: 「think の th が s になって sink に聞こえました。」). If it was actually fine, say so kindly.\n" +
        "- howTo: 1-3 short sentences in Japanese on how to move the mouth, tongue or lips to make that sound, or which part to stress.\n" +
        "- sounds: 0 to 2 sounds to practice, most important first, using these codes: " +
        "th (think/this), r_l (R vs L), v_b (V vs B), f_h (F vs H), s_sh (S vs SH), vowel (cat/cut/cot), " +
        "long_short (ship/sheep), final_consonant (dropped final consonants), extra_vowel (adding vowels after consonants), " +
        "stress (word stress). Use \"other\" with a short English otherName (for example \"Z sound\") only when none of these fits. " +
        "Empty if it was fine.";

    public const string WordHelpSchema =
        "{\"type\":\"OBJECT\",\"properties\":{\"problem\":{\"type\":\"STRING\"},\"howTo\":{\"type\":\"STRING\"}," +
        "\"sounds\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\",\"properties\":{" +
        "\"code\":{\"type\":\"STRING\",\"enum\":[\"th\",\"r_l\",\"v_b\",\"f_h\",\"s_sh\",\"vowel\",\"long_short\"," +
        "\"final_consonant\",\"extra_vowel\",\"stress\",\"other\"]},\"otherName\":{\"type\":\"STRING\"}},\"required\":[\"code\"]}}}," +
        "\"required\":[\"problem\",\"howTo\",\"sounds\"]}";

    /// <summary>1 語の発音の助け: できていない音の説明・直し方 (日本語) と、練習する音 (最大 2、大事な順)</summary>
    public class WordHelp
    {
        public string Word = "";
        public string Problem = "";
        public string HowTo = "";
        public List<SoundHit> Sounds = new List<SoundHit>();
    }

    /// <summary>返事を読む。知らない音・名前の無いその他は落とし、同じ音は 1 回、最大 2 個。読めなければ null</summary>
    public static WordHelp ParseWordHelp(string json, string word)
    {
        object parsed;
        try { parsed = MiniJson.Parse(json ?? ""); }
        catch (FormatException) { return null; }
        if (!(parsed is Dictionary<string, object> d)) return null;
        var help = new WordHelp
        {
            Word = VocabDeck.Key(word),
            Problem = JsonGet.Str(d, "problem").Trim(),
            HowTo = JsonGet.Str(d, "howTo").Trim(),
        };
        if (help.Problem.Length == 0 && help.HowTo.Length == 0) return null;
        if (d.TryGetValue("sounds", out object arr) && arr is List<object> list)
        {
            foreach (var s in list.OfType<Dictionary<string, object>>())
            {
                if (help.Sounds.Count >= 2) break;
                string code = JsonGet.Str(s, "code").Trim();
                if (!SoundTypes.IsKnown(code)) continue;
                string label = code == SoundTypes.Other ? SoundTypes.NormalizeOtherName(JsonGet.Str(s, "otherName")) : "";
                if (code == SoundTypes.Other && label.Length == 0) continue;
                if (help.Sounds.Any(x => x.Code == code && x.Label == label)) continue;
                help.Sounds.Add(new SoundHit { Code = code, Label = label, Word = help.Word });
            }
        }
        return help;
    }

    /// <summary>1 語の録音を送って、どの音ができていないかを聞く。ipa は無ければ空でよい</summary>
    public async Task<WordHelp> GetWordHelpAsync(string word, string ipa, byte[] wav)
    {
        if (string.IsNullOrWhiteSpace(word)) throw new ArgumentException("No word.", nameof(word));
        if (wav == null || wav.Length == 0) throw new ArgumentException("No recording.", nameof(wav));
        string ipaPart = string.IsNullOrWhiteSpace(ipa) ? "" : " (/" + ipa.Trim() + "/)";
        string prompt = WordHelpPrompt.Replace("{0}", word.Trim()).Replace("{1}", ipaPart);
        // 押してすぐ見たいので: 速いモデルを先に、混んでいたら 1 回だけで次へ、全体で WordHelpTotalSeconds まで
        var models = WordHelpModels.Concat(new[] { _model, _fallbackModel })
            .Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToArray();
        var task = GeneratePartsAsync(TextPart(prompt) + "," + AudioPart(wav), WordHelpSchema,
                                      "\"temperature\":0.2", WordHelpTimeoutSeconds, models, attemptsPerModel: 1);
        if (await Task.WhenAny(task, Task.Delay(WordHelpTotalSeconds * 1000)) != task)
        {
            _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);   // 後で失敗しても警告を出さない
            throw new TimeoutException("Gemini took more than " + WordHelpTotalSeconds + " seconds.");
        }
        string innerJson = await task;
        return ParseWordHelp(innerJson, word) ?? throw new Exception("Could not parse Gemini response: " + innerJson);
    }

    /// <summary>「Didn't sound right?」で先に使うモデル (速い順)。動かなければいつものモデルへ</summary>
    public static readonly string[] WordHelpModels = { "gemini-3.1-flash-lite-preview", "gemini-flash-lite-latest" };
    public const int WordHelpTimeoutSeconds = 25;   // 1 回のリクエスト
    public const int WordHelpTotalSeconds = 40;     // 全体 (これを過ぎたら「もう一度」)
}
