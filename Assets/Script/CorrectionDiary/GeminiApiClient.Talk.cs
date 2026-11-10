using System;
using System.Threading.Tasks;

/// <summary>
/// GeminiApiClient の続き: Talk (Lufka との会話) で、自分の発話を 1 つ添削する。
/// 返事 (Lufka の声) を待たせないよう、会話とは別に裏で呼ぶ。
/// </summary>
public partial class GeminiApiClient
{
    private const string TalkCorrectionPrompt =
        "You help a Japanese learner practice spoken English. Below is one thing the learner said to their conversation partner. " +
        "It comes from speech recognition, so ignore punctuation, capitalization and fillers (uh, um, like).\n" +
        "If it has a grammar or word-choice mistake, return ok=false, corrected = the same sentence with the smallest fix " +
        "(keep the meaning and the learner's words; do not add information), and note_ja = what you fixed in natural Japanese, " +
        "at most 30 characters (for example: 過去のことは went).\n" +
        "If it is correct or natural enough for casual conversation, return ok=true with empty corrected and note_ja.\n\n" +
        "Partner said before: {0}\nLearner said: {1}";

    public const string TalkCorrectionSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"ok\":{\"type\":\"BOOLEAN\"}," +
        "\"corrected\":{\"type\":\"STRING\"}," +
        "\"note_ja\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"ok\",\"corrected\",\"note_ja\"]}";

    private const int TalkTimeoutSeconds = 15;

    /// <summary>発話を 1 つ添削する。lufkaLine は直前の Lufka の発言 (文脈。無ければ空)。失敗は例外</summary>
    public async Task<TalkCorrection> CorrectTalkAsync(string utterance, string lufkaLine)
    {
        string prompt = string.Format(TalkCorrectionPrompt,
            string.IsNullOrWhiteSpace(lufkaLine) ? "(nothing yet)" : lufkaLine.Trim(), (utterance ?? "").Trim());
        string json = await GeneratePartsAsync(TextPart(prompt), TalkCorrectionSchema, "\"temperature\":0.2", TalkTimeoutSeconds);
        return TalkCoach.Parse(json) ?? throw new Exception("Gemini returned an unexpected correction: " + json);
    }
}
