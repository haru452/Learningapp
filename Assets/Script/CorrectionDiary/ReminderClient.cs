using System;
using System.Globalization;
using System.Threading.Tasks;

/// <summary>
/// リマインダー用 Worker (diary-reminder-worker) に「今日の日記を書いた」と連絡する。
/// </summary>
public class ReminderClient
{
    private readonly string _baseUrl;
    private readonly string _secret;

    public ReminderClient(string workerUrl, string secret)
    {
        _baseUrl = workerUrl?.Trim().TrimEnd('/');
        _secret = secret?.Trim();
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_baseUrl) && !string.IsNullOrEmpty(_secret);

    /// <param name="date">日記を書いた日 (年月日だけが使われる)</param>
    /// <param name="streak">現在の連続日数。不明なら 0</param>
    public async Task NotifyDiaryDoneAsync(DateTime date, int streak)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Reminder server is not configured.");

        string body =
            "{\"date\":\"" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\"," +
            "\"streak\":" + Math.Max(0, streak) + "}";

        var headers = new[] { ("Authorization", "Bearer " + _secret) };

        const int maxAttempts = 3;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await HttpUtil.PostJsonAsync(_baseUrl + "/ping", body, headers, timeoutSeconds: 20);
                return;
            }
            catch (HttpException e)
            {
                // 400 (送った内容が不正) と 401 (合言葉が違う) は、再試行しても直らない
                bool permanent = e.StatusCode == 400 || e.StatusCode == 401;
                if (permanent || attempt == maxAttempts - 1) throw;

                await Task.Delay(2000 * (attempt + 1));
            }
        }
    }
}
