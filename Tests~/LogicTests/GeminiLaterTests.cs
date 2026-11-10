// Gemini が混んでいたときに答えを残して、あとで送る仕組みのテスト。Program.cs の Main から呼ぶ。
using System;
using System.Collections.Generic;

public static partial class LogicTests
{
    private static void GeminiLaterTests()
    {
        Check(GeminiLater.IsBusy(new HttpException(503, "HTTP 503 UNAVAILABLE")), "later: 503 is busy");
        Check(GeminiLater.IsBusy(new HttpException(500, "HTTP 500")), "later: 500 is busy");
        Check(GeminiLater.IsBusy(new HttpException(429, "HTTP 429")), "later: 429 is busy");
        Check(GeminiLater.IsBusy(new HttpException(0, "timeout")), "later: timeout / no network is busy");
        Check(GeminiLater.IsBusy(new GeminiQuotaException("all keys")), "later: quota is busy");
        Check(GeminiLater.IsBusy(new Exception("wrap", new HttpException(503, "x"))), "later: inner busy error");
        Check(!GeminiLater.IsBusy(new HttpException(400, "HTTP 400 bad request")), "later: 400 is not busy");
        Check(!GeminiLater.IsBusy(new Exception("Could not parse Gemini response")), "later: parse error is not busy");
        Check(!GeminiLater.IsBusy(null), "later: null is not busy");

        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var job = new GeminiLater.Job { id = "a", kind = GeminiLater.Diary, createdUtc = "2026-11-09T12:00:00Z" };
        Check(!GeminiLater.ShouldDrop(job, now), "later: keep a new job");
        job.failures = GeminiLater.MaxFailures;
        Check(GeminiLater.ShouldDrop(job, now), "later: drop after too many failures");
        var old = new GeminiLater.Job { id = "b", kind = GeminiLater.Topic, createdUtc = "2026-11-01T12:00:00Z", busyTries = 99 };
        Check(GeminiLater.ShouldDrop(old, now), "later: drop after 7 days");
        Check(old.CreatedUtc == new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), "later: created time read as UTC");

        var v = new GeminiLater.Job { heardWords = new List<string> { "think", "red" }, heardTexts = new List<string> { "sink" } };
        var heard = GeminiLater.HeardOf(v);
        Check(heard.Count == 1 && heard["think"] == "sink", "later: heard map from two lists");
        Check(GeminiLater.KindLabel(GeminiLater.Vocab) == "Vocab pronunciation", "later: kind label");
    }
}
