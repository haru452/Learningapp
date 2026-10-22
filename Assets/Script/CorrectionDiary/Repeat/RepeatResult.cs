using System;

/// <summary>リピーティングの 1 文の結果 (クラウドに送る形。端末の一時保存にも使う)</summary>
[Serializable]
public class RepeatResult
{
    public string sentence;
    public string heard;
    public int accuracy;      // 0〜100
    public int replays;       // 聞き直した回数
    public string source;     // diary / summary / topic / tatoeba
    public string voice;      // human / gemini / device
    public long tatoebaId;    // 0 = なし
    public long audioId;      // 0 = なし
    public string mode;       // repeat / shadow (空ならリピーティング)
}
