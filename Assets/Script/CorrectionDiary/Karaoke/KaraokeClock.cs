using System.Collections.Generic;

/// <summary>
/// 動画の時刻 → 録音の秒。動画が止まったり読み込みで待ったりしても、録音は進み続けるので、
/// 再生中に届いた (録音の秒, 動画の時刻) の組から、その後の時刻を合わせる (純粋な部分。テストあり)
/// </summary>
public class KaraokeClock
{
    private readonly double _startVideo;   // 組がまだ無いときの、録音の 0 秒の動画の時刻
    private readonly List<(double rec, double video)> _anchors = new List<(double rec, double video)>();

    public KaraokeClock(double startVideo = 0) => _startVideo = startVideo;

    public int Count => _anchors.Count;

    /// <summary>再生中に届いた組を足す (動画の時刻の順に届く)</summary>
    public void Add(double rec, double video)
    {
        if (_anchors.Count > 0 && video < _anchors[_anchors.Count - 1].video) _anchors.Clear();   // 巻き戻した: 合わせ直す
        _anchors.Add((rec, video));
    }

    /// <summary>動画の時刻 t が、録音の何秒目か (その前の最後の組から数える)</summary>
    public double RecordSeconds(double t)
    {
        for (int i = _anchors.Count - 1; i >= 0; i--)
            if (_anchors[i].video <= t) return _anchors[i].rec + (t - _anchors[i].video);
        return t - _startVideo;
    }
}
