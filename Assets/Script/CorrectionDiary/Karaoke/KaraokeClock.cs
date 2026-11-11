using System.Collections.Generic;

/// <summary>
/// 動画の時刻 → 録音の秒。動画が止まったり読み込みで待ったりしても、録音は進み続けるので、
/// 再生中に届いた (録音の秒, 動画の時刻) の組から、その後の時刻を合わせる (純粋な部分。テストあり)。
/// 巻き戻して歌い直した所は、新しく歌った方を使う。巻き戻す前にしか歌っていない所は、前に歌った方を使う
/// </summary>
public class KaraokeClock
{
    private const double MaxGap = 2.0;     // 組は 1 秒ごとに届く。これより離れた組は、その時刻を歌った組ではない
    private readonly double _startVideo;   // 組がまだ無いときの、録音の 0 秒の動画の時刻
    private readonly List<(double rec, double video)> _anchors = new List<(double rec, double video)>();

    public KaraokeClock(double startVideo = 0) => _startVideo = startVideo;

    public int Count => _anchors.Count;

    /// <summary>再生中に届いた組を足す (録音の秒の順に届く。巻き戻すと動画の時刻は戻る)</summary>
    public void Add(double rec, double video) => _anchors.Add((rec, video));

    /// <summary>
    /// 動画の時刻 t が、録音の何秒目か。新しい組から見て、t の少し前 (MaxGap 以内) の組を使う。
    /// そういう組が無ければ、t より前で一番新しい組から数える
    /// </summary>
    public double RecordSeconds(double t)
    {
        int fallback = -1;
        for (int i = _anchors.Count - 1; i >= 0; i--)
        {
            double since = t - _anchors[i].video;
            if (since < 0) continue;
            if (since <= MaxGap) return _anchors[i].rec + since;
            if (fallback < 0) fallback = i;
        }
        return fallback >= 0 ? _anchors[fallback].rec + (t - _anchors[fallback].video) : t - _startVideo;
    }
}
