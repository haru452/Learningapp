using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>端末に保存する音声のファイル名と、上限 (個数・容量) を超えたときに消すものを決める</summary>
public static class AudioCachePolicy
{
    public const int MaxFiles = 300;
    public const long MaxBytes = 60L * 1024 * 1024;

    public static string TatoebaName(long audioId) => "tatoeba_" + audioId + ".mp3";

    /// <summary>文と声の名前から作る (同じ文・声なら同じ名前)</summary>
    public static string GeminiName(string text, string voice)
    {
        using (var sha = SHA1.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes((voice ?? "") + "\n" + (text ?? "")));
            var sb = new StringBuilder("gemini_");
            for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("x2"));
            return sb.Append(".wav").ToString();
        }
    }

    /// <summary>上限を超えた分を、最後に使ったのが古い順に返す</summary>
    public static List<string> ToDelete(IList<(string name, long bytes, DateTime lastUsed)> files,
                                        int maxCount = MaxFiles, long maxBytes = MaxBytes)
    {
        var del = new List<string>();
        if (files == null) return del;
        var ordered = files.OrderBy(f => f.lastUsed).ToList();
        int count = ordered.Count;
        long total = ordered.Sum(f => f.bytes);
        foreach (var f in ordered)
        {
            if (count <= maxCount && total <= maxBytes) break;
            del.Add(f.name);
            count--;
            total -= f.bytes;
        }
        return del;
    }
}
