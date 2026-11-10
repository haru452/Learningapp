using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 残しておいた答え (GeminiLater) を、アプリを開いている間に 2 分おき (とアプリに戻ったとき) に 1 件ずつ送る。
/// まだ混んでいたら、その回はそこでやめて次の回に。送れたら画面の上に知らせる (AppToast)。
/// </summary>
public class GeminiLaterRunner : MonoBehaviour
{
    private const float IntervalSeconds = 120f;
    private const float FirstDelaySeconds = 20f;   // アプリを開いた直後は、ほかの準備を先にする

    private static GeminiLaterRunner _instance;
    private float _next;
    private bool _running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (GeminiLater.Count > 0) Ensure();
    }

    /// <summary>送る係を用意する (答えを残したときに呼ぶ)</summary>
    public static void Ensure()
    {
        if (_instance != null || !Application.isPlaying) return;
        var go = new GameObject("GeminiLater");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<GeminiLaterRunner>();
    }

    private void Awake() => _next = Time.realtimeSinceStartup + FirstDelaySeconds;

    private void Update()
    {
        if (_running || Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + IntervalSeconds;
        _ = RunAsync();
    }

    // アプリに戻ってきたら、少し待ってから送ってみる
    private void OnApplicationFocus(bool focus)
    {
        if (focus && !_running) _next = Mathf.Min(_next, Time.realtimeSinceStartup + 5f);
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            foreach (var job in GeminiLater.Load())
            {
                if (this == null) return;
                if (GeminiLater.ShouldDrop(job, DateTime.UtcNow))
                {
                    GeminiLater.Remove(job);
                    Debug.LogWarning($"[Later] gave up on a saved {job.kind} answer (failed {job.failures} times, from {job.createdUtc}).");
                    AppToast.Show($"Could not grade a saved {GeminiLater.KindLabel(job.kind)} answer, so it was removed.");
                    continue;
                }

                try
                {
                    string done = await GeminiLaterJobs.RunAsync(job);
                    GeminiLater.Remove(job);
                    Debug.Log("[Later] sent a saved answer: " + done);
                    AppToast.Show(done);
                }
                catch (Exception e) when (GeminiLater.IsBusy(e))
                {
                    job.busyTries++;
                    GeminiLater.Update(job);
                    Debug.Log("[Later] Gemini is still busy. Trying again later: " + FirstLine(e.Message));
                    return;   // 混んでいるので、ほかのものも次の回に
                }
                catch (Exception e)
                {
                    job.failures++;
                    GeminiLater.Update(job);
                    Debug.LogWarning($"[Later] a saved {job.kind} answer failed ({job.failures}/{GeminiLater.MaxFailures}): {e.Message}");
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    private static string FirstLine(string s)
    {
        s = s ?? "";
        int i = s.IndexOf('\n');
        return i < 0 ? s : s.Substring(0, i);
    }
}
