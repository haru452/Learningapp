using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using LLMUnity;

/// <summary>
/// ステップ4用: LLM 単体の動作確認と、実機での性能計測。
///
/// ・起動時にモデルのダウンロード(初回のみ)とウォームアップを行う
/// ・テキスト入力 → 返信をストリーミング表示
/// ・「最初の文字が出るまで」「生成速度」「メモリ使用量」「発熱状態」を画面に表示
/// ・「ベンチマーク」ボタンで固定の3問を連続実行し、結果を一覧表示
///
/// LLMUnity v3 (LLM / LLMAgent) 用。Canvas 不要(OnGUI で描画)。
/// </summary>
public class LlmChatTestUI : MonoBehaviour
{
    [SerializeField] LLMAgent llmAgent;

    static readonly string[] Prompts =
    {
        "Hello! Please introduce yourself.",
        "What should I cook for dinner tonight?",
        "I'm feeling a little tired today.",
    };

    // 状態
    string input = Prompts[0];
    string status = "初期化中…";
    string reply = "";
    string benchLog = "";
    float downloadProgress;
    bool ready;
    bool busy;
    bool keepHistory = true;

    // 計測値
    float t0;
    float firstTokenSec = -1f;
    float totalSec;
    float tokensPerSec;
    int chunks;

    // 端末情報(1秒ごとに更新)
    string statsText = "";
    float nextStatsTime;

    // ------------------------------------------------------------------ 初期化

    static LlmChatTestUI instance;

    void Awake()
    {
        // 同じコンポーネントが複数あると画面が二重に重なるので、2つ目以降は無効化する
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[LlmChatTestUI] 重複しているため無効化しました: " + gameObject.name);
            enabled = false;
            Destroy(this);
            return;
        }
        instance = this;

        // Inspector で未設定なら、シーン内の LLMAgent を自動で探す
        if (llmAgent == null) llmAgent = FindFirstObjectByType<LLMAgent>();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    async void Start()
    {
        if (llmAgent == null)
        {
            status = "エラー: LLMAgent が Inspector で未設定です";
            return;
        }

        try
        {
            status = "モデルを準備中…(初回はダウンロードします)";
            await LLM.WaitUntilModelSetup(p => downloadProgress = p);

            status = "ウォームアップ中…";
            var tcs = new TaskCompletionSource<bool>();
            _ = llmAgent.Warmup(() => tcs.TrySetResult(true));
            await tcs.Task;

            ready = true;
            status = "準備完了";
        }
        catch (Exception e)
        {
            status = "エラー: " + e.Message;
            Debug.LogException(e);
        }
    }

    void Update()
    {
        if (Time.realtimeSinceStartup >= nextStatsTime)
        {
            nextStatsTime = Time.realtimeSinceStartup + 1f;
            statsText = DeviceStats.Describe();
        }
    }

    // ------------------------------------------------------------------ チャット

    void OnPartial(string replySoFar)
    {
        // v3 のコールバックには「ここまでの返信全体」が渡される
        if (chunks == 0) firstTokenSec = Time.realtimeSinceStartup - t0;
        chunks++;
        reply = replySoFar;
    }

    async Task RunChat(string message, bool addToHistory)
    {
        reply = "";
        chunks = 0;
        firstTokenSec = -1f;
        t0 = Time.realtimeSinceStartup;

        await llmAgent.Chat(message, OnPartial, null, addToHistory);

        totalSec = Time.realtimeSinceStartup - t0;
        float generationSec = totalSec - Mathf.Max(firstTokenSec, 0f);
        // コールバック回数 ≒ トークン数(概算)
        tokensPerSec = (chunks > 1 && generationSec > 0.01f) ? (chunks - 1) / generationSec : 0f;
    }

    async void Send(string message)
    {
        if (!ready || busy || string.IsNullOrWhiteSpace(message)) return;
        busy = true;
        status = "考え中…";
        try
        {
            await RunChat(message, keepHistory);
            status = "完了";
        }
        catch (Exception e)
        {
            status = "エラー: " + e.Message;
            Debug.LogException(e);
        }
        finally { busy = false; }
    }

    async void RunBenchmark()
    {
        if (!ready || busy) return;
        busy = true;
        benchLog = "";
        float sumFirst = 0f, sumRate = 0f;

        try
        {
            for (int i = 0; i < Prompts.Length; i++)
            {
                status = $"ベンチマーク {i + 1}/{Prompts.Length}…";
                await RunChat(Prompts[i], false); // 履歴に残さない(毎回同条件にする)

                sumFirst += firstTokenSec;
                sumRate += tokensPerSec;
                benchLog += $"#{i + 1}: 初回 {firstTokenSec:F2}s / {tokensPerSec:F1} tok/s / 全体 {totalSec:F1}s / {reply.Length}文字\n";
            }

            int n = Prompts.Length;
            benchLog += $"平均: 初回 {sumFirst / n:F2}s / {sumRate / n:F1} tok/s";
            Debug.Log("[LLM Benchmark]\n" + benchLog);
            status = "ベンチマーク完了";
        }
        catch (Exception e)
        {
            status = "エラー: " + e.Message;
            Debug.LogException(e);
        }
        finally { busy = false; }
    }

    // ------------------------------------------------------------------ 画面

    void OnGUI()
    {
        int fontSize = Mathf.Max(20, Screen.height / 60);
        var label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };
        var button = new GUIStyle(GUI.skin.button) { fontSize = fontSize, wordWrap = true };
        var field = new GUIStyle(GUI.skin.textField) { fontSize = fontSize, wordWrap = true };
        var toggle = new GUIStyle(GUI.skin.toggle) { fontSize = fontSize };

        float margin = Screen.width * 0.04f;
        float btnH = Screen.height * 0.06f;

        GUILayout.BeginArea(new Rect(margin, margin, Screen.width - margin * 2f, Screen.height - margin * 2f));

        string progress = (!ready && downloadProgress > 0f && downloadProgress < 1f)
            ? $" ({(int)(downloadProgress * 100)}%)" : "";
        GUILayout.Label("状態: " + status + progress, label);

        // 入力
        input = GUILayout.TextField(input, field, GUILayout.MinHeight(btnH));

        GUILayout.BeginHorizontal();
        GUI.enabled = ready && !busy;
        if (GUILayout.Button("送信", button, GUILayout.Height(btnH))) Send(input);
        if (GUILayout.Button("ベンチマーク", button, GUILayout.Height(btnH))) RunBenchmark();
        GUI.enabled = busy;
        if (GUILayout.Button("中止", button, GUILayout.Height(btnH))) llmAgent.CancelRequests();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        keepHistory = GUILayout.Toggle(keepHistory, "会話履歴に残す", toggle);

        // プリセット
        GUI.enabled = ready && !busy;
        foreach (var p in Prompts)
        {
            if (GUILayout.Button(p, button, GUILayout.Height(btnH))) { input = p; Send(p); }
        }
        GUI.enabled = true;

        // 返信
        GUILayout.Space(10f);
        GUILayout.Label("返信:\n" + reply, label);

        // 計測値
        string first = firstTokenSec >= 0f ? $"{firstTokenSec:F2} s" : "-";
        GUILayout.Label($"最初の文字まで: {first} / 速度: {tokensPerSec:F1} tok/s(概算) / 全体: {totalSec:F1} s", label);
        GUILayout.Label(statsText, label);

        if (!string.IsNullOrEmpty(benchLog)) GUILayout.Label("ベンチマーク結果:\n" + benchLog, label);

        GUILayout.EndArea();
    }
}

/// <summary>メモリ・発熱など端末の状態を文字列にする</summary>
static class DeviceStats
{
    static readonly string[] ThermalNames =
        { "なし", "軽度", "中程度", "重度", "危険", "緊急", "シャットダウン" };

    public static string Describe()
    {
        var sb = new StringBuilder();
        sb.Append($"端末RAM: {SystemInfo.systemMemorySize} MB");

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var dbg = new AndroidJavaClass("android.os.Debug"))
            {
                long pssKb = dbg.CallStatic<long>("getPss");   // アプリ全体(ネイティブ含む)のメモリ
                sb.Append($" / アプリ使用量(PSS): {pssKb / 1024} MB");
            }
            sb.Append(" / 熱状態: " + GetThermalStatus());
        }
        catch (Exception e)
        {
            sb.Append(" / (情報取得失敗: " + e.Message + ")");
        }
#endif
        return sb.ToString();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static string GetThermalStatus()
    {
        using (var ver = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            if (ver.GetStatic<int>("SDK_INT") < 29) return "非対応(Android 10 未満)";
        }

        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var pm = activity.Call<AndroidJavaObject>("getSystemService", "power"))
        {
            int s = pm.Call<int>("getCurrentThermalStatus");
            return (s >= 0 && s < ThermalNames.Length) ? ThermalNames[s] : s.ToString();
        }
    }
#endif
}