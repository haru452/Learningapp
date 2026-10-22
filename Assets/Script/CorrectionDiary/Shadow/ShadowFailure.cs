using System;

/// <summary>
/// 回の用意 (ダウンロード・Gemini の切り出し) が同じ回で続けて失敗したら、その回を飛ばす。
/// 上限 (429)・通信なし・キャンセルは回のせいではないので数えない。
/// </summary>
public static class ShadowFailure
{
    public const int SkipAfter = 2;

    public static bool CountsAgainstEpisode(Exception e, bool online) =>
        online && !(e is GeminiQuotaException) && !(e is OperationCanceledException);

    public static bool ShouldSkip(int failuresInARow) => failuresInARow >= SkipAfter;
}
