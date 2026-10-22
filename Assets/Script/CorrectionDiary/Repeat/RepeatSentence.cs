/// <summary>リピーティングで出す 1 文</summary>
public class RepeatSentence
{
    public string Text;
    public string Source;      // diary / summary / topic / tatoeba
    public bool Missed;        // 前回言えなかった文
    public long? TatoebaId;    // Tatoeba の文の番号
    public long? AudioId;      // Tatoeba の録音の番号 (あれば人の声で流す)
    public string Author;      // 録音した人 (Tatoeba)
    public string License;     // 録音のライセンス (Tatoeba)

    public bool HasHumanAudio => AudioId.HasValue && AudioId.Value > 0;
}
