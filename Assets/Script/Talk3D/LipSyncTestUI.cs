using UnityEngine;

/// <summary>
/// 口パクのテスト用ボタン。Canvas や EventSystem の設定は不要(OnGUI で描画)。
/// スマホの画面サイズに合わせて文字とボタンを自動で拡大する。
/// </summary>
public class LipSyncTestUI : MonoBehaviour
{
    [SerializeField] PseudoLipSync lipSync;

    void Awake()
    {
        if (lipSync == null) lipSync = FindFirstObjectByType<PseudoLipSync>();
    }

    void OnGUI()
    {
        if (lipSync == null) return;

        int fontSize = Mathf.Max(24, Screen.height / 30);
        var style = new GUIStyle(GUI.skin.button) { fontSize = fontSize };

        float w = Screen.width * 0.6f;
        float h = Screen.height * 0.09f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.85f;

        string label = lipSync.IsSpeaking ? "話すのをやめる" : "話し始める";
        if (GUI.Button(new Rect(x, y, w, h), label, style))
        {
            lipSync.IsSpeaking = !lipSync.IsSpeaking;
        }
    }
}
