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
    public string geminiApiKey;
    public string deeplApiKey;
    public string discordWebhookUrl;
    public string discordBotToken;
    public string discordChannelId;
    public string reminderWorkerUrl;
    public string reminderSecret;
    public string supabaseUrl;
    public string supabaseAnonKey;

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
