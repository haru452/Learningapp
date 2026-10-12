using UnityEngine;

/// <summary>
/// CharacterAnimator の動作確認・調整用ボタン(画面の中ほどに表示)。
/// 向きや角度の調整が終わったら、このコンポーネントは無効にしてよい。
/// </summary>
public class CharacterTestUI : MonoBehaviour
{
    [SerializeField] CharacterAnimator character;

    static readonly string[] Emotions =
        { "neutral", "happy", "sad", "angry", "surprised", "relaxed" };

    void Awake()
    {
        if (character == null) character = FindFirstObjectByType<CharacterAnimator>();
    }

    void OnGUI()
    {
        if (character == null) return;

        int fontSize = Mathf.Max(18, Screen.height / 70);
        var button = new GUIStyle(GUI.skin.button) { fontSize = fontSize };

        float margin = Screen.width * 0.04f;
        float width = Screen.width - margin * 2f;
        float btnH = Screen.height * 0.045f;
        float y = Screen.height * 0.50f;

        // 表情(3列 × 2行)
        int cols = 3;
        float cellW = (width - 10f * (cols - 1)) / cols;
        for (int i = 0; i < Emotions.Length; i++)
        {
            float x = margin + (i % cols) * (cellW + 10f);
            float yy = y + (i / cols) * (btnH + 6f);
            if (GUI.Button(new Rect(x, yy, cellW, btnH), Emotions[i], button))
                character.SetEmotion(Emotions[i]);
        }
        y += (btnH + 6f) * 2f + 6f;

        // うなずき
        float half = (width - 10f) / 2f;
        if (GUI.Button(new Rect(margin, y, half, btnH), "うなずく(1回)", button)) character.Nod(1, 10f, 0.5f);
        if (GUI.Button(new Rect(margin + half + 10f, y, half, btnH), "うなずく(2回)", button)) character.Nod(2, 12f, 0.45f);
        y += btnH + 6f;

        // 状態
        float q = (width - 30f) / 4f;
        string[] modes = { "待機", "聞く", "考える", "話す" };
        var values = new[]
        {
            CharacterAnimator.Mode.Idle, CharacterAnimator.Mode.Listening,
            CharacterAnimator.Mode.Thinking, CharacterAnimator.Mode.Speaking,
        };
        for (int i = 0; i < modes.Length; i++)
        {
            if (GUI.Button(new Rect(margin + i * (q + 10f), y, q, btnH), modes[i], button))
                character.SetMode(values[i]);
        }
    }
}
