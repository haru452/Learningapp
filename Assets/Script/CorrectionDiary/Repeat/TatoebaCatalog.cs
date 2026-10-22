using System;
using System.Collections.Generic;

/// <summary>
/// Resources/RepeatSentences.json (Tatoeba の文の一覧) を読む。
/// 形: {"sentences":[{"id":1,"audioId":2,"text":"...","author":"...","license":"..."}]}
/// 壊れていたり無かったりしたら空のリストを返す (JsonUtility を使わないので、Unity の外でもテストできる)。
/// </summary>
public static class TatoebaCatalog
{
    public static List<RepeatSentence> Parse(string json)
    {
        var list = new List<RepeatSentence>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null || !(root.TryGetValue("sentences", out object arr) && arr is List<object> items)) return list;
            foreach (var o in items)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                string text = Str(d, "text").Trim();
                if (text.Length == 0) continue;
                list.Add(new RepeatSentence
                {
                    Text = text,
                    Source = "tatoeba",
                    TatoebaId = Num(d, "id"),
                    AudioId = Num(d, "audioId"),
                    Author = Str(d, "author"),
                    License = Str(d, "license"),
                });
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    internal static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) && v is string s ? s : "";

    internal static long? Num(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out object v) && v is double x ? (long)x : (long?)null;
}
