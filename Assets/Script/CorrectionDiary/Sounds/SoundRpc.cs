using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Supabase の苦手な発音の RPC に送る JSON を作り、返ってきた行を読む</summary>
public static class SoundRpc
{
    /// <summary>save_pronunciation_issues の p_issues: [{sound, label, weight, word, source}] (label はその他のときだけ)</summary>
    public static string IssuesJson(IList<SoundHit> hits, int weight, string source)
    {
        var sb = new StringBuilder("[");
        if (hits != null)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"sound\":").Append(Str(h.Code))
                  .Append(",\"label\":").Append(Str(h.Code == SoundTypes.Other ? h.Label : ""))
                  .Append(",\"weight\":").Append(weight == 3 ? 3 : 1)
                  .Append(",\"word\":").Append(Str(h.Word))
                  .Append(",\"source\":").Append(Str(source))
                  .Append('}');
            }
        }
        return sb.Append(']').ToString();
    }

    /// <summary>get_my_weak_sounds の結果を読む。読めなければ空</summary>
    public static List<WeakSound> ParseWeakSounds(string body)
    {
        var list = new List<WeakSound>();
        try
        {
            if (!(MiniJson.Parse(body) is List<object> rows)) return list;
            foreach (var o in rows)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                var w = new WeakSound
                {
                    Sound = TatoebaCatalog.Str(d, "sound"),
                    Label = TatoebaCatalog.Str(d, "label"),
                    Points = (int)(TatoebaCatalog.Num(d, "points") ?? 0),
                    PreviousPoints = TatoebaCatalog.Num(d, "previous_points") is long p ? (int?)p : null,
                };
                if (d.TryGetValue("words", out object ws) && ws is List<object> wl)
                    foreach (var x in wl) if (x is string s && s.Length > 0) w.Words.Add(s);
                if (w.Sound.Length > 0) list.Add(w);
            }
        }
        catch (Exception)
        {
            list.Clear();
        }
        return list;
    }

    private static string Str(string s) => "\"" + HttpUtil.JsonEscape(s ?? "") + "\"";
}
