using UnityEngine;

/// <summary>
/// 2D キャラの確認用の画面。キャラ名のボタンで読み込み、「話し始める」で口パクを確かめる。
/// Canvas や EventSystem の設定は不要 (OnGUI で描画)。LipSyncTestUI と同じ作り。
/// </summary>
public class Character2DTestUI : MonoBehaviour
{
    [SerializeField] Character2DView view;
    [SerializeField] PseudoLipSync lipSync;

    string[] names;
    string message = "読み込んでいます…";
    bool loading;

    void Awake()
    {
        if (view == null) view = FindFirstObjectByType<Character2DView>();
        if (lipSync == null) lipSync = FindFirstObjectByType<PseudoLipSync>();
    }

    void Start()
    {
        StartCoroutine(CharacterPack.LoadIndex(
            n => { names = n; message = n.Length == 0 ? "キャラがありません。CharacterMaker で書き出してください" : "キャラを選んでください"; },
            e => message = e));
    }

    void Select(string name)
    {
        loading = true;
        message = $"{name} を読み込んでいます…";
        StartCoroutine(CharacterPack.Load(name,
            c => { view.Show(c); message = name; loading = false; },
            e => { message = e; loading = false; }));
    }

    void OnGUI()
    {
        int fontSize = Mathf.Max(24, Screen.height / 30);
        var button = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
        var label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = true };

        float margin = Screen.width * 0.04f;
        float h = Screen.height * 0.06f;
        GUI.Label(new Rect(margin, margin, Screen.width - margin * 2, h * 1.6f), message, label);

        // キャラ名のボタン (上に横並び、はみ出したら次の行)
        if (names != null)
        {
            float x = margin, y = margin + h * 1.7f;
            float w = (Screen.width - margin * 2) / 3f - 8f;
            GUI.enabled = !loading;
            foreach (var n in names)
            {
                if (x + w > Screen.width - margin + 1) { x = margin; y += h + 8f; }
                if (GUI.Button(new Rect(x, y, w, h), n, button)) Select(n);
                x += w + 8f;
            }
            GUI.enabled = true;
        }

        if (lipSync == null || view == null || !view.HasCharacter) return;
        float bw = Screen.width * 0.6f;
        string text = lipSync.IsSpeaking ? "話すのをやめる" : "話し始める";
        if (GUI.Button(new Rect((Screen.width - bw) * 0.5f, Screen.height * 0.87f, bw, Screen.height * 0.08f), text, button))
            lipSync.IsSpeaking = !lipSync.IsSpeaking;
    }
}
