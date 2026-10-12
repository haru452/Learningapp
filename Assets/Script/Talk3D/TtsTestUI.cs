using System;
using UnityEngine;

/// <summary>
/// ステップ2用のテスト画面。
/// ・文字を入力して「読み上げ」を押すと TTS が喋る
/// ・TTS の IsSpeaking を毎フレーム PseudoLipSync に反映し、喋っている間だけ口が動く
/// Canvas 不要(OnGUI で描画)。
/// </summary>
public class TtsTestUI : MonoBehaviour
{
    [SerializeField] PseudoLipSync lipSync;

    [Header("TTS の言語 (例: en/US, en/GB, ja/JP)")]
    [SerializeField] string ttsLanguage = "en";
    [SerializeField] string ttsCountry = "US";

    [TextArea]
    [SerializeField] string inputText = "Hello! Nice to meet you. How is your day going?";

    static readonly string[] Presets =
    {
        "Hello! Nice to meet you.",
        "It's a beautiful day today. Would you like to go for a walk?",
        "Hmm, let me think about that for a moment.",
    };

    ITextToSpeech tts;

    void Awake()
    {
        if (lipSync == null) lipSync = FindFirstObjectByType<PseudoLipSync>();

#if UNITY_ANDROID && !UNITY_EDITOR
        tts = new AndroidTts(ttsLanguage, ttsCountry);
#else
        tts = new EditorDummyTts();
#endif
        tts.Init();
    }

    void Update()
    {
        // 喋っている間だけ口を動かす(ここが TTS と口パクの接続点)
        if (lipSync != null) lipSync.IsSpeaking = tts.IsSpeaking;
    }

    void OnDestroy() => tts?.Dispose();

    async void Speak(string text)
    {
        try
        {
            await tts.SpeakAsync(text, destroyCancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogException(e); }
    }

    void OnGUI()
    {
        int fontSize = Mathf.Max(24, Screen.height / 35);
        var button = new GUIStyle(GUI.skin.button) { fontSize = fontSize, wordWrap = true };
        var field = new GUIStyle(GUI.skin.textField) { fontSize = fontSize, wordWrap = true };

        float margin = Screen.width * 0.05f;
        float width = Screen.width - margin * 2f;
        float rowH = Screen.height * 0.07f;
        float y = Screen.height * 0.03f;

        // 入力欄(キーボードで隠れにくいよう、画面上部に配置)
        inputText = GUI.TextField(new Rect(margin, y, width, rowH * 1.5f), inputText, field);
        y += rowH * 1.5f + 10f;

        // 読み上げ / 停止
        float half = (width - 10f) / 2f;
        if (GUI.Button(new Rect(margin, y, half, rowH), "読み上げ", button))
            Speak(inputText);
        if (GUI.Button(new Rect(margin + half + 10f, y, half, rowH), "停止", button))
            tts.Stop();
        y += rowH + 20f;

        // プリセット文(日本語入力が不安定な端末でも試せるように)
        foreach (var preset in Presets)
        {
            if (GUI.Button(new Rect(margin, y, width, rowH), preset, button))
                Speak(preset);
            y += rowH + 10f;
        }
    }
}
