using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.Networking;

/// <summary>
/// 単語カードの裏に出す画像を Pixabay で探して、画像のデータを取る。
/// Pixabay の決まり: 画像は端末に保存して使い (同じ単語で何度も聞かない)、画面に Pixabay から取ったことを出す。
/// </summary>
public class PixabayClient
{
    private const string ApiUrl = "https://pixabay.com/api/";
    private readonly string _apiKey;

    public PixabayClient(string apiKey)
    {
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>単語の画像を探す URL (セーフサーチ・英語で検索・人気順で 3 件)</summary>
    public static string SearchUrl(string apiKey, string word) =>
        ApiUrl + "?key=" + Uri.EscapeDataString(apiKey ?? "") + "&q=" + Uri.EscapeDataString(word ?? "") +
        "&lang=en&image_type=all&safesearch=true&order=popular&per_page=3";

    /// <summary>検索の応答から、いちばん上の画像の URL (webformatURL、幅 640 まで) を選ぶ。無ければ null</summary>
    public static string PickImageUrl(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        object root;
        try { root = MiniJson.Parse(json); }
        catch (Exception) { return null; }
        var hits = (root as Dictionary<string, object>)?.TryGetValue("hits", out var h) == true ? h as List<object> : null;
        if (hits == null) return null;
        foreach (var hit in hits)
        {
            if (!(hit is Dictionary<string, object> d)) continue;
            string url = JsonGet.Str(d, "webformatURL");
            if (url.Length > 0) return url;
        }
        return null;
    }

    /// <summary>
    /// 単語の画像のデータ (jpg/png) を返す。見つからなければ null。
    /// 通信できない・キーの間違いなどは HttpException を投げる (「画像なし」と保存しないため)。
    /// </summary>
    public async Task<byte[]> GetImageAsync(string word)
    {
        if (!IsConfigured) throw new InvalidOperationException("Pixabay API key is not set.");
        string url = PickImageUrl(await HttpUtil.GetAsync(SearchUrl(_apiKey, word)));
        if (url == null) return null;
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = 30;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
                throw new HttpException(req.responseCode, "HTTP " + req.responseCode + " : " + req.error);
            return req.downloadHandler.data;
        }
    }
}
