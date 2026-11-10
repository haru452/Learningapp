using System;
using System.Collections.Generic;

/// <summary>
/// シャドーイングの「Back 10s」。動画を戻した分だけ直前の録音を捨て、残した録音が動画の時間とずれないようにする。
/// 時刻はすべて録音を始めてからの秒数。
/// </summary>
public class ShadowRewind
{
    public const double BackSeconds = 10;

    private readonly List<(double start, double end)> _kept = new List<(double start, double end)>();
    private double _openStart;   // 今録っている区間の始まり

    /// <summary>戻した回数</summary>
    public int Count { get; private set; }

    /// <summary>録音の時刻 now で戻す。残した録音の終わりから最大 seconds 秒を捨て、実際に捨てた秒数を返す</summary>
    public double Rewind(double now, double seconds = BackSeconds)
    {
        if (now > _openStart) _kept.Add((_openStart, now));
        _openStart = Math.Max(_openStart, now);
        double left = seconds, removed = 0;
        while (left > 0 && _kept.Count > 0)
        {
            var last = _kept[_kept.Count - 1];
            double len = last.end - last.start;
            if (len <= left)
            {
                _kept.RemoveAt(_kept.Count - 1);
                left -= len; removed += len;
            }
            else
            {
                _kept[_kept.Count - 1] = (last.start, last.end - left);
                removed += left; left = 0;
            }
        }
        if (removed > 0) Count++;
        return removed;
    }

    /// <summary>end 秒で録音を止めたときに残す区間 (古い順)</summary>
    public List<(double start, double end)> Segments(double end)
    {
        var list = new List<(double start, double end)>(_kept);
        if (end > _openStart) list.Add((_openStart, end));
        return list;
    }

    /// <summary>残す録音の長さ (秒)。動画のどこまで進んだかと同じになる</summary>
    public double KeptSeconds(double now)
    {
        double sum = 0;
        foreach (var s in Segments(now)) sum += s.end - s.start;
        return sum;
    }

    /// <summary>16bit PCM の WAV から、残す区間だけをつなげる。戻していなければ元の WAV をそのまま返す</summary>
    public static byte[] Cut(byte[] wav, ShadowRewind rewind)
    {
        const int header = 44;
        if (wav == null || wav.Length <= header || rewind == null || rewind.Count == 0) return wav;
        int channels = BitConverter.ToInt16(wav, 22);
        int rate = BitConverter.ToInt32(wav, 24);
        int block = channels * 2;
        int dataLength = wav.Length - header;
        if (rate <= 0 || block <= 0) return wav;

        int ToByte(double sec)
        {
            double samples = Math.Round(sec * rate);
            if (samples <= 0) return 0;
            if (samples * block >= dataLength) return dataLength - dataLength % block;   // 録音の終わりまで
            return (int)samples * block;
        }

        var data = new List<byte>();
        foreach (var s in rewind.Segments(double.MaxValue / 2))
        {
            int from = ToByte(s.start), to = ToByte(s.end);
            if (to <= from) continue;
            var chunk = new byte[to - from];
            Buffer.BlockCopy(wav, header + from, chunk, 0, chunk.Length);
            data.AddRange(chunk);
        }
        var pcm = data.ToArray();
        return WavSplitter.FromPcm(pcm, channels, rate);
    }
}
