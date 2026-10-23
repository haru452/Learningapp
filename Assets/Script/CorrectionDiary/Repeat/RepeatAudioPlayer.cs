using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// リピーティングの文を流す。
///  - Tatoeba の文 (AudioId あり): 本物の人の録音を Tatoeba からダウンロードして流す
///  - 自分の添削文: Gemini の音声合成 (声 Leda) で作って流す
///  - どちらも失敗したとき・Gemini が上限の日: 端末の読み上げ (AndroidTts)
/// 録音・作った音声は端末に保存して使い回す (最大 300 個・60 MB)。ゆっくりは TimeStretch で 0.75 倍に引き伸ばす。
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class RepeatAudioPlayer : MonoBehaviour
{
    public const string VoiceHuman = "human";
    public const string VoiceGemini = "gemini";
    public const string VoiceDevice = "device";
    public const double SlowSpeed = 0.75;

    private const string TatoebaAudioUrl = "https://tatoeba.org/audio/download/";
    private const string QuotaPrefsKey = "repeat_tts_blocked_at";

    private AudioSource _source;
    private GeminiApiClient _gemini;
    private string _ttsModel = "gemini-3.8-flash-tts";
    private string _ttsVoice = "Leda";
    private ITextToSpeech _deviceTts;

    private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, AudioClip> _slowClips = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, string> _voices = new Dictionary<string, string>();
    private readonly Dictionary<string, Task<string>> _preparing = new Dictionary<string, Task<string>>();

    private static string CacheDir => Path.Combine(Application.persistentDataPath, "repeat_audio");

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        _deviceTts = new AndroidTts("en", "US");
#else
        _deviceTts = new EditorDummyTts();
#endif
        try { _deviceTts.Init(); }
        catch (Exception e) { Debug.LogWarning("[RepeatAudio] device TTS init failed: " + e.Message); }
        DeleteOutdatedFiles();
    }

    // 前の作り方で保存した Gemini の音声 (指示を読み上げていた・最後に雑音があった) を消す
    private static void DeleteOutdatedFiles()
    {
        try
        {
            if (!Directory.Exists(CacheDir)) return;
            foreach (var f in new DirectoryInfo(CacheDir).GetFiles())
                if (AudioCachePolicy.IsOutdated(f.Name)) TryDelete(f.FullName);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[RepeatAudio] could not remove old audio: " + e.Message);
        }
    }

    private void OnDestroy()
    {
        Stop();
        _deviceTts?.Dispose();
        foreach (var c in _clips.Values.Concat(_slowClips.Values)) if (c != null) Destroy(c);
    }

    public void Configure(GeminiApiClient gemini, string ttsModel, string ttsVoice)
    {
        _gemini = gemini;
        if (!string.IsNullOrWhiteSpace(ttsModel)) _ttsModel = ttsModel.Trim();
        if (!string.IsNullOrWhiteSpace(ttsVoice)) _ttsVoice = ttsVoice.Trim();
    }

    /// <summary>今日 Gemini の読み上げが上限になっているか (夜 3 時区切り)</summary>
    public static bool GeminiBlockedToday
    {
        get
        {
            string s = PlayerPrefs.GetString(QuotaPrefsKey, "");
            return long.TryParse(s, out long ticks) && TtsQuotaGate.IsBlocked(new DateTime(ticks), DateTime.Now);
        }
    }

    private static void BlockGeminiToday()
    {
        PlayerPrefs.SetString(QuotaPrefsKey, DateTime.Now.Ticks.ToString());
        PlayerPrefs.Save();
    }

    /// <summary>音声を用意する (先読みにも使う)。使う声 (human / gemini / device) を返す</summary>
    public Task<string> PrepareAsync(RepeatSentence s)
    {
        string key = RepeatSetBuilder.Key(s.Text);
        if (_voices.TryGetValue(key, out string done)) return Task.FromResult(done);
        if (_preparing.TryGetValue(key, out var running)) return running;
        var task = PrepareCoreAsync(s, key);
        _preparing[key] = task;
        return task;
    }

    private async Task<string> PrepareCoreAsync(RepeatSentence s, string key)
    {
        AudioClip clip = null;
        string voice = VoiceDevice;
        try
        {
            if (s.HasHumanAudio)
            {
                clip = await LoadTatoebaAsync(s.AudioId.Value);
                if (clip != null) voice = VoiceHuman;
            }
            if (clip == null && _gemini != null && !GeminiBlockedToday)
            {
                clip = await LoadGeminiAsync(s.Text);
                if (clip != null) voice = VoiceGemini;
            }
        }
        catch (GeminiQuotaException e)
        {
            Debug.LogWarning("[RepeatAudio] Gemini TTS limit reached. Using the device voice today: " + e.Message);
            BlockGeminiToday();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[RepeatAudio] could not prepare audio. Using the device voice: " + e.Message);
        }

        if (this == null) return voice;
        if (clip != null) _clips[key] = clip;
        _voices[key] = voice;
        _preparing.Remove(key);
        return voice;
    }

    /// <summary>文を流して、終わるまで待つ。slow なら 0.75 倍 (声の高さは変えない)</summary>
    public async Task PlayAsync(RepeatSentence s, bool slow, CancellationToken ct)
    {
        string key = RepeatSetBuilder.Key(s.Text);
        await PrepareAsync(s);
        ct.ThrowIfCancellationRequested();

        if (_clips.TryGetValue(key, out AudioClip clip) && clip != null)
        {
            await PlayClipAsync(slow ? GetSlowClip(key, clip) : clip, ct);
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        _deviceTts.SetVoiceStyle(1f, slow ? (float)SlowSpeed : 1f);
        await _deviceTts.SpeakAsync(s.Text, ct);
#else
        // 端末の読み上げが無い (エディタ・iOS): 音を出せないので、言い返す練習にならない
        throw new RepeatAudioUnavailableException("no voice could be played");
#endif
    }

    /// <summary>用意した AudioClip を流して、終わるまで待つ (シャドーイング・自分の声)</summary>
    public async Task PlayClipAsync(AudioClip clip, CancellationToken ct)
    {
        if (clip == null) throw new RepeatAudioUnavailableException("no audio");
        ct.ThrowIfCancellationRequested();
        _source.clip = clip;
        _source.Play();
        // 再生が始まるのを少し待ってから、終わるまで待つ
        float start = Time.realtimeSinceStartup;
        while (!_source.isPlaying && Time.realtimeSinceStartup - start < 0.5f) { ct.ThrowIfCancellationRequested(); await Task.Yield(); }
        while (_source != null && _source.isPlaying)
        {
            if (ct.IsCancellationRequested) { _source.Stop(); ct.ThrowIfCancellationRequested(); }
            await Task.Yield();
        }
    }

    public void Stop()
    {
        if (_source != null) _source.Stop();
        try { _deviceTts?.Stop(); } catch (Exception) { }
    }

    /// <summary>直前に用意した文の声 (まだなら device)</summary>
    public string VoiceOf(RepeatSentence s) =>
        _voices.TryGetValue(RepeatSetBuilder.Key(s.Text), out string v) ? v : VoiceDevice;

    // ------------------------------------------------------------
    private AudioClip GetSlowClip(string key, AudioClip clip)
    {
        if (_slowClips.TryGetValue(key, out AudioClip slow) && slow != null) return slow;
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) return clip;
        float[] mono = data;
        if (clip.channels > 1)
        {
            mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0;
                for (int c = 0; c < clip.channels; c++) sum += data[i * clip.channels + c];
                mono[i] = sum / clip.channels;
            }
        }
        float[] stretched = TimeStretch.Stretch(mono, clip.frequency, SlowSpeed);
        slow = AudioClip.Create(clip.name + "_slow", stretched.Length, 1, clip.frequency, false);
        slow.SetData(stretched, 0);
        _slowClips[key] = slow;
        return slow;
    }

    private async Task<AudioClip> LoadTatoebaAsync(long audioId)
    {
        Directory.CreateDirectory(CacheDir);
        string path = Path.Combine(CacheDir, AudioCachePolicy.TatoebaName(audioId));
        if (!File.Exists(path))
        {
            using (var req = UnityWebRequest.Get(TatoebaAudioUrl + audioId))
            {
                req.timeout = 20;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success || req.downloadHandler.data == null || req.downloadHandler.data.Length < 1000)
                    throw new Exception("Tatoeba audio download failed: " + req.responseCode + " " + req.error);
                File.WriteAllBytes(path, req.downloadHandler.data);
            }
            EvictOldFiles();
        }
        Touch(path);
        try
        {
            return TrimClip(await LoadClipAsync(path, AudioType.MPEG));
        }
        catch (Exception)
        {
            TryDelete(path);   // 壊れたファイルは消して、次はダウンロードし直す
            throw;
        }
    }

    private async Task<AudioClip> LoadGeminiAsync(string text)
    {
        Directory.CreateDirectory(CacheDir);
        string path = Path.Combine(CacheDir, AudioCachePolicy.GeminiName(text, _ttsVoice));
        if (File.Exists(path))
        {
            Touch(path);
            try { return await LoadClipAsync(path, AudioType.WAV); }
            catch (Exception) { TryDelete(path); }
        }

        byte[] pcm = await _gemini.SynthesizeSpeechAsync(text, _ttsVoice, _ttsModel);
        // 最後に付くことがある「ザー」という雑音を切ってから保存する
        float[] samples = PcmAudio.TrimTrailingNoise(PcmAudio.Pcm16ToFloats(pcm), GeminiApiClient.TtsSampleRate);
        File.WriteAllBytes(path, PcmAudio.ToWav(samples, 1, GeminiApiClient.TtsSampleRate));
        EvictOldFiles();
        var clip = AudioClip.Create("gemini_tts", samples.Length, 1, GeminiApiClient.TtsSampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    // 流すときだけ、最後の雑音を切る (保存してある録音ファイルはそのまま)
    private static AudioClip TrimClip(AudioClip clip)
    {
        if (clip == null || clip.channels != 1) return clip;
        var data = new float[clip.samples];
        if (!clip.GetData(data, 0)) return clip;
        float[] trimmed = PcmAudio.TrimTrailingNoise(data, clip.frequency);
        if (trimmed.Length >= data.Length) return clip;
        var result = AudioClip.Create(clip.name, trimmed.Length, 1, clip.frequency, false);
        result.SetData(trimmed, 0);
        Destroy(clip);
        return result;
    }

    internal static async Task<AudioClip> LoadClipAsync(string path, AudioType type)
    {
        using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
        {
            ((DownloadHandlerAudioClip)req.downloadHandler).compressed = false;   // GetData (ゆっくり再生) に使うため
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityWebRequest.Result.Success) throw new Exception("could not load audio: " + req.error);
            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null || clip.samples == 0) throw new Exception("empty audio");
            return clip;
        }
    }

    private static void Touch(string path)
    {
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (Exception) { }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { }
    }

    private static void EvictOldFiles()
    {
        try
        {
            var files = new DirectoryInfo(CacheDir).GetFiles()
                .Select(f => (f.Name, f.Length, f.LastWriteTimeUtc)).ToList();
            foreach (string name in AudioCachePolicy.ToDelete(files))
                TryDelete(Path.Combine(CacheDir, name));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[RepeatAudio] could not clean the audio cache: " + e.Message);
        }
    }
}

/// <summary>Tatoeba の録音も Gemini の声も端末の読み上げも使えず、文を流せなかった</summary>
public class RepeatAudioUnavailableException : Exception
{
    public RepeatAudioUnavailableException(string message) : base(message) { }
}
