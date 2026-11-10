using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// KaraokeScreen の続き: 歌う (動画を流して録音し、歌詞を進める) → Finish &amp; check → 採点 → 結果・My voice。
/// </summary>
public partial class KaraokeScreen
{
    private const float PlayWaitSeconds = 60f;    // ▶ を押すまで待つ
    private const int ExtraRecordSeconds = 30;

    // 動画の時刻: 1 秒ごとに届く OnTime と、その後の経過時間から出す
    private bool _playing, _videoEnded, _finishRequested;
    private string _videoError;
    private double _lastVideoTime;
    private float _lastVideoTimeAt;
    private float _recordStartedAt;      // 録音を始めた時刻 (Time.realtimeSinceStartup)
    private double _recordStartVideo;    // 録音を始めたときの動画の時刻
    private KaraokeClock _clock = new KaraokeClock();   // 動画の時刻 → 録音の秒 (一時停止しても合わせる)

    // 採点した結果 (My voice 用に録音も持っておく)
    private class Result
    {
        public byte[] Wav;
        public KaraokeClock Clock;
        public List<LrcLine> Sung;
        public int TotalLines;
        public KaraokeCheck Check;
        public int Score;
    }
    private Result _result;

    // 時刻は再生中 1 秒ごとに届く。届かなくなったら (一時停止・読み込み中) 1.5 秒より先へは進めない。
    // エディターでは動画がブラウザで開くので、こちらの時計で進める
    private double VideoTime
    {
        get
        {
            if (!_playing) return _lastVideoTime;
            double since = Time.realtimeSinceStartup - _lastVideoTimeAt;
            return _lastVideoTime + (YouTubePlayerView.IsEmbeddedSupported ? Math.Min(since, 1.5) : since);
        }
    }

    private void OnVideoPlaying()
    {
        if (!_playing) { _lastVideoTimeAt = Time.realtimeSinceStartup; }
        _playing = true;
    }

    private void OnVideoTime(double t)
    {
        _lastVideoTime = t;
        _lastVideoTimeAt = Time.realtimeSinceStartup;
        // 録音の何秒目に動画が何秒だったかを覚える (止まっても、あとで行と録音を合わせられるように)
        if (_state == State.Singing) _clock.Add(Time.realtimeSinceStartup - _recordStartedAt, t);
    }

    private async void OnStart()
    {
        if (_state != State.Ready || _song == null) return;
        int token = ++_token;
        if (!await _recorder.RequestPermissionAsync())
        {
            if (token == _token) { _status = "Microphone permission denied. Please allow it in Settings."; Render(); }
            return;
        }
        if (token != _token) return;

        _playing = _videoEnded = _finishRequested = false;
        _videoError = null;
        _lastVideoTime = 0;
        _recordStartVideo = 0;
        _result = null;
        _state = State.WaitingToPlay;
        _status = "Tap ▶ on the video. Recording starts with the song.";
        Render();
        await Task.Yield();   // 枠の大きさが決まってから動画を置く
        if (token != _token) return;
        _video.SetVideoArea(_videoArea);
        _video.Show(_chosenVideo.id);
        // エディターでは動画がブラウザで開くので、すぐ始まったことにする (時計はこちらで進める)
        if (!YouTubePlayerView.IsEmbeddedSupported) OnVideoPlaying();

        float waitEnd = Time.realtimeSinceStartup + PlayWaitSeconds;
        while (!_playing)
        {
            if (token != _token) return;
            if (_videoError != null || Time.realtimeSinceStartup > waitEnd)
            {
                _video.Hide();
                _state = State.Ready;
                _status = "The video could not be played. Try another video.";
                Render();
                return;
            }
            await Task.Yield();
        }

        _recorder.MaxSeconds = _chosenVideo.durationSeconds + ExtraRecordSeconds;
        if (!_recorder.StartRecording())
        {
            _video.Hide();
            _state = State.Ready;
            _status = "Could not start the microphone.";
            Render();
            return;
        }
        _recordStartedAt = Time.realtimeSinceStartup;
        _recordStartVideo = VideoTime;
        _clock = new KaraokeClock(_recordStartVideo);
        _state = State.Singing;
        _status = "Sing along! Tap \"Finish & check\" any time (after the first verse is fine).";
        Render();

        while (!_finishRequested && !_videoEnded && _recorder.IsRecording)
        {
            if (token != _token) return;
            RenderLyrics();
            await Task.Yield();
        }
        if (token != _token) return;
        await FinishAsync(token);
    }

    private void OnFinish()
    {
        if (_state == State.Singing) _finishRequested = true;
        else if (_state == State.WaitingToPlay) { _token++; _video.Hide(); _state = State.Ready; _status = ""; Render(); }
    }

    // 歌うのをやめて、歌った行だけを採点する
    private async Task FinishAsync(int token)
    {
        double stoppedAt = VideoTime;
        byte[] wav = _recorder.IsRecording ? _recorder.StopRecording() : null;
        _video.Hide();
        _playing = false;
        var sung = LrcLyrics.SungLines(_lines, stoppedAt, _offset);
        if (wav == null || sung.Count < 2)
        {
            _state = State.Ready;
            _status = "Please sing a little more. Tap Start to try again.";
            Render();
            return;
        }

        int total = _lines.Count(l => !l.IsBlank);
        var chunks = LrcLyrics.Chunks(_lines, _offset, _clock.RecordSeconds, KaraokeWav.Seconds(wav));
        var wavs = chunks.Select(c => KaraokeWav.Slice(wav, c.start, c.end)).ToList();
        var chunkLines = chunks.Select(c => c.lines.Select(i => (i, _lines[i].Text)).ToList()).ToList();
        _result = new Result { Wav = wav, Clock = _clock, Sung = sung, TotalLines = total };
        CountStudyDay();   // 歌った日を連続記録に数える (採点できなくても)
        await CheckAsync(token, wavs, chunkLines);
    }

    private List<byte[]> _pendingWavs;
    private List<List<(int index, string text)>> _pendingLines;

    private async Task CheckAsync(int token, List<byte[]> wavs, List<List<(int index, string text)>> chunkLines)
    {
        _pendingWavs = wavs;
        _pendingLines = chunkLines;
        _state = State.Checking;
        _status = "Checking your pronunciation... (this can take a minute). You can use other tabs; the result will be here.";
        Render();
        var gemini = KaraokeResults.CreateGemini();
        DateTime takenAt = DateTime.UtcNow;
        try
        {
            var check = await KaraokeResults.CheckAllAsync(gemini, wavs, chunkLines);
            if (token != _token) return;
            _pendingWavs = null;
            _pendingLines = null;
            _result.Check = check;
            _result.Score = KaraokeScorer.Score(_result.Sung, check.Lines);
            _state = State.Result;
            _status = "Saving...";
            Render();
            NotifyIfHidden("Karaoke: checked — " + _result.Score + " / 100");
            bool sent = await KaraokeResults.SaveAsync(_song.Title, _song.Artist, _chosenVideo.id, _result.Score, check, takenAt);
            if (token == _token && _state == State.Result) { _status = sent ? "Saved and sent to Discord." : "Saved."; Render(); }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Karaoke] check failed: " + e.Message);
            if (token != _token) return;
            if (GeminiLater.IsBusy(e) && SaveForLater(gemini, wavs, chunkLines, takenAt))
            {
                _pendingWavs = null;
                _pendingLines = null;
                _result = null;
                _state = State.Ready;
                _status = DiaryManager.SavedForLaterMessage;
                NotifyIfHidden("Karaoke: busy — it will be checked later");
            }
            else
            {
                _state = State.Result;   // 結果の画面の「Check again」で送り直す
                _status = "Could not check (" + FirstLine(e.Message) + "). Tap \"Check again\".";
                NotifyIfHidden("Karaoke: could not check — open Karaoke and tap Check again");
            }
            Render();
        }
    }

    private void OnCheckAgain()
    {
        if (_state != State.Result || _pendingWavs == null || _result == null) return;
        _ = CheckAsync(++_token, _pendingWavs, _pendingLines);
    }

    // Gemini が混んでいたら、切った録音と行を端末に残してあとで自動で送る (GeminiLater)
    private bool SaveForLater(GeminiApiClient gemini, List<byte[]> wavs, List<List<(int index, string text)>> chunkLines, DateTime takenAt)
    {
        var job = new GeminiLater.Job
        {
            kind = GeminiLater.Karaoke,
            createdUtc = PracticeJson.Utc(takenAt),
            model = gemini.Model,
            fallbackModel = gemini.FallbackModel,
            videoTitle = _song.Title,
            artist = _song.Artist,
            videoId = _chosenVideo.id,
            chunkLines = chunkLines.Select(c => string.Join(",", c.Select(l => l.index))).ToList(),
            lineIndexes = chunkLines.SelectMany(c => c.Select(l => l.index)).ToList(),
            lineTexts = chunkLines.SelectMany(c => c.Select(l => l.text)).ToList(),
        };
        return GeminiLater.Add(job, wavs);
    }

    private void CountStudyDay()
    {
        var diary = FindObjectOfType<DiaryManager>(true);
        if (diary != null) diary.CountOtherPracticeAsStudyDay();
    }

    /// <summary>行を押したら、その行の自分の声を流す</summary>
    private void PlayMyVoice(int lineIndex)
    {
        if (_result?.Wav == null || lineIndex < 0 || lineIndex >= _lines.Count) return;
        double start = _result.Clock.RecordSeconds(_lines[lineIndex].Time + _offset);
        double end = lineIndex + 1 < _lines.Count ? _result.Clock.RecordSeconds(_lines[lineIndex + 1].Time + _offset) : start + 8;
        var clip = VocabAudio.WavToClip(KaraokeWav.Slice(_result.Wav, start - 0.3, end + 0.3));
        if (clip == null) return;
        _audio.Stop();
        _audio.clip = clip;
        _audio.Play();
    }
}
