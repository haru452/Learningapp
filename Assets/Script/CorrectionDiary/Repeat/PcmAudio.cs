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
