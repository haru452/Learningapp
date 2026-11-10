using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// 単語カードの裏の画像を端末に保存する (vocab/images/単語.img)。
/// 見つからなかった単語は「単語.none」に日時を書き、NoneRetryDays 日は聞き直さない。
/// 利用者が「Hide」で隠した単語は「単語.hidden」を置く (Show image で消す)。
/// </summary>
public static class VocabImageStore
{
    public const int NoneRetryDays = 30;

    private static string Dir => Path.Combine(VocabStore.Root, "images");
    private static string ImagePath(string word) => Path.Combine(Dir, VocabStore.SafeName(word) + ".img");
    private static string NonePath(string word) => Path.Combine(Dir, VocabStore.SafeName(word) + ".none");
    private static string HiddenPath(string word) => Path.Combine(Dir, VocabStore.SafeName(word) + ".hidden");

    /// <summary>利用者がこの単語の画像を隠したか</summary>
    public static bool IsHidden(string word)
    {
        try { return File.Exists(HiddenPath(word)); }
        catch (Exception) { return false; }
    }

    /// <summary>この単語の画像を隠す / また出す</summary>
    public static void SetHidden(string word, bool hidden)
    {
        try
        {
            if (hidden) { Directory.CreateDirectory(Dir); File.WriteAllText(HiddenPath(word), ""); }
            else File.Delete(HiddenPath(word));
        }
        catch (Exception e) { Debug.LogWarning("[VocabImage] could not save the hide setting: " + e.Message); }
    }

    /// <summary>「画像なし」と記録してから NoneRetryDays 日たっていなければ true</summary>
    public static bool NoneIsFresh(DateTime markedUtc, DateTime nowUtc) => nowUtc - markedUtc < TimeSpan.FromDays(NoneRetryDays);

    /// <summary>保存した画像。無ければ null</summary>
    public static byte[] Load(string word)
    {
        try
        {
            string path = ImagePath(word);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e) { Debug.LogWarning("[VocabImage] could not read image: " + e.Message); return null; }
    }

    /// <summary>最近「画像なし」とわかった単語なら true (Pixabay に聞かない)</summary>
    public static bool IsKnownNone(string word, DateTime nowUtc)
    {
        try
        {
            string path = NonePath(word);
            if (!File.Exists(path)) return false;
            return DateTime.TryParse(File.ReadAllText(path), CultureInfo.InvariantCulture,
                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
                   && NoneIsFresh(at, nowUtc);
        }
        catch (Exception) { return false; }
    }

    public static void Save(string word, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(ImagePath(word), data);
            File.Delete(NonePath(word));
        }
        catch (Exception e) { Debug.LogWarning("[VocabImage] could not save image: " + e.Message); }
    }

    public static void SaveNone(string word, DateTime nowUtc)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(NonePath(word), nowUtc.ToString("o", CultureInfo.InvariantCulture));
        }
        catch (Exception e) { Debug.LogWarning("[VocabImage] could not save image state: " + e.Message); }
    }
}
