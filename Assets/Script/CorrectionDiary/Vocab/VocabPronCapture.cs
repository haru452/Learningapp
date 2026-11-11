using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 発音チェックの取り込み: 録音を始めてから端末の音声認識で聞き取る (録音は Gemini のまとめ採点に使う)。
///  - 認識が始まらない・すぐ (1.5 秒以内) エラーになる端末は、録音を止めて認識だけでやり直し、次からも認識だけにする
///    (PlayerPrefs "vocab_stt_only" = 1)
///  - 録音には声が入っているのに認識が空 = 録音にマイクを取られて認識に何も聞こえない端末。次からは認識だけにする。
///    この回は録音を Gemini で文字起こしする (待つのは最大 TranscribeTimeoutMs まで)
///  - 端末の音声認識が無い (Editor など) ときは録音だけ。聞き取りは Gemini の文字起こし。数秒で自動で止める
/// </summary>
public class VocabPronCapture
{
    public const string SttOnlyPrefsKey = "vocab_stt_only";
    private const float EarlyErrorSeconds = 1.5f;
    private const float StopTimeoutSeconds = 2f;
    private const int RecordOnlyMilliseconds = 3500;
    private const int TranscribeTimeoutMs = 10000;   // Gemini が混んでいても、この時間で諦めて「もう一度」にする
    private const float VoicePeak = 0.08f;           // 録音の一番大きい音がこれ以上なら、声が入っている   // 録音だけのときは、この時間で自動で止める (単語 1 つなので足りる)

    private enum Mode { None, Both, SttOnly, RecordOnly }

    private readonly AudioRecorder _recorder;
    private readonly NativeSpeechRecognizer _stt;
    private readonly GeminiApiClient _gemini;

    private Mode _mode = Mode.None;
    private float _startedAt;
    private bool _ended;
    private string _heard = "";
    private int _session;   // 自動停止の予約が古い聞き取りに効かないようにする

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
        if (IsActive || _clipping) return false;
        if (!await _recorder.RequestPermissionAsync()) return false;
        if (_stt.IsAvailable && !await _stt.RequestSpeechPermissionAsync()) return false;

        _ended = false;
        _heard = "";
        _startedAt = Time.realtimeSinceStartup;

        if (!_stt.IsAvailable)
        {
            if (!_recorder.StartRecording()) return false;
            _mode = Mode.RecordOnly;
            _ = AutoEndRecordOnlyAsync(++_session);
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
        _session++;

        string heard = _heard;
        bool sttHeardNothing = mode == Mode.Both && string.IsNullOrWhiteSpace(heard) && HasVoice(wav);
        if (sttHeardNothing)
        {
            Debug.LogWarning("[VocabPron] recognition heard nothing, but the recording has a voice. Using recognition only from now on.");
            PlayerPrefs.SetInt(SttOnlyPrefsKey, 1);
            PlayerPrefs.Save();
        }
        if ((mode == Mode.RecordOnly || sttHeardNothing) && wav != null && _gemini != null)
            heard = await TranscribeWithTimeoutAsync(wav);
        return (heard ?? "", mode == Mode.SttOnly ? null : wav);
    }

    private bool _clipping;   // RecordClipAsync で録音中

    /// <summary>
    /// 認識を使わずに録音だけを ms ミリ秒する (「Didn't sound right?」で、録音が無い単語のとき)。
    /// 録音と認識を同時にできない端末でも使える。始められない・途中で Cancel されたら null
    /// </summary>
    public async Task<byte[]> RecordClipAsync(int ms)
    {
        if (IsActive || _clipping || _recorder.IsRecording) return null;
        _clipping = true;
        try
        {
            if (!await _recorder.RequestPermissionAsync()) return null;
            if (IsActive || !_recorder.StartRecording()) return null;
            int session = _session;
            await Task.Delay(ms);
            if (session != _session || !_recorder.IsRecording) return null;   // 途中で Cancel された
            return _recorder.StopRecording();
        }
        finally { _clipping = false; }
    }

    public bool IsClipping => _clipping;

    public void Cancel()
    {
        if (_stt != null && _stt.IsListening) _stt.Cancel();
        if (_recorder != null && _recorder.IsRecording) _recorder.StopRecording();
        _mode = Mode.None;
        _session++;
    }

    private async Task<string> TranscribeWithTimeoutAsync(byte[] wav)
    {
        var task = _gemini.TranscribeAsync(wav);
        if (await Task.WhenAny(task, Task.Delay(TranscribeTimeoutMs)) != task)
        {
            Debug.LogWarning("[VocabPron] transcription took too long. Giving up for this try.");
            _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);   // 後で失敗しても警告を出さない
            return "";
        }
        try { return await task; }
        catch (Exception e) { Debug.LogWarning("[VocabPron] transcription failed: " + e.Message); return ""; }
    }

    private static bool HasVoice(byte[] wav)
    {
        if (wav == null || wav.Length <= 44) return false;
        for (int i = 44; i + 1 < wav.Length; i += 2)
            if (Math.Abs(BitConverter.ToInt16(wav, i)) >= VoicePeak * short.MaxValue) return true;
        return false;
    }

    private async Task AutoEndRecordOnlyAsync(int session)
    {
        await Task.Delay(RecordOnlyMilliseconds);
        if (session != _session || _mode != Mode.RecordOnly || _ended) return;
        _ended = true;
        AutoEnded?.Invoke();
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
