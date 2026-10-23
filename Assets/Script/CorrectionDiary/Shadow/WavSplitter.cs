using System;
using System.Collections.Generic;

/// <summary>16bit PCM の WAV (AudioRecorder / PcmAudio.ToWav の形、ヘッダー 44 バイト) を、決まった秒数ずつの WAV に分ける</summary>
public static class WavSplitter
{
    private const int HeaderSize = 44;

    public static List<byte[]> Split(byte[] wav, int seconds)
    {
        var parts = new List<byte[]>();
        if (wav == null || wav.Length <= HeaderSize || seconds <= 0) return parts;
        int channels = BitConverter.ToInt16(wav, 22);
        int rate = BitConverter.ToInt32(wav, 24);
        int bytesPerSecond = rate * channels * 2;
        int dataLength = wav.Length - HeaderSize;
        if (bytesPerSecond <= 0) return parts;

        int partBytes = seconds * bytesPerSecond;
        int offset = 0;
        while (offset < dataLength)
        {
            int length = Math.Min(partBytes, dataLength - offset);
            // 残りが 1 秒未満なら、この部分に足す
            if (dataLength - offset - length < bytesPerSecond) length = dataLength - offset;
            parts.Add(Build(wav, HeaderSize + offset, length, channels, rate));
            offset += length;
        }
        return parts;
    }

    private static byte[] Build(byte[] src, int start, int length, int channels, int rate)
    {
        var w = new byte[HeaderSize + length];
        void Str(int at, string s) { for (int i = 0; i < 4; i++) w[at + i] = (byte)s[i]; }
        void I32(int at, int v) => BitConverter.GetBytes(v).CopyTo(w, at);
        void I16(int at, short v) => BitConverter.GetBytes(v).CopyTo(w, at);
        Str(0, "RIFF"); I32(4, 36 + length); Str(8, "WAVE");
        Str(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, (short)channels); I32(24, rate);
        I32(28, rate * channels * 2); I16(32, (short)(channels * 2)); I16(34, 16);
        Str(36, "data"); I32(40, length);
        Buffer.BlockCopy(src, start, w, HeaderSize, length);
        return w;
    }
}
