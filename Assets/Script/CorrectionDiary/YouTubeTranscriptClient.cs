using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

/// <summary>
/// YouTube 動画の英語字幕をスマホから取る (非公式。Python の youtube-transcript-api 1.x と同じ手順)。
/// player API (Android クライアントのふり) → 字幕トラックの URL → timedtext XML。
/// YouTube 側の仕様が変わったらここの定数を変える。
/// </summary>
public class YouTubeTranscriptClient
{
    // 変わりやすい値は 1 か所にまとめる
    private const string PlayerUrl = "https://www.youtube.com/youtubei/v1/player";
    private const string ClientName = "ANDROID";
    private const string ClientVersion = "20.10.38";

    /// <summary>
    /// 英語字幕を取る。取れなければ null (字幕なし・JSON でない・HTTP エラー)。
    /// 通信できないとき (StatusCode == 0) だけ HttpException を外へ投げる。
    /// </summary>
    public async Task<List<TranscriptLine>> GetEnglishAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return null;
        try
        {
            string body = "{\"context\":{\"client\":{\"clientName\":\"" + ClientName +
                          "\",\"clientVersion\":\"" + ClientVersion + "\"}},\"videoId\":\"" +
                          HttpUtil.JsonEscape(videoId) + "\"}";
            string playerJson = await HttpUtil.PostJsonAsync(PlayerUrl, body);
            string trackUrl = PickTrackUrl(playerJson);
            if (trackUrl == null) return null;
            string xml = await HttpUtil.GetAsync(trackUrl);
            var lines = ParseTimedText(xml);
            return lines.Count > 0 ? lines : null;
        }
        catch (HttpException e) when (e.StatusCode != 0)
        {
            return null; // 4xx/5xx は「取れなかった」として扱う
        }
    }

    /// <summary>
    /// player の応答 JSON から英語字幕の URL を選ぶ。人の字幕 (kind なし) を優先、無ければ自動 (asr)。
    /// 人の英語が複数なら "en" ちょうどを優先。URL から &amp;fmt=... を除く。無ければ null。
    /// </summary>
    public static string PickTrackUrl(string playerJson)
    {
        object root;
        try { root = MiniJson.Parse(playerJson); }
        catch (Exception) { return null; }

        var tracks = Dig(root, "captions", "playerCaptionsTracklistRenderer", "captionTracks") as List<object>;
        if (tracks == null) return null;

        string human = null, humanExact = null, auto = null;
        foreach (var t in tracks)
        {
            var d = t as Dictionary<string, object>;
            if (d == null) continue;
            string lang = JsonGet.Str(d, "languageCode");
            string url = JsonGet.Str(d, "baseUrl");
            if (url.Length == 0) continue;
            if (!(lang == "en" || lang.StartsWith("en-", StringComparison.Ordinal))) continue;

            string kind = JsonGet.Str(d, "kind");
            if (kind.Length == 0)
            {
                if (human == null) human = url;
                if (lang == "en" && humanExact == null) humanExact = url;
            }
            else if (kind == "asr" && auto == null)
            {
                auto = url;
            }
        }
        string picked = humanExact ?? human ?? auto;
        return picked == null ? null : Regex.Replace(picked, "&fmt=[^&]*", "");
    }

    /// <summary>
    /// timedtext XML (&lt;transcript&gt;&lt;text start dur&gt;) を読む。
    /// XML の実体参照と、二重に符号化された HTML 実体 (&amp;#39; など) を戻し、改行は空白にする。
    /// 空の文は捨てる。XML が壊れていれば空のリスト (例外にしない)。
    /// </summary>
    public static List<TranscriptLine> ParseTimedText(string xml)
    {
        var result = new List<TranscriptLine>();
        if (string.IsNullOrEmpty(xml)) return result;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            XDocument doc;
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
                doc = XDocument.Load(reader);
            // 子要素 (<font> など) があっても全体のテキストを使う
            foreach (var e in doc.Descendants("text"))
            {
                string text = Clean(e.Value);
                if (text.Length == 0) continue;
                result.Add(new TranscriptLine
                {
                    Start = ParseSeconds((string)e.Attribute("start")),
                    Duration = ParseSeconds((string)e.Attribute("dur")),
                    Text = text
                });
            }
        }
        catch (Exception)
        {
            return new List<TranscriptLine>();
        }
        return result;
    }

    private static string Clean(string raw)
    {
        string s = WebUtility.HtmlDecode(raw ?? "");
        s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        return Regex.Replace(s, " {2,}", " ").Trim();
    }

    private static double ParseSeconds(string v) =>
        v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

    private static object Dig(object node, params string[] path)
    {
        foreach (var key in path)
        {
            var d = node as Dictionary<string, object>;
            if (d == null || !d.TryGetValue(key, out node)) return null;
        }
        return node;
    }
}
