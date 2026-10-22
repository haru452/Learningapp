// Unity を使わない部分 (ミスの種類・言い直しの照合など) のテスト。
// Unity はフォルダ名が "~" で終わるものを読まないので、ここに置いてもビルドには入らない。
// 実行: Roslyn (csc) で、このファイルとテスト対象の .cs をまとめてコンパイルして実行する。
// 最後に "ALL LOGIC TESTS PASSED" と出れば成功 (失敗すると終了コード 1)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public static class LogicTests
{
    private static int _failed;

    private static void Check(bool ok, string what)
    {
        if (ok) return;
        _failed++;
        Console.WriteLine("FAILED: " + what);
    }

    private static Mistake M(string type) => new Mistake { type = type, wrong = "w", @fixed = "f" };

    public static int Main()
    {
        MistakeTypeTests();
        ReadAloudTests();
        SchemaTests();
        AttemptOrderTests();
        TabBadgeTests();
        PronunciationMessageTests();
        TopicCatalogTests();
        MistakesJsonTests();
        RepeatSetTests();
        TatoebaCatalogTests();
        PcmAudioTests();
        TimeStretchTests();
        AudioCacheTests();
        RepeatTtsTests();
        RepeatRpcTests();
        RepeatDiscordTests();

        if (_failed > 0)
        {
            Console.WriteLine(_failed + " test(s) failed");
            return 1;
        }
        Console.WriteLine("ALL LOGIC TESTS PASSED");
        return 0;
    }

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
            Check(text.EndsWith("He said \"hi\"."), "tts body keeps the sentence: " + text);
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

    private static void MistakeTypeTests()
    {
        Check(MistakeTypes.All.Length == 10, "10 mistake types");
        Check(MistakeTypes.Normalize(" Tense ") == "tense", "normalize trims and lowercases");
        Check(MistakeTypes.Normalize("spelling") == "other", "unknown -> other");
        Check(MistakeTypes.Normalize(null) == "other", "null -> other");
        Check(MistakeTypes.Label("verb_form") == "Verb forms", "label");
        Check(MistakeTypes.Label("nonsense") == "Other", "unknown label");
        var c = MistakeTypes.Count(new[] { M("tense"), M("TENSE"), M("xyz") });
        Check(c["tense"] == 2 && c["other"] == 1, "count normalizes types");
        Check(MistakeTypes.Count(null).Count == 0, "count of null is empty");
    }

    private static void ReadAloudTests()
    {
        var r = ReadAloudMatcher.Match("I don't like it.", "i do not like it");
        Check(r.AccuracyPercent == 100 && r.Words.Count == 4 && r.Words[1].Text == "don't" && r.Words.TrueForAll(w => w.Matched),
            "contraction matches its expansion");

        r = ReadAloudMatcher.Match("I went to the park yesterday.", "I went the park yesterday");
        Check(r.AccuracyPercent == 83 && !r.Words[2].Matched && r.Words[5].Text == "yesterday.", "missing word: 5/6");

        r = ReadAloudMatcher.Match("She has two cats.", "she has two cat");
        Check(r.AccuracyPercent == 75 && !r.Words[3].Matched, "wrong word form: 3/4");

        r = ReadAloudMatcher.Match("Hello, world!", "hello hello world");
        Check(r.AccuracyPercent == 100, "extra words are not penalized");

        r = ReadAloudMatcher.Match("I’m fine.", "I am fine");
        Check(r.AccuracyPercent == 100, "curly apostrophe");

        Check(ReadAloudMatcher.Match("Hi there.", "").AccuracyPercent == 0, "nothing heard -> 0");
        Check(ReadAloudMatcher.Match("Hi there.", null).AccuracyPercent == 0, "null heard -> 0");
        var empty = ReadAloudMatcher.Match("", "anything");
        Check(empty.Words.Count == 0 && empty.AccuracyPercent == 0, "empty expected");

        // ハイフン・ダッシュは語の区切りとして扱う (音声認識は "well known" のように分けて返す)
        r = ReadAloudMatcher.Match("It is a well-known place.", "it is a well known place");
        Check(r.AccuracyPercent == 100, $"hyphen splits words ({r.AccuracyPercent})");
        r = ReadAloudMatcher.Match("I think—honestly—it is fine.", "I think honestly it is fine");
        Check(r.AccuracyPercent == 100, $"em dash splits words ({r.AccuracyPercent})");
        r = ReadAloudMatcher.Match("Wait - what?", "wait what");
        Check(r.AccuracyPercent == 100 && r.Words.Count == 2, $"lone dash is not a word ({r.AccuracyPercent}, {r.Words.Count})");
    }

    // 発音チェックのアドバイスを Discord に送る文
    private static void PronunciationMessageTests()
    {
        string m = DiscordClient.PronunciationMessage(new DateTime(2026, 10, 22, 9, 30, 0),
            "Do you prefer paper books?", "I prefer reading paper books.", "Say \"prefer\" with stress on the second syllable.");
        Check(m.StartsWith("\U0001F3A4 **Pronunciation check - 2026-10-22 09:30**"), "pron message header: " + m);
        Check(m.Contains("Topic: Do you prefer paper books?"), "pron message has topic");
        Check(m.Contains("> I prefer reading paper books."), "pron message quotes the sentence");
        Check(m.Contains("Say \"prefer\" with stress"), "pron message has advice");
        string noTopic = DiscordClient.PronunciationMessage(new DateTime(2026, 10, 22), null, "Hi.", "Clear.");
        Check(!noTopic.Contains("Topic:"), "no topic line when topic is empty");
        Check(DiscordClient.PronunciationMessage(DateTime.Now, "t", "s", new string('a', 5000)).Length <= 2000,
            "pron message fits in one Discord message");
    }

    // ほかの画面にいる間に採点が終わったら、タブの名前に印を付ける (開いたら消す)
    private static void TabBadgeTests()
    {
        Check(TabBadge.Add("Topic") == "Topic ●", "badge added: " + TabBadge.Add("Topic"));
        Check(TabBadge.Add(TabBadge.Add("Topic")) == "Topic ●", "badge is not added twice");
        Check(TabBadge.Remove(TabBadge.Add("Topic")) == "Topic", "badge removed");
        Check(TabBadge.Remove("Topic") == "Topic", "remove without badge keeps the label");
        Check(TabBadge.Has("Topic ●") && !TabBadge.Has("Topic"), "has badge");
        Check(TabBadge.Add(null) == "\u25CF", "null label: " + TabBadge.Add(null));
        Check(TabBadge.Message(72) == "Topic: graded — 72 / 100", "toast with score: " + TabBadge.Message(72));
        Check(TabBadge.Message(null) == "Topic: grading failed — open Topic and tap Retry", "toast on error: " + TabBadge.Message(null));
    }

    // API キーが 2 つあるとき: 自分のキーで (メイン → 予備モデル)、上限なら もう片方のキーで (メイン → 予備モデル)
    private static void AttemptOrderTests()
    {
        string Show(System.Collections.Generic.List<(int key, string model)> list) =>
            string.Join(" ", list.Select(a => a.key + ":" + a.model));

        var two = GeminiApiClient.AttemptOrder(new[] { "k1", "k2" }, new[] { "main", "lite" });
        Check(Show(two) == "0:main 0:lite 1:main 1:lite", "two keys: own key first, then the other " + Show(two));

        var three = GeminiApiClient.AttemptOrder(GeminiApiClient.KeyList("own", new[] { "b1", "b2" }), new[] { "main", "lite" });
        Check(Show(three) == "0:main 0:lite 1:main 1:lite 2:main 2:lite", "three keys: own, then the others in order " + Show(three));
        var keys = GeminiApiClient.KeyList("own", new[] { "", "own", null, "b2" });
        Check(string.Join(",", keys) == "own,b2", "key list drops empty and duplicate keys " + string.Join(",", keys));

        var oneKey = GeminiApiClient.AttemptOrder(new[] { "k1", "" }, new[] { "main", "lite" });
        Check(Show(oneKey) == "0:main 0:lite", "empty second key is skipped " + Show(oneKey));

        var sameKey = GeminiApiClient.AttemptOrder(new[] { "k1", "k1" }, new[] { "main", null });
        Check(Show(sameKey) == "0:main", "same key twice and empty fallback model are skipped " + Show(sameKey));

        // 録音の採点は、聞く・書き起こす・採点するので時間がかかる (90 秒では足りないことがある)
        Check(GeminiApiClient.AudioGradeTimeoutSeconds >= 180, "audio grading waits at least 180 s");
        Check(GeminiApiClient.DefaultTimeoutSeconds == 90, "other requests keep 90 s");

        var none = GeminiApiClient.AttemptOrder(new[] { "", null }, new[] { "main" });
        Check(none.Count == 0, "no keys -> no attempts");
    }

    // Gemini に渡す JSON スキーマが正しい JSON で、決めた形になっていること
    private static void SchemaTests()
    {
        var schemas = new (string name, string json)[]
        {
            ("diary", GeminiApiClient.DiarySchema),
            ("summary", GeminiApiClient.SummaryGradeSchema),
            ("topicGrade", GeminiApiClient.TopicGradeSchema),
            ("topicPrompt", GeminiApiClient.TopicPromptSchema),
            ("pronunciation", GeminiApiClient.PronunciationSchema),
        };
        foreach (var (name, json) in schemas)
        {
            try { JsonDocument.Parse(json); }
            catch (Exception e) { Check(false, name + " schema is not valid JSON: " + e.Message); }
        }

        var grade = JsonDocument.Parse(GeminiApiClient.TopicGradeSchema).RootElement;
        var props = grade.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Check(props.Take(5).SequenceEqual(new[] { "goodPoints", "fixPoints", "contentScore", "organizationScore", "languageScore" }),
            "topic grade: reasons come before scores");
        var order = grade.GetProperty("propertyOrdering").EnumerateArray().Select(e => e.GetString()).ToList();
        Check(order.SequenceEqual(props), "topic grade: propertyOrdering matches properties");

        foreach (var (name, json) in new[] { ("diary", GeminiApiClient.DiarySchema), ("summary", GeminiApiClient.SummaryGradeSchema), ("topicGrade", GeminiApiClient.TopicGradeSchema) })
        {
            var root = JsonDocument.Parse(json).RootElement;
            bool has = root.GetProperty("properties").TryGetProperty("mistakes", out var m);
            Check(has, name + ": has mistakes");
            if (!has) continue;
            var types = m.GetProperty("items").GetProperty("properties").GetProperty("type").GetProperty("enum")
                .EnumerateArray().Select(e => e.GetString()).ToArray();
            Check(types.SequenceEqual(MistakeTypes.All), name + ": mistake type enum matches MistakeTypes.All");
        }

        // 録音を直接採点するとき: 最初に transcript (聞いたまま) を書き、そのあとは文字の採点と同じ
        var audio = JsonDocument.Parse(GeminiApiClient.TopicGradeAudioSchema).RootElement;
        var audioProps = audio.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        var audioOrder = audio.GetProperty("propertyOrdering").EnumerateArray().Select(e => e.GetString()).ToList();
        var audioRequired = audio.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Check(audioOrder.FirstOrDefault() == "transcript", "topic grade (audio): transcript comes first");
        Check(audioOrder.Skip(1).SequenceEqual(order), "topic grade (audio): the rest matches the text schema");
        Check(audioOrder.SequenceEqual(audioProps) && audioRequired.Contains("transcript"), "topic grade (audio): ordering/required");
        Check(audio.GetProperty("properties").GetProperty("mistakes").ToString() == grade.GetProperty("properties").GetProperty("mistakes").ToString(),
            "topic grade (audio): same mistakes property");

        var prompt = JsonDocument.Parse(GeminiApiClient.TopicPromptSchema).RootElement.GetProperty("properties");
        Check(prompt.TryGetProperty("kind", out _) && prompt.TryGetProperty("topic", out _) && prompt.TryGetProperty("hint", out _),
            "topic prompt: kind/topic/hint");
        Check(GeminiApiClient.TopicContentMax + GeminiApiClient.TopicOrganizationMax + GeminiApiClient.TopicLanguageMax == 100
              && GeminiApiClient.TopicContentMax == 40, "topic score split 40/30/30");
    }

    private static void TopicCatalogTests()
    {
        Check(TopicCatalog.SecondsFor("daily") == 45, "daily 45s");
        Check(TopicCatalog.SecondsFor("exam") == 60, "exam 60s");
        Check(TopicCatalog.SecondsFor("work") == 60, "work 60s");
        Check(TopicCatalog.SecondsFor("??") == 60, "unknown 60s");

        var rng = new Random(7);
        var recent = new List<string>();
        for (int i = 0; i < 5; i++) recent.Add(TopicCatalog.Offline("work", rng, null).topic);
        bool ok = true;
        for (int i = 0; i < 200; i++)
        {
            var t = TopicCatalog.Offline("work", rng, recent);
            if (t.kind != "work" || string.IsNullOrWhiteSpace(t.topic) || recent.Contains(t.topic) || string.IsNullOrWhiteSpace(t.hint)) ok = false;
        }
        Check(ok, "offline topic: right kind, not recent, has hint");
        foreach (string kind in TopicCatalog.Kinds)
            Check(TopicCatalog.CountFor(kind) >= 20, "at least 20 offline topics for " + kind);
        var kinds = new HashSet<string>();
        for (int i = 0; i < 100; i++) kinds.Add(TopicCatalog.RandomKind(rng));
        Check(kinds.SetEquals(TopicCatalog.Kinds), "random kind covers all kinds");
    }

    // クラウドに送るミスの JSON (jsonb としてそのまま RPC の本文に入れる)
    private static void MistakesJsonTests()
    {
        var one = new[] { new Mistake { type = "tense", wrong = "I go", @fixed = "I went" } };
        Check(MistakeTypes.ToJsonArray(one) == "[{\"type\":\"tense\",\"wrong\":\"I go\",\"fixed\":\"I went\"}]", "one mistake json");
        Check(MistakeTypes.ToJsonArray(null) == "[]", "null -> []");
        Check(MistakeTypes.ToJsonArray(new Mistake[0]) == "[]", "empty -> []");

        var tricky = new[] { new Mistake { type = "Other", wrong = "say \"hi\"\nnow", @fixed = "back\\slash" }, null };
        string json = MistakeTypes.ToJsonArray(tricky);
        var parsed = JsonDocument.Parse(json).RootElement;
        Check(parsed.GetArrayLength() == 1, "null items skipped");
        Check(parsed[0].GetProperty("wrong").GetString() == "say \"hi\"\nnow", "quotes and newline escaped");
        Check(parsed[0].GetProperty("fixed").GetString() == "back\\slash", "backslash escaped");
        Check(parsed[0].GetProperty("type").GetString() == "other", "type normalized");
    }
}
