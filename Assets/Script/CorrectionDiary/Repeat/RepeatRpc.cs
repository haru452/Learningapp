using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>Supabase のリピーティング用 RPC に送る JSON を作り、返ってきた行を読む</summary>
public static class RepeatRpc
{
    /// <summary>save_repeat_results の p_results (キーは SQL と同じ snake_case)</summary>
    public static string ResultsJson(IList<RepeatResult> results)
    {
        var sb = new StringBuilder("[");
        if (results != null)
        {
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (i > 0) sb.Append(',');
                bool shadow = r.mode == "shadow";   // シャドーイングは英文・言った内容を送らない (Discord にだけ送る)。動画とアドバイスは送る
                sb.Append("{\"sentence\":").Append(Str(shadow ? "" : r.sentence))
                  .Append(",\"heard\":").Append(Str(shadow ? "" : r.heard))
                  .Append(",\"accuracy\":").Append(r.accuracy.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"replays\":").Append(r.replays.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"source\":").Append(Str(r.source))
                  .Append(",\"voice\":").Append(Str(r.voice))
                  .Append(",\"tatoeba_id\":").Append(r.tatoebaId > 0 ? r.tatoebaId.ToString(CultureInfo.InvariantCulture) : "null")
                  .Append(",\"mode\":").Append(Str(r.mode == "shadow" ? "shadow" : "repeat"))
                  .Append(",\"audio_id\":").Append(r.audioId > 0 ? r.audioId.ToString(CultureInfo.InvariantCulture) : "null");
                if (shadow)
                    sb.Append(",\"video_id\":").Append(Str(r.videoId))
                      .Append(",\"video_title\":").Append(Str(r.videoTitle))
                      .Append(",\"advice\":").Append(Str(r.advice));
                sb.Append('}');
            }
        }
        return sb.Append(']').ToString();
    }

    /// <summary>
    /// save_repeat_results に足す引数 (p_timezone はあとで足される)。
    /// シャドーイングの結果を含むときは p_version 2 を付ける: mode 列の無い古いサーバーは「関数が無い」で失敗し、
    /// 結果は端末に一時保存される (リピーティングと間違えて保存されない)
    /// </summary>
    public static string SaveArgs(IList<RepeatResult> results, DateTime takenAtUtc)
    {
        string takenAt = takenAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        bool shadow = results != null && results.Any(r => r != null && r.mode == "shadow");
        return ",\"p_results\":" + ResultsJson(results) + ",\"p_taken_at\":\"" + takenAt + "\"" + (shadow ? ",\"p_version\":2" : "");
    }

    /// <summary>get_my_repeat_sentences の結果を読む。読めなければ空</summary>
    public static List<RepeatSentence> ParseSentences(string body)
    {
        var list = new List<RepeatSentence>();
        try
        {
            if (!(MiniJson.Parse(body) is List<object> rows)) return list;
            foreach (var o in rows)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                string text = TatoebaCatalog.Str(d, "sentence").Trim();
                if (text.Length == 0) continue;
                list.Add(new RepeatSentence
                {
                    Text = text,
                    Source = TatoebaCatalog.Str(d, "source"),
                    Missed = d.TryGetValue("missed", out object m) && m is bool b && b,
                    TatoebaId = TatoebaCatalog.Num(d, "tatoeba_id"),
                    AudioId = TatoebaCatalog.Num(d, "audio_id"),
                });
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    private static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
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
        return sb.Append('"').ToString();
    }
}
