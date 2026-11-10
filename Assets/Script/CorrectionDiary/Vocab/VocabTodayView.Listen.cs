using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// VocabTodayView の続き: 聞いてから見るカード。
/// 新しいカードは単語を見せずに、単語の音 → 例文の音を流す。1 回目のタップで表 (単語)、そのあとは裏・表を交互に。
/// 苦手の練習 (Weak) は今までどおり単語を見せる
/// </summary>
public partial class VocabTodayView
{
    private const int SentenceGapMs = 400;   // 単語と例文の間

    private bool _hidden;                    // まだ単語を見せていない
    private CancellationTokenSource _listenCts;

    /// <summary>カードを出したとき: 今日のカードなら単語を隠して、音と例文を流す</summary>
    private void StartListenFirst()
    {
        _hidden = _mode == Mode.Card;
        if (_hidden) PlayListen(CurrentCard);
    }

    /// <summary>カードを押した: 隠していればまず表を出す。そのあとは裏・表を交互に</summary>
    private void OnCardTapped()
    {
        if (VideosOpen) return;
        if (_hidden) _hidden = false;
        else _flipped = !_flipped;
        Render();
        ShowStatusForCard();
    }

    /// <summary>Sound: 隠している間は単語と例文、見せたあとは単語だけ</summary>
    private void OnSoundClicked()
    {
        var c = CurrentCard;
        if (_hidden) PlayListen(c);
        else { StopListen(); PlayModel(c); }
    }

    private void PlayListen(VocabCard c)
    {
        StopListen();
        if (c == null) return;
        _listenCts = new CancellationTokenSource();
        var ct = _listenCts.Token;
        _dicts.TryGetValue(c.Word, out DictionaryResult dict);
        if (dict == null) dict = VocabStore.LoadDict(c.Word);
        string url = dict != null ? dict.Entries.Select(e => e.AudioUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u)) ?? "" : "";
        string example = VocabContext.ExamplesByPos(dict).SelectMany(g => g.examples).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        _ = PlaySafeAsync(async () =>
        {
            try
            {
                await _ctx.Audio.PlayWordAsync(c.Word, url, ct);
                if (string.IsNullOrWhiteSpace(example) || ct.IsCancellationRequested) return;
                await Task.Delay(SentenceGapMs, ct);
                await _ctx.Audio.PlaySentenceAsync(example, ct);
            }
            catch (OperationCanceledException) { }   // 止めたときは何もしない
        });
    }

    /// <summary>流している音と例文を止める (評価・前に戻る・Say it・タブを移ったとき)</summary>
    private void StopListen()
    {
        _listenCts?.Cancel();
        _listenCts = null;
    }

    private string HiddenFrontText(VocabCard c)
    {
        var sb = new StringBuilder();
        sb.Append("<size=160%><b>Listen</b></size>\n");
        sb.Append("<color=#aaaaaa>What's the word?</color>");
        if (_pron.TryGetValue(c.Word, out string verdict))
        {
            sb.Append("\n\n").Append(VocabContext.PronLabel(verdict));
            _heard.TryGetValue(c.Word, out string heard);
            sb.Append("\n<size=75%>Heard: \"").Append(VocabContext.Esc(heard ?? "")).Append("\"</size>");
        }
        sb.Append("\n\n<size=65%><color=#aaaaaa>Tap \"Sound\" to hear it again. Tap the card to see the word.</color></size>");
        return sb.ToString();
    }
}
