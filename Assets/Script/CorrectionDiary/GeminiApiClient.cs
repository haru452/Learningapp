using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Gemini API に WAV 音声を直接送り、文字起こし / 添削結果を JSON で受け取る。
/// YouTube 動画の要約の採点 (Versant 対策) にも使う。
/// </summary>
public class GeminiApiClient
{
    private const string EndpointBase =
        "https://generativelanguage.googleapis.com/v1beta/models/";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _fallbackModel;

    /// <param name="fallbackModel">メインモデルが混雑時(503等)に切り替える予備モデル。空なら使わない</param>
    public GeminiApiClient(string apiKey, string model = "gemini-3.8-flash", string fallbackModel = null)
    {
        _apiKey = apiKey;
        _model = model;
        _fallbackModel = fallbackModel;
    }

    // ------------------------------------------------------------
    // Result types
    // ------------------------------------------------------------
    [Serializable]
    public class CorrectionResult
    {
        public string transcript;
        public string corrected;
        public string explanation;
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
        "If the diary entry is empty, return empty strings.\n\n" +
        "Diary entry:\n";

    private const string DiarySchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"corrected\":{\"type\":\"STRING\"}," +
        "\"explanation\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"corrected\",\"explanation\"]}";

    /// <summary>
    /// テキスト(日記)の添削。結果の corrected / explanation を使う (transcript は空)。
    /// </summary>
    public async Task<CorrectionResult> CorrectDiaryAsync(string diaryText)
    {
        string innerJson = await GenerateAsync(null, DiaryPrompt + diaryText, DiarySchema);
        var result = JsonUtility.FromJson<CorrectionResult>(innerJson);
        if (result == null) throw new Exception("Could not parse Gemini response: " + innerJson);
        return result;
    }

    // ------------------------------------------------------------
    // Versant 対策: 動画の要約を採点する
    // ------------------------------------------------------------
    // 項目ごとの満点 (合計 100)
    public const int ContentMax = 50;        // 内容: 要点を押さえているか・間違いが無いか
    public const int OrganizationMax = 25;   // 構成: 話の順序・つながり
    public const int LanguageMax = 25;       // 文法・語彙

    [Serializable]
    public class SummaryGrade
    {
        public string transcript;        // 音声を送った場合の文字起こし (テキストを送った場合は空)
        public int contentScore;         // 0〜ContentMax
        public int organizationScore;    // 0〜OrganizationMax
        public int languageScore;        // 0〜LanguageMax
        public int score;                // 合計 (0〜100)。Gemini には出させず、アプリで計算する
        public string referenceSummary;  // お手本の要約
        public string goodPoints;        // 押さえられていたポイント
        public string missedPoints;      // 抜けていたポイント・誤解
        public string languageFeedback;  // 文法・言い回しの指摘
        public string improvedSummary;   // ユーザーの要約を活かした改善例
    }

    private const string SummaryGradePrompt =
        "You are an examiner preparing an English learner for the Versant English test " +
        "(similar to the \"Story Retelling\" / \"Summary\" tasks).\n" +
        "The learner watched the attached video once, then had {0} seconds to summarize it out loud in English.\n" +
        "{1}\n" +
        "Evaluate the summary against the video. Score each part separately with an integer:\n" +
        "- \"contentScore\" (0-{2}): are the main idea and the key points of the video captured, " +
        "and is anything stated wrongly? Missing the main idea or saying something false costs the most.\n" +
        "- \"organizationScore\" (0-{3}): is it easy to follow? E.g. the main idea first, then supporting points " +
        "in a logical order, with linking words (first, then, because, so...). Ignore hesitations like \"um\".\n" +
        "- \"languageScore\" (0-{4}): grammar accuracy and the range and correctness of vocabulary.\n" +
        "Remember it was spoken under time pressure, so do not expect a complete summary for a high score.\n" +
        "- \"referenceSummary\": a model summary of the video in 3-4 simple sentences that could be said in {0} seconds.\n" +
        "- \"goodPoints\": the key points the learner covered, as short bullet lines starting with \"- \".\n" +
        "- \"missedPoints\": important points the learner missed or got wrong, as short bullet lines starting with \"- \".\n" +
        "- \"languageFeedback\": at most 3 grammar or wording corrections, each like \"I has -> I had (past tense)\". " +
        "Ignore punctuation and capitalization.\n" +
        "- \"improvedSummary\": rewrite the learner's own summary as natural, correct English, " +
        "keeping their structure but fixing errors and adding the most important missing point.\n" +
        "Write all feedback in simple English. If the learner said nothing meaningful, give 0 for every score " +
        "and still fill referenceSummary.";

    private const string SpokenSummaryNote =
        "Their speech was transcribed automatically, so punctuation may be missing and a word may occasionally be " +
        "misrecognized. Set \"transcript\" to an empty string.\n\nThe learner's summary:\n";

    private const string AudioSummaryNote =
        "Their spoken summary is the attached audio. First set \"transcript\" to EXACTLY what they said, " +
        "keeping any grammar mistakes.";

    private const string SummaryGradeSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"transcript\":{\"type\":\"STRING\"}," +
        "\"contentScore\":{\"type\":\"INTEGER\"}," +
        "\"organizationScore\":{\"type\":\"INTEGER\"}," +
        "\"languageScore\":{\"type\":\"INTEGER\"}," +
        "\"referenceSummary\":{\"type\":\"STRING\"}," +
        "\"goodPoints\":{\"type\":\"STRING\"}," +
        "\"missedPoints\":{\"type\":\"STRING\"}," +
        "\"languageFeedback\":{\"type\":\"STRING\"}," +
        "\"improvedSummary\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"transcript\",\"contentScore\",\"organizationScore\",\"languageScore\"," +
        "\"referenceSummary\",\"goodPoints\",\"missedPoints\",\"languageFeedback\",\"improvedSummary\"]}";

    /// <summary>
    /// YouTube 動画をお手本に、ユーザーが話した要約を採点する。
    /// spokenSummary (内蔵の音声認識の結果) か wav (録音) のどちらかを渡す。
    /// </summary>
    public async Task<SummaryGrade> GradeVideoSummaryAsync(
        string youtubeUrl, int timeLimitSeconds, string spokenSummary, byte[] wav = null)
    {
        string note = wav != null ? AudioSummaryNote : SpokenSummaryNote + spokenSummary;
        string prompt = SummaryGradePrompt
            .Replace("{0}", timeLimitSeconds.ToString())
            .Replace("{1}", note)
            .Replace("{2}", ContentMax.ToString())
            .Replace("{3}", OrganizationMax.ToString())
            .Replace("{4}", LanguageMax.ToString());

        // 動画はプロンプトより前に置くのが推奨されている
        string tail = "," + TextPart(prompt) + (wav != null ? "," + AudioPart(wav) : "");

        // 要約の採点に必要なのは主に音声なので、映像は低画質・少ないコマ数で読ませてトークンを節約する
        // (1分あたりのトークン上限 TPM に引っかかりにくくする)。
        // もしこの設定が受け付けられなかった (400) ときは、今までどおりの送り方でやり直す。
        string innerJson;
        try
        {
            innerJson = await GeneratePartsAsync(
                YouTubePart(youtubeUrl, VideoFps) + tail, SummaryGradeSchema,
                extraConfig: "\"mediaResolution\":\"MEDIA_RESOLUTION_LOW\",\"temperature\":0.2");
        }
        catch (HttpException e) when (e.StatusCode == 400)
        {
            Debug.LogWarning("[Gemini] Light video options were rejected. Retrying without them: " + e.Message);
            innerJson = await GeneratePartsAsync(YouTubePart(youtubeUrl) + tail, SummaryGradeSchema);
        }
        var result = JsonUtility.FromJson<SummaryGrade>(innerJson);
        if (result == null) throw new Exception("Could not parse Gemini response: " + innerJson);
        if (wav == null) result.transcript = spokenSummary;

        // 満点を超える値が返ってきても、範囲に収めてから合計する
        result.contentScore = Mathf.Clamp(result.contentScore, 0, ContentMax);
        result.organizationScore = Mathf.Clamp(result.organizationScore, 0, OrganizationMax);
        result.languageScore = Mathf.Clamp(result.languageScore, 0, LanguageMax);
        result.score = result.contentScore + result.organizationScore + result.languageScore;
        return result;
    }

    // ------------------------------------------------------------
    // Internal
    // ------------------------------------------------------------
    private const int MaxAttemptsPerModel = 3;

    /// <summary>要約の採点で、動画を何秒に1コマ読むか (0.5 = 2秒に1コマ)</summary>
    private const float VideoFps = 0.5f;

    // 500 / 503: サーバー側の一時的な障害・混雑 → 少し待てば通ることが多いので、同じモデルでやり直す
    private static bool IsTemporary(HttpException e) => e.StatusCode == 500 || e.StatusCode == 503;

    // 429: 利用上限 (1分あたり / 1日あたり) → 同じモデルで送り直すと、さらに枠を使ってしまう
    // 404: モデル名が見つからない
    // → どちらも同じモデルではやり直さず、すぐ予備モデルに切り替える (上限はモデルごとに別)
    private static bool ShouldSwitchModel(HttpException e) => e.StatusCode == 429 || e.StatusCode == 404;

    /// <summary>
    /// 一時的なエラー (500/503) は待ってリトライ (1秒→2秒) し、それでも失敗したら予備モデルに切り替える。
    /// 利用上限 (429) やモデル名の間違い (404) のときは、リトライせずにすぐ予備モデルに切り替える。
    /// </summary>
    private async Task<string> PostWithRetryAsync(string body)
    {
        string[] models = string.IsNullOrEmpty(_fallbackModel)
            ? new[] { _model }
            : new[] { _model, _fallbackModel };

        HttpException lastError = null;

        foreach (string model in models)
        {
            string url = $"{EndpointBase}{model}:generateContent";

            for (int attempt = 0; attempt < MaxAttemptsPerModel; attempt++)
            {
                try
                {
                    return await HttpUtil.PostJsonAsync(
                        url, body, new[] { ("x-goog-api-key", _apiKey) }, timeoutSeconds: 90);
                }
                catch (HttpException e) when (ShouldSwitchModel(e))
                {
                    lastError = e;
                    Debug.LogWarning($"[Gemini] {model} failed ({e.StatusCode}). Switching to the fallback model without retrying.");
                    break;
                }
                catch (HttpException e) when (IsTemporary(e))
                {
                    lastError = e;
                    Debug.LogWarning($"[Gemini] {model} attempt {attempt + 1} failed ({e.StatusCode}). Retrying...");
                    if (attempt < MaxAttemptsPerModel - 1)
                        await Task.Delay(1000 * (1 << attempt));
                }
            }
        }

        throw lastError;
    }

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
    private async Task<string> GeneratePartsAsync(string partsJson, string schemaJson, string extraConfig = null)
    {
        if (string.IsNullOrEmpty(_apiKey))
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

        string response = await PostWithRetryAsync(body);

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