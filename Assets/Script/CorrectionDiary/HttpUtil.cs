using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

/// <summary>HTTPステータスコード付きの例外 (リトライ判定用)</summary>
public class HttpException : Exception
{
    public long StatusCode { get; }

    public HttpException(long statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// UnityWebRequest を async/await で扱うための共通ヘルパー。
/// (Unity 2022.3 では UnityWebRequestAsyncOperation が await できないため Task.Yield でポーリング)
/// </summary>
public static class HttpUtil
{
    public static async Task<string> PostJsonAsync(
        string url,
        string json,
        (string name, string value)[] headers = null,
        int timeoutSeconds = 60)
    {
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("Content-Type", "application/json");

            if (headers != null)
            {
                foreach (var h in headers)
                    req.SetRequestHeader(h.name, h.value);
            }

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                throw new HttpException(
                    req.responseCode,
                    $"HTTP {req.responseCode} : {req.error}\n{req.downloadHandler?.text}");
            }

            return req.downloadHandler.text;
        }
    }

    /// <summary>JSON文字列リテラル用のエスケープ (前後のダブルクォートは含まない)</summary>
    public static string JsonEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 16);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}