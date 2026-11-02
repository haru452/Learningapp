using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Merriam-Webster Learner's Dictionary (dictionaryapi.com) で単語を引く。
/// キーは ApiSecrets.json の merriamWebsterApiKey。429・5xx は少し待って 2 回までやり直す
/// </summary>
public class MerriamWebsterClient : IDictionaryProvider
{
    private const string Endpoint = "https://www.dictionaryapi.com/api/v3/references/learners/json/";
    private const int Retries = 2;
    private readonly string _apiKey;

    public MerriamWebsterClient(string apiKey)
    {
        _apiKey = (apiKey ?? "").Trim();
    }

    public bool IsConfigured => _apiKey.Length > 0;

    public async Task<DictionaryResult> LookupAsync(string word)
    {
        if (!IsConfigured) throw new InvalidOperationException("No dictionary key.");
        string w = VocabDeck.Key(word);
        string url = Endpoint + Uri.EscapeDataString(w) + "?key=" + Uri.EscapeDataString(_apiKey);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                string body = await HttpUtil.GetAsync(url, 20);
                return MerriamWebsterParser.ParseOrThrow(body, w);
            }
            catch (HttpException e) when ((e.StatusCode == 429 || e.StatusCode >= 500) && attempt < Retries)
            {
                Debug.LogWarning($"[Dictionary] {e.StatusCode}, retrying ({attempt + 1})");
                await Task.Delay(1000 * (1 << attempt));
            }
        }
    }
}
