using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TopicPracticeManager の続き: 3. 終了 → 採点 と 4. 言い直し。
/// </summary>
public partial class TopicPracticeManager
{
    // ------------------------------------------------------------
    // 3. 終了 → 採点
    // ------------------------------------------------------------
    private async Task FinishSpeakingAsync()
    {
        if (_state != State.Speaking || _finishing) return;
        _finishing = true;
        _busy = true;
        if (timerText != null) timerText.text = "";
        SetLoading(true);
        SetStatus("Finishing...");
        RefreshButtons();

        // 録音した答えは、文字起こしせずにそのまま採点に送る (Gemini が聞いて文字起こしもする)
        if (!_useNative)
        {
            _listening = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            byte[] wav = recorder.IsRecording ? recorder.StopRecording() : null;
            _busy = false;
            if (wav == null || wav.Length == 0)
            {
                SetLoading(false);
                SetState(State.Idle);
                SetStatus("Could not record your answer. Tap \"New Topic\" to try again.");
                return;
            }
            await GradeAsync(null, wav);
            return;
        }

        string text;
        try
        {
            text = await StopListeningAndGetTextAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            text = "";
        }
        if (this == null) return;
        if (transcriptText != null) transcriptText.text = text;

        _busy = false;
        if (CountWords(text) < 2)
        {
            SetLoading(false);
            SetState(State.Idle);
            SetStatus("Please speak a little more. Tap \"New Topic\" to try again.");
            return;
        }

        await GradeAsync(text);
    }

    /// <summary>答えを採点する。wav があれば録音を送り、文字起こしも Gemini に任せる (text は使わない)</summary>
    private async Task GradeAsync(string text, byte[] wav = null)
    {
        _busy = true;
        SetState(State.Grading);
        SetLoading(true);
        SetStatus(wav != null ? "Listening to your answer and grading it... (this can take up to a minute)" : "Grading your answer...");
        if (retryButton != null) retryButton.gameObject.SetActive(false);

        try
        {
            int seconds = TopicCatalog.SecondsFor(_topic.kind);
            var grade = wav != null
                ? await _gemini.GradeTopicAudioAsync(_topic.kind, _topic.topic, seconds, wav, _focusTypes)
                : await _gemini.GradeTopicAsync(_topic.kind, _topic.topic, seconds, text, _focusTypes);
            if (this == null) return;
            _retryText = null;
            _retryWav = null;

            if (wav != null)
            {
                text = grade.transcript;
                if (transcriptText != null) transcriptText.text = text;
                if (CountWords(text) < 2)
                {
                    SetState(State.Idle);
                    SetStatus("Please speak a little more. Tap \"New Topic\" to try again.");
                    return;
                }
            }

            var counts = MistakeTypes.Count(grade.mistakes);
            ShowGrade(grade, text, counts);
            SaveLastCounts(counts);
            SetUpReadAloud(grade.readAloud);

            _ticket = new SaveTicket();
            SaveResultToCloudAsync(_ticket, _topic, seconds, grade, text);   // 待たない
            CountAsStudyDay();

            string discordError = await SendToDiscordAsync(grade);
            SetState(State.Idle);
            SetStatus(discordError == null
                ? "Done! Read the corrected sentences aloud, or tap \"New Topic\"."
                : "Graded, but Discord failed: " + discordError);
            NotifyIfHidden(grade.score);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (wav != null) _retryWav = wav;   // 録音は残しておく (答えを失わないように)
            else _retryText = text;
            SetState(State.Idle);
            SetStatus("Error: " + FirstLine(e.Message) + "  (Tap Retry to grade the same answer again)");
            NotifyIfHidden(null);
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            RefreshButtons();
        }
    }

    private void ShowGrade(GeminiApiClient.TopicGrade g, string spoken, Dictionary<string, int> counts)
    {
        if (resultText == null) return;
        var sb = new StringBuilder();
        sb.Append("<b>Score: ").Append(g.score).Append(" / 100</b>\n")
          .Append("<size=70%>Content ").Append(g.contentScore).Append('/').Append(GeminiApiClient.TopicContentMax)
          .Append("  ·  Organization ").Append(g.organizationScore).Append('/').Append(GeminiApiClient.TopicOrganizationMax)
          .Append("  ·  Grammar & vocabulary ").Append(g.languageScore).Append('/').Append(GeminiApiClient.TopicLanguageMax)
          .Append("</size>\n\n");

        // 苦手のチェック: 今回と前回の回数
        if (_focusTypes.Count > 0)
        {
            var last = LoadLastCounts();
            sb.Append("<b>Focus check</b>\n");
            foreach (string t in _focusTypes)
            {
                counts.TryGetValue(t, out int now);
                string before = last.TryGetValue(t, out int prev) ? prev.ToString() : "-";
                sb.Append(MistakeTypes.Label(t)).Append(": this time ").Append(now)
                  .Append(" / last time ").Append(before).Append('\n');
            }
            sb.Append('\n');
        }

        AppendSection(sb, "Good points", g.goodPoints);
        AppendSection(sb, "To improve", g.fixPoints);
        string corrected = string.IsNullOrWhiteSpace(g.corrected) ? "" : TextDiff.UnderlineChanges(spoken, g.corrected, out _);
        AppendSection(sb, "Corrected", corrected);
        AppendSection(sb, "More natural", g.natural);
        if (g.phrases != null && g.phrases.Length > 0)
            AppendSection(sb, "Useful phrases", "- " + string.Join("\n- ", g.phrases));
        resultText.text = sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------
    // 4. 言い直し
    // ------------------------------------------------------------
    private void SetUpReadAloud(string[] sentences)
    {
        _readSentences = sentences ?? new string[0];
        _readIndex = 0;
        _readScores.Clear();
        _lastPronComment = "";
        _lastReadResult = "";
        RefreshReadAloud();
    }

    private bool ReadAloudFinished => _readIndex >= _readSentences.Length;

    private async void OnReadAloudClicked()
    {
        if (_busy || _state != State.Idle || _recordingPron || _readSentences.Length == 0) return;

        if (_reading) { await StopReadingAsync(); return; }

        // 1 文読み終わった後のボタンは「次の文へ」
        if (!string.IsNullOrEmpty(_lastReadResult))
        {
            _readIndex++;
            _lastReadResult = "";
            _lastPronComment = "";
            RefreshReadAloud();
            if (ReadAloudFinished) FinishReadAloud();
            return;
        }

        if (ReadAloudFinished) return;
        if (!await StartListeningAsync()) return;
        _reading = true;
        SetStatus("Read the sentence aloud, then tap Stop.");
        RefreshReadAloud();
    }

    private async Task StopReadingAsync()
    {
        if (!_reading || _finishing) return;
        _finishing = true;
        string heard;
        try
        {
            heard = await StopListeningAndGetTextAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            heard = "";
        }
        finally
        {
            _finishing = false;
            _reading = false;
        }
        if (this == null || ReadAloudFinished) return;

        // 何も聞き取れなかった (通信エラー・無音など) ときは 0% にせず、もう一度読んでもらう
        if (string.IsNullOrWhiteSpace(heard))
        {
            SetStatus("I couldn't hear you. Tap \"Read aloud\" to try again.");
            RefreshReadAloud();
            return;
        }

        var match = ReadAloudMatcher.Match(_readSentences[_readIndex], heard);
        _readScores.Add(match.AccuracyPercent);
        // 言えなかった単語のつづりから苦手な音を数える (重み 1) → 今日の発音練習 (待たない)
        SaveSoundsAndPractice(SoundGuesser.FromMissedWords(match.Words.Where(w => !w.Matched).Select(w => w.Text)), 1, "read_aloud");
        var sb = new StringBuilder();
        foreach (var w in match.Words)
            sb.Append("<color=").Append(w.Matched ? MatchColor : MissColor).Append('>').Append(w.Text).Append("</color> ");
        sb.Append("\n<b>").Append(match.AccuracyPercent).Append("%</b>");
        if (!string.IsNullOrWhiteSpace(heard)) sb.Append("  <size=70%>(heard: ").Append(heard).Append(")</size>");
        _lastReadResult = sb.ToString();
        SetStatus("Tap \"Next\" for the next sentence, or check your pronunciation.");
        RefreshReadAloud();
    }

    // 苦手な音を保存してから、今日の発音練習を送るか判断する (画面は待たない。失敗は Console だけ)
    private async void SaveSoundsAndPractice(List<SoundHit> hits, int weight, string source)
    {
        try
        {
            if (supabase == null) return;
            await supabase.SavePronunciationIssuesAsync(hits, weight, source, DateTime.UtcNow);
            await DailySoundPractice.TryAsync(supabase, ApiSecrets.Data.youtubeApiKey, _pronDiscord);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Topic] sounds: " + e.Message);
        }
    }

    private void FinishReadAloud()
    {
        if (_readScores.Count == 0) return;
        int average = (int)Math.Round(_readScores.Average(), MidpointRounding.AwayFromZero);
        if (readAloudText != null) readAloudText.text = "<b>Read-aloud accuracy: " + average + "%</b>";
        SetStatus("Read-aloud finished! Tap \"New Topic\" for another topic.");

        var ticket = _ticket;
        if (ticket == null) return;
        ticket.ReadAloudAccuracy = average;
        if (ticket.Id > 0) _ = SaveReadAloudAsync(ticket.Id, average);
        else if (ticket.PendingLocalId != null) PendingTopicStore.SetReadAloud(ticket.PendingLocalId, average);
        // どちらでもない (保存中) ときは、保存が終わったところで入れる
    }

    private async Task SaveReadAloudAsync(long id, int accuracy)
    {
        try { await supabase.SetTopicReadAloudAsync(id, accuracy); }
        catch (Exception e) { Debug.LogWarning("[TopicPractice] could not save read-aloud accuracy: " + e.Message); }
    }

    private async void OnPronunciationClicked()
    {
        if (_busy || _state != State.Idle || _reading || ReadAloudFinished) return;

        if (_recordingPron) { await StopPronunciationAsync(); return; }

        if (!await recorder.RequestPermissionAsync())
        {
            SetStatus("Microphone permission denied. Please allow it in Settings.");
            return;
        }
        if (!recorder.StartRecording())
        {
            SetStatus("Could not start recording.");
            return;
        }
        _recordingPron = true;
        _pronEnd = Time.realtimeSinceStartup + MaxPronunciationSeconds;
        SetStatus("Read the sentence aloud, then tap Stop.");
        RefreshReadAloud();
    }

    private async Task StopPronunciationAsync()
    {
        if (!_recordingPron) return;
        _recordingPron = false;
        byte[] wav = recorder.IsRecording ? recorder.StopRecording() : null;
        RefreshReadAloud();
        if (wav == null || ReadAloudFinished) return;

        _busy = true;
        SetLoading(true);
        SetStatus("Checking your pronunciation...");
        RefreshButtons();
        try
        {
            string sentence = _readSentences[_readIndex];
            var feedback = await _gemini.CheckPronunciationAsync(sentence, wav);
            _lastPronComment = feedback.Comment;
            if (feedback.Sounds.Count > 0)
                _lastPronComment += "\nSound: " + string.Join(", ", feedback.Sounds.Select(x => SoundTypes.Label(x.Code, x.Label)));
            SetStatus("Pronunciation checked.");
            SendPronunciationToDiscord(sentence, feedback.Comment);   // 待たない
            SaveSoundsAndPractice(feedback.Sounds, 3, "check");      // Gemini の判定 (重み 3)
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Error: " + FirstLine(e.Message));
        }
        finally
        {
            SetLoading(false);
            _busy = false;
            RefreshButtons();
            RefreshReadAloud();
        }
    }

    // 言い直しの間は、お題 (質問) の場所に読む文を出す (どこを読めばいいか分かるように)
    private bool _showingSentenceAtTop;

    // 読む文を囲む枠 (お題の文字の上に、実行時に作る)
    private GameObject _sentenceBox;
    private Vector4 _topicMargin;
    private static readonly Color SentenceBoxColor = new Color(1f, 1f, 1f, 0.75f);
    private const float SentenceBoxLine = 3f;

    private void SetSentenceBox(bool on)
    {
        if (topicText == null) return;
        if (_sentenceBox == null)
        {
            if (!on) return;
            _topicMargin = topicText.margin;
            _sentenceBox = new GameObject("ReadAloudBox", typeof(RectTransform));
            var box = (RectTransform)_sentenceBox.transform;
            box.SetParent(topicText.transform, false);
            box.anchorMin = Vector2.zero;
            box.anchorMax = Vector2.one;
            box.offsetMin = Vector2.zero;
            box.offsetMax = Vector2.zero;

            var fill = _sentenceBox.AddComponent<Image>();      // うっすら背景
            fill.color = new Color(1f, 1f, 1f, 0.06f);
            fill.raycastTarget = false;

            // 上下左右の線
            AddEdge(box, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, SentenceBoxLine));
            AddEdge(box, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, SentenceBoxLine));
            AddEdge(box, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(SentenceBoxLine, 0));
            AddEdge(box, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(SentenceBoxLine, 0));
        }
        _sentenceBox.SetActive(on);
        topicText.margin = on ? new Vector4(24, 16, 24, 16) : _topicMargin;   // 文字が枠に付かないように
    }

    private static void AddEdge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)edge.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = edge.GetComponent<Image>();
        img.color = SentenceBoxColor;
        img.raycastTarget = false;
    }

    private void ShowSentenceAtTop()
    {
        _showingSentenceAtTop = true;
        SetSentenceBox(true);
        if (kindText != null) kindText.text = "Read aloud  ·  " + (_readIndex + 1) + " / " + _readSentences.Length;
        if (topicText != null) topicText.text = _readSentences[_readIndex];
        if (hintText != null)
            hintText.text = "Read this sentence aloud. \"Read aloud\" checks the words, " +
                            "\"Pronunciation check\" gives pronunciation tips.";
    }

    private void RestoreTopicAtTop()
    {
        if (!_showingSentenceAtTop) return;
        _showingSentenceAtTop = false;
        SetSentenceBox(false);
        if (_topic != null) ShowTopic();
        else ClearTopic();
    }

    private void RefreshReadAloud()
    {
        bool any = _readSentences.Length > 0;
        if (readAloudGroup != null) readAloudGroup.SetActive(any);
        if (!any || ReadAloudFinished) RestoreTopicAtTop();
        if (!any) return;
        if (ReadAloudFinished)
        {
            // 全部読み終わった (readAloudText には FinishReadAloud が平均を出している)
            if (readAloudButtonLabel != null) readAloudButtonLabel.text = "Done";
            if (readAloudButton != null) readAloudButton.interactable = false;
            if (pronunciationButton != null) pronunciationButton.interactable = false;
            return;
        }

        if (readAloudText != null)
        {
            // 読む文は上 (お題の場所) に出すので、ここには結果だけ
            var sb = new StringBuilder();
            sb.Append("<b>Read aloud (").Append(_readIndex + 1).Append('/').Append(_readSentences.Length).Append(")</b>");
            if (string.IsNullOrEmpty(_lastReadResult) && string.IsNullOrEmpty(_lastPronComment))
                sb.Append("\nRead the sentence shown at the top.");
            if (!string.IsNullOrEmpty(_lastReadResult)) sb.Append("\n").Append(_lastReadResult);
            if (!string.IsNullOrEmpty(_lastPronComment)) sb.Append("\n\n<i>").Append(_lastPronComment).Append("</i>");
            readAloudText.text = sb.ToString();
        }
        ShowSentenceAtTop();

        if (readAloudButtonLabel != null)
            readAloudButtonLabel.text = _reading ? "Stop"
                : !string.IsNullOrEmpty(_lastReadResult) ? (_readIndex + 1 < _readSentences.Length ? "Next" : "Finish")
                : "Read aloud";
        if (readAloudButton != null) readAloudButton.interactable = !_busy && !_recordingPron;
        if (pronunciationButtonLabel != null)
            pronunciationButtonLabel.text = _recordingPron ? "Stop" : "Pronunciation check";
        if (pronunciationButton != null) pronunciationButton.interactable = !_busy && !_reading;
    }
}
