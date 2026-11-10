using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// RepeatPracticeManager の続き: 4. 結果、5. まとめ・保存、表示。
/// </summary>
public partial class RepeatPracticeManager
{
    // ------------------------------------------------------------
    // 4. 結果
    // ------------------------------------------------------------
    private void ShowResult(string heard)
    {
        var s = Current;
        var match = ReadAloudMatcher.Match(s.Text, heard);
        _results[_index] = new RepeatResult
        {
            sentence = s.Text,
            heard = heard,
            accuracy = match.AccuracyPercent,
            replays = _replays,
            source = string.IsNullOrEmpty(s.Source) ? "tatoeba" : s.Source,
            voice = player.VoiceOf(s),
            tatoebaId = s.TatoebaId ?? 0,
            audioId = s.AudioId ?? 0,
        };

        ShowSentence(s.Text);
        var sb = new StringBuilder();
        foreach (var w in match.Words)
            sb.Append("<color=").Append(w.Matched ? MatchColor : MissColor).Append('>').Append(Escape(w.Text)).Append("</color> ");
        sb.Append("\n<b>").Append(match.AccuracyPercent).Append("%</b>")
          .Append("  <size=70%>(heard: ").Append(Escape(heard)).Append(")</size>");
        SetText(resultText, sb.ToString());
        SetText(headerText, "Result (" + (_index + 1) + " / " + _set.Count + ")");
        SetState(State.Result);
        SetStatus(match.AccuracyPercent >= MissedThreshold
            ? "Nice! Tap \"" + NextLabel + "\"."
            : "Listen again and \"Try again\", or tap \"" + NextLabel + "\".");
    }

    private string NextLabel => _index + 1 < _set.Count ? "Next" : "Finish";

    // ------------------------------------------------------------
    // 5. まとめ・保存
    // ------------------------------------------------------------
    private async Task FinishSessionAsync()
    {
        var results = Enumerable.Range(0, _set.Count).Where(_results.ContainsKey).Select(i => _results[i]).ToList();
        SetState(State.Summary);
        ShowSentence(null);
        SetText(creditText, "");

        if (results.Count == 0)
        {
            SetText(headerText, "Repeat");
            SetText(resultText, "");
            SetStatus("No sentences were said. Tap \"Start\" to try again.");
            return;
        }

        int average = (int)Math.Round(results.Average(r => r.accuracy), MidpointRounding.AwayFromZero);
        var sb = new StringBuilder();
        sb.Append("<b>Average: ").Append(average).Append("%</b>\n\n");
        foreach (var r in results)
            sb.Append(r.accuracy >= MissedThreshold ? "<color=" + MatchColor + ">" : "<color=" + MissColor + ">")
              .Append(r.accuracy.ToString().PadLeft(3)).Append("%</color>  ").Append(Escape(r.sentence)).Append('\n');
        var missed = results.Where(r => r.accuracy < MissedThreshold).ToList();
        if (missed.Count > 0) sb.Append("\n<size=80%>Sentences under ").Append(MissedThreshold).Append("% will come back next time.</size>");
        // 言えなかった単語のつづりから、気をつける音を出す (多い順に最大 3)
        var soundHits = SoundGuesser.FromMissedWords(results.SelectMany(r =>
            ReadAloudMatcher.Match(r.sentence, r.heard).Words.Where(w => !w.Matched).Select(w => w.Text)));
        var watch = SoundGuesser.Top(soundHits, 3);
        if (watch.Count > 0)
            sb.Append("\n<size=80%>Watch out: ").Append(string.Join(", ", watch.Select(c => SoundTypes.Label(c, null)))).Append("</size>");
        SetText(headerText, "Summary");
        SetText(resultText, sb.ToString().TrimEnd());
        SetStatus("Saving...");

        RecentRepeatLog.Add(_set.Where(s => s.Source == "tatoeba").Select(s => RepeatSetBuilder.Key(s.Text)));
        var diary = FindDiary();
        if (diary != null) diary.CountOtherPracticeAsStudyDay();   // 端末内の連続記録と夜のリマインダー

        DateTime takenAt = DateTime.UtcNow;
        string saveNote = await SaveAsync(results, takenAt);
        string discordError = await SendToDiscordAsync(results);
        if (supabase != null) _ = SaveSoundsAndPracticeAsync(soundHits, takenAt);   // 画面は待たない
        if (this == null) return;
        SetStatus((saveNote ?? "Saved.") + (discordError == null ? "" : "  Discord failed: " + discordError) +
                  "  Tap \"Start\" for 5 more.");
    }

    // 失敗しても結果は画面に残る。ログインしていない・送れないときは端末に一時保存し、ログイン時にまとめて送る
    private async Task<string> SaveAsync(List<RepeatResult> results, DateTime takenAt)
    {
        if (supabase == null || !supabase.IsConfigured) return "Done.";
        if (!await supabase.EnsureSignedInAsync())
        {
            PendingRepeatStore.Add(results, takenAt);
            return "Saved on this phone (sign in to upload).";
        }
        try
        {
            await supabase.SaveRepeatResultsAsync(results, takenAt);
            if (PendingRepeatStore.Count > 0) _ = supabase.UploadPendingRepeatResultsAsync();
            var diary = FindDiary();
            if (diary != null) diary.RefreshStreakFromCloud();
            return "Saved.";
        }
        catch (SupabaseService.SupabaseException e) when (e.StatusCode == 400)
        {
            Debug.LogWarning("[Repeat] results could not be saved (invalid data): " + e.Message);
            return "Could not save.";
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Repeat] could not save. Keeping them to upload later: " + e.Message);
            PendingRepeatStore.Add(results, takenAt);
            return "Saved on this phone (will upload later).";
        }
    }

    private async Task<string> SendShadowToDiscordAsync(YouTubeSearchClient.Video video, int average, string advice,
                                                        List<ShadowScorer.SentenceScore> scores)
    {
        if (!sendToDiscord || !_discord.IsConfigured) return null;
        try
        {
            await _discord.SendShadowResultsAsync(DateTime.Now, video.title, video.WatchUrl, average, advice, scores);
            return null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] Discord failed: " + e.Message);
            return FirstLine(e.Message);
        }
    }

    // 苦手な音を保存してから、今日の発音練習を送るか判断する (失敗は Console だけ)
    private async Task SaveSoundsAndPracticeAsync(List<SoundHit> hits, DateTime takenAt)
    {
        try
        {
            await supabase.SavePronunciationIssuesAsync(hits, 1, "repeat", takenAt);
            await DailySoundPractice.TryAsync(supabase, ApiSecrets.Data.youtubeApiKey, PronunciationDiscord());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Repeat] sounds: " + e.Message);
        }
    }

    // 今日の発音練習を送る、発音用の Discord (お題の発音チェックと同じチャンネル)
    private DiscordClient _pronDiscord;
    private DiscordClient PronunciationDiscord()
    {
        if (_pronDiscord != null) return _pronDiscord;
        var s = ApiSecrets.Data;
        string token = string.IsNullOrWhiteSpace(s.pronunciationDiscordChannelId) ? "" : s.discordBotToken;
        return _pronDiscord = new DiscordClient(s.pronunciationDiscordWebhookUrl, token, s.pronunciationDiscordChannelId);
    }

    private async Task<string> SendToDiscordAsync(List<RepeatResult> results)
    {
        if (!sendToDiscord || !_discord.IsConfigured) return null;
        try
        {
            await _discord.SendRepeatResultsAsync(DateTime.Now, results);
            return null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Repeat] Discord failed: " + e.Message);
            return FirstLine(e.Message);
        }
    }

    // ------------------------------------------------------------
    // 表示
    // ------------------------------------------------------------
    private void SetState(State state)
    {
        _state = state;
        bool listenable = state == State.Ready || state == State.Result;
        if (mainButtonLabel != null)
            mainButtonLabel.text = state == State.Result ? NextLabel
                : state == State.Summary ? "Start" : state == State.Idle ? "Start" : "...";
        mainButton.interactable = state == State.Idle || state == State.Summary || state == State.Result;
        if (againButton != null) { againButton.gameObject.SetActive(state != State.Idle && state != State.Summary); againButton.interactable = listenable; }
        if (slowButton != null) { slowButton.gameObject.SetActive(state != State.Idle && state != State.Summary); slowButton.interactable = listenable; }
        if (speakButton != null)
        {
            speakButton.gameObject.SetActive(state != State.Idle && state != State.Summary);
            speakButton.interactable = listenable || state == State.Speaking;
        }
        if (speakButtonLabel != null)
            speakButtonLabel.text = state == State.Speaking ? "Stop" : state == State.Result ? "Try again" : "Speak";
        RefreshModeButtons();
    }

    private void ShowCredit(RepeatSentence s)
    {
        if (creditText == null) return;
        creditText.text = s.HasHumanAudio && !string.IsNullOrEmpty(s.Author)
            ? "Sentences & audio: Tatoeba (tatoeba.org) — voice: " + Escape(s.Author) + " (" + Escape(s.License) + ")"
            : s.HasHumanAudio ? "Sentences & audio: Tatoeba (tatoeba.org)" : _note;
    }

    // 英文は言い返した後だけ、枠で囲んで出す
    private GameObject _box;

    private void ShowSentence(string text)
    {
        if (sentenceText == null) return;
        sentenceText.text = text == null ? "" : Escape(text);
        if (_box == null && text != null) _box = CreateBox(sentenceText);
        if (_box != null) _box.SetActive(text != null);
    }

    private static GameObject CreateBox(TMP_Text target)
    {
        var go = new GameObject("SentenceBox", typeof(RectTransform));
        var box = (RectTransform)go.transform;
        box.SetParent(target.transform, false);
        box.anchorMin = Vector2.zero; box.anchorMax = Vector2.one;
        box.offsetMin = Vector2.zero; box.offsetMax = Vector2.zero;
        var fill = go.AddComponent<Image>();
        fill.color = new Color(1f, 1f, 1f, 0.06f);
        fill.raycastTarget = false;
        void Edge(Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
        {
            var e = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)e.transform;
            rt.SetParent(box, false);
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
            var img = e.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.75f);
            img.raycastTarget = false;
        }
        Edge(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 3));
        Edge(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 3));
        Edge(new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(3, 0));
        Edge(new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(3, 0));
        target.margin = new Vector4(24, 16, 24, 16);
        return go;
    }

    private DiaryManager FindDiary()
    {
        if (_diary == null) _diary = FindObjectOfType<DiaryManager>(true);
        return _diary;
    }

    private void SetStatus(string s) => SetText(statusText, s);
    private static void SetText(TMP_Text t, string s) { if (t != null) t.text = s ?? ""; }
    private void SetLoading(bool on) { if (loadingIndicator != null) loadingIndicator.SetActive(on); }

    // TMP のタグ (<b> など) として読まれないように
    private static string Escape(string s) => (s ?? "").Replace("<", "<​");

    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = s.IndexOf('\n');
        return i >= 0 ? s.Substring(0, i) : s;
    }
}
