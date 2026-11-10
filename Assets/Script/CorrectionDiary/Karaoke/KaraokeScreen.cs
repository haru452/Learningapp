using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 洋楽カラオケ (シャドーイングとは別のモード)。曲名 → LRCLIB の時刻つき歌詞 + YouTube の動画 → 歌詞を見ながら歌う (録音) →
/// 「Finish &amp; check」(途中でもよい) → 歌った行だけを Gemini が採点 → 赤い単語・点数・アドバイス・行ごとの My voice。
/// この部分: 状態と、探す・選ぶ。歌う・採点は .Sing.cs、画面の部品は .Ui.cs。
/// </summary>
public partial class KaraokeScreen : MonoBehaviour
{
    private enum State { Idle, Searching, Choosing, Ready, WaitingToPlay, Singing, Checking, Result }

    private State _state = State.Idle;
    private LrclibClient _lrclib;
    private YouTubeSearchClient _youtube;
    private YouTubePlayerView _video;
    private AudioRecorder _recorder;
    private AudioSource _audio;

    // 選んだ曲
    private List<LrcSong> _songs = new List<LrcSong>();
    private List<YouTubeSearchClient.Video> _videos = new List<YouTubeSearchClient.Video>();
    private YouTubeSearchClient.Video _chosenVideo;
    private LrcSong _song;
    private List<LrcLine> _lines = new List<LrcLine>();
    private double _offset;
    private int _token;   // 画面を閉じたら増やして、途中の処理を止める

    private void Awake()
    {
        _lrclib = new LrclibClient();
        var s = ApiSecrets.Data;
        _youtube = new YouTubeSearchClient(s.youtubeApiKey);
        _video = gameObject.AddComponent<YouTubePlayerView>();
        _recorder = gameObject.AddComponent<AudioRecorder>();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _video.OnPlaying += OnVideoPlaying;
        _video.OnTime += OnVideoTime;
        _video.OnEnded += () => _videoEnded = true;
        _video.OnError += e => _videoError = e;
    }

    private void OnEnable() => Render();

    // 画面を閉じたら、動画・録音・再生を止める。歌っている途中なら、その回は捨てる。探している途中ならやめる。
    // 採点中はそのまま続けて保存する (戻ってきたら結果が出ている)
    private void OnDisable()
    {
        if (_state != State.Checking) _token++;
        if (_state == State.Searching) ToIdle("Type a song title and tap Search.");
        if (_state == State.WaitingToPlay || _state == State.Singing)
        {
            if (_recorder != null && _recorder.IsRecording) _recorder.StopRecording();
            _state = _song != null ? State.Ready : State.Idle;
            _status = "";
        }
        if (_video != null) _video.Hide();
        if (_audio != null) _audio.Stop();
    }

    // ---------------- 探す ----------------

    private async void OnSearch()
    {
        string query = (_input != null ? _input.text : "").Trim();
        if (query.Length == 0 || _state == State.Searching || IsBusy) return;
        int token = ++_token;
        _state = State.Searching;
        _status = "Searching for lyrics...";
        Render();
        try
        {
            _songs = await _lrclib.SearchAsync(query);
            if (token != _token) return;
            if (_songs.Count == 0) { ToIdle("No lyrics for this song. Try another title."); return; }
            if (!_youtube.IsConfigured) { ToIdle("YouTube API key is not set."); return; }

            _status = "Finding the video...";
            Render();
            _videos = await _youtube.SearchVideosByTitleAsync(LrclibClient.VideoQuery(_songs[0]), 60, 900, 5);
            if (token != _token) return;
            if (_videos.Count == 0) { ToIdle("No video found for this song. Try another title."); return; }
            _state = State.Choosing;
            _status = "Choose a video. Lyric videos usually match the song best.";
        }
        catch (HttpException e) when (YouTubeSearchClient.IsQuotaExceeded(e))
        {
            if (token == _token) ToIdle("Today's YouTube search limit is reached. Please try again tomorrow.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Karaoke] search failed: " + e.Message);
            if (token == _token) ToIdle("Could not search (" + FirstLine(e.Message) + "). Please try again.");
        }
        Render();
    }

    /// <summary>候補の動画を選ぶ: 長さが一番近い歌詞を使い、ずらし量を決める (覚えた値があればそれ)</summary>
    private void Choose(YouTubeSearchClient.Video video)
    {
        if (_state != State.Choosing) return;
        _chosenVideo = video;
        _song = LrclibClient.PickBest(_songs, video.durationSeconds);
        _lines = LrcLyrics.Parse(_song.Synced);
        _offset = KaraokeOffsetStore.Get(video.id) ?? LrcLyrics.AutoOffset(video.durationSeconds, _song.Duration);
        _state = State.Ready;
        _status = EarphoneCheck.HasEarphones()
            ? "Put on your earphones and tap Start. Lyrics follow the song."
            : "No earphones found. Use earphones so the music is not recorded (or tap Start anyway).";
        Render();
    }

    /// <summary>歌詞を早く / 遅く (+ = 歌詞を遅らせる)。動画ごとに覚える</summary>
    private void Nudge(double seconds)
    {
        if (_chosenVideo == null) return;
        _offset = Math.Round(_offset + seconds, 1);
        KaraokeOffsetStore.Set(_chosenVideo.id, _offset);
        Render();
    }

    /// <summary>最初からやり直す (別の曲を探す)</summary>
    private void OnNewSong()
    {
        if (IsBusy) return;
        _token++;
        _song = null;
        _chosenVideo = null;
        _lines = new List<LrcLine>();
        _result = null;
        if (_audio != null) _audio.Stop();
        ToIdle("Type a song title and tap Search.");
        Render();
    }

    private bool IsBusy => _state == State.Searching || _state == State.WaitingToPlay || _state == State.Singing || _state == State.Checking;

    private void ToIdle(string status)
    {
        _state = State.Idle;
        _status = status;
    }

    private static string FirstLine(string s)
    {
        s = s ?? "";
        int i = s.IndexOf('\n');
        return i < 0 ? s : s.Substring(0, i);
    }
}
