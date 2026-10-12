using System.Collections.Generic;
using UnityEngine;
using UniVRM10;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// VRM モデルをタッチ(クリック)したときの反応。
///
/// VRM には最初からコライダーが無いので、起動時に頭・体・腕・手の骨へ
/// 見えない球の当たり判定を自動で付け、画面のタッチ位置からレイを飛ばして
/// どこに触れたかを調べる。
///
///  頭 → 笑顔でうなずき + 一言      体 → 驚いてのけぞる + 一言
///  腕 → 軽くうなずく + 一言        手 → 笑顔 + 一言
///  短時間に何度も触ると、少し怒る
/// </summary>
public class TouchReaction : MonoBehaviour
{
    public enum Region { Head, Body, Arm, Hand }

    [SerializeField] CharacterAnimator character;
    [SerializeField] ConversationController conversation;
    [SerializeField] Camera cam;

    [Header("動作")]
    [Tooltip("オフにすると、声は出さず、表情と動きだけで反応する")]
    [SerializeField] bool speak = true;
    [Tooltip("画面の下から、この割合までの範囲のタッチは無視する(画面下のボタンと重ならないように)")]
    [Range(0f, 0.5f)] [SerializeField] float ignoreBottomFraction = 0.2f;
    [SerializeField] float cooldown = 0.6f;

    [Header("当たり判定")]
    [Tooltip("当たり判定の大きさの倍率。触りにくい/触れすぎるときに調整する")]
    [Range(0.5f, 2.5f)] [SerializeField] float zoneScale = 1f;
    [Tooltip("Scene ビュー(Gizmos をオン)で、当たり判定を線で表示する")]
    [SerializeField] bool drawZones = true;

    // 英語のセリフ(TTS を日本語にする場合は、ここを書き換える)
    static readonly string[] HeadLines =
        { "Hehe, that feels nice!", "Mmm, that's so relaxing.", "You're being so gentle today." };
    static readonly string[] BodyLines =
        { "Eek! That tickles!", "H-hey! Be careful!", "Whoa, you surprised me!" };
    static readonly string[] ArmLines =
        { "Hm? Did you need something?", "Yes? I'm listening!" };
    static readonly string[] HandLines =
        { "Oh, are we holding hands?", "Hello there!" };
    static readonly string[] AnnoyedLines =
        { "Okay, okay, that's enough!", "Hey, stop poking me!" };

    readonly Dictionary<Collider, Region> zones = new Dictionary<Collider, Region>();
    readonly Queue<float> recentTaps = new Queue<float>();
    Animator animator;
    bool zonesBuilt;
    float nextBuildTry;
    float lastReactTime = -10f;
    string lastLine;

    // ------------------------------------------------------------------ 初期化

    void Awake()
    {
        if (character == null) character = FindFirstObjectByType<CharacterAnimator>();
        if (conversation == null) conversation = FindFirstObjectByType<ConversationController>();
    }

    /// <summary>当たり判定をつくる。モデルが後から読み込まれる場合に備え、できるまで繰り返す。</summary>
    void TryBuildZones()
    {
        if (Time.unscaledTime < nextBuildTry) return;
        nextBuildTry = Time.unscaledTime + 0.5f;

        var vrm = FindFirstObjectByType<Vrm10Instance>();
        if (vrm == null) return;

        animator = vrm.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;

        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (head == null || hips == null) return;

        // 体の大きさに合わせて判定のサイズを決める(腰〜頭の距離が約0.65mを基準)
        float s = Mathf.Max(0.3f, Vector3.Distance(head.position, hips.position) / 0.65f) * zoneScale;
        Vector3 up = animator.transform.up;

        // 頭
        AddSphere(head, head.position + up * (0.09f * s), 0.12f * s, Region.Head);

        // 体(胸とおなか)
        var chest = FirstBone(HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine);
        var spine = FirstBone(HumanBodyBones.Spine, HumanBodyBones.Hips);
        if (chest != null) AddSphere(chest, chest.position, 0.15f * s, Region.Body);
        if (spine != null && spine != chest) AddSphere(spine, spine.position, 0.14f * s, Region.Body);

        // 腕(上腕・前腕の中ほど)と手
        AddLimb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.06f * s);
        AddLimb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.06f * s);
        AddLimb(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.055f * s);
        AddLimb(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.055f * s);

        var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (leftHand != null) AddSphere(leftHand, leftHand.position, 0.07f * s, Region.Hand);
        if (rightHand != null) AddSphere(rightHand, rightHand.position, 0.07f * s, Region.Hand);

        zonesBuilt = true;
        Debug.Log($"[TouchReaction] 当たり判定を {zones.Count} 個つくりました");
    }

    Transform FirstBone(params HumanBodyBones[] candidates)
    {
        foreach (var id in candidates)
        {
            var t = animator.GetBoneTransform(id);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>from の骨から to の骨までの中ほどに、from の骨の子として球をつくる</summary>
    void AddLimb(HumanBodyBones from, HumanBodyBones to, float worldRadius)
    {
        var a = animator.GetBoneTransform(from);
        var b = animator.GetBoneTransform(to);
        if (a == null || b == null) return;
        AddSphere(a, (a.position + b.position) * 0.5f, worldRadius, Region.Arm);
    }

    void AddSphere(Transform parent, Vector3 worldCenter, float worldRadius, Region region)
    {
        var go = new GameObject("TouchZone_" + region);
        go.transform.SetParent(parent, false);
        go.transform.position = worldCenter;   // 骨の子にするので、アニメーションに追従する

        var sphere = go.AddComponent<SphereCollider>();
        sphere.isTrigger = true;               // 物理的にはぶつからない(タッチ判定専用)

        // 骨のスケールを考慮して、ワールドでの半径が worldRadius になるようにする
        Vector3 ls = go.transform.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        sphere.radius = worldRadius / Mathf.Max(scale, 0.0001f);

        zones[sphere] = region;
    }

    // ------------------------------------------------------------------ タッチ検出

    void Update()
    {
        if (!zonesBuilt)
        {
            TryBuildZones();
            return;
        }

        if (!TryGetPress(out Vector2 pos)) return;
        if (pos.y < Screen.height * ignoreBottomFraction) return;       // 画面下のボタン付近は無視

        // メッセージウィンドウの上のタップは、文字送りを飛ばす操作なので、モデルへのタッチにしない
        if (MessageWindow.Instance != null && MessageWindow.Instance.IsOverWindow(pos)) return;
        if (Time.time - lastReactTime < cooldown) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam == null) return;

        Physics.SyncTransforms();   // 骨の最新の位置を、当たり判定に反映する
        Ray ray = cam.ScreenPointToRay(pos);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide);

        // 手前にある、触れる場所のうち一番近いものを選ぶ
        float best = float.MaxValue;
        Region? found = null;
        foreach (var hit in hits)
        {
            if (hit.distance < best && zones.TryGetValue(hit.collider, out Region r))
            {
                best = hit.distance;
                found = r;
            }
        }

        if (found.HasValue) React(found.Value);
    }

    /// <summary>入力設定(旧 Input Manager / 新 Input System)のどちらでも動くように分ける</summary>
    static bool TryGetPress(out Vector2 pos)
    {
        pos = default;
#if ENABLE_INPUT_SYSTEM
        var touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
        {
            pos = touch.primaryTouch.position.ReadValue();
            return true;
        }
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            pos = mouse.position.ReadValue();
            return true;
        }
        return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase != TouchPhase.Began) return false;
            pos = t.position;
            return true;
        }
        if (Input.GetMouseButtonDown(0))
        {
            pos = Input.mousePosition;
            return true;
        }
        return false;
#else
        return false;
#endif
    }

    // ------------------------------------------------------------------ 反応

    void React(Region region)
    {
        lastReactTime = Time.time;

        // 4秒以内に5回以上触られたら、うるさがる
        recentTaps.Enqueue(Time.time);
        while (recentTaps.Count > 0 && Time.time - recentTaps.Peek() > 4f) recentTaps.Dequeue();
        bool annoyed = recentTaps.Count >= 5;
        if (annoyed) recentTaps.Clear();

        string line;
        if (annoyed)
        {
            if (character != null)
            {
                character.SetEmotion("angry");
                character.Flinch(5f);
            }
            line = Pick(AnnoyedLines);
        }
        else
        {
            switch (region)
            {
                case Region.Head:
                    if (character != null)
                    {
                        character.SetEmotion(Random.value < 0.5f ? "happy" : "relaxed");
                        character.Nod(1, 14f, 0.8f);   // なでられて、頭をそっと下げる
                    }
                    line = Pick(HeadLines);
                    break;

                case Region.Body:
                    if (character != null)
                    {
                        character.SetEmotion("surprised");
                        character.Flinch(7f);          // 驚いてのけぞる
                    }
                    line = Pick(BodyLines);
                    break;

                case Region.Hand:
                    if (character != null)
                    {
                        character.SetEmotion("happy");
                        character.Nod(1, 8f, 0.5f);
                    }
                    line = Pick(HandLines);
                    break;

                default: // Arm
                    if (character != null)
                    {
                        character.SetEmotion("relaxed");
                        character.Nod(1, 8f, 0.5f);
                    }
                    line = Pick(ArmLines);
                    break;
            }
        }

        // 声は、会話コントローラが「話せる状態」のときだけ出る(考え中・返事中は動きだけ)
        if (speak && conversation != null) conversation.SpeakReaction(line);
    }

    /// <summary>同じセリフが続かないように選ぶ</summary>
    string Pick(string[] lines)
    {
        string chosen = lines[Random.Range(0, lines.Length)];
        for (int i = 0; i < 4 && chosen == lastLine && lines.Length > 1; i++)
            chosen = lines[Random.Range(0, lines.Length)];
        lastLine = chosen;
        return chosen;
    }

    // ------------------------------------------------------------------ 調整用の表示

    void OnDrawGizmos()
    {
        if (!drawZones || zones.Count == 0) return;

        foreach (var pair in zones)
        {
            var col = pair.Key as SphereCollider;
            if (col == null) continue;

            switch (pair.Value)
            {
                case Region.Head: Gizmos.color = Color.yellow; break;
                case Region.Body: Gizmos.color = Color.red; break;
                case Region.Arm: Gizmos.color = Color.cyan; break;
                default: Gizmos.color = Color.green; break;
            }

            Vector3 ls = col.transform.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            Gizmos.DrawWireSphere(col.transform.TransformPoint(col.center), col.radius * scale);
        }
    }
}