using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 単語帳の「動画の場面」(テーブル vocab_clips) を Supabase の RPC で読み書きする。
/// ・get_vocab_clips(p_words) : 単語ごとの場面を読む
/// ・add_vocab_clips(p_clips) : 見つけた場面を保存する (戻り値は入れた件数)
/// Task 9 のクリップ取得元には、これらのメソッドを関数として渡す。
/// </summary>
public partial class SupabaseService
{
    /// <summary>指定した単語の場面を読む。単語が空・失敗・ログインなしは空のリスト</summary>
    public async Task<List<VocabClip>> GetVocabClipsAsync(IList<string> words)
    {
        if (words == null || words.Count == 0) return new List<VocabClip>();
        if (!IsConfigured || !IsSignedIn) return new List<VocabClip>();
        try
        {
            string res = await RpcAsync("get_vocab_clips", ",\"p_words\":" + BuildWordsJson(words));
            return ParseClips(res);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Supabase] could not get vocab clips: " + e.Message);
            return new List<VocabClip>();
        }
    }

    /// <summary>見つけた場面を保存する。入れた件数を返す。空・失敗・ログインなしは 0</summary>
    public async Task<int> AddVocabClipsAsync(IList<VocabClip> clips)
    {
        if (clips == null || clips.Count == 0) return 0;
        if (!IsConfigured || !IsSignedIn) return 0;
        try
        {
            string res = await RpcAsync("add_vocab_clips", ",\"p_clips\":" + BuildClipsJson(clips));
            // PostgREST は int を素の数字 (例: 3) で返す
            return MiniJson.Parse(res) is double d ? (int)d : 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Supabase] could not add vocab clips: " + e.Message);
            return 0;
        }
    }

    /// <summary>単語の配列を JSON にする。空・null は []</summary>
    public static string BuildWordsJson(IList<string> words)
    {
        var sb = new StringBuilder("[");
        if (words != null)
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(HttpUtil.JsonEscape(words[i] ?? "")).Append('"');
            }
        return sb.Append(']').ToString();
    }

    /// <summary>場面の配列を、vocab_clips の列名をキーにした JSON にする。空・null は []</summary>
    public static string BuildClipsJson(IList<VocabClip> clips)
    {
        var sb = new StringBuilder("[");
        if (clips != null)
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                if (c == null) continue;
                if (sb.Length > 1) sb.Append(',');
                sb.Append("{\"word\":\"").Append(HttpUtil.JsonEscape(c.Word ?? ""))
                  .Append("\",\"video_id\":\"").Append(HttpUtil.JsonEscape(c.VideoId ?? ""))
                  .Append("\",\"start_ms\":").Append(c.StartMs)
                  .Append(",\"end_ms\":").Append(c.EndMs)
                  .Append(",\"sentence\":\"").Append(HttpUtil.JsonEscape(c.Sentence ?? ""))
                  .Append("\",\"channel\":\"").Append(HttpUtil.JsonEscape(c.Channel ?? ""))
                  .Append("\",\"source\":\"").Append(HttpUtil.JsonEscape(c.Source ?? ""))
                  .Append("\"}");
            }
        return sb.Append(']').ToString();
    }

    /// <summary>get_vocab_clips の結果を読む。壊れた JSON・配列でないものは空。word か video_id が無い行は飛ばす</summary>
    public static List<VocabClip> ParseClips(string json)
    {
        var list = new List<VocabClip>();
        if (string.IsNullOrEmpty(json)) return list;
        object root;
        try { root = MiniJson.Parse(json); }
        catch { return list; }
        if (!(root is List<object> rows)) return list;
        foreach (var row in rows)
        {
            if (!(row is Dictionary<string, object> o)) continue;
            string word = Str(o, "word"), videoId = Str(o, "video_id");
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(videoId)) continue;
            list.Add(new VocabClip
            {
                Word = word,
                VideoId = videoId,
                StartMs = Num(o, "start_ms"),
                EndMs = Num(o, "end_ms"),
                Sentence = Str(o, "sentence"),
                Channel = Str(o, "channel"),
                Source = Str(o, "source"),
            });
        }
        return list;
    }

    private static string Str(Dictionary<string, object> o, string key)
        => o.TryGetValue(key, out var v) && v is string s ? s : "";

    private static int Num(Dictionary<string, object> o, string key)
        => o.TryGetValue(key, out var v) && v is double d ? (int)d : 0;
}
