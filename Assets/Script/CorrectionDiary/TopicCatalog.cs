using System;

/// <summary>
/// お題スピーキングの種類と、種類ごとの話す時間。
/// </summary>
public static class TopicCatalog
{
    /// <summary>exam = 試験風の意見・説明 / daily = 身近な話題 / work = 仕事・面接</summary>
    public static readonly string[] Kinds = { "exam", "daily", "work" };

    /// <summary>話す時間 (秒): daily は 45 秒、それ以外は 60 秒</summary>
    public static int SecondsFor(string kind) => kind == "daily" ? 45 : 60;
}
