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
    /// Gemini の音声の最後に付くことがある雑音 (「ザー」「ザッ」) だけを切る。雑音でないところは一切変えない。
    /// 20 ms ごとに大きさと「高い音の割合」(隣の値との差の大きさ ÷ 音の大きさ) を見る。
    /// 雑音 = 高い音ばかり (4〜5 kHz 付近)。声 = 主に低い音 (1〜2 kHz 以下)。小さい声・短い言葉は声として残す。
    /// 切るのは次の 2 つだけ (何回か続いていても):
    ///  1. 最後に 0.2 秒以上の無音があり、そのあとに 0.4 秒以下の短い「高い音だけの音」がある (実際にあった「ザッ」)
    ///  2. 最後に「高い音だけの音」が 0.3 秒以上続いている (「ザー」)。文の最後の s / sh (0.3 秒未満) は残る
    /// 切ったときだけ、最後の 30 ms を小さくしていく
    /// </summary>
    public static float[] TrimTrailingNoise(float[] samples, int sampleRate)
    {
        if (samples == null || samples.Length == 0) return new float[0];
        int win = Math.Max(1, sampleRate / 50);
        int windows = (samples.Length + win - 1) / win;
        var energy = new double[windows];
        var diff = new double[windows];
        var rms = new double[windows];
        for (int w = 0; w < windows; w++)
        {
            int start = w * win, end = Math.Min(samples.Length, start + win);
            double e = 0, d = 0;
            for (int i = start; i < end; i++)
            {
                e += samples[i] * (double)samples[i];
                if (i > 0) { double dd = samples[i] - samples[i - 1]; d += dd * dd; }
            }
            energy[w] = e;
            diff[w] = d;
            rms[w] = Math.Sqrt(e / (end - start));
        }

        const double NoiseRatio = 0.5;     // 高い音の割合がこれより大きい = 雑音らしい (約 2.7 kHz より上が中心)
        bool NoiseLike(int w) => energy[w] > 0 && diff[w] / energy[w] > NoiseRatio;

        double peak = 0;
        for (int w = 0; w < windows; w++) if (!NoiseLike(w)) peak = Math.Max(peak, rms[w]);
        if (peak < 0.01) for (int w = 0; w < windows; w++) peak = Math.Max(peak, rms[w]);
        if (peak < 0.01) return (float[])samples.Clone();
        bool Silent(int w) => rms[w] < peak * 0.02;

        const int GapWindows = 10;         // 0.2 秒
        const int BlipWindows = 20;        // 0.4 秒
        const int HissWindows = 15;        // 0.3 秒
        int keepSamples = samples.Length;
        bool cut = false;
        for (int round = 0; round < 3; round++)
        {
            int usable = Math.Min(windows, (keepSamples + win - 1) / win);
            int last = usable - 1;
            while (last >= 0 && Silent(last)) last--;
            if (last < 0) break;
            int first = last;
            while (first > 0 && !Silent(first - 1)) first--;

            // 1. 無音のあとの短い「高い音だけの音」
            int gapEnd = first - 1, gapStart = gapEnd;
            while (gapStart >= 0 && Silent(gapStart)) gapStart--;
            double segE = 0, segD = 0;
            for (int w = first; w <= last; w++) { segE += energy[w]; segD += diff[w]; }
            bool noisySegment = segE > 0 && segD / segE > NoiseRatio;
            if (gapStart >= 0 && last - first + 1 <= BlipWindows && gapEnd - gapStart >= GapWindows && noisySegment)
            {
                keepSamples = Math.Min(keepSamples, (gapStart + 1) * win + sampleRate / 10);   // 声の後ろに 0.1 秒の無音を残す
                cut = true;
                continue;
            }

            // 2. 最後に続く「高い音だけの音」
            int voiceEnd = last;
            while (voiceEnd >= first && NoiseLike(voiceEnd)) voiceEnd--;
            if (last - voiceEnd >= HissWindows && voiceEnd >= first)
            {
                keepSamples = Math.Min(keepSamples, (voiceEnd + 1) * win + sampleRate * 60 / 1000);
                cut = true;
                continue;
            }
            break;
        }
        if (!cut) return (float[])samples.Clone();

        int length = Math.Max(1, Math.Min(samples.Length, keepSamples));
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
