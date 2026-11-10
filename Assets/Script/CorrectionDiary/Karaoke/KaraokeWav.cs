using System;

/// <summary>16bit PCM の WAV (ヘッダー 44 バイト) から、秒で指定した部分を切り出す (行ごとの My voice と採点に送る部分)</summary>
public static class KaraokeWav
{
    private const int HeaderSize = 44;

    public static double Seconds(byte[] wav)
    {
        int bps = BytesPerSecond(wav);
        return bps <= 0 ? 0 : (double)(wav.Length - HeaderSize) / bps;
    }

    /// <summary>start〜end 秒の WAV。録音の外なら null。end が録音より後なら最後まで</summary>
    public static byte[] Slice(byte[] wav, double start, double end)
    {
        int bps = BytesPerSecond(wav);
        if (bps <= 0) return null;
        int channels = BitConverter.ToInt16(wav, 22);
        int block = channels * 2;
        int dataLength = wav.Length - HeaderSize;
        int from = Align(Math.Max(0, start) * bps, block);
        int to = Math.Min(dataLength, Align(end * bps, block));
        if (from >= dataLength || to <= from) return null;
        var pcm = new byte[to - from];
        Buffer.BlockCopy(wav, HeaderSize + from, pcm, 0, pcm.Length);
        return WavSplitter.FromPcm(pcm, channels, BitConverter.ToInt32(wav, 24));
    }

    private static int BytesPerSecond(byte[] wav)
    {
        if (wav == null || wav.Length <= HeaderSize) return 0;
        return BitConverter.ToInt32(wav, 24) * BitConverter.ToInt16(wav, 22) * 2;
    }

    private static int Align(double bytes, int block) => (int)(Math.Round(bytes) / block) * block;
}
