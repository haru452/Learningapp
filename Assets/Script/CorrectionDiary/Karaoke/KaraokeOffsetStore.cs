using UnityEngine;

/// <summary>動画ごとの歌詞のずらし量 (± ボタンで直した値) を端末に覚える</summary>
public static class KaraokeOffsetStore
{
    private static string Key(string videoId) => "karaoke_offset_" + videoId;

    public static double? Get(string videoId)
    {
        if (string.IsNullOrEmpty(videoId) || !PlayerPrefs.HasKey(Key(videoId))) return null;
        return PlayerPrefs.GetFloat(Key(videoId));
    }

    public static void Set(string videoId, double offset)
    {
        if (string.IsNullOrEmpty(videoId)) return;
        PlayerPrefs.SetFloat(Key(videoId), (float)offset);
        PlayerPrefs.Save();
    }
}
