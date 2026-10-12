using UnityEngine;

/// <summary>
/// IsSpeaking が true の間だけ口を動かす疑似口パク。
/// 音声の波形は見ず、ランダムな間隔で開き具合の目標値を変えて、
/// それに滑らかに追従させることで「それらしい」動きにする。
/// 後で TTS の再生状態と接続するときは、IsSpeaking を書き換えるだけでよい。
/// </summary>
public class PseudoLipSync : MonoBehaviour
{
    [SerializeField] MouthTargetBase mouth;

    [Header("見た目の調整")]
    [Range(0f, 1f)] public float maxOpen = 0.8f;       // 口の最大の開き
    [Range(0f, 1f)] public float minOpen = 0.2f;       // 話している間の最小の開き
    public Vector2 changeInterval = new Vector2(0.06f, 0.16f); // 目標を変える間隔(秒)
    [Range(0f, 1f)] public float closeChance = 0.2f;   // 一瞬閉じる確率(音節の切れ目っぽさ)
    public float followSpeed = 20f;                    // 大きいほどキビキビ動く

    /// <summary>外部(TTS側)から切り替える。</summary>
    public bool IsSpeaking { get; set; }

    float target;
    float current;
    float timer;

    void Awake()
    {
        if (mouth == null) mouth = GetComponent<MouthTargetBase>();
        if (mouth == null) mouth = FindFirstObjectByType<MouthTargetBase>();
        if (mouth == null) Debug.LogError("MouthTargetBase(Vrm10MouthTarget など)が見つかりません。");
    }

    void Update()
    {
        if (IsSpeaking)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                target = Random.value < closeChance
                    ? 0.05f
                    : Random.Range(minOpen, maxOpen);
                timer = Random.Range(changeInterval.x, changeInterval.y);
            }
        }
        else
        {
            target = 0f;
            timer = 0f;
        }

        // フレームレートに依存しない滑らかな追従
        float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        current = Mathf.Lerp(current, target, t);

        if (mouth != null) mouth.SetMouthOpen(current);
    }

    void OnDisable()
    {
        if (mouth != null) mouth.SetMouthOpen(0f);
    }
}
