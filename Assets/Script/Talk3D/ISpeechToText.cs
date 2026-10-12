using System;

/// <summary>Android SpeechRecognizer のエラーコード(+ 独自コード)</summary>
public static class SttErrorCode
{
    public const int Unavailable = -1;          // 独自: 音声認識サービスが端末にない
    public const int NetworkTimeout = 1;
    public const int Network = 2;
    public const int Audio = 3;
    public const int Server = 4;
    public const int Client = 5;
    public const int SpeechTimeout = 6;         // 何も話さなかった
    public const int NoMatch = 7;               // 聞き取れなかった
    public const int RecognizerBusy = 8;
    public const int InsufficientPermissions = 9;
    public const int TooManyRequests = 10;
    public const int ServerDisconnected = 11;
    public const int LanguageNotSupported = 12;
    public const int LanguageUnavailable = 13;
}

/// <summary>
/// 音声認識(STT)の共通インターフェース。
/// 後で sherpa-onnx など別の実装に差し替えるときも、これを実装すればよい。
/// イベントは Pump() を呼んだとき、Unity のメインスレッドで発火する。
/// </summary>
public interface ISpeechToText : IDisposable
{
    /// <summary>認識途中の暫定テキスト</summary>
    event Action<string> OnPartialResult;

    /// <summary>1回の発話の確定テキスト</summary>
    event Action<string> OnFinalResult;

    /// <summary>エラー (コード, メッセージ)</summary>
    event Action<int, string> OnError;

    bool IsListening { get; }

    /// <param name="locale">例: "en-US", "ja-JP"</param>
    void Init(string locale);

    /// <summary>1回分の発話の聞き取りを開始する(認識は1発話で終了する)</summary>
    void StartListening();

    /// <summary>聞き取りを終了して、ここまでの結果を確定させる</summary>
    void StopListening();

    /// <summary>聞き取りを中断し、結果を捨てる</summary>
    void Cancel();

    /// <summary>毎フレーム Update から呼ぶ。コールバックをメインスレッドに戻す。</summary>
    void Pump();
}
