using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Gemini API に WAV 音声を直接送り、文字起こし / 添削結果を JSON で受け取る。
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
    // Internal
    // ------------------------------------------------------------
    private const int MaxAttemptsPerModel = 3;

    private static bool IsRetryable(HttpException e)
    {
        // 429: レート制限 / 500,503: サーバー側の一時的な障害・混雑
        return e.StatusCode == 429 || e.StatusCode == 500 || e.StatusCode == 503;
    }

    /// <summary>
    /// 一時的なエラー時は待ってリトライ(1秒→2秒)し、それでも失敗したら予備モデルに切り替える。
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
                catch (HttpException e) when (IsRetryable(e))
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

    private async Task<string> GenerateAsync(byte[] wav, string prompt, string schemaJson)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("Gemini API key is not set.");

        // wav が null のときはテキストのみのリクエストにする
        string audioPart = wav != null
            ? ",{\"inline_data\":{\"mime_type\":\"audio/wav\",\"data\":\"" + Convert.ToBase64String(wav) + "\"}}"
            : "";

        // JsonUtility は null/空フィールドも出力してしまうため、リクエストは文字列で組み立てる
        string body =
            "{\"contents\":[{\"parts\":[" +
                "{\"text\":\"" + HttpUtil.JsonEscape(prompt) + "\"}" +
                audioPart +
            "]}]," +
            "\"generationConfig\":{" +
                "\"responseMimeType\":\"application/json\"," +
                "\"responseSchema\":" + schemaJson +
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