using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// 短い答えの練習 (Versant の Short Answer Questions)。
/// Start → Gemini が 10 問作る → 1 問ずつ読み上げ (音声だけ) → すぐ聞き取る → 正解か・問題文・正解を出す → Next … → まとめ。
/// まとめでクラウドに保存し、連続記録を進める (日記の画面の連続記録も数える)。
/// 音声認識が使えないとき (Unity エディターなど) は、文字で答える欄を出す。
/// </summary>
public partial class ShortAnswerScreen : MonoBehaviour
{
    private enum State { Idle, Loading, Asking, Listening, Result, Summary }

    private const string RecentKey = "answer.recent";
    private const int RecentMax = 60;
    private const string TtsModel = "gemini-3.8-flash-tts";
    private const string TtsVoice = "Leda";
    private const string QuestionModel = "gemini-flash-lite-latest";
    private const string QuestionFallbackModel = "gemini-3.8-flash";

    private State _state = State.Idle;
    private GeminiApiClient _questions;
    private RepeatAudioPlayer _player;
    private NativeSpeechRecognizer _stt;
    private SupabaseService _supabase;
    private CancellationTokenSource _cts;
    private List<ShortAnswerQuestion> _set = new List<ShortAnswerQuestion>();
    private readonly List<ShortAnswerResult> _results = new List<ShortAnswerResult>();
    private int _index;
    private string _partial = "";
    private bool _listening;

    private ShortAnswerQuestion Current => _index >= 0 && _index < _set.Count ? _set[_index] : null;

    private void Awake()
    {
        var s = ApiSecrets.Data;
        string key = ApiSecrets.Pick(s.geminiAnswerApiKey, s.geminiApiKey);
        var backups = BackupKeys();
        _questions = CreateQuestionClient();
        string ttsKey = ApiSecrets.Pick(s.geminiTtsApiKey, key);
        _player = gameObject.AddComponent<RepeatAudioPlayer>();
        _player.Configure(string.IsNullOrWhiteSpace(ttsKey) ? null : new GeminiApiClient(ttsKey, "gemini-3.8-flash", null, backups),
                          TtsModel, TtsVoice);
        _stt = FindObjectsOfType<NativeSpeechRecognizer>(true).FirstOrDefault();   // 全画面で 1 つを共有する
        _supabase = FindObjectOfType<SupabaseService>(true);
        if (_stt != null)
        {
            _stt.OnPartialResult += OnPartial;
            _stt.OnSessionEnded += OnSessionEnded;
        }
        ShowIdle();
    }

    private static string[] BackupKeys()
    {
        var s = ApiSecrets.Data;
        return new[] { s.geminiApiKey, s.geminiSummaryApiKey, s.geminiTopicApiKey, s.geminiVocabApiKey };
    }

    /// <summary>問題を作る Gemini (専用のキー、空なら日記のキー)。キーが無ければ null</summary>
    public static GeminiApiClient CreateQuestionClient()
    {
        var s = ApiSecrets.Data;
        string key = ApiSecrets.Pick(s.geminiAnswerApiKey, s.geminiApiKey);
        // 問題作りは簡単なので軽いモデルを先に使う (速く、混みにくい)。混んでいたら 3.8 flash に切り替える
        return string.IsNullOrWhiteSpace(key) ? null : new GeminiApiClient(key, QuestionModel, QuestionFallbackModel, BackupKeys());
    }

    private void OnDestroy()
    {
        if (_stt == null) return;
        _stt.OnPartialResult -= OnPartial;
        _stt.OnSessionEnded -= OnSessionEnded;
    }

    // ほかの画面へ移ったら、読み上げと聞き取りを止めて最初に戻す (まとめの保存は続ける)
    private void OnDisable()
    {
        _cts?.Cancel();
        _player?.Stop();
        if (_listening) { _listening = false; _stt?.Cancel(); }
        if (_state == State.Summary) return;
        // 答えなかった問題は作り置きに戻す (次の回で使う)
        if (_state != State.Idle && _set.Count > _results.Count) ShortAnswerStock.PutBack(_set.Skip(_results.Count));
        _set = new List<ShortAnswerQuestion>();
        ShowIdle();
    }

    // ------------------------------------------------------------
    // ボタン
    // ------------------------------------------------------------
    private void OnMain()
    {
        switch (_state)
        {
            case State.Idle:
            case State.Summary: _ = StartSetAsync(); break;
            case State.Listening: StopListening(); break;
            case State.Result: Next(); break;
        }
    }

    private void OnReplay()
    {
        if (_state == State.Listening) { _listening = false; _stt?.Cancel(); }
        if (_state == State.Listening || _state == State.Result) _ = AskAsync(listen: _state == State.Listening);
    }

    private void OnCheckTyped()
    {
        if (_state != State.Listening || _input == null) return;
        Judge(_input.text);
    }

    // ------------------------------------------------------------
    // 流れ
    // ------------------------------------------------------------
    private async Task StartSetAsync()
    {
        if (_questions == null) { Show("No Gemini key", "Add geminiApiKey (or geminiAnswerApiKey) to ApiSecrets.json."); return; }
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _results.Clear();
        _set = new List<ShortAnswerQuestion>();
        SetState(State.Loading);

        // 作り置きがあれば待たずに始める。足りなければ、作っている途中のものを待つ
        var set = ShortAnswerStock.Take(GeminiApiClient.ShortAnswerCount);
        if (set.Count < GeminiApiClient.ShortAnswerCount)
        {
            Show("Making questions...", "");
            await ShortAnswerStock.FillAsync(_questions, LoadRecent);
            if (ct.IsCancellationRequested) { ShortAnswerStock.PutBack(set); return; }
            set = ShortAnswerStock.Merge(set, ShortAnswerStock.Take(GeminiApiClient.ShortAnswerCount - set.Count));
        }
        if (set.Count == 0)
        {
            SetState(State.Idle);
            if (ShortAnswerStock.IsBusy(ShortAnswerStock.LastError))
                Show("Gemini is busy right now", "It's not your internet. The app keeps trying in the background, " +
                     "so wait a minute and tap \"Start\" again.");
            else
                Show("Could not make questions", "Check the internet and tap \"Start\" again.");
            return;
        }
        _set = set;
        SaveRecent(_set.Select(q => q.Question));
        _ = ShortAnswerStock.FillAsync(_questions, LoadRecent);   // 使った分を裏で作り足す
        _index = 0;
        await AskAsync(listen: true);
    }

    /// <summary>
    /// 今の問題を読み上げる (文字は出さない)。listen なら、そのまま聞き取る。
    /// listen でなければ (答えたあとの Replay) 聞き直すだけで、答えの表示はそのまま
    /// </summary>
    private async Task AskAsync(bool listen)
    {
        var q = Current;
        if (q == null) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        if (listen)
        {
            SetState(State.Asking);
            Show("Listen...", "Answer in a word or a short phrase.");
        }
        try
        {
            await _player.PlayAsync(new RepeatSentence { Text = q.Question, Source = "answer" }, false, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogWarning("[Answer] could not play the question: " + e.Message);
            Show("Could not play the question", q.Question);   // 流せなければ文を見せる
        }
        if (ct.IsCancellationRequested) return;
        if (listen && _index + 1 < _set.Count) _ = _player.PrepareAsync(new RepeatSentence { Text = _set[_index + 1].Question, Source = "answer" });
        if (listen) await StartListeningAsync();
    }

    private async Task StartListeningAsync()
    {
        SetState(State.Listening);
        _partial = "";
        if (_stt == null || !_stt.IsAvailable)
        {
            Show("Your answer?", "Type it below.");   // Unity エディターなど
            if (_input != null) { _input.text = ""; _input.ActivateInputField(); }
            return;
        }
        if (!await MicAllowedAsync()) { SetState(State.Result); Show("Microphone is off", "Allow the microphone, then tap \"Replay\"."); return; }
        if (!await _stt.RequestSpeechPermissionAsync() || !_stt.StartListening("en-US"))
        {
            Show("Could not start listening", "Tap \"Replay\" to try again.");
            SetState(State.Result);
            return;
        }
        _listening = true;
        Show("Answer now...", "");
    }

    private void StopListening()
    {
        if (!_listening) return;
        _stt.StopListening();   // 結果は OnSessionEnded に来る
        Show("...", _partial);
    }

    private void OnPartial(string text)
    {
        if (!_listening) return;
        _partial = text ?? "";
        Show("Answer now...", _partial);
    }

    private void OnSessionEnded(string text, string error)
    {
        if (!_listening) return;   // ほかの画面の聞き取り
        _listening = false;
        string heard = !string.IsNullOrWhiteSpace(text) ? text.Trim() : _partial;
        Judge(heard);
    }

    private void Judge(string heard)
    {
        var q = Current;
        if (q == null || _state != State.Listening) return;
        bool ok = ShortAnswerJudge.IsCorrect(heard, q.Answers);
        _results.Add(new ShortAnswerResult { Question = q.Question, Answers = q.Answers, Heard = heard ?? "", Correct = ok });
        SetState(State.Result);
        var sb = new StringBuilder();
        sb.Append("<color=#FFFFFF>").Append(Esc(q.Question)).Append("</color>\n\n");
        sb.Append("You said: ").Append(string.IsNullOrWhiteSpace(heard) ? "(nothing)" : Esc(heard)).Append('\n');
        sb.Append("Answer: <color=#22D3EE>").Append(Esc(string.Join(" / ", q.Answers))).Append("</color>");
        Show(ok ? "<color=#4ADE80>Correct</color>" : "<color=#F87171>Not quite</color>", sb.ToString());
    }

    private void Next()
    {
        _index++;
        if (_index < _set.Count) { _ = AskAsync(listen: true); return; }
        _ = FinishAsync();
    }

    private async Task FinishAsync()
    {
        SetState(State.Summary);
        int correct = _results.Count(r => r.Correct);
        var sb = new StringBuilder();
        foreach (var r in _results)
            sb.Append(r.Correct ? "<color=#4ADE80>OK</color>  " : "<color=#F87171>NG</color>  ").Append(Esc(r.Question))
              .Append("\n    <color=#22D3EE>").Append(Esc(r.Answers.FirstOrDefault() ?? "")).Append("</color>")
              .Append(r.Correct ? "" : "  (you: " + Esc(string.IsNullOrWhiteSpace(r.Heard) ? "-" : r.Heard) + ")").Append("\n\n");
        Show(correct + " / " + _results.Count + " correct", sb.ToString());
        if (_results.Count == 0) return;

        var diary = FindObjectOfType<DiaryManager>(true);
        if (diary != null) diary.CountOtherPracticeAsStudyDay();   // 端末の連続記録とリマインダー
        if (_supabase == null || !_supabase.IsConfigured) return;
        try
        {
            if (!await _supabase.EnsureSignedInAsync()) return;
            await _supabase.CompleteShortAnswerAsync(_results.ToList(), DateTime.UtcNow);
            if (diary != null) diary.RefreshStreakFromCloud();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Answer] could not save the results to the cloud: " + e.Message);
        }
    }

    // ------------------------------------------------------------
    // 表示
    // ------------------------------------------------------------
    private void ShowIdle()
    {
        SetState(State.Idle);
        Show("Short answer", "Listen to a short question and answer in a word or two, like the Versant test.\n\n" +
             "\"What do you use to open a door?\"  →  <color=#22D3EE>a key</color>\n\n" +
             GeminiApiClient.ShortAnswerCount + " questions. You only hear them. The text comes after you answer.");
    }

    private void SetState(State s)
    {
        _state = s;
        if (_mainLabel != null)
            _mainLabel.text = s == State.Listening ? "Stop" : s == State.Result ? (_index + 1 < _set.Count ? "Next" : "See results")
                            : s == State.Summary ? "Start again" : "Start";
        if (_mainButton != null) _mainButton.interactable = s != State.Loading && s != State.Asking;
        if (_replayButton != null) _replayButton.gameObject.SetActive(s == State.Listening || s == State.Result);
        bool typing = s == State.Listening && (_stt == null || !_stt.IsAvailable);
        if (_inputRow != null) _inputRow.SetActive(typing);
        if (_progress != null)
            _progress.text = s == State.Idle || s == State.Loading || _set.Count == 0 ? ""
                : s == State.Summary ? "Done" : "Question " + (_index + 1) + " / " + _set.Count + "   ·   " + _results.Count(r => r.Correct) + " correct";
    }

    private void Show(string main, string detail)
    {
        if (_mainText != null) _mainText.text = main;
        if (_detailText != null) _detailText.text = detail;
    }

    private static string Esc(string s) => (s ?? "").Replace("<", "<​");

    private static async Task<bool> MicAllowedAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone)) return true;
        Permission.RequestUserPermission(Permission.Microphone);
        for (int i = 0; i < 100 && !Permission.HasUserAuthorizedPermission(Permission.Microphone); i++) await Task.Delay(100);
        return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
        await Task.CompletedTask;
        return true;
#endif
    }

    // 最近出した問題 (同じ問題を出さないように Gemini に伝える)
    private static List<string> LoadRecent() =>
        PlayerPrefs.GetString(RecentKey, "").Split('\n').Where(x => x.Length > 0).ToList();

    private static void SaveRecent(IEnumerable<string> added)
    {
        var list = added.Concat(LoadRecent()).Distinct().Take(RecentMax);
        PlayerPrefs.SetString(RecentKey, string.Join("\n", list.Select(x => x.Replace("\n", " "))));
        PlayerPrefs.Save();
    }
}
