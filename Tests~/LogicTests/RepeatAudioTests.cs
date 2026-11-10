using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// リピーティングと音声 (PCM・速さ・キャッシュ・TTS・雑音の切り取り) のテスト。Program.cs の Main から呼ぶ。
/// </summary>
public static partial class LogicTests
{
    // ---------------- リピーティング ----------------
    private static RepeatSentence RS(string text, string source = "tatoeba", bool missed = false) =>
        new RepeatSentence { Text = text, Source = source, Missed = missed };

    private static List<RepeatSentence> Many(string prefix, int n, string source) =>
        Enumerable.Range(1, n).Select(i => RS(prefix + " sentence number " + i + ".", source)).ToList();

    private static void RepeatSetTests()
    {
        var missed = Many("Missed", 3, "diary").Select(r => { r.Missed = true; return r; }).ToList();
        var own = Many("Own", 3, "topic");
        var tatoeba = Many("Tatoeba", 10, "tatoeba");
        var set = RepeatSetBuilder.Build(missed, own, tatoeba, new HashSet<string>(), new Random(1));
        Check(set.Count == 5, "repeat set: 5 sentences (" + set.Count + ")");
        Check(set.Take(2).All(r => r.Missed), "repeat set: first 2 are missed");
        Check(set.Count(r => r.Missed) == 2, "repeat set: at most 2 missed");
        Check(set.Count(r => r.Source == "topic") <= 2, "repeat set: at most 2 own");
        Check(set.Count(r => r.Source == "tatoeba") >= 1, "repeat set: rest from tatoeba");

        var noTatoeba = RepeatSetBuilder.Build(missed, own, new List<RepeatSentence>(), new HashSet<string>(), new Random(1));
        Check(noTatoeba.Count == 4, "repeat set: no tatoeba -> missed + own only (" + noTatoeba.Count + ")");

        var dupOwn = new List<RepeatSentence> { RS("I went to the park today.", "diary") };
        var dupTat = new List<RepeatSentence> { RS("i went to the PARK today", "tatoeba"), RS("She has a big black dog.") };
        var dup = RepeatSetBuilder.Build(new List<RepeatSentence>(), dupOwn, dupTat, new HashSet<string>(), new Random(2));
        Check(dup.Count == 2 && dup.Count(r => RepeatSetBuilder.Key(r.Text) == "i went to the park today") == 1,
            "repeat set: same sentence (case/punctuation) only once");

        var recent = new HashSet<string> { RepeatSetBuilder.Key("Tatoeba sentence number 1.") };
        var fresh = RepeatSetBuilder.Build(new List<RepeatSentence>(), new List<RepeatSentence>(), tatoeba, recent, new Random(3));
        Check(fresh.All(r => r.Text != "Tatoeba sentence number 1."), "repeat set: avoids recent tatoeba");
        var fewTat = new List<RepeatSentence> { RS("Only this one is here today.") };
        var forced = RepeatSetBuilder.Build(new List<RepeatSentence>(), new List<RepeatSentence>(), fewTat,
            new HashSet<string> { RepeatSetBuilder.Key("Only this one is here today.") }, new Random(3));
        Check(forced.Count == 1, "repeat set: uses a recent sentence when nothing else is left");

        var a1 = RepeatSetBuilder.Build(missed, own, tatoeba, new HashSet<string>(), new Random(9)).Select(r => r.Text);
        var a2 = RepeatSetBuilder.Build(missed, own, tatoeba, new HashSet<string>(), new Random(9)).Select(r => r.Text);
        Check(a1.SequenceEqual(a2), "repeat set: same seed, same order");
        Check(RepeatSetBuilder.Key("  She HAS two-cats! ") == "she has twocats", "repeat key matches SQL _sentence_key");
    }

    private static void TatoebaCatalogTests()
    {
        string json = "{\"sentences\":[{\"id\":5,\"audioId\":77,\"text\":\"Don't \\\"go\\\" there, \\u00e9.\",\"author\":\"CK\",\"license\":\"CC BY-NC-ND 3.0\"}," +
                      "{\"id\":6,\"audioId\":78,\"text\":\"Second one here.\",\"author\":\"RB\",\"license\":\"CC BY 4.0\"}]}";
        var list = TatoebaCatalog.Parse(json);
        Check(list.Count == 2, "tatoeba: 2 sentences (" + list.Count + ")");
        if (list.Count == 2)
        {
            Check(list[0].Text == "Don't \"go\" there, \u00e9." && list[0].TatoebaId == 5 && list[0].AudioId == 77, "tatoeba: fields and escapes");
            Check(list[0].Author == "CK" && list[0].License == "CC BY-NC-ND 3.0" && list[0].Source == "tatoeba", "tatoeba: author/license/source");
        }
        Check(TatoebaCatalog.Parse("{}").Count == 0, "tatoeba: {} -> empty");
        Check(TatoebaCatalog.Parse("not json").Count == 0, "tatoeba: broken -> empty");
        Check(TatoebaCatalog.Parse(null).Count == 0, "tatoeba: null -> empty");
    }

    private static void PcmAudioTests()
    {
        var f = PcmAudio.Pcm16ToFloats(new byte[] { 0x00, 0x80, 0xFF, 0x7F });
        Check(f.Length == 2 && f[0] == -1f && Math.Abs(f[1] - 32767f / 32768f) < 1e-6, "pcm16 -> floats");
        byte[] wav = PcmAudio.ToWav(new float[2400], 1, 24000);
        Check(wav.Length == 44 + 4800 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF", "wav header and size");
        Check(BitConverter.ToInt32(wav, 24) == 24000 && BitConverter.ToInt16(wav, 22) == 1, "wav sample rate and channels");
    }

    private static int ZeroCrossings(float[] x)
    {
        int n = 0;
        for (int i = 1; i < x.Length; i++) if ((x[i - 1] < 0) != (x[i] < 0)) n++;
        return n;
    }

    private static void TimeStretchTests()
    {
        const int rate = 16000;
        var sine = Enumerable.Range(0, rate).Select(i => (float)Math.Sin(2 * Math.PI * 440 * i / rate)).ToArray();
        var slow = TimeStretch.Stretch(sine, rate, 0.75);
        double seconds = slow.Length / (double)rate;
        Check(Math.Abs(seconds - 1 / 0.75) < 0.02 * (1 / 0.75), "stretch: length x1.33 (" + seconds + " s)");
        // 始めと終わりの 0.1 秒を除いた部分で、ゼロ交差から周波数を出す
        var mid = slow.Skip(rate / 10).Take(slow.Length - rate / 5).ToArray();
        double hz = ZeroCrossings(mid) / 2.0 / (mid.Length / (double)rate);
        Check(Math.Abs(hz - 440) < 440 * 0.03, "stretch: pitch kept (" + hz + " Hz)");
        Check(TimeStretch.Stretch(new float[0], rate, 0.75).Length == 0, "stretch: empty");
        Check(TimeStretch.Stretch(sine, rate, 1.0).Length == sine.Length, "stretch: speed 1 keeps length");
    }

    private static void AudioCacheTests()
    {
        Check(AudioCachePolicy.TatoebaName(1234) == "tatoeba_1234.mp3", "cache: tatoeba name");
        string g1 = AudioCachePolicy.GeminiName("I went home.", "Leda");
        Check(g1 == AudioCachePolicy.GeminiName("I went home.", "Leda"), "cache: same text+voice -> same name");
        Check(g1 != AudioCachePolicy.GeminiName("I went home!", "Leda") && g1 != AudioCachePolicy.GeminiName("I went home.", "Kore"),
            "cache: different text or voice -> different name");
        Check(g1.EndsWith(".wav") && g1.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.'), "cache: safe file name " + g1);

        var t0 = new DateTime(2026, 10, 1);
        var files = Enumerable.Range(0, 301).Select(i => ("f" + i, 1000L, t0.AddMinutes(i))).ToList();
        var del = AudioCachePolicy.ToDelete(files);
        Check(del.Count == 1 && del[0] == "f0", "cache: over 300 files -> delete the oldest");
        var big = new List<(string, long, DateTime)> { ("old", 40L << 20, t0), ("mid", 15L << 20, t0.AddDays(1)), ("new", 10L << 20, t0.AddDays(2)) };
        var delBig = AudioCachePolicy.ToDelete(big);
        Check(delBig.Count == 1 && delBig[0] == "old", "cache: over 60 MB -> delete oldest until under");
        Check(AudioCachePolicy.ToDelete(new List<(string, long, DateTime)>()).Count == 0, "cache: empty");
        Check(AudioCachePolicy.IsOutdated("gemini_0123456789abcdef0123.wav"), "cache: old Gemini audio is outdated");
        Check(!AudioCachePolicy.IsOutdated(AudioCachePolicy.GeminiName("Hi there you.", "Leda")), "cache: current Gemini audio kept");
        Check(!AudioCachePolicy.IsOutdated("tatoeba_123.mp3"), "cache: Tatoeba audio kept");
    }

    private static void RepeatTtsTests()
    {
        string body = GeminiApiClient.TtsRequestBody("He said \"hi\".", "Leda");
        JsonDocument doc = null;
        try { doc = JsonDocument.Parse(body); } catch (Exception e) { Check(false, "tts body is JSON: " + e.Message); }
        if (doc != null)
        {
            var cfg = doc.RootElement.GetProperty("generationConfig");
            Check(cfg.GetProperty("responseModalities")[0].GetString() == "AUDIO", "tts body asks for AUDIO");
            Check(cfg.GetProperty("speechConfig").GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig")
                     .GetProperty("voiceName").GetString() == "Leda", "tts body voice name");
            string text = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
            Check(text == "He said \"hi\".", "tts body sends only the sentence (no instruction to be read aloud): " + text);
        }

        string resp = "{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"audio/L16;codec=pcm;rate=24000\",\"data\":\"" +
                      Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) + "\"}}]}}]}";
        var pcm = GeminiApiClient.ExtractInlineAudio(resp);
        Check(pcm != null && pcm.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "tts: audio extracted from response");
        Check(GeminiApiClient.ExtractInlineAudio("{\"candidates\":[]}") == null, "tts: no audio -> null");

        var day = new DateTime(2026, 10, 22, 23, 0, 0);
        Check(TtsQuotaGate.IsBlocked(day, new DateTime(2026, 10, 23, 2, 59, 0)), "quota: blocked until 3:00 next morning");
        Check(!TtsQuotaGate.IsBlocked(day, new DateTime(2026, 10, 23, 3, 0, 0)), "quota: free again at 3:00");
        Check(!TtsQuotaGate.IsBlocked(DateTime.MinValue, day), "quota: never blocked");
    }

    private static void TrimNoiseTests()
    {
        const int rate = 24000;
        // 1 秒の声 (サイン波) のあとに 0.5 秒の「ザー」(小さなノイズ)
        var rnd = new Random(1);
        var speech = Enumerable.Range(0, rate).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 220 * i / rate)));
        var hiss = Enumerable.Range(0, rate / 2).Select(_ => (float)((rnd.NextDouble() * 2 - 1) * 0.03));
        var input = speech.Concat(hiss).ToArray();
        var trimmed = PcmAudio.TrimTrailingNoise(input, rate);
        double sec = trimmed.Length / (double)rate;
        Check(sec >= 1.0 && sec <= 1.2, "trim: trailing hiss removed (" + sec + " s)");
        Check(Math.Abs(trimmed[trimmed.Length - 1]) < 0.01f, "trim: ends with a fade out");
        Check(Math.Abs(trimmed[rate / 2] - input[rate / 2]) < 1e-6, "trim: speech untouched");

        // 声と同じくらい大きい「ザー」(0.6 秒) も切る (雑音は波の向きがとても細かく変わる)
        var loudHiss = Enumerable.Range(0, rate * 6 / 10).Select(_ => (float)((rnd.NextDouble() * 2 - 1) * 0.5));
        var loud = PcmAudio.TrimTrailingNoise(speech.Concat(loudHiss).ToArray(), rate);
        double loudSec = loud.Length / (double)rate;
        Check(loudSec >= 1.0 && loudSec <= 1.2, "trim: loud trailing hiss removed (" + loudSec + " s)");

        // 文の最後の「s」(短いノイズ 80 ms) は残す
        var sBurst = Enumerable.Range(0, rate * 8 / 100).Select(_ => (float)((rnd.NextDouble() * 2 - 1) * 0.3));
        var withS = speech.Concat(sBurst).Concat(new float[rate / 2]).ToArray();
        var keptS = PcmAudio.TrimTrailingNoise(withS, rate);
        Check(keptS.Length >= rate + rate * 7 / 100, "trim: final 's' sound kept (" + keptS.Length / (double)rate + " s)");

        // 実際にあった形: 声 → 0.4 秒の無音 → 0.15 秒の大きな「ザッ」(声より大きい) → 無音
        var burst = Enumerable.Range(0, rate * 15 / 100).Select(_ => (float)((rnd.NextDouble() * 2 - 1) * 0.6));
        var withBurst = speech.Concat(new float[rate * 4 / 10]).Concat(burst).Concat(new float[rate / 10]).ToArray();
        double burstSec = PcmAudio.TrimTrailingNoise(withBurst, rate).Length / (double)rate;
        Check(burstSec <= 1.2, "trim: isolated noise burst after a pause removed (" + burstSec + " s)");

        // 間をあけて続く 2 つ目の言葉 (0.5 秒の声) は消さない
        var word2 = Enumerable.Range(0, rate / 2).Select(i => (float)(0.4 * Math.Sin(2 * Math.PI * 180 * i / rate)));
        var twoWords = speech.Concat(new float[rate * 3 / 10]).Concat(word2).ToArray();
        double twoSec = PcmAudio.TrimTrailingNoise(twoWords, rate).Length / (double)rate;
        Check(twoSec >= 1.75, "trim: second word after a pause kept (" + twoSec + " s)");

        // 間 (0.25 秒) のあとの、小さく短い最後の言葉 (0.3 秒、声の 1/6 の大きさ) は切らない
        var softWord = Enumerable.Range(0, rate * 3 / 10).Select(i => (float)(0.08 * Math.Sin(2 * Math.PI * 150 * i / rate)));
        var softEnd = speech.Concat(new float[rate / 4]).Concat(softWord).Concat(new float[rate / 5]).ToArray();
        double softSec = PcmAudio.TrimTrailingNoise(softEnd, rate).Length / (double)rate;
        Check(softSec >= 1.55, "trim: soft short last word after a pause kept (" + softSec + " s)");

        // 声がだんだん小さくなって終わる (最後の 0.4 秒は声の 1/10) のも切らない
        var fading = Enumerable.Range(0, rate * 4 / 10).Select(i => (float)(0.05 * Math.Sin(2 * Math.PI * 200 * i / rate)));
        var fadeEnd = speech.Concat(fading).ToArray();
        double fadeSec = PcmAudio.TrimTrailingNoise(fadeEnd, rate).Length / (double)rate;
        Check(fadeSec >= 1.39, "trim: quiet end of the voice kept (" + fadeSec + " s)");

        var clean = speech.ToArray();
        var cleanOut = PcmAudio.TrimTrailingNoise(clean, rate);
        Check(cleanOut.Length == clean.Length && cleanOut[clean.Length - 1] == clean[clean.Length - 1], "trim: clean audio unchanged");
        var quiet = new float[rate];
        Check(PcmAudio.TrimTrailingNoise(quiet, rate).Length == rate, "trim: all quiet -> unchanged");
        Check(PcmAudio.TrimTrailingNoise(new float[0], rate).Length == 0, "trim: empty");
    }

    // ---------------- シャドーイング (架空のフィード・架空の文だけを使う) ----------------
    // ---------------- シャドーイング (YouTube。架空の文・作った音だけを使う) ----------------
    private static byte[] FakeWav(int seconds, int rate = 16000)
    {
        var samples = new float[seconds * rate];
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)Math.Sin(i * 0.05) * 0.3f;
        return PcmAudio.ToWav(samples, 1, rate);
    }

    private static void RepeatRpcTests()
    {
        var results = new List<RepeatResult>
        {
            new RepeatResult { sentence = "Say \"hi\".", heard = "say hi", accuracy = 100, replays = 2, source = "tatoeba", voice = "human", tatoebaId = 5, audioId = 9 },
            new RepeatResult { sentence = "Own one here.", heard = "", accuracy = 60, replays = 0, source = "diary", voice = "gemini" },
        };
        string json = RepeatRpc.ResultsJson(results);
        var arr = JsonDocument.Parse(json).RootElement;
        Check(arr.GetArrayLength() == 2, "rpc: 2 results");
        Check(arr[0].GetProperty("sentence").GetString() == "Say \"hi\"." && arr[0].GetProperty("tatoeba_id").GetInt64() == 5
              && arr[0].GetProperty("audio_id").GetInt64() == 9 && arr[0].GetProperty("replays").GetInt32() == 2, "rpc: snake_case keys");
        Check(arr[1].GetProperty("audio_id").ValueKind == JsonValueKind.Null, "rpc: no audio id -> null");

        string body = "[{\"sentence\":\"I went home today.\",\"source\":\"tatoeba\",\"missed\":true,\"tatoeba_id\":5,\"audio_id\":9}," +
                      "{\"sentence\":\"She cooked dinner for us.\",\"source\":\"diary\",\"missed\":false,\"tatoeba_id\":null,\"audio_id\":null}]";
        var rows = RepeatRpc.ParseSentences(body);
        Check(rows.Count == 2 && rows[0].Missed && rows[0].AudioId == 9 && rows[0].HasHumanAudio, "rpc: missed tatoeba row");
        Check(rows.Count == 2 && !rows[1].Missed && rows[1].Source == "diary" && rows[1].AudioId == null && !rows[1].HasHumanAudio, "rpc: own row");
        Check(RepeatRpc.ParseSentences("oops").Count == 0, "rpc: broken -> empty");
    }

    private static void RepeatDiscordTests()
    {
        var results = new List<RepeatResult>
        {
            new RepeatResult { sentence = "I went to the station yesterday.", heard = "i went to the station yesterday", accuracy = 100 },
            new RepeatResult { sentence = "Could you send me the file by Friday?", heard = "could you send me file friday", accuracy = 60 },
        };
        string m = DiscordClient.RepeatMessage(new DateTime(2026, 10, 22, 17, 40, 0), results);
        Check(m.StartsWith("\U0001F501 **Repeat practice - 2026-10-22 17:40**"), "repeat discord header: " + m);
        Check(m.Contains("Average: 80%"), "repeat discord average");
        Check(m.Contains("\u2705 100%  I went to the station yesterday."), "repeat discord ok line");
        Check(m.Contains("\u26A0\uFE0F  60%  Could you send me the file by Friday?") && m.Contains("(heard: could you send me file friday)"),
            "repeat discord missed line with heard");
        Check(!m.Split('\n').First(l => l.Contains("station")).Contains("heard"), "repeat discord: no heard on ok lines");
    }
}
