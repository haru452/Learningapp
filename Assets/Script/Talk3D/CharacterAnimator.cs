using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

/// <summary>
/// VRM キャラクターの「体の動き」と「表情」を、アニメーションファイルなしで動かす。
///
///  ・Tポーズ解除: 腕を下ろし、ひじを軽く曲げた自然な立ち姿にする
///  ・待機の動き: 呼吸、ゆらぎ、まばたき
///  ・会話の状態に応じた動き: 聞いている(少し首をかしげる)/ 考えている(上を見て首をかしげる)/ 話している(頭が小さく動く)
///  ・うなずき: Nod() で呼び出す。ユーザーが話している間は相づちとして自動でうなずく
///  ・表情: SetEmotion("happy") など。しばらくすると自然に戻る
///
/// 実装の考え方: ヒューマノイドの骨の「元の回転(Tポーズ)」を覚えておき、
/// 毎フレーム、元の回転に「差分」を足して姿勢を作る(累積しない)。
/// </summary>
[DefaultExecutionOrder(100)] // VRM の SpringBone(髪・服の揺れ)より先に実行する
public class CharacterAnimator : MonoBehaviour
{
    public enum Mode { Idle, Listening, Thinking, Speaking }

    [SerializeField] Vrm10Instance vrm;
    [SerializeField] Animator animator;

    [Header("向き")]
    [Tooltip("うなずきが上向きになる、ひじが後ろに曲がる場合はオンにする")]
    [SerializeField] bool modelFacesMinusZ = false;

    [Header("基本ポーズ(Tポーズ解除)")]
    [Range(0f, 90f)] [SerializeField] float armDownAngle = 65f;
    [Range(0f, 60f)] [SerializeField] float elbowBend = 15f;

    [Header("待機の動き")]
    [SerializeField] float breathAmount = 1.0f;   // 呼吸の大きさ(度)
    [SerializeField] float breathSpeed = 0.25f;   // 呼吸の速さ(回/秒)
    [SerializeField] float swayAmount = 1.5f;     // 体のゆらぎ(度)

    [Header("表情")]
    [Range(0f, 1f)] [SerializeField] float emotionStrength = 0.8f;
    [SerializeField] float emotionHoldSeconds = 6f;   // この秒数のあと自然に戻る
    [SerializeField] float emotionFadeSpeed = 3f;     // 表情が切り替わる速さ
    [SerializeField] bool autoBlink = true;

    // ------------------------------------------------------------------ 内部データ

    static readonly HumanBodyBones[] Order =
    {
        // 親 → 子の順に処理する(親の回転を先に決める)
        HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
    };

    class Bone
    {
        public Transform t;
        public Quaternion restLocal;  // 元(Tポーズ)のローカル回転
        public float side = 1f;       // 腕の左右(下ろす回転の向き)
    }

    readonly Dictionary<HumanBodyBones, Bone> bones = new Dictionary<HumanBodyBones, Bone>();
    Transform root;
    bool initialized;
    float nextInitTry;

    // 動きの状態
    Mode mode = Mode.Idle;
    float thinkSide = 1f;
    float headPitch, headYaw, headRoll;     // なめらかにした現在値(度) pitch: + で下向き
    float nodPitch;
    float totalPitch, totalRoll, breath, sway;

    // のけぞり(驚いたとき)
    float flinchT = -1f;
    float flinchAmp;
    float flinchPitch, flinchRoll;

    // うなずき
    float nodTime = -1f;
    int nodCount = 1;
    float nodAmplitude = 10f;
    float nodDuration = 0.5f;
    float lastAizuchi = -10f;

    // 表情
    static readonly ExpressionKey[] EmotionKeys =
    {
        ExpressionKey.Happy, ExpressionKey.Angry, ExpressionKey.Sad,
        ExpressionKey.Relaxed, ExpressionKey.Surprised,
    };
    readonly float[] emotionWeights = new float[5];
    int emotionIndex = -1;   // -1 = 通常
    float emotionTimer;

    // まばたき
    float blinkTimer = 2f;
    float blinkT = -1f;

    // ------------------------------------------------------------------ 公開 API

    /// <summary>実行時ロードした VRM を後から指定する用</summary>
    public void SetVrm(Vrm10Instance instance)
    {
        vrm = instance;
        animator = null;
        initialized = false;
        bones.Clear();
    }

    public void SetMode(Mode m)
    {
        if (mode == m) return;
        mode = m;
        if (m == Mode.Thinking) thinkSide = Random.value < 0.5f ? -1f : 1f;
    }

    /// <summary>うなずく。count 回、amplitude 度、1回あたり duration 秒</summary>
    public void Nod(int count = 2, float amplitude = 12f, float duration = 0.5f)
    {
        nodCount = Mathf.Max(1, count);
        nodAmplitude = amplitude;
        nodDuration = Mathf.Max(0.15f, duration);
        nodTime = 0f;
    }

    /// <summary>ユーザーが話している間に呼ぶ。間隔を空けて、相づちのうなずきをする。</summary>
    public void NotifyUserSpeaking()
    {
        if (Time.time - lastAizuchi < 2.8f) return;
        lastAizuchi = Time.time;
        if (Random.value < 0.75f) Nod(1, 9f, 0.55f);
    }

    /// <summary>驚いてのけぞる。amplitude は反りの大きさ(度)。</summary>
    public void Flinch(float amplitude = 7f)
    {
        flinchAmp = amplitude;
        flinchT = 0f;
    }

    /// <summary>感情名で表情を変える。使える名前: happy, sad, angry, surprised, relaxed, neutral</summary>
    public bool SetEmotion(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;

        int idx;
        switch (tag.Trim().ToLowerInvariant())
        {
            case "happy": case "joy": case "smile": case "excited": idx = 0; break;
            case "angry": case "mad": idx = 1; break;
            case "sad": case "sorry": idx = 2; break;
            case "relaxed": case "calm": case "gentle": idx = 3; break;
            case "surprised": case "surprise": idx = 4; break;
            case "neutral": case "normal": case "none": idx = -1; break;
            default: return false;
        }

        emotionIndex = idx;
        emotionTimer = emotionHoldSeconds;
        return true;
    }

    // ------------------------------------------------------------------ 初期化

    readonly HashSet<string> logged = new HashSet<string>();

    void LogOnce(string message)
    {
        if (logged.Add(message)) Debug.LogWarning("[CharacterAnimator] " + message);
    }

    /// <summary>
    /// VRM 1.0 の ControlRig(正規化された骨)から、骨の Transform を取り出す関数を作る。
    /// VRM 1.0 では、体の骨は Vrm10Instance が ControlRig から毎フレーム上書きするため、
    /// ポーズは ControlRig 側に与える必要がある。取得できなければ null を返す。
    /// (UniVRM のバージョン差で型名が変わっても動くよう、メソッドは名前で呼び出す)
    /// </summary>
    System.Func<HumanBodyBones, Transform> GetControlRigAccessor()
    {
        try
        {
            if (vrm.Runtime == null) return null;

            object rig = vrm.Runtime.ControlRig;
            if (rig == null) return null;

            var method = rig.GetType().GetMethod("GetBoneTransform", new[] { typeof(HumanBodyBones) });
            if (method == null) return null;

            return id => method.Invoke(rig, new object[] { id }) as Transform;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[CharacterAnimator] ControlRig を取得できませんでした: " + e.Message);
            return null;
        }
    }

    bool TryInit()
    {
        if (Time.unscaledTime < nextInitTry) return false;
        nextInitTry = Time.unscaledTime + 0.5f;

        if (vrm == null) vrm = FindFirstObjectByType<Vrm10Instance>();
        if (vrm == null)
        {
            LogOnce("Vrm10Instance が見つかりません。VRM 1.0 のモデルをシーンに置いてください。");
            return false;
        }

        if (animator == null) animator = vrm.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            LogOnce("Animator が見つかりません。");
            return false;
        }
        if (!animator.isHuman)
        {
            LogOnce("Animator が Humanoid ではありません。");
            return false;
        }

        root = animator.transform;

        var controlRig = GetControlRigAccessor();
        bool usingControlRig = controlRig != null;

        bones.Clear();
        foreach (var id in Order)
        {
            Transform t = usingControlRig ? controlRig(id) : null;
            if (t == null) t = animator.GetBoneTransform(id);   // ControlRig に無ければ直接の骨を使う
            if (t == null) continue;
            bones[id] = new Bone { t = t, restLocal = t.localRotation };
        }

        if (bones.Count == 0)
        {
            LogOnce("操作できる骨が1つも見つかりませんでした。");
            return false;
        }

        Debug.Log($"[CharacterAnimator] 初期化しました: {(usingControlRig ? "ControlRig" : "Animator の骨を直接")}を使用 / 骨 {bones.Count} 個");

        // 腕の左右を、位置から判定する(名前に頼らない)
        Vector3 charRight = root.rotation * Frame() * Vector3.right;
        foreach (var id in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
        {
            if (!bones.TryGetValue(id, out var b)) continue;
            // キャラの右側にある腕は -方向に回すと下がる
            b.side = Vector3.Dot(b.t.position - root.position, charRight) >= 0f ? -1f : 1f;
        }

        initialized = true;
        return true;
    }

    Quaternion Frame() => modelFacesMinusZ ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

    // ------------------------------------------------------------------ 毎フレーム

    void LateUpdate()
    {
        if (!initialized) TryInit();

        float dt = Time.deltaTime;
        if (initialized)
        {
            UpdateMotion(dt);
            ApplyPose();
        }
        UpdateExpressions(dt);   // 表情とまばたきは、骨の初期化とは無関係に動かす
    }

    void OnDisable()
    {
        // 無効にしたとき、元のポーズ(Tポーズ)に戻す
        foreach (var b in bones.Values)
            if (b.t != null) b.t.localRotation = b.restLocal;
    }

    static float Noise(float speed, float seed) =>
        Mathf.PerlinNoise(Time.time * speed, seed) * 2f - 1f;

    void UpdateMotion(float dt)
    {
        float t = Time.time;

        // 状態ごとの「目標の頭の向き」(度)
        float tp, ty, tr;
        switch (mode)
        {
            case Mode.Listening:   // 少し首をかしげて聞く
                tp = 2f + Noise(0.3f, 1f) * 1.5f;
                ty = Noise(0.25f, 7f) * 2f;
                tr = 3f + Noise(0.2f, 13f) * 1.5f;
                break;
            case Mode.Thinking:    // 上を見て、首をかしげる
                tp = -6f;
                ty = 10f * thinkSide;
                tr = 8f * thinkSide;
                break;
            case Mode.Speaking:    // 話しながら、頭が小さく動く
                tp = Noise(0.7f, 1f) * 3f;
                ty = Noise(0.6f, 7f) * 4f;
                tr = Noise(0.5f, 13f) * 3f;
                break;
            default:               // 待機
                tp = Noise(0.3f, 1f) * 1.5f;
                ty = Noise(0.25f, 7f) * 2f;
                tr = Noise(0.2f, 13f) * 1.5f;
                break;
        }

        float k = 1f - Mathf.Exp(-4f * dt);   // フレームレートに依存しないなめらかさ
        headPitch = Mathf.Lerp(headPitch, tp, k);
        headYaw = Mathf.Lerp(headYaw, ty, k);
        headRoll = Mathf.Lerp(headRoll, tr, k);

        // うなずき: 1回ごとに「下がって戻る」
        if (nodTime >= 0f)
        {
            nodTime += dt;
            float phase = nodTime / nodDuration;
            if (phase >= nodCount)
            {
                nodTime = -1f;
                nodPitch = 0f;
            }
            else
            {
                nodPitch = nodAmplitude * Mathf.Sin(Mathf.PI * (phase - Mathf.Floor(phase)));
            }
        }

        // のけぞり: 一瞬うしろに反って、小さく震えながら元に戻る
        if (flinchT >= 0f)
        {
            flinchT += dt;
            float decay = Mathf.Exp(-flinchT * 5f);
            flinchPitch = -flinchAmp * decay;
            flinchRoll = flinchAmp * 0.6f * decay * Mathf.Sin(flinchT * 22f);
            if (decay < 0.02f)
            {
                flinchT = -1f;
                flinchPitch = 0f;
                flinchRoll = 0f;
            }
        }

        totalPitch = headPitch + nodPitch + flinchPitch;
        totalRoll = headRoll + flinchRoll;
        breath = Mathf.Sin(t * breathSpeed * Mathf.PI * 2f) * breathAmount;
        sway = Noise(0.15f, 31f) * swayAmount;
    }

    Quaternion OffsetFor(HumanBodyBones id, Bone b)
    {
        switch (id)
        {
            case HumanBodyBones.Spine:
                return Quaternion.Euler(totalPitch * 0.1f + flinchPitch * 0.7f, headYaw * 0.15f + sway * 0.5f, totalRoll * 0.15f + sway);
            case HumanBodyBones.Chest:
                return Quaternion.Euler(-breath, 0f, 0f);
            case HumanBodyBones.Neck:
                return Quaternion.Euler(totalPitch * 0.4f, headYaw * 0.4f, totalRoll * 0.4f);
            case HumanBodyBones.Head:
                return Quaternion.Euler(totalPitch * 0.5f, headYaw * 0.45f, totalRoll * 0.45f);
            case HumanBodyBones.LeftUpperArm:
            case HumanBodyBones.RightUpperArm:
                // 正面向きの Z 軸まわりに回すと、腕が下がる(左右は side で反転)
                return Quaternion.AngleAxis(b.side * (armDownAngle + breath * 0.5f), Vector3.forward);
            case HumanBodyBones.LeftLowerArm:
            case HumanBodyBones.RightLowerArm:
                // 腕を下ろした状態で、前腕が前に曲がる
                return Quaternion.Euler(-elbowBend, 0f, 0f);
            default:
                return Quaternion.identity;
        }
    }

    void ApplyPose()
    {
        Quaternion rootRot = root.rotation;
        Quaternion frame = Frame();
        Quaternion frameInv = Quaternion.Inverse(frame);
        Quaternion rootInv = Quaternion.Inverse(rootRot);

        foreach (var id in Order)
        {
            if (!bones.TryGetValue(id, out var b) || b.t == null) continue;

            // 1) まず元の回転に戻す(親の最新の姿勢の上に乗る)
            b.t.localRotation = b.restLocal;

            // 2) キャラ基準の差分を、ワールド基準の回転に直して掛ける
            Quaternion d = OffsetFor(id, b);
            Quaternion worldDelta = rootRot * frame * d * frameInv * rootInv;
            b.t.rotation = worldDelta * b.t.rotation;
        }
    }

    void UpdateExpressions(float dt)
    {
        if (vrm == null || vrm.Runtime == null) return;
        var expr = vrm.Runtime.Expression;

        // 感情: 指定された1つだけをなめらかに上げ、他は下げる
        if (emotionIndex >= 0)
        {
            emotionTimer -= dt;
            if (emotionTimer <= 0f) emotionIndex = -1;   // 時間が来たら通常に戻る
        }
        for (int i = 0; i < EmotionKeys.Length; i++)
        {
            float target = (i == emotionIndex) ? emotionStrength : 0f;
            emotionWeights[i] = Mathf.MoveTowards(emotionWeights[i], target, emotionFadeSpeed * dt);
            expr.SetWeight(EmotionKeys[i], emotionWeights[i]);
        }

        // まばたき
        if (autoBlink)
        {
            blinkTimer -= dt;
            if (blinkTimer <= 0f && blinkT < 0f)
            {
                blinkT = 0f;
                blinkTimer = Random.Range(2.5f, 5.5f);
            }
            if (blinkT >= 0f)
            {
                blinkT += dt;
                float p = blinkT / 0.18f;
                expr.SetWeight(ExpressionKey.Blink, p < 1f ? Mathf.Sin(p * Mathf.PI) : 0f);
                if (p >= 1f) blinkT = -1f;
            }
        }
    }
}