using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 単語帳の端末の保存 (persistentDataPath/vocab/)。
///   deck.json (単語帳) / reviews.json (評価の履歴、消さない) / dict/&lt;word&gt;.json (辞書の結果、取り直さない) /
///   audio/ (辞書の音声) / rec/&lt;word&gt;.wav (発音の録音。採点が済んだら消す。次の起動時にも全部消す)
/// </summary>
public static class VocabStore
{
    public static string Root => Path.Combine(Application.persistentDataPath, "vocab");
    public static string AudioDir => Path.Combine(Root, "audio");
    private static string DeckPath => Path.Combine(Root, "deck.json");
    private static string ReviewsPath => Path.Combine(Root, "reviews.json");
    private static string DictDir => Path.Combine(Root, "dict");
    private static string RecDir => Path.Combine(Root, "rec");

    /// <summary>単語帳を読む。壊れていたら deck.broken.json に退避して空から始める (上書きしない)</summary>
    public static VocabDeck LoadDeck()
    {
        try
        {
            if (!File.Exists(DeckPath)) return new VocabDeck(new List<VocabCard>());
            string text = File.ReadAllText(DeckPath, Encoding.UTF8);
            var cards = VocabJson.DeckFromJson(text);
            if (cards.Count == 0 && !IsEmptyArray(text))
            {
                Debug.LogWarning("[Vocab] deck.json could not be read. Moved to deck.broken.json");
                File.Copy(DeckPath, Path.Combine(Root, "deck.broken.json"), true);
                File.Delete(DeckPath);
            }
            return new VocabDeck(cards);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] could not load the deck: " + e.Message);
            return new VocabDeck(new List<VocabCard>());
        }
    }

    public static void SaveDeck(VocabDeck deck) => WriteAtomic(DeckPath, VocabJson.DeckToJson(deck.Cards));

    public static List<VocabReview> LoadReviews()
    {
        try { return File.Exists(ReviewsPath) ? VocabJson.ReviewsFromJson(File.ReadAllText(ReviewsPath, Encoding.UTF8)) : new List<VocabReview>(); }
        catch (Exception e)
        {
            Debug.LogWarning("[Vocab] could not load reviews: " + e.Message);
            return new List<VocabReview>();
        }
    }

    public static void AppendReview(VocabReview review)
    {
        var all = LoadReviews();
        all.Add(review);
        WriteAtomic(ReviewsPath, VocabJson.ReviewsToJson(all));
    }

    /// <summary>保存した辞書の結果 (無い・壊れていれば null)</summary>
    public static DictionaryResult LoadDict(string word)
    {
        string path = DictPath(word);
        try { return File.Exists(path) ? VocabJson.DictFromJson(File.ReadAllText(path, Encoding.UTF8)) : null; }
        catch (Exception) { return null; }
    }

    public static void SaveDict(string word, DictionaryResult result)
    {
        if (result == null || !result.Found) return;   // 見つからなかった結果は保存しない (あとで載るかもしれない)
        try { WriteAtomic(DictPath(word), VocabJson.DictToJson(result)); }
        catch (Exception e) { Debug.LogWarning("[Vocab] could not save the dictionary result: " + e.Message); }
    }

    public static string RecPath(string word)
    {
        Directory.CreateDirectory(RecDir);
        return Path.Combine(RecDir, SafeName(word) + ".wav");
    }

    public static void DeleteRecording(string word)
    {
        try { File.Delete(Path.Combine(RecDir, SafeName(word) + ".wav")); } catch (Exception) { }
    }

    public static void DeleteAllRecordings()
    {
        try { if (Directory.Exists(RecDir)) Directory.Delete(RecDir, true); }
        catch (Exception e) { Debug.LogWarning("[Vocab] could not delete recordings: " + e.Message); }
    }

    /// <summary>ファイル名に使える形 (英数字以外は _、長い語は切る)</summary>
    public static string SafeName(string word)
    {
        string k = VocabDeck.Key(word);
        var sb = new StringBuilder();
        foreach (char c in k) sb.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : '_');
        string s = sb.ToString();
        if (s.Length > 60) s = s.Substring(0, 60);
        // 別の語が同じ名前にならないよう、記号を含む語には元の語から作った番号を足す
        if (s != k) s += "_" + StableHash(k).ToString("x8");
        return s.Length == 0 ? "_" : s;
    }

    // 起動のたびに変わらないハッシュ (FNV-1a)
    private static uint StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    private static string DictPath(string word)
    {
        Directory.CreateDirectory(DictDir);
        return Path.Combine(DictDir, SafeName(word) + ".json");
    }

    private static bool IsEmptyArray(string text) => text.Trim().Replace(" ", "") == "[]" || text.Trim().Length == 0;

    // 一時ファイルに書いてから置き換える (途中で落ちても前のファイルが残る)
    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}
