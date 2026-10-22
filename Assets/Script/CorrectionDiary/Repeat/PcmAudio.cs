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
    /// 20 ms ごとに、音の大きさと「波の向きが変わる回数」(ゼロ交差) を見る。
    /// 声 (母音) = 大きくて、向きが変わる回数が少ない。雑音 = 向きが細かく何度も変わる (大きくても雑音)。
    /// 最後の声の位置 + 100 ms までを残し、最後の 60 ms を小さくしていく。声が見つからなければ、そのまま返す。
    /// さらに、最後に 0.2 秒以上の無音のあと 0.4 秒以下の短い音だけがあるとき (声より大きい「ザッ」が実際にあった) は、
    /// 文の一部ではないので、その無音の前で切る
    /// </summary>
    public static float[] TrimTrailingNoise(float[] samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0) return new float[0];
        int win = Math.Max(1, sampleRate / 50);
        int windows = (samples.Length + win - 1) / win;
        var rms = new double[windows];
        var zcr = new double[windows];
        for (int w = 0; w < windows; w++)
        {
            int start = w * win, end = Math.Min(samples.Length, start + win);
            double sum = 0;
            int crossings = 0;
            for (int i = start; i < end; i++)
            {
                sum += samples[i] * (double)samples[i];
                if (i > start && (samples[i] < 0) != (samples[i - 1] < 0)) crossings++;
            }
            rms[w] = Math.Sqrt(sum / (end - start));
            zcr[w] = crossings / (double)Math.Max(1, end - start - 1);
        }

        const double NoiseZcr = 0.25;   // 1 サンプルごとの向きの変わりやすさ (白い雑音は約 0.5、母音は 0.1 以下)
        double peak = 0;
        for (int w = 0; w < windows; w++) if (zcr[w] < NoiseZcr) peak = Math.Max(peak, rms[w]);
        if (peak < 0.01) return (float[])samples.Clone();

        // 最後の「無音のあとの短い音」を取り除く (何回か続いていても)
        int usable = windows;                  // ここより前の窓だけを見る
        const int GapWindows = 10;             // 0.2 秒
        const int BlipWindows = 20;            // 0.4 秒
        for (int round = 0; round < 3; round++)
        {
            bool Silent(int w) => rms[w] < peak * 0.02;
            int end = usable - 1;
            while (end >= 0 && Silent(end)) end--;
            if (end < 0) break;
            int start = end;
            while (start > 0 && !Silent(start - 1)) start--;
            int gapEnd = start - 1, gapStart = gapEnd;
            while (gapStart >= 0 && Silent(gapStart)) gapStart--;
            int gap = gapEnd - gapStart;
            bool somethingBefore = gapStart >= 0;
            if (!(somethingBefore && end - start + 1 <= BlipWindows && gap >= GapWindows)) break;
            usable = gapStart + 1;             // 無音の前まで
        }

        int last = -1;
        for (int w = usable - 1; w >= 0; w--)
            if (zcr[w] < NoiseZcr && rms[w] >= peak * 0.15) { last = w; break; }
        if (last < 0) return (float[])samples.Clone();
        int length = Math.Min(Math.Min(samples.Length, usable * win), (last + 1) * win + sampleRate / 10);

        var result = new float[length];
        Array.Copy(samples, result, length);
        int fade = Math.Min(length, sampleRate * 60 / 1000);
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
