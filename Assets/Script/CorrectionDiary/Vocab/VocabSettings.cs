using System;
using System.Globalization;
using UnityEngine;

/// <summary>単語帳の設定 (PlayerPrefs)</summary>
public static class VocabSettings
{
    public static readonly int[] SessionSizes = { 5, 10, 15, 20 };
    public static readonly int[] Retentions = { 80, 85, 90, 95 };

    /// <summary>A1..B2。まだ選んでいなければ空</summary>
    public static string Level
    {
        get
        {
            string l = PlayerPrefs.GetString("vocab_level", "");
            return CefrWordList.LevelIndex(l) >= 0 ? l : "";
        }
        set
        {
            // 今と同じレベルを押し直しただけなら、提案の数え始め (vocab_level_set_at) は変えない
            if (CefrWordList.LevelIndex(value) < 0 || value == Level) return;
            PlayerPrefs.SetString("vocab_level", value);
            PlayerPrefs.SetString("vocab_level_set_at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }
    }

    /// <summary>今のレベルにした時刻 (レベルの提案は、ここから後の評価で決める)</summary>
    public static DateTime LevelSetAt =>
        DateTime.TryParse(PlayerPrefs.GetString("vocab_level_set_at", ""), CultureInfo.InvariantCulture,
                          DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
            ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : DateTime.MinValue;

    public static int SessionSize
    {
        get { int n = PlayerPrefs.GetInt("vocab_session_size", 10); return Array.IndexOf(SessionSizes, n) >= 0 ? n : 10; }
        set { if (Array.IndexOf(SessionSizes, value) >= 0) { PlayerPrefs.SetInt("vocab_session_size", value); PlayerPrefs.Save(); } }
    }

    /// <summary>目標の想起率 (%)</summary>
    public static int Retention
    {
        get { int n = PlayerPrefs.GetInt("vocab_retention", 90); return Array.IndexOf(Retentions, n) >= 0 ? n : 90; }
        set { if (Array.IndexOf(Retentions, value) >= 0) { PlayerPrefs.SetInt("vocab_retention", value); PlayerPrefs.Save(); } }
    }
}
