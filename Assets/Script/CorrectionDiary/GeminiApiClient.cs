using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Gemini API に WAV 音声を直接送り、文字起こし / 添削結果を JSON で受け取る。
/// YouTube 動画の要約の採点 (Versant 対策) にも使う。
/// </summary>
public partial class GeminiApiClient
{
    private const string EndpointBase =
        "https://generativelanguage.googleapis.com/v1beta/models/";

    private readonly string[] _apiKeys;   // [0] = この機能のキー、[1..] = 上限のときに借りるほかのキー (順に)
    private readonly string _model;
    private readonly string _fallbackModel;

    /// <summary>メインのモデル (あとで送り直すとき、同じモデルを使うため)</summary>
    public string Model => _model;
    public string FallbackModel => _fallbackModel;

    /// <param name="fallbackModel">メインモデルが混雑時(503等)に切り替える予備モデル。空なら使わない</param>
    /// <param name="backupApiKeys">
    /// apiKey が利用上限 (429) になったときに順に使うほかのキー。空のものは使わない。
    /// ※ Gemini の上限は「キーごと」ではなく「Google Cloud のプロジェクトごと」。別のプロジェクトで作ったキーでないと枠は増えない
    /// </param>
    public GeminiApiClient(string apiKey, string model = "gemini-3.8-flash", string fallbackModel = null,
                           string[] backupApiKeys = null)
    {
        _apiKeys = KeyList(apiKey, backupApiKeys);
        _model = model;
        _fallbackModel = fallbackModel;
    }

    /// <summary>自分のキー → ほかのキーの順に並べる。空・重複したキーは除く</summary>
    public static string[] KeyList(string apiKey, string[] backupApiKeys)
    {
        var all = new List<string> { apiKey };
        if (backupApiKeys != null) all.AddRange(backupApiKeys);
        return all.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToArray();
    }

    /// <summary>
    /// 試す順番: 自分のキーでメイン → 予備モデル、それでも上限なら次のキーでメイン → 予備モデル …。
    /// 空・重複したキーやモデルは飛ばす。key は「空と重複を除いたキー」の番号
    /// </summary>
    public static List<(int key, string model)> AttemptOrder(string[] apiKeys, string[] models)
    {
        var keys = (apiKeys ?? new string[0]).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList();
        var ms = (models ?? new string[0]).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        var order = new List<(int key, string model)>();
        for (int k = 0; k < keys.Count; k++)
            foreach (string m in ms) order.Add((k, m));
        return order;
    }

    private List<string> UsableKeys => _apiKeys.ToList();

    // ------------------------------------------------------------
    // Result types
    // ------------------------------------------------------------
    [Serializable]
    public class CorrectionResult
    {
        public string transcript;
        public string corrected;
        public string explanation;
        public Mistake[] mistakes;   // 日記・話し言葉の添削のときだけ (それ以外は空)
    }

    [Serializable]
    private class TranscriptOnlyResult
    {
        public string transcript;
    }

    // Gemini レスポンス (必要部分のみ)
    [Serializable] private class GeminiResponse { public Candidate[] candidates; }
    [Serializable] private class Candidate { public Content content; }
    [Serializable] private class Content { public Part[] parts; }
    [Serializable] private class Part { public string text; }

    // ------------------------------------------------------------
    // Prompts / Schemas
    // ------------------------------------------------------------
    private const string CorrectionPrompt =
        "You are a friendly English teacher. The attached audio is a learner speaking English.\n" +
        "1. \"transcript\": transcribe EXACTLY what the speaker said, keeping any grammar mistakes. Do not fix anything.\n" +
        "2. \"corrected\": rewrite it as grammatically correct, natural English. Keep the original meaning; do not add new information.\n" +
        "3. \"explanation\": briefly explain in simple English what was corrected and why.\n" +
        "If there is no intelligible speech, return empty strings for all fields.";

    private const string TranscribePrompt =
        "Transcribe the attached English speech EXACTLY as spoken, keeping any grammar mistakes. " +
        "Return only the transcript. If there is no intelligible speech, return an empty string.";

    private const string CorrectionSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"transcript\":{\"type\":\"STRING\"}," +
        "\"corrected\":{\"type\":\"STRING\"}," +
        "\"explanation\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"transcript\",\"corrected\",\"explanation\"]}";

    // ------------------------------------------------------------
    // ミスの一覧 (日記・要約・お題の添削で共通。種類は MistakeTypes.All と同じ並び)
    // ------------------------------------------------------------
    private const string MistakesInstruction =
        "\"mistakes\": every grammar or word mistake as {type, wrong, fixed}. type is one of: " +
        "article, tense, preposition, agreement, plural, verb_form, word_order, word_choice, missing_word, other. " +
        "wrong/fixed are the short phrase before and after the fix. Ignore punctuation and capitalization. " +
        "Empty list if none.";

    private const string MistakesProperty =
        "\"mistakes\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\",\"properties\":{" +
        "\"type\":{\"type\":\"STRING\",\"enum\":[\"article\",\"tense\",\"preposition\",\"agreement\",\"plural\"," +
        "\"verb_form\",\"word_order\",\"word_choice\",\"missing_word\",\"other\"]}," +
        "\"wrong\":{\"type\":\"STRING\"},\"fixed\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"type\",\"wrong\",\"fixed\"]}}";

    private const string TranscriptSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"transcript\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"transcript\"]}";

    // ------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------

    /// <summary>音声 → 文字起こし + 修正文 + 解説 (Geminiダイレクトモード)</summary>
    public async Task<CorrectionResult> TranscribeAndCorrectAsync(byte[] wav)
    {
        string innerJson = await GenerateAsync(wav, CorrectionPrompt, CorrectionSchema);
        var result = JsonUtility.FromJson<CorrectionResult>(innerJson);
        if (result == null) throw new Exception("Could not parse Gemini response: " + innerJson);
        return result;
    }

    /// <summary>音声 → 文字起こしのみ (DeepL Write モードのSTTステップ)</summary>
    public async Task<string> TranscribeAsync(byte[] wav)
    {
        string innerJson = await GenerateAsync(wav, TranscribePrompt, TranscriptSchema);
        var result = JsonUtility.FromJson<TranscriptOnlyResult>(innerJson);
        if (result == null) throw new Exception("Could not parse Gemini response: " + innerJson);
        return result.transcript ?? "";
    }

    private const string SpokenPrompt =
        "You are a friendly English teacher. Below is what an English learner said out loud. " +
        "It was transcribed automatically, so punctuation and capitalization may be missing, " +
        "and a word may occasionally be misrecognized.\n" +
        "1. \"corrected\": rewrite it as grammatically correct, natural English. " +
        "Keep the speaker's meaning; do not add new information.\n" +
        "2. \"explanation\": briefly explain in simple English what was corrected and why.\n" +
        "3. " + MistakesInstruction + "\n" +
        "If the text is empty, return empty strings.\n\n" +
        "What the learner said:\n";

    /// <summary>
    /// 文字起こし済みの「話し言葉」を添削する (音声は送らない)。
    /// 結果の corrected / explanation を使う (transcript は空)。
    /// </summary>
    public async Task<CorrectionResult> CorrectSpokenTextAsync(string spokenText)
    {
        string innerJson = await GenerateAsync(null, SpokenPrompt + spokenText, DiarySchema);
        var result = JsonUtility.FromJson<CorrectionResult>(innerJson);
        if (result == null) throw new Exception("Could not parse Gemini response: " + innerJson);
        result.mistakes = CleanMistakes(result.mistakes);
        return result;
    }

    private const string DiaryPrompt =
        "You are a friendly English teacher. Below is a diary entry written by an English learner. " +
        "It was transcribed from speech, so punctuation, capitalization and paragraph breaks may be missing.\n" +
        "1. \"corrected\": rewrite the whole diary entry as grammatically correct, natural English. " +
        "Add proper punctuation and paragraph breaks. Keep the writer's meaning, tone and first-person voice. " +
        "Do not add new events or information, and do not drop any content.\n" +
        "2. \"explanation\": list the most important corrections (at most 5) in simple English, " +
        "each as a short line like: \"I has -> I had (past tense of 'have')\". " +
        "If nothing needed fixing, say so.\n" +
        "3. " + MistakesInstruction + "\n" +
        "If the diary entry is empty, return empty strings.\n\n" +
        "Diary entry:\n";

    public const string DiarySchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"corrected\":{\"type\":\"STRING\"}," +
        "\"explanation\":{\"type\":\"STRING\"}," +
        MistakesProperty + "}," +
        "\"required\":[\"corrected\",\"explanation\",\"mistakes\"]}";

    /// <summary>
    /// テキスト(日記)の添削。結果の corrected / explanation を使う (transcript は空)。
    /// </summary>
    public async Task<CorrectionResult> CorrectDiaryAsync(string diaryText)
    {
        string innerJson = await GenerateAsync(null, DiaryPrompt + diaryText, DiarySchema);
        var result = JsonUtility.FromJson<CorrectionResult>(innerJson);
        if (result == null) throw new Exception("Could not parse Gemini response: " + innerJson);
        result.mistakes = CleanMistakes(result.mistakes);
        return result;
    }

    /// <summary>空のミスを取り除き、種類を 10 種類のどれかにそろえる</summary>
    private static Mistake[] CleanMistakes(Mistake[] mistakes)
    {
        if (mistakes == null) return new Mistake[0];
        return mistakes
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.wrong) && !string.IsNullOrWhiteSpace(m.@fixed))
            .Select(m => new Mistake { type = MistakeTypes.Normalize(m.type), wrong = m.wrong.Trim(), @fixed = m.@fixed.Trim() })
            .ToArray();
    }

    // ------------------------------------------------------------
    // Internal
    // ------------------------------------------------------------
    private const int MaxAttemptsPerModel = 3;

    /// <summary>ふつうのリクエストを待つ時間 (秒)</summary>
    public const int DefaultTimeoutSeconds = 90;
    /// <summary>答えの録音を採点するときに待つ時間 (秒)。録音を聞いて書き起こし、採点まで 1 回でするので長めに</summary>
    public const int AudioGradeTimeoutSeconds = 180;

    /// <summary>要約の採点で、動画を何秒に1コマ読むか (0.5 = 2秒に1コマ)</summary>
    private const float VideoFps = 0.5f;

    // 500 / 503: サーバー側の一時的な障害・混雑 → 少し待てば通ることが多いので、同じモデルでやり直す
    private static bool IsTemporary(HttpException e) => e.StatusCode == 500 || e.StatusCode == 503;

    // 429: 利用上限 (1分あたり / 1日あたり) → 同じモデルで送り直すと、さらに枠を使ってしまう
    // 404: モデル名が見つからない
    // → どちらも同じモデルではやり直さず、すぐ予備モデルに切り替える (上限はモデルごとに別)
    // (それでも 429 なら、もう 1 つのキーに切り替える)
    private static bool ShouldSwitchModel(HttpException e) => e.StatusCode == 429 || e.StatusCode == 404;

    /// <summary>
    /// 一時的なエラー (500/503) は待ってリトライ (1秒→2秒) し、それでも失敗したら予備モデルに切り替える。
    /// 利用上限 (429) やモデル名の間違い (404) のときは、リトライせずにすぐ予備モデルに切り替える。
    /// </summary>
    private Task<string> PostWithRetryAsync(string body, int timeoutSeconds = DefaultTimeoutSeconds) =>
        PostToModelsAsync(body, new[] { _model, _fallbackModel }, timeoutSeconds);

    private async Task<string> PostToModelsAsync(string body, string[] models, int timeoutSeconds, int attemptsPerModel = MaxAttemptsPerModel)
    {
        var keys = UsableKeys;
        if (keys.Count == 0) throw new InvalidOperationException("Gemini API key is not set.");
        var order = AttemptOrder(keys.ToArray(), models);

        HttpException lastError = null;

        foreach (var (keyIndex, model) in order)
        {
            string url = $"{EndpointBase}{model}:generateContent";
            string apiKey = keys[keyIndex];

            for (int attempt = 0; attempt < attemptsPerModel; attempt++)
            {
                try
                {
                    return await HttpUtil.PostJsonAsync(
                        url, body, new[] { ("x-goog-api-key", apiKey) }, timeoutSeconds: timeoutSeconds);
                }
                catch (HttpException e) when (ShouldSwitchModel(e))
                {
                    lastError = e;
                    Debug.LogWarning($"[Gemini] {model} (key {keyIndex + 1}) failed ({e.StatusCode}). Switching to the next model/key without retrying.");
                    break;
                }
                catch (HttpException e) when (IsTemporary(e))
                {
                    lastError = e;
                    Debug.LogWarning($"[Gemini] {model} attempt {attempt + 1} failed ({e.StatusCode}). Retrying...");
                    if (attempt < attemptsPerModel - 1)
                        await Task.Delay(1000 * (1 << attempt));
                }
            }
        }

        throw lastError;
    }

    // ------------------------------------------------------------
    // 読み上げ (Gemini の音声合成)
    // ------------------------------------------------------------

    /// <summary>
    /// 読み上げのリクエスト (返事を音声で、指定した声で)。文だけを送る
    /// (「Say this clearly:」のような指示を付けると、それも読み上げてしまうため)
    /// </summary>
    public static string TtsRequestBody(string text, string voice) =>
        "{\"contents\":[{\"parts\":[" + TextPart((text ?? "").Trim()) + "]}]," +
        "\"generationConfig\":{\"responseModalities\":[\"AUDIO\"]," +
        "\"speechConfig\":{\"voiceConfig\":{\"prebuiltVoiceConfig\":{\"voiceName\":\"" +
        HttpUtil.JsonEscape(string.IsNullOrWhiteSpace(voice) ? "Leda" : voice.Trim()) + "\"}}}}}";

    /// <summary>返事の inlineData.data (base64) を取り出す。無ければ null</summary>
    public static byte[] ExtractInlineAudio(string response)
    {
        if (string.IsNullOrEmpty(response)) return null;
        int i = response.IndexOf("\"inlineData\"", StringComparison.Ordinal);
        if (i < 0) i = response.IndexOf("\"inline_data\"", StringComparison.Ordinal);
        if (i < 0) return null;
        int d = response.IndexOf("\"data\"", i, StringComparison.Ordinal);
        if (d < 0) return null;
        int start = response.IndexOf('"', response.IndexOf(':', d) + 1);
        int end = start < 0 ? -1 : response.IndexOf('"', start + 1);
        if (start < 0 || end < 0) return null;
        try { return Convert.FromBase64String(response.Substring(start + 1, end - start - 1)); }
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// 文を読み上げた音声 (PCM 16bit・24kHz・モノラル) を返す。キーはこのクライアントのキー → 借りるキーの順。
    /// どのキーも利用上限 (429) なら GeminiQuotaException
    /// </summary>
    public async Task<byte[]> SynthesizeSpeechAsync(string text, string voice, string ttsModel)
    {
        string response;
        try
        {
            response = await PostToModelsAsync(TtsRequestBody(text, voice), new[] { ttsModel }, DefaultTimeoutSeconds);
        }
        catch (HttpException e) when (e.StatusCode == 429)
        {
            throw new GeminiQuotaException(e.Message);
        }
        var pcm = ExtractInlineAudio(response);
        if (pcm == null || pcm.Length < 2) throw new Exception("Gemini returned no audio.");
        return pcm;
    }

    public const int TtsSampleRate = 24000;

    private Task<string> GenerateAsync(byte[] wav, string prompt, string schemaJson)
    {
        // wav が null のときはテキストのみのリクエストにする
        string parts = TextPart(prompt) + (wav != null ? "," + AudioPart(wav) : "");
        return GeneratePartsAsync(parts, schemaJson);
    }

    private static string TextPart(string text) =>
        "{\"text\":\"" + HttpUtil.JsonEscape(text) + "\"}";

    private static string AudioPart(byte[] wav) =>
        "{\"inline_data\":{\"mime_type\":\"audio/wav\",\"data\":\"" + Convert.ToBase64String(wav) + "\"}}";

    // 公開されている YouTube 動画は URL をそのまま渡せる
    // fps を指定すると、動画を読むコマ数を減らせる (指定しなければ Gemini の初期値)
    private static string YouTubePart(string videoUrl, float fps = 0f) =>
        "{\"file_data\":{\"file_uri\":\"" + HttpUtil.JsonEscape(videoUrl) + "\"}" +
        (fps > 0f
            ? ",\"video_metadata\":{\"fps\":" + fps.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"
            : "") +
        "}";

    /// <param name="partsJson">parts 配列の中身 (カンマ区切りの JSON オブジェクト)</param>
    /// <param name="extraConfig">generationConfig に足す設定 (例: "\"temperature\":0.2")。無ければ null</param>
    /// <param name="models">使うモデル (先頭から順に)。null ならいつものモデル → 予備モデル</param>
    /// <param name="attemptsPerModel">混んでいる (500/503) ときに同じモデルで試す回数</param>
    private async Task<string> GeneratePartsAsync(string partsJson, string schemaJson, string extraConfig = null,
                                                  int timeoutSeconds = DefaultTimeoutSeconds, string[] models = null,
                                                  int attemptsPerModel = MaxAttemptsPerModel)
    {
        if (UsableKeys.Count == 0)
            throw new InvalidOperationException("Gemini API key is not set.");

        // JsonUtility は null/空フィールドも出力してしまうため、リクエストは文字列で組み立てる
        string body =
            "{\"contents\":[{\"parts\":[" +
                partsJson +
            "]}]," +
            "\"generationConfig\":{" +
                "\"responseMimeType\":\"application/json\"," +
                "\"responseSchema\":" + schemaJson +
                (string.IsNullOrEmpty(extraConfig) ? "" : "," + extraConfig) +
            "}}";

        string response = models == null
            ? await PostWithRetryAsync(body, timeoutSeconds)
            : await PostToModelsAsync(body, models, timeoutSeconds, attemptsPerModel);

        var parsed = JsonUtility.FromJson<GeminiResponse>(response);
        string text = parsed?.candidates?.Length > 0
            ? parsed.candidates[0]?.content?.parts?.Length > 0
                ? parsed.candidates[0].content.parts[0].text
                : null
            : null;

        if (string.IsNullOrEmpty(text))
            throw new Exception("Gemini returned no result: " + response);

        return text;
    }
}

/// <summary>Gemini の利用上限 (429) にどのキーも達していた</summary>
public class GeminiQuotaException : Exception
{
    public GeminiQuotaException(string message) : base(message) { }
}
