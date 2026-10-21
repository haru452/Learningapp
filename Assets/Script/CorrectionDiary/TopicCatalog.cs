using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// お題スピーキングの種類と、種類ごとの話す時間。
/// 通信できず Gemini にお題を作ってもらえないときのための、お題のリストも持つ。
/// </summary>
public static class TopicCatalog
{
    /// <summary>exam = 試験風の意見・説明 / daily = 身近な話題 / work = 仕事・面接</summary>
    public static readonly string[] Kinds = { "exam", "daily", "work" };

    /// <summary>話す時間 (秒): daily は 45 秒、それ以外は 60 秒</summary>
    public static int SecondsFor(string kind) => kind == "daily" ? 45 : 60;

    public static string RandomKind(Random rng) => Kinds[rng.Next(Kinds.Length)];

    /// <summary>その種類の、端末内のお題の数</summary>
    public static int CountFor(string kind) => TopicsFor(kind).Length;

    /// <summary>端末内のリストからお題を1つ選ぶ。recent (最近のお題) に入っているものは避ける (全部使っていたら何でも)</summary>
    public static GeminiApiClient.TopicPrompt Offline(string kind, Random rng, IList<string> recent)
    {
        if (!Kinds.Contains(kind)) kind = "daily";
        string[] all = TopicsFor(kind);
        var candidates = recent == null ? all : all.Where(t => !recent.Contains(t)).ToArray();
        if (candidates.Length == 0) candidates = all;
        return new GeminiApiClient.TopicPrompt
        {
            kind = kind,
            topic = candidates[rng.Next(candidates.Length)],
            hint = Hints[kind],
        };
    }

    private static string[] TopicsFor(string kind) =>
        kind == "exam" ? Exam : kind == "work" ? Work : Daily;

    private static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
    {
        { "exam", "Give your opinion first, then two reasons and an example." },
        { "daily", "Say what it is, then describe it and why you like it or remember it." },
        { "work", "Explain the situation, what you did, and what happened in the end." },
    };

    private static readonly string[] Exam =
    {
        "Do you think working from home is better than working in an office? Why?",
        "Should students have part-time jobs while they are in school? Why or why not?",
        "Is it better to live in a big city or in the countryside? Explain your opinion.",
        "Do you think social media does more good than harm? Why?",
        "Should public transportation be free? Give reasons for your answer.",
        "Is it important to learn a foreign language today? Why?",
        "Do you think people should read paper books or e-books? Why?",
        "Should schools teach cooking and money management? Explain.",
        "Is it better to travel alone or with friends? Why?",
        "Do you think robots will take many people's jobs? Explain your view.",
        "Should children have smartphones? Why or why not?",
        "Is online shopping better than shopping in stores? Explain.",
        "Should people be allowed to use AI to write their homework? Why?",
        "Do you think it is better to save money or to spend it on experiences?",
        "Should companies let workers choose their own working hours? Why?",
        "Is it more important to have a high salary or an interesting job?",
        "Should every student study abroad at least once? Explain your opinion.",
        "Do you think fast food should be taxed more? Why or why not?",
        "Is it better to plan a trip carefully or to travel without a plan?",
        "Should sports be a required subject in university? Why?",
    };

    private static readonly string[] Daily =
    {
        "Tell me about a place you like to visit on weekends.",
        "What did you do last weekend? Describe it.",
        "Describe your favorite food and why you like it.",
        "Tell me about a hobby you enjoy and how you started it.",
        "Describe a memorable trip you have taken.",
        "What is your morning routine like?",
        "Tell me about a movie or TV show you watched recently.",
        "Describe a friend who is important to you.",
        "What would you do if you had a free day tomorrow?",
        "Tell me about something new you learned recently.",
        "Describe your hometown to someone who has never been there.",
        "What is a habit you want to change, and why?",
        "Tell me about the best gift you have ever received.",
        "Describe a restaurant or cafe you like.",
        "What did you want to be when you were a child? Why?",
        "Tell me about a time you were really happy.",
        "Describe your favorite season and what you do in it.",
        "What kind of music do you like, and when do you listen to it?",
        "Tell me about a small problem you had this week and how you handled it.",
        "Describe your room or the place where you study.",
    };

    private static readonly string[] Work =
    {
        "Please introduce yourself as if you were in a job interview.",
        "Describe a time you solved a problem at work or school.",
        "What are your strengths and weaknesses?",
        "Tell me about a time you worked in a team. What was your role?",
        "Why do you want to work in the industry you are interested in?",
        "Describe a project you are proud of.",
        "How do you handle stress or tight deadlines?",
        "Tell me about a mistake you made and what you learned from it.",
        "Where do you see yourself in five years?",
        "Explain your current job or studies to someone outside your field.",
        "Describe a time you had to persuade someone.",
        "How would you explain a delay in a project to your manager?",
        "What makes a good leader, in your opinion? Give an example.",
        "Tell me about a time you had to learn something quickly.",
        "How do you organize your tasks when you are very busy?",
        "Describe a difficult customer or classmate and how you dealt with them.",
        "Why should we hire you? Give two reasons.",
        "Explain a process you know well, step by step.",
        "Tell me about a goal you set and how you achieved it.",
        "How would you give helpful feedback to a coworker?",
    };
}
