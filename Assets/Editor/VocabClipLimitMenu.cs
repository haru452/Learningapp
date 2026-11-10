using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 確かめるとき用: 動画の場面の「1 日 30 回」の数を 0 に戻す (この PC の Unity で再生するときの保存だけ。スマホは変わらない)。
/// YouTube 自体の上限 (403 quotaExceeded) で止まった日は、これでは戻らない。
/// </summary>
public static class VocabClipLimitMenu
{
    [MenuItem("Tools/Vocabulary/Reset Today's Video Search Count")]
    private static void Reset()
    {
        string path = Path.Combine(Application.persistentDataPath, "vocab", "clip_search.json");
        if (File.Exists(path)) File.Delete(path);
        EditorUtility.DisplayDialog("Vocabulary", "今日の動画の検索の回数を 0 に戻しました。\n(この PC の Unity だけ。スマホは変わりません)", "OK");
    }
}
