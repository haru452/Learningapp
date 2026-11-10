using System;
using UnityEngine;

/// <summary>
/// API キーなどの秘密情報を Assets/Resources/ApiSecrets.json から読み込む。
/// ApiSecrets.json は .gitignore で除外しているので GitHub には上がらない。
/// 雛形はリポジトリ直下の ApiSecrets.example.json を参照。
///
/// ファイルに値があればそれを優先し、空なら Inspector の値を使う。
/// ※ Resources に置いたファイルはビルドしたアプリには含まれる (GitHub に出ないだけ)。
/// </summary>
[Serializable]
public class ApiSecrets
{
    // Gemini のキーは画面ごとに分けられる (空なら geminiApiKey を使う)。上限 (429) になったら、ほかの画面のキーを借りる。
    // ※ 上限は Google Cloud のプロジェクトごとなので、キーはそれぞれ別のプロジェクトで作ること
    public string geminiApiKey;          // 日記
    public string geminiSummaryApiKey;   // 要約練習
    public string geminiTopicApiKey;     // お題スピーキング
    public string geminiTtsApiKey;       // リピーティングの読み上げ (Gemini の声)
    public string geminiShadowApiKey;    // シャドーイング (お手本・書き起こし・アドバイス)
    public string geminiVocabApiKey;     // 単語帳 (発音のまとめ採点)
    public string deeplApiKey;
    public string discordWebhookUrl;
    public string discordBotToken;
    public string discordChannelId;
    public string reminderWorkerUrl;
    public string reminderSecret;
    public string supabaseUrl;
    public string supabaseAnonKey;
    public string youtubeApiKey;
    public string youtubeShadowApiKey;   // シャドーイングの動画検索 (空なら youtubeApiKey)
    public string youtubeVocabApiKey;    // 単語帳の動画の場面の検索 (空なら youtubeApiKey)
    public string summaryDiscordWebhookUrl;   // 要約練習の結果を送る Webhook (日記とは別のチャンネル)
    public string summaryDiscordChannelId;    // Webhook の代わりに Bot で送る場合のチャンネルID
    public string pronunciationDiscordWebhookUrl;   // お題スピーキングの発音チェックのアドバイスを送る Webhook (別のチャンネル)
    public string pronunciationDiscordChannelId;    // Webhook の代わりに Bot で送る場合のチャンネルID
    public string repeatDiscordWebhookUrl;          // リピーティングの結果を送る Webhook (別のチャンネル)
    public string repeatDiscordChannelId;           // Webhook の代わりに Bot で送る場合のチャンネルID
    public string pixabayApiKey;                    // 単語カードの裏の画像 (pixabay.com/api/docs の Key)。空なら画像は出さない
    public string merriamWebsterApiKey;             // 単語帳の辞書 (Merriam-Webster Learner's Dictionary、dictionaryapi.com の Key (Dictionary))

    private const string ResourceName = "ApiSecrets";
    private static ApiSecrets _data;

    public static ApiSecrets Data
    {
        get
        {
            if (_data != null) return _data;

            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null)
            {
                Debug.LogWarning("[ApiSecrets] Assets/Resources/ApiSecrets.json が見つかりません。"
                                 + "ApiSecrets.example.json をコピーして作成してください。");
                _data = new ApiSecrets();
            }
            else
            {
                try { _data = JsonUtility.FromJson<ApiSecrets>(asset.text) ?? new ApiSecrets(); }
                catch (Exception e)
                {
                    Debug.LogError("[ApiSecrets] ApiSecrets.json の読み込みに失敗しました: " + e.Message);
                    _data = new ApiSecrets();
                }
            }
            return _data;
        }
    }

    /// <summary>ファイルの値が空でなければそれを、空なら Inspector の値を返す。</summary>
    public static string Pick(string fileValue, string inspectorValue) =>
        string.IsNullOrWhiteSpace(fileValue) ? inspectorValue : fileValue.Trim();
}
