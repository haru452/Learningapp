using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 読み上げ(TTS)の共通インターフェース。
/// 後で高品質TTSに差し替えるときも、このインターフェースを実装するだけでよい。
/// </summary>
public interface ITextToSpeech : IDisposable
{
    /// <summary>初期化を開始する(完了は SpeakAsync 内で自動的に待つ)</summary>
    void Init();

    /// <summary>現在読み上げ中か</summary>
    bool IsSpeaking { get; }

    /// <summary>読み上げを依頼し、再生が終わるまで待つ。連続で呼ぶと順番に読まれる。</summary>
    Task SpeakAsync(string text, CancellationToken ct);

    /// <summary>読み上げを即停止し、待ち行列も破棄する</summary>
    void Stop();

    /// <summary>声の高さ(1 = 標準、大きいほど高い)と、話す速さ(1 = 標準)</summary>
    void SetVoiceStyle(float pitch, float rate);

    /// <summary>声の名前を指定する(空なら端末の既定)。使える名前は起動時に Console に出る</summary>
    void SetVoiceName(string voiceName);
}