using System.Collections.Generic;

/// <summary>MiniJson で読んだオブジェクトから値を取り出す (単語帳用。無い・型が違うときは空)</summary>
public static class JsonGet
{
    public static string Str(Dictionary<string, object> d, string key) =>
        d != null && d.TryGetValue(key, out object v) && v is string s ? s : "";
}
