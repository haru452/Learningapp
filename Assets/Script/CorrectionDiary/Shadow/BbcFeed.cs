using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

/// <summary>
/// BBC Learning English「6 Minute English」のポッドキャストのフィード (RSS) を読む。
/// 音声 (MP3) はスマホの中だけに保存する (git・Web には入れない)。
/// </summary>
public static class BbcFeed
{
    public const string FeedUrl = "https://podcasts.files.bbci.co.uk/p02pc9tn.rss";

    public class Episode
    {
        public string Id;
        public string Title;
        public DateTime Published;
        public string AudioUrl;
    }

    /// <summary>新しい順。音声 (enclosure) の無い回は除く。読めなければ空</summary>
    public static List<Episode> Parse(string xml)
    {
        var list = new List<Episode>();
        if (string.IsNullOrWhiteSpace(xml)) return list;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), settings)) doc.Load(reader);
            XmlNodeList items = doc.GetElementsByTagName("item");
            foreach (XmlNode item in items)
            {
                string url = (item["enclosure"]?.Attributes?["url"]?.Value ?? "").Trim();
                if (url.Length == 0) continue;
                string id = IdFrom(item["guid"]?.InnerText);
                if (id.Length == 0) id = IdFrom(FileName(url));
                if (id.Length == 0) continue;
                list.Add(new Episode
                {
                    Id = id,
                    Title = (item["title"]?.InnerText ?? "").Trim(),
                    Published = ParseDate(item["pubDate"]?.InnerText),
                    AudioUrl = url,
                });
            }
        }
        catch (Exception)
        {
            return new List<Episode>();
        }
        return list.OrderByDescending(e => e.Published).ToList();
    }

    // guid の最後の区切り (: か /) より後ろ。ファイル名に使えない文字は除く
    private static string IdFrom(string raw)
    {
        string s = (raw ?? "").Trim();
        int cut = Math.Max(s.LastIndexOf(':'), s.LastIndexOf('/'));
        if (cut >= 0) s = s.Substring(cut + 1);
        return new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
    }

    private static string FileName(string url)
    {
        string path = url;
        int q = path.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) path = path.Substring(0, q);
        string name = path.Substring(path.LastIndexOf('/') + 1);
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    private static DateTime ParseDate(string s)
    {
        s = (s ?? "").Trim();
        // 曜日が日付と合わないフィードもあるので、曜日は読まない
        int comma = s.IndexOf(',');
        if (comma >= 0 && comma <= 4) s = s.Substring(comma + 1).Trim();
        s = s.Replace(" GMT", " +0000").Replace(" UT", " +0000");
        if (DateTimeOffset.TryParseExact(s, new[] { "d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz" }, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset r)) return r.UtcDateTime;
        if (DateTime.TryParseExact(s, "r", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d)) return d;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset o))
            return o.UtcDateTime;
        return DateTime.MinValue;
    }
}
