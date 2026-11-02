using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 単語の音声を流す。辞書の音声 (mp3) は vocab/audio/ に保存して使い回す (最大 300 個、古いものから消す)。
/// 音声が無い・取れないときは端末の読み上げ (Editor では音は出ない)
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VocabAudio : MonoBehaviour
{
    private AudioSource _source;
    private ITextToSpeech _deviceTts;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
#if UNITY_ANDROID && !UNITY_EDITOR
        _deviceTts = new AndroidTts("en", "US");
#else
        _deviceTts = new EditorDummyTts();
#endif
        try { _deviceTts.Init(); }
        catch (Exception e) { Debug.LogWarning("[VocabAudio] device TTS init failed: " + e.Message); }
    }

    private void OnDestroy()
    {
        try { _deviceTts?.Dispose(); } catch (Exception) { }
    }

    /// <summary>単語を流して、終わるまで待つ。audioUrl が空・取れないときは端末の読み上げ</summary>
    public async Task PlayWordAsync(string word, string audioUrl, CancellationToken ct)
    {
        Stop();
        AudioClip clip = null;
        if (!string.IsNullOrWhiteSpace(audioUrl))
        {
            try { clip = await LoadAsync(audioUrl); }
            catch (Exception e) { Debug.LogWarning("[VocabAudio] dictionary audio failed, using device voice: " + e.Message); }
        }
        ct.ThrowIfCancellationRequested();
        if (clip != null) { await PlayClipAsync(clip, ct); return; }
        _deviceTts.SetVoiceStyle(1f, 0.9f);
        await _deviceTts.SpeakAsync(word ?? "", ct);
    }

    /// <summary>録音などの AudioClip を流して、終わるまで待つ</summary>
    public async Task PlayClipAsync(AudioClip clip, CancellationToken ct)
    {
        if (clip == null) return;
        Stop();
        _source.clip = clip;
        _source.Play();
        float start = Time.realtimeSinceStartup;
        while (_source != null && !_source.isPlaying && Time.realtimeSinceStartup - start < 0.5f) await Task.Yield();
        while (_source != null && _source.isPlaying)
        {
            if (ct.IsCancellationRequested) { _source.Stop(); return; }
            await Task.Yield();
        }
    }

    /// <summary>WAV のバイト列 (録音) を流す</summary>
    public Task PlayWavAsync(byte[] wav, CancellationToken ct)
    {
        var clip = WavToClip(wav);
        return clip == null ? Task.CompletedTask : PlayClipAsync(clip, ct);
    }

    public void Stop()
    {
        if (_source != null && _source.isPlaying) _source.Stop();
        try { _deviceTts?.Stop(); } catch (Exception) { }
    }

    private static async Task<AudioClip> LoadAsync(string url)
    {
        Directory.CreateDirectory(VocabStore.AudioDir);
        string name = Path.GetFileName(new Uri(url).AbsolutePath);
        string path = Path.Combine(VocabStore.AudioDir, name);
        if (!File.Exists(path))
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success || req.downloadHandler.data == null || req.downloadHandler.data.Length < 200)
                    throw new Exception("download failed: " + req.responseCode + " " + req.error);
                File.WriteAllBytes(path, req.downloadHandler.data);
            }
            Evict();
        }
        try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (Exception) { }
        try { return await RepeatAudioPlayer.LoadClipAsync(path, AudioType.MPEG); }
        catch (Exception)
        {
            try { File.Delete(path); } catch (Exception) { }   // 壊れたファイルは消して、次は取り直す
            throw;
        }
    }

    private static void Evict()
    {
        try
        {
            var files = new DirectoryInfo(VocabStore.AudioDir).GetFiles().Select(f => (f.Name, f.Length, f.LastWriteTimeUtc)).ToList();
            foreach (string n in AudioCachePolicy.ToDelete(files))
                File.Delete(Path.Combine(VocabStore.AudioDir, n));
        }
        catch (Exception e) { Debug.LogWarning("[VocabAudio] could not clean the audio cache: " + e.Message); }
    }

    // 16bit PCM の WAV (AudioRecorder が作るもの) → AudioClip
    public static AudioClip WavToClip(byte[] wav)
    {
        if (wav == null || wav.Length <= 44) return null;
        int channels = BitConverter.ToInt16(wav, 22);
        int rate = BitConverter.ToInt32(wav, 24);
        if (channels <= 0 || rate <= 0) return null;
        float[] samples = PcmAudio.Pcm16ToFloats(wav.Skip(44).ToArray());
        if (samples.Length < channels) return null;
        var clip = AudioClip.Create("my_voice", samples.Length / channels, channels, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
