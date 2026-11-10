using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TopicPracticeManager の続き: 結果の保存・送信・連続記録、端末に覚えておくもの、Helpers。
/// </summary>
public partial class TopicPracticeManager
{
    // ------------------------------------------------------------
    // 結果の保存・送信・連続記録
    // ------------------------------------------------------------
    // 失敗しても採点結果は画面に残る。ログインしていない・送れないときは端末に一時保存し、ログイン時にまとめて送る
    private async void SaveResultToCloudAsync(SaveTicket ticket, GeminiApiClient.TopicPrompt topic, int seconds,
                                              GeminiApiClient.TopicGrade g, string transcript)
    {
        if (supabase == null || !supabase.IsConfigured) return;

        DateTime takenAt = DateTime.UtcNow;
        if (!await supabase.EnsureSignedInAsync())
        {
            ticket.PendingLocalId = PendingTopicStore.Add(topic.kind, topic.topic, seconds, g, transcript, takenAt,
                                                          ticket.ReadAloudAccuracy);
            return;
        }

        try
        {
            long id = await supabase.SaveTopicResultAsync(topic.kind, topic.topic, seconds, g, transcript,
                                                          ticket.ReadAloudAccuracy, takenAt);
            int? sent = ticket.ReadAloudAccuracy;
            ticket.Id = id;
            // 保存している間に言い直しが終わっていたら、その一致率を入れる
            if (id > 0 && ticket.ReadAloudAccuracy.HasValue && ticket.ReadAloudAccuracy != sent)
                await SaveReadAloudAsync(id, ticket.ReadAloudAccuracy.Value);
            if (this == null) return;
            if (PendingTopicStore.Count > 0) _ = supabase.UploadPendingTopicResultsAsync();
            var diary = FindDiary();
            if (diary != null) diary.RefreshStreakFromCloud();   // サーバーが数えた連続記録を、カードに反映する
        }
        catch (SupabaseService.SupabaseException e) when (e.StatusCode == 400)
        {
            Debug.LogWarning("[TopicPractice] the result could not be saved (invalid data): " + e.Message);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TopicPractice] could not save the result. Keeping it to upload later: " + e.Message);
            ticket.PendingLocalId = PendingTopicStore.Add(topic.kind, topic.topic, seconds, g, transcript, takenAt,
                                                          ticket.ReadAloudAccuracy);
        }
    }

    // 発音チェックのアドバイスを、発音用のチャンネルに送る (失敗しても画面の結果はそのまま)
    private async void SendPronunciationToDiscord(string sentence, string advice)
    {
        if (!sendPronunciationToDiscord || _pronDiscord == null || !_pronDiscord.IsConfigured) return;
        try
        {
            await _pronDiscord.SendPronunciationAsync(DateTime.Now, _topic?.topic, sentence, advice);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TopicPractice] could not send pronunciation advice to Discord: " + e.Message);
            if (this != null && !_busy) SetStatus("Pronunciation checked, but Discord failed: " + FirstLine(e.Message));
        }
    }

    private async Task<string> SendToDiscordAsync(GeminiApiClient.TopicGrade g)
    {
        if (!sendToDiscord || !_discord.IsConfigured) return null;
        SetStatus("Sending to Discord...");
        try
        {
            await _discord.SendTopicResultAsync(DateTime.Now, _topic.kind, _topic.topic, g);
            return null;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return FirstLine(e.Message);
        }
    }

    // Gemini が混んでいたら、答え (文か録音) を端末に残してあとで自動で送る (GeminiLater)。残せたら true
    private bool SaveForLater(Exception e, string text, byte[] wav)
    {
        if (_topic == null || !GeminiLater.IsBusy(e) || (wav == null && string.IsNullOrWhiteSpace(text))) return false;
        var job = new GeminiLater.Job
        {
            kind = GeminiLater.Topic,
            model = _gemini.Model,
            fallbackModel = _gemini.FallbackModel,
            sendToDiscord = sendToDiscord,
            topicKind = _topic.kind,
            topic = _topic.topic,
            seconds = TopicCatalog.SecondsFor(_topic.kind),
            focusTypes = new List<string>(_focusTypes),
            text = wav != null ? "" : text,
        };
        if (!GeminiLater.Add(job, wav != null ? new[] { wav } : null)) return false;
        CountAsStudyDay();   // 話した日を連続記録に数える
        SetStatus(DiaryManager.SavedForLaterMessage);
        return true;
    }

    private DiaryManager FindDiary()
    {
        if (_diary == null) _diary = FindObjectOfType<DiaryManager>(true);
        return _diary;
    }

    // お題スピーキングをした日も、連続記録に数える (端末内の記録 + 夜のリマインダー。クラウドはサーバーが数える)
    private void CountAsStudyDay()
    {
        var diary = FindDiary();
        if (diary != null) diary.CountOtherPracticeAsStudyDay();
    }

    // ------------------------------------------------------------
    // 端末に覚えておくもの
    // ------------------------------------------------------------
    private static List<string> LoadRecentTopics()
    {
        string saved = PlayerPrefs.GetString(RecentTopicsKey, "");
        return string.IsNullOrEmpty(saved) ? new List<string>() : saved.Split('\n').ToList();
    }

    private static void RememberTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;
        var list = LoadRecentTopics();
        list.Remove(topic);
        list.Add(topic.Replace('\n', ' '));
        if (list.Count > RecentTopicsToKeep) list.RemoveRange(0, list.Count - RecentTopicsToKeep);
        PlayerPrefs.SetString(RecentTopicsKey, string.Join("\n", list));
        PlayerPrefs.Save();
    }

    // 前回のお題スピーキングの、種類ごとのミスの回数 ("tense:2,article:1")
    private static Dictionary<string, int> LoadLastCounts()
    {
        var counts = new Dictionary<string, int>();
        string saved = PlayerPrefs.GetString(LastCountsKey, "");
        foreach (string pair in saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1], out int n)) counts[MistakeTypes.Normalize(kv[0])] = n;
        }
        return counts;
    }

    private static void SaveLastCounts(Dictionary<string, int> counts)
    {
        PlayerPrefs.SetString(LastCountsKey, string.Join(",", counts.Select(kv => kv.Key + ":" + kv.Value)));
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private void ShowTopic()
    {
        if (kindText != null)
        {
            string label = _topic.kind == "exam" ? "Exam" : _topic.kind == "work" ? "Work" : "Daily";
            kindText.text = label + "  ·  " + TopicCatalog.SecondsFor(_topic.kind) + " sec";
        }
        if (topicText != null) topicText.text = _topic.topic;
        if (hintText != null) hintText.text = string.IsNullOrWhiteSpace(_topic.hint) ? "" : "Hint: " + _topic.hint;
    }

    private void ClearTopic()
    {
        _showingSentenceAtTop = false;
        SetSentenceBox(false);
        if (kindText != null) kindText.text = "";
        if (topicText != null) topicText.text = "";
        if (hintText != null) hintText.text = "";
        if (weakPointText != null) weakPointText.text = "";
    }

    private void ClearResults()
    {
        if (timerText != null) timerText.text = "";
        if (transcriptText != null) transcriptText.text = "";
        if (resultText != null) resultText.text = "";
        _ticket = null;
        SetUpReadAloud(null);
    }

    private void CancelListening()
    {
        _listening = false;
        _sessionId++;
        if (nativeStt != null && nativeStt.IsListening) nativeStt.Cancel();
        if (recorder != null && recorder.IsRecording) recorder.StopRecording();
        if (timerText != null) timerText.text = "";
    }

    private void SetState(State state)
    {
        _state = state;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        if (mainButtonLabel != null)
        {
            switch (_state)
            {
                case State.Idle: mainButtonLabel.text = "New Topic"; break;
                case State.Generating: mainButtonLabel.text = "Making a topic..."; break;
                case State.Thinking: mainButtonLabel.text = "Start now"; break;
                case State.Speaking: mainButtonLabel.text = "Finish"; break;
                case State.Grading: mainButtonLabel.text = "Grading..."; break;
            }
        }
        mainButton.interactable = !_busy && !_reading && !_recordingPron &&
            (_state == State.Idle || _state == State.Thinking || (_state == State.Speaking && !_finishing));

        if (skipThinkButton != null)
        {
            // 考える時間はメインのボタンが「Start now」になるので、同じ働きのこのボタンは出さない (2 つ並ぶと紛らわしい)
            skipThinkButton.gameObject.SetActive(false);
            skipThinkButton.interactable = !_busy;
        }
        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(_state == State.Idle && (!string.IsNullOrEmpty(_retryText) || _retryWav != null));
            retryButton.interactable = !_busy;
        }
        RefreshReadAloud();
    }

    private string Compose(string current)
    {
        var sb = new StringBuilder();
        foreach (string s in _segments)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(s);
        }
        if (!string.IsNullOrWhiteSpace(current))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(current.Trim());
        }
        return sb.ToString();
    }

    private void CommitPartial()
    {
        if (string.IsNullOrWhiteSpace(_currentPartial)) return;
        _segments.Add(_currentPartial.Trim());
        _currentPartial = "";
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static void AppendSection(StringBuilder sb, string heading, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.Append("<b>").Append(heading).Append("</b>\n").Append(body.Trim()).Append("\n\n");
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = "Status: " + message;
    }

    private void SetLoading(bool on)
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(on);
    }

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf('\n');
        return i >= 0 ? s.Substring(0, i) : s;
    }
}
