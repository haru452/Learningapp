using System;

/// <summary>
/// 声の高さを変えずに、音声をゆっくりにする (WSOLA)。
/// 短い区間を少しずつずらしながら重ね合わせ、波形がつながる位置を探して並べる。
/// </summary>
public static class TimeStretch
{
    /// <param name="speed">再生の速さ (0.5〜1.0。0.75 なら長さは 1/0.75 倍)</param>
    public static float[] Stretch(float[] samples, int sampleRate, double speed)
    {
        if (samples == null || samples.Length == 0) return new float[0];
        speed = Math.Max(0.5, Math.Min(1.0, speed));
        if (speed >= 0.999) return (float[])samples.Clone();

        int frame = Math.Max(64, sampleRate * 40 / 1000);       // 40 ms
        if (frame % 2 == 1) frame++;
        int hop = frame / 2;                                     // 出力側は半分ずつ重ねる
        int tolerance = Math.Max(1, sampleRate * 10 / 1000);     // ±10 ms で、つながる位置を探す
        int outLength = (int)Math.Round(samples.Length / speed);

        var window = new float[frame];
        for (int i = 0; i < frame; i++) window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / frame));

        var output = new float[outLength + frame];
        var weight = new float[outLength + frame];
        int prevStart = 0;

        for (int k = 0; ; k++)
        {
            int outPos = k * hop;
            if (outPos >= outLength) break;
            int nominal = (int)Math.Round(outPos * speed);
            int start = nominal;
            if (k > 0)
            {
                int natural = prevStart + hop;                   // 前の区間のそのままの続き
                double best = double.NegativeInfinity;
                for (int d = -tolerance; d <= tolerance; d++)
                {
                    int cand = nominal + d;
                    if (cand < 0 || cand + hop > samples.Length || natural + hop > samples.Length) continue;
                    double c = 0;
                    for (int i = 0; i < hop; i++) c += samples[natural + i] * samples[cand + i];
                    if (c > best) { best = c; start = cand; }
                }
            }
            for (int i = 0; i < frame; i++)
            {
                int src = start + i;
                float v = src >= 0 && src < samples.Length ? samples[src] : 0f;
                output[outPos + i] += v * window[i];
                weight[outPos + i] += window[i];
            }
            prevStart = start;
        }

        var result = new float[outLength];
        for (int i = 0; i < outLength; i++)
            result[i] = weight[i] > 1e-3f ? output[i] / weight[i] : output[i];
        return result;
    }
}
