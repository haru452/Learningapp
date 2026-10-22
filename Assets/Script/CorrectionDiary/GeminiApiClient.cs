using System;
using System.Collections.Generic;
using System.Linq;
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

    private readonly string[] _apiKeys;   // [0] = この機能のキー、[1..] = 上限のときに借りるほかのキー (順に)
    private readonly string _model;
    private readonly string _fallbackModel;

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
        public Mistake[] mistakes;       // 文法・語の使い方のミス (集計用)
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
        "- " + MistakesInstruction + "\n" +
        "Write all feedback in simple English. If the learner said nothing meaningful, give 0 for every score " +
        "and still fill referenceSummary.";

    private const string SpokenSummaryNote =
        "Their speech was transcribed automatically, so punctuation may be missing and a word may occasionally be " +
        "misrecognized. Set \"transcript\" to an empty string.\n\nThe learner's summary:\n";

    private const string AudioSummaryNote =
        "Their spoken summary is the attached audio. First set \"transcript\" to EXACTLY what they said, " +
        "keeping any grammar mistakes.";

    public const string SummaryGradeSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"transcript\":{\"type\":\"STRING\"}," +
        "\"contentScore\":{\"type\":\"INTEGER\"}," +
        "\"organizationScore\":{\"type\":\"INTEGER\"}," +
        "\"languageScore\":{\"type\":\"INTEGER\"}," +
        "\"referenceSummary\":{\"type\":\"STRING\"}," +
        "\"goodPoints\":{\"type\":\"STRING\"}," +
        "\"missedPoints\":{\"type\":\"STRING\"}," +
        "\"languageFeedback\":{\"type\":\"STRING\"}," +
        "\"improvedSummary\":{\"type\":\"STRING\"}," +
        MistakesProperty + "}," +
        "\"required\":[\"transcript\",\"contentScore\",\"organizationScore\",\"languageScore\"," +
        "\"referenceSummary\",\"goodPoints\",\"missedPoints\",\"languageFeedback\",\"improvedSummary\",\"mistakes\"]}";

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
        result.mistakes = CleanMistakes(result.mistakes);
        return result;
    }

    // ------------------------------------------------------------
    // お題スピーキング: お題を作る・採点する・発音にコメントする
    // ------------------------------------------------------------
    // 項目ごとの満点 (合計 100)。要約より「構成」の比重が大きい
    public const int TopicContentMax = 40;        // 内容: お題に答え、理由や例があるか
    public const int TopicOrganizationMax = 30;   // 構成: 結論→理由→例の順、つなぎの言葉
    public const int TopicLanguageMax = 30;       // 文法・語彙

    [Serializable]
    public class TopicPrompt
    {
        public string kind;    // exam / daily / work
        public string topic;   // お題 (英語)
        public string hint;    // 話の組み立てのヒント (1文)
    }

    [Serializable]
    public class TopicGrade
    {
        public string goodPoints;        // 良かった点 ("- " で始まる箇条書き)
        public string fixPoints;         // 直す点 ("- " で始まる箇条書き)
        public int contentScore;         // 0〜TopicContentMax
        public int organizationScore;    // 0〜TopicOrganizationMax
        public int languageScore;        // 0〜TopicLanguageMax
        public int score;                // 合計 (アプリで計算する)
        public string corrected;         // 話した内容を正しく直したもの
        public string natural;           // 同じ内容を、ネイティブらしい言い方で
        public string[] phrases;         // 次に使える表現 (3つ)
        public Mistake[] mistakes;       // 文法・語の使い方のミス
        public string[] readAloud;       // 言い直し用: 直しが入った文 (最大3)
        public string transcript;        // 録音を直接採点したとき: 聞いたままの文字起こし
    }

    [Serializable]
    private class PronunciationResult
    {
        public string comment;
    }

    private const string TopicPromptPrompt =
        "You create speaking practice topics for an English learner (intermediate level).\n" +
        "Kind of topic: {0}\n" +
        "- exam: an opinion or explanation question like in the Versant or IELTS speaking test " +
        "(e.g. \"Do you think working from home is better than working in an office? Why?\").\n" +
        "- daily: a personal, everyday question about experiences, habits or preferences " +
        "(e.g. \"Tell me about a place you like to visit on weekends.\").\n" +
        "- work: a business or job-interview question " +
        "(e.g. \"Describe a time you solved a problem at work or school.\").\n" +
        "{1}{2}" +
        "The learner will answer out loud in {3} seconds, so make the topic answerable in that time.\n" +
        "Return \"kind\" (the same kind), \"topic\" (one question in simple English) and " +
        "\"hint\" (one short sentence on how to structure the answer, e.g. \"Give your opinion, then two reasons and an example.\").";

    public const string TopicPromptSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" +
        "\"kind\":{\"type\":\"STRING\"}," +
        "\"topic\":{\"type\":\"STRING\"}," +
        "\"hint\":{\"type\":\"STRING\"}}," +
        "\"required\":[\"kind\",\"topic\",\"hint\"]}";

    private const string TopicGradePrompt =
        "You are an English speaking coach. The learner was given this topic ({0}) and answered out loud " +
        "in {1} seconds without preparation:\n\"{2}\"\n\n" +
        "Their answer was transcribed automatically, so punctuation may be missing and a word may occasionally " +
        "be misrecognized. Do not count punctuation or capitalization as mistakes.\n{3}\n" +
        "First write the feedback, then decide the scores from it:\n" +
        "- \"goodPoints\": what they did well, as short bullet lines starting with \"- \".\n" +
        "- \"fixPoints\": the most important things to improve (content, structure, grammar), " +
        "as short bullet lines starting with \"- \".\n" +
        "- \"contentScore\" (0-{4}): {4}-{5} = answers the topic fully with clear reasons and an example; " +
        "{6}-{7} = answers it but the reasons or examples are weak or unclear; 1-{8} = only partly answers it; " +
        "0 = says nothing meaningful.\n" +
        "- \"organizationScore\" (0-{9}): {9}-{10} = clear structure (main point first, then reasons and examples) " +
        "with linking words (first, also, because, for example, so); {11}-{12} = some structure but jumps around; " +
        "1-{13} = hard to follow. Ignore hesitations like \"um\".\n" +
        "- \"languageScore\" (0-{9}): {9}-{10} = few mistakes and varied vocabulary; {11}-{12} = several mistakes " +
        "but the meaning is clear; 1-{13} = many mistakes that make it hard to understand.\n" +
        "Remember it was spoken under time pressure, so do not expect perfect English for a high score.\n" +
        "- \"corrected\": their answer rewritten as correct English, keeping their words and ideas as much as possible.\n" +
        "- \"natural\": the same ideas said the way a native speaker would naturally say them.\n" +
        "- \"phrases\": exactly 3 useful phrases they could use next time for this kind of topic.\n" +
        "- " + MistakesInstruction + "\n" +
        "- \"readAloud\": up to 3 sentences from \"corrected\" that contain a correction, for the learner to read aloud.\n" +
        "Write all feedback in simple English. If the learner said nothing meaningful, give 0 for every score " +
        "and empty lists.\n\nThe learner's answer:\n";

    private const string WeakPointsNote =
        "This learner often makes these kinds of mistakes: {0}. Check for them carefully and do not miss any.\n";

    private const string TopicGradeProperties =
        "\"goodPoints\":{\"type\":\"STRING\"}," +
        "\"fixPoints\":{\"type\":\"STRING\"}," +
        "\"contentScore\":{\"type\":\"INTEGER\"}," +
        "\"organizationScore\":{\"type\":\"INTEGER\"}," +
        "\"languageScore\":{\"type\":\"INTEGER\"}," +
        "\"corrected\":{\"type\":\"STRING\"}," +
        "\"natural\":{\"type\":\"STRING\"}," +
        "\"phrases\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\"}}," +
        MistakesProperty + "," +
        "\"readAloud\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\"}}";

    private const string TopicGradeFields =
        "\"goodPoints\",\"fixPoints\",\"contentScore\",\"organizationScore\",\"languageScore\"," +
        "\"corrected\",\"natural\",\"phrases\",\"mistakes\",\"readAloud\"";

    public const string TopicGradeSchema =
        "{\"type\":\"OBJECT\",\"properties\":{" + TopicGradeProperties + "}," +
        "\"propertyOrdering\":[" + TopicGradeFields + "]," +
        "\"required\":[" + TopicGradeFields + "]}";

    /// <summary>録音をそのまま採点するとき: 最初に聞いたままの transcript を書かせる (文字起こしと採点を 1 回の通信で)</summary>
    public const string TopicGradeAudioSchema =
        "{\"type\":\"OBJECT\",\"properties\":{\"transcript\":{\"type\":\"STRING\"}," + TopicGradeProperties + "}," +
        "\"propertyOrdering\":[\"transcript\"," + TopicGradeFields + "]," +
        "\"required\":[\"transcript\"," + TopicGradeFields + "]}";

    private const string TopicGradeAudioNote =
        "The learner's answer is the attached audio (it is not written below). First write \"transcript\": " +
        "exactly what they said, word for word, keeping every grammar mistake (do not correct anything; leave out " +
        "only fillers like \"um\"). If you cannot hear any English, write an empty string. " +
        "Then grade that transcript as described above.";

    private const string PronunciationPrompt =
        "The attached audio is an English learner reading this sentence aloud:\n\"{0}\"\n" +
        "In 2-3 short sentences of simple English, say which words were hard to understand and how to say them " +
        "more clearly. If it was clear, say so and give one small tip.";

    public const string PronunciationSchema =
        "{\"type\":\"OBJECT\",\"properties\":{\"comment\":{\"type\":\"STRING\"}},\"required\":[\"comment\"]}";

    /// <summary>お題を1つ作る。weakTypes: 苦手なミスの種類 (それを使いやすいお題にする)。recentTopics: 最近のお題 (避ける)</summary>
    public async Task<TopicPrompt> GenerateTopicAsync(string kind, IList<string> weakTypes, IList<string> recentTopics)
    {
        string weak = weakTypes != null && weakTypes.Count > 0
            ? "Choose a topic where the learner will naturally need these grammar points: " +
              string.Join(", ", weakTypes) + ".\n"
            : "";
        string recent = recentTopics != null && recentTopics.Count > 0
            ? "Do not repeat or closely resemble these recent topics:\n- " +
              string.Join("\n- ", recentTopics.Take(10)) + "\n"
            : "";
        int seconds = TopicCatalog.SecondsFor(kind);
        string prompt = TopicPromptPrompt
            .Replace("{0}", kind).Replace("{1}", weak).Replace("{2}", recent)
            .Replace("{3}", seconds.ToString());

        string innerJson = await GeneratePartsAsync(TextPart(prompt), TopicPromptSchema, "\"temperature\":1.0");
        var result = JsonUtility.FromJson<TopicPrompt>(innerJson);
        if (result == null || string.IsNullOrWhiteSpace(result.topic))
            throw new Exception("Could not parse Gemini response: " + innerJson);
        result.kind = kind;
        result.topic = result.topic.Trim();
        result.hint = (result.hint ?? "").Trim();
        return result;
    }

    /// <summary>お題への答え (音声認識の結果) を採点・添削する。weakTypes: 苦手なミスの種類 (重点的に見る)</summary>
    public Task<TopicGrade> GradeTopicAsync(string kind, string topic, int seconds, string spokenText, IList<string> weakTypes) =>
        GradeTopicCoreAsync(kind, topic, seconds, spokenText, null, weakTypes);

    /// <summary>
    /// 答えの録音をそのまま採点する。文字起こしも同じ 1 回の通信で行い、結果の transcript に入れる
    /// (文字起こしと採点を別々に頼むより、Gemini の使用回数が 1 回少ない)
    /// </summary>
    public Task<TopicGrade> GradeTopicAudioAsync(string kind, string topic, int seconds, byte[] wav, IList<string> weakTypes)
    {
        if (wav == null || wav.Length == 0) throw new ArgumentException("No recording.", nameof(wav));
        return GradeTopicCoreAsync(kind, topic, seconds, null, wav, weakTypes);
    }

    private async Task<TopicGrade> GradeTopicCoreAsync(string kind, string topic, int seconds, string spokenText, byte[] wav,
                                                       IList<string> weakTypes)
    {
        string weak = weakTypes != null && weakTypes.Count > 0
            ? WeakPointsNote.Replace("{0}", string.Join(", ", weakTypes))
            : "";
        string prompt = TopicGradePrompt
            .Replace("{0}", kind).Replace("{1}", seconds.ToString()).Replace("{2}", topic ?? "")
            .Replace("{3}", weak)
            .Replace("{4}", TopicContentMax.ToString()).Replace("{5}", (TopicContentMax - 5).ToString())
            .Replace("{6}", (TopicContentMax - 6).ToString()).Replace("{7}", (TopicContentMax / 2).ToString())
            .Replace("{8}", (TopicContentMax / 2 - 1).ToString())
            .Replace("{9}", TopicOrganizationMax.ToString()).Replace("{10}", (TopicOrganizationMax - 5).ToString())
            .Replace("{11}", (TopicOrganizationMax - 6).ToString()).Replace("{12}", (TopicOrganizationMax / 2).ToString())
            .Replace("{13}", (TopicOrganizationMax / 2 - 1).ToString());

        string innerJson = wav == null
            ? await GeneratePartsAsync(TextPart(prompt + spokenText), TopicGradeSchema, "\"temperature\":0.2")
            : await GeneratePartsAsync(TextPart(prompt + "(attached audio)\n\n" + TopicGradeAudioNote) + "," + AudioPart(wav),
                                       TopicGradeAudioSchema, "\"temperature\":0.2", AudioGradeTimeoutSeconds);
        var result = JsonUtility.FromJson<TopicGrade>(innerJson);
        if (result == null) throw new Exception("Could not parse Gemini response: " + innerJson);
        result.transcript = wav == null ? (spokenText ?? "").Trim() : (result.transcript ?? "").Trim();

        result.contentScore = Mathf.Clamp(result.contentScore, 0, TopicContentMax);
        result.organizationScore = Mathf.Clamp(result.organizationScore, 0, TopicOrganizationMax);
        result.languageScore = Mathf.Clamp(result.languageScore, 0, TopicLanguageMax);
        result.score = result.contentScore + result.organizationScore + result.languageScore;
        result.phrases = (result.phrases ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).Take(3).ToArray();
        result.readAloud = (result.readAloud ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).Take(3).ToArray();
        result.mistakes = CleanMistakes(result.mistakes);
        return result;
    }

    // ---------------- シャドーイング: 番組の音声を文と時間に切り出す ----------------
    private const string ShadowSegmentPrompt =
        "This is an English podcast episode (a conversation between hosts). " +
        "Split the spoken conversation into sentences and give each sentence's start and end time in seconds " +
        "from the beginning of the audio (2 decimal places). Write each sentence exactly as it was spoken. " +
        "Give the speaker's name if it is said, otherwise \"A\" or \"B\". " +
        "Leave out the music at the start and end and the fixed programme introduction and closing lines. " +
        "List the sentences in the order they are spoken.";

    public const string ShadowSegmentSchema =
        "{\"type\":\"OBJECT\",\"properties\":{\"sentences\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"OBJECT\"," +
        "\"properties\":{\"text\":{\"type\":\"STRING\"},\"speaker\":{\"type\":\"STRING\"}," +
        "\"start\":{\"type\":\"NUMBER\"},\"end\":{\"type\":\"NUMBER\"}}," +
        "\"required\":[\"text\",\"speaker\",\"start\",\"end\"]}}},\"required\":[\"sentences\"]}";

    /// <summary>番組の音声 (MP3 など) を渡し、{"sentences":[...]} の JSON をそのまま返す</summary>
    public async Task<string> SegmentEpisodeAsync(byte[] audio, string mimeType)
    {
        if (audio == null || audio.Length == 0) throw new ArgumentException("No audio.", nameof(audio));
        string part = "{\"inline_data\":{\"mime_type\":\"" + HttpUtil.JsonEscape(string.IsNullOrEmpty(mimeType) ? "audio/mpeg" : mimeType) +
                      "\",\"data\":\"" + Convert.ToBase64String(audio) + "\"}}";
        return await GeneratePartsAsync(TextPart(ShadowSegmentPrompt) + "," + part, ShadowSegmentSchema,
                                        "\"temperature\":0.1", AudioGradeTimeoutSeconds);
    }

    /// <summary>お手本の文を読んだ録音を聞いて、発音について短くコメントする</summary>
    public async Task<string> CheckPronunciationAsync(string expectedText, byte[] wav)
    {
        if (wav == null || wav.Length == 0) throw new ArgumentException("No recording.", nameof(wav));
        string prompt = PronunciationPrompt.Replace("{0}", expectedText ?? "");
        string innerJson = await GeneratePartsAsync(TextPart(prompt) + "," + AudioPart(wav), PronunciationSchema);
        var result = JsonUtility.FromJson<PronunciationResult>(innerJson);
        if (result == null || string.IsNullOrWhiteSpace(result.comment))
            throw new Exception("Could not parse Gemini response: " + innerJson);
        return result.comment.Trim();
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

    private async Task<string> PostToModelsAsync(string body, string[] models, int timeoutSeconds)
    {
        var keys = UsableKeys;
        if (keys.Count == 0) throw new InvalidOperationException("Gemini API key is not set.");
        var order = AttemptOrder(keys.ToArray(), models);

        HttpException lastError = null;

        foreach (var (keyIndex, model) in order)
        {
            string url = $"{EndpointBase}{model}:generateContent";
            string apiKey = keys[keyIndex];

            for (int attempt = 0; attempt < MaxAttemptsPerModel; attempt++)
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
                    if (attempt < MaxAttemptsPerModel - 1)
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
    private async Task<string> GeneratePartsAsync(string partsJson, string schemaJson, string extraConfig = null,
                                                  int timeoutSeconds = DefaultTimeoutSeconds)
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

        string response = await PostWithRetryAsync(body, timeoutSeconds);

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
