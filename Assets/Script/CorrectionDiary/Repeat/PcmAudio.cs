using System;
using System.IO;

/// <summary>音声データの変換 (Gemini の PCM 16bit → float、float → WAV ファイル)</summary>
public static class PcmAudio
{
    /// <summary>16bit リトルエンディアンの PCM を -1〜1 の float にする</summary>
    public static float[] Pcm16ToFloats(byte[] pcm)
    {
        if (pcm == null) return new float[0];
        var f = new float[pcm.Length / 2];
        for (int i = 0; i < f.Length; i++)
            f[i] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) / 32768f;
        return f;
    }

    /// <summary>
    /// 声が終わった後の「ザー」という雑音を切る (Gemini の音声の最後に付くことがある)。
    /// 20 ms ごとの音の大きさを見て、いちばん大きいところの 15% を超える最後の位置 + 120 ms までを残し、
    /// 最後の 30 ms を小さくしていく (プツッと切れないように)。全部小さい音なら、そのまま返す
    /// </summary>
    public static float[] TrimTrailingNoise(float[] samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0) return new float[0];
        int win = Math.Max(1, sampleRate / 50);
        int windows = (samples.Length + win - 1) / win;
        var rms = new double[windows];
        double peak = 0;
        for (int w = 0; w < windows; w++)
        {
            int start = w * win, end = Math.Min(samples.Length, start + win);
            double sum = 0;
            for (int i = start; i < end; i++) sum += samples[i] * (double)samples[i];
            rms[w] = Math.Sqrt(sum / (end - start));
            peak = Math.Max(peak, rms[w]);
        }
        if (peak < 0.01) return (float[])samples.Clone();

        int last = 0;
        for (int w = windows - 1; w >= 0; w--)
            if (rms[w] >= peak * 0.15) { last = w; break; }
        int length = Math.Min(samples.Length, (last + 1) * win + sampleRate * 120 / 1000);

        var result = new float[length];
        Array.Copy(samples, result, length);
        int fade = Math.Min(length, sampleRate * 30 / 1000);
        for (int i = 0; i < fade; i++)
            result[length - fade + i] *= (float)(fade - 1 - i) / Math.Max(1, fade - 1);
        return result;
    }

    /// <summary>float のサンプルを 16bit PCM の WAV ファイルにする</summary>
    public static byte[] ToWav(float[] samples, int channels, int sampleRate)
    {
        samples = samples ?? new float[0];
        int dataBytes = samples.Length * 2;
        using (var ms = new MemoryStream(44 + dataBytes))
        using (var w = new BinaryWriter(ms))
        {
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16);
            w.Write((short)1);                       // PCM
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);      // 1 秒あたりのバイト数
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(dataBytes);
            foreach (float s in samples)
                w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, s)) * 32767f));
            return ms.ToArray();
        }
    }
}
