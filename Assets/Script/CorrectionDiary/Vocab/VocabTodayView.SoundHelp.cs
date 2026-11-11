using System.Linq;
using UnityEngine;

/// <summary>Today の続き: カードの「Didn't sound right?」(VocabSoundHelp) とのつなぎと、音の練習動画をアプリの中で流すこと</summary>
public partial class VocabTodayView
{
    /// <summary>「Didn't sound right?」をつなぐ (VocabManager から)</summary>
    public void SetSoundHelp(VocabSoundHelp help)
    {
        _help = help;
        if (help == null) return;
        help.CurrentWord = () => (_mode == Mode.Card || _mode == Mode.Weak) && !_flipped && !VideosOpen ? CurrentCard?.Word : null;
        help.IpaFor = w => _dicts.TryGetValue(w, out var d) && d != null && d.Found ? d.Entries.Select(e => e.Ipa).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "" : "";
        help.Changed = Render;
        help.PlayVideo = OpenSoundVideo;
    }

    // 「Watch: 〇〇 video」: カードと評価の行を隠して、動画の画面で練習動画を流す。Close でカードに戻る。
    // 動画の画面が無いとき (シーンを組み立て直す前) は YouTube で開く
    private void OpenSoundVideo(YouTubeSearchClient.Video video)
    {
        if (video == null) return;
        if (_videos == null) { Application.OpenURL(video.WatchUrl); return; }
        if (CurrentCard == null || VideosOpen || (_mode != Mode.Card && _mode != Mode.Weak)) return;
        _sayToken++;
        _ctx.Capture.Cancel();
        StopListen();
        _ctx.Audio.Stop();
        if (_v.SayLabel != null) _v.SayLabel.text = "Say it";
        SetActive(_v.CardArea, false);
        SetActive(_v.RatingRow, false);
        VocabContext.Show(_v.RatingHint, false);
        if (_v.StartButton != null && _mode == Mode.Weak) VocabContext.Show(_v.StartButton, false);
        _ctx.SetStatus("Watch, then tap \"Close\" and try \"Say it\" again.");
        _videos.OpenVideo(video.id, video.title, video.channel, () =>
        {
            SetActive(_v.CardArea, true);
            if (_mode == Mode.Weak) VocabContext.Show(_v.StartButton, true);
            Render();
            ShowStatusForCard();
        });
    }
}
