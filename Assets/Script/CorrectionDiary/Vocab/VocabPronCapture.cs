using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 発音チェックの取り込み: 録音を始めてから端末の音声認識で聞き取る (録音は Gemini のまとめ採点に使う)。
///  - 認識が始まらない・すぐ (1.5 秒以内) エラーになる端末は、録音を止めて認識だけでやり直し、次からも認識だけにする
///    (PlayerPrefs "vocab_stt_only" = 1)
///  - 端末の音声認識が無い (Editor など) ときは録音だけ。聞き取りは Gemini の文字起こし
/// </summary>
public class VocabPronCapture
{
    public const string SttOnlyPrefsKey = "vocab_stt_only";
    private const float EarlyErrorSeconds = 1.5f;
    private const float StopTimeoutSeconds = 2f;

    private enum Mode { None, Both, SttOnly, RecordOnly }

    private readonly AudioRecorder _recorder;
    private readonly NativeSpeechRecognizer _stt;
    private readonly GeminiApiClient _gemini;

    private Mode _mode = Mode.None;
    private float _startedAt;
    private bool _ended;
    private string _heard = "";

    /// <summary>認識が自分で終わった (話し終わって無音になった)。画面はここで StopAsync を呼ぶ</summary>
    public event Action AutoEnded;

    public bool IsActive => _mode != Mode.None;

    /// <summary>この端末では録音と認識を同時にできない (Gemini の採点はしない)</summary>
    public static bool SttOnlyDevice => PlayerPrefs.GetInt(SttOnlyPrefsKey, 0) == 1;

    public VocabPronCapture(AudioRecorder recorder, NativeSpeechRecognizer stt, GeminiApiClient gemini)
    {
        _recorder = recorder;
        _stt = stt;
        _gemini = gemini;
        _stt.OnSessionEnded += OnSessionEnded;
    }

    public void Dispose()
    {
        if (_stt != null) _stt.OnSessionEnded -= OnSessionEnded;
        Cancel();
    }

    /// <summary>聞き取りを始める。マイクが使えないなどで始められなければ false</summary>
    public async Task<bool> StartAsync()
    {
        if (IsActive) return false;
        if (!await _recorder.RequestPermissionAsync()) return false;
        if (_stt.IsAvailable && !await _stt.RequestSpeechPermissionAsync()) return false;

        _ended = false;
        _heard = "";
        _startedAt = Time.realtimeSinceStartup;

        if (!_stt.IsAvailable)
        {
            if (!_recorder.StartRecording()) return false;
            _mode = Mode.RecordOnly;
            return true;
        }
        if (!SttOnlyDevice && _recorder.StartRecording())
        {
            _mode = Mode.Both;
            if (_stt.StartListening("en-US")) return true;
            Debug.LogWarning("[VocabPron] recognition did not start while recording. Using recognition only from now on.");
            FallBackToSttOnly();
            return IsActive;
        }
        _mode = Mode.SttOnly;
        if (_stt.StartListening("en-US")) return true;
        _mode = Mode.None;
        return false;
    }

    /// <summary>止めて、聞き取れた文字と録音 (無ければ null) を返す</summary>
    public async Task<(string heard, byte[] wav)> StopAsync()
    {
        if (!IsActive) return ("", null);
        if (_stt.IsListening)
        {
            _stt.StopListening();
            float deadline = Time.realtimeSinceStartup + StopTimeoutSeconds;
            while (_stt.IsListening && Time.realtimeSinceStartup < deadline) await Task.Yield();
            if (_stt.IsListening) _stt.Cancel();
        }
        byte[] wav = _recorder.IsRecording ? _recorder.StopRecording() : null;
        Mode mode = _mode;
        _mode = Mode.None;

        string heard = _heard;
        if (mode == Mode.RecordOnly && wav != null && _gemini != null)
        {
            try { heard = await _gemini.TranscribeAsync(wav); }
            catch (Exception e) { Debug.LogWarning("[VocabPron] transcription failed: " + e.Message); heard = ""; }
        }
        return (heard ?? "", mode == Mode.SttOnly ? null : wav);
    }

    public void Cancel()
    {
        if (_stt != null && _stt.IsListening) _stt.Cancel();
        if (_recorder != null && _recorder.IsRecording) _recorder.StopRecording();
        _mode = Mode.None;
    }

    private void OnSessionEnded(string text, string error)
    {
        if (!IsActive) return;
        if (_mode == Mode.Both && error != null && Time.realtimeSinceStartup - _startedAt < EarlyErrorSeconds)
        {
            Debug.LogWarning("[VocabPron] recognition failed right away while recording (" + error + "). Using recognition only from now on.");
            FallBackToSttOnly();
            return;
        }
        _heard = text ?? "";
        if (_ended) return;
        _ended = true;
        AutoEnded?.Invoke();
    }

    private void FallBackToSttOnly()
    {
        if (_recorder.IsRecording) _recorder.StopRecording();
        PlayerPrefs.SetInt(SttOnlyPrefsKey, 1);
        PlayerPrefs.Save();
        _mode = Mode.SttOnly;
        _startedAt = Time.realtimeSinceStartup;
        if (!_stt.StartListening("en-US")) _mode = Mode.None;
    }
}
