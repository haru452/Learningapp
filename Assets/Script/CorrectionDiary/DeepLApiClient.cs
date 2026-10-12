using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// DeepL Write API (/v2/write/rephrase) に英文を送り、自然な表現へ校正した結果を受け取る。
/// </summary>
public class DeepLApiClient
{
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _targetLang;

    /// <param name="apiKey">DeepL APIキー</param>
    /// <param name="useFreeApi">Freeキーなら true (api-free.deepl.com)、Proキーなら false (api.deepl.com)</param>
    /// <param name="targetLang">"en-US" または "en-GB"</param>
    public DeepLApiClient(string apiKey, bool useFreeApi = true, string targetLang = "en-US")
    {
        _apiKey = apiKey;
        _targetLang = targetLang;
        _endpoint = useFreeApi
            ? "https://api-free.deepl.com/v2/write/rephrase"
            : "https://api.deepl.com/v2/write/rephrase";
    }

    [Serializable] private class WriteResponse { public Improvement[] improvements; }
    [Serializable] private class Improvement { public string text; }

    public async Task<string> RephraseAsync(string text)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("DeepL API key is not set.");
        if (string.IsNullOrWhiteSpace(text))
            return "";

        string body =
            "{\"text\":[\"" + HttpUtil.JsonEscape(text) + "\"]," +
            "\"target_lang\":\"" + _targetLang + "\"}";

        string response = await HttpUtil.PostJsonAsync(
            _endpoint, body,
            new[] { ("Authorization", "DeepL-Auth-Key " + _apiKey) });

        var parsed = JsonUtility.FromJson<WriteResponse>(response);
        if (parsed?.improvements == null || parsed.improvements.Length == 0)
            throw new Exception("DeepL Write returned no result: " + response);

        return parsed.improvements[0].text;
    }
}
