using UnityEngine;

/// <summary>
/// ギャルゲー風のメッセージウィンドウ。
///
///  ・画面の中心より少し下に、角丸の半透明ウィンドウを表示する
///  ・左上に名札(キャラの名前 / ユーザー側は別の色)
///  ・キャラの返事は、1文ずつ「文字送り」で表示する(声に合わせて)
///  ・文字が出終わると「▼」が点滅する。ウィンドウをタップすると、文字送りを飛ばして全文を出す
///  ・考え中は「…」がアニメーションする
///  ・しばらく経つと、自動でフェードアウトする
///
/// Canvas 不要(OnGUI で描画)。ConversationController が自動で追加するので、
/// 手動でシーンに付ける必要はない。
/// </summary>
public class MessageWindow : MonoBehaviour
{
    public static MessageWindow Instance { get; private set; }

    enum Kind { None, Character, User, Thinking }

    [Header("名前")]
    [SerializeField] string characterName = "Lufka";
    [SerializeField] string userName = "You";

    [Header("位置とサイズ")]
    [Tooltip("ウィンドウの中心の高さ。0 = 画面の上端、1 = 下端。0.5 が中心")]
    [Range(0.3f, 0.9f)] [SerializeField] float anchorY = 0.62f;
    [Range(0.1f, 0.4f)] [SerializeField] float heightFraction = 0.20f;
    [Range(0.5f, 1f)] [SerializeField] float widthFraction = 0.92f;
    [Tooltip("文字サイズ = 画面の高さ ÷ この値。小さくするほど文字が大きくなる")]
    [SerializeField] float fontDivisor = 52f;

    [Header("文字送り")]
    [SerializeField] float charsPerSecond = 22f;
    [Tooltip("文字が出終わってから、この秒数でフェードアウト。0 で消えない")]
    [SerializeField] float hideAfterSeconds = 6f;

    [Header("色")]
    [SerializeField] Color windowColor = new Color(0.06f, 0.09f, 0.20f, 0.80f);
    [SerializeField] Color borderColor = new Color(0.80f, 0.90f, 1.00f, 0.90f);
    [SerializeField] Color namePlateColor = new Color(0.90f, 0.40f, 0.60f, 0.95f);
    [SerializeField] Color userNamePlateColor = new Color(0.30f, 0.60f, 0.90f, 0.95f);

    // 表示内容
    Kind kind = Kind.None;
    string text = "";
    float visible;          // 表示済みの文字数
    float completedAt = -1f;
    float alpha;

    // 見た目
    Texture2D windowTex, plateTex;
    GUIStyle boxStyle, plateStyle, textStyle, nameStyle, indicatorStyle;
    bool styleDirty = true;

    const int WindowRadius = 22;
    const int PlateRadius = 12;

    // ------------------------------------------------------------------ 公開 API

    /// <summary>キャラの返事を1文表示する(文字送りあり)</summary>
    public void ShowCharacterLine(string line) => Set(Kind.Character, line, true);

    /// <summary>ユーザーの発話(音声認識の結果)を表示する(文字送りなし)</summary>
    public void ShowUserLine(string line) => Set(Kind.User, line, false);

    /// <summary>考え中の「…」を表示する</summary>
    public void ShowThinking() => Set(Kind.Thinking, "", false, keepVisible: true);

    public void Hide() => kind = Kind.None;

    /// <summary>画面上のタッチ位置(左下が原点)が、ウィンドウの上かどうか</summary>
    public bool IsOverWindow(Vector2 screenPos)
    {
        if (alpha <= 0.05f) return false;

        Rect r = WindowRect();
        r.yMin -= Screen.height * 0.03f;   // 名札の分
        Vector2 gui = new Vector2(screenPos.x, Screen.height - screenPos.y);
        return r.Contains(gui);
    }

    // ------------------------------------------------------------------ 内部

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (windowTex != null) Destroy(windowTex);
        if (plateTex != null) Destroy(plateTex);
    }

    void OnValidate() => styleDirty = true;

    void Set(Kind k, string t, bool typewriter, bool keepVisible = false)
    {
        kind = k;
        // リッチテキストのタグと誤認されないよう、< > は無害な文字に置き換える
        text = (t ?? "").Replace("<", "(").Replace(">", ")");
        visible = typewriter ? 0f : text.Length;
        // 文字送りが終わった時刻(自動で消すための基準)。考え中は消さない
        completedAt = (typewriter || keepVisible) ? -1f : Time.unscaledTime;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (kind == Kind.Character && visible < text.Length)
        {
            visible = Mathf.Min(text.Length, visible + charsPerSecond * dt);
            if (visible >= text.Length) completedAt = Time.unscaledTime;
        }

        if (kind != Kind.None && completedAt >= 0f && hideAfterSeconds > 0f
            && Time.unscaledTime - completedAt > hideAfterSeconds)
        {
            kind = Kind.None;
        }

        alpha = Mathf.MoveTowards(alpha, kind == Kind.None ? 0f : 1f, dt * 4f);
    }

    Rect WindowRect()
    {
        float w = Screen.width * widthFraction;
        float h = Screen.height * heightFraction;
        return new Rect((Screen.width - w) * 0.5f, Screen.height * anchorY - h * 0.5f, w, h);
    }

    // ------------------------------------------------------------------ 描画

    void OnGUI()
    {
        if (alpha <= 0.01f) return;
        EnsureStyles();

        Rect r = WindowRect();
        int f = textStyle.fontSize;
        Color oldColor = GUI.color;

        // ウィンドウ本体
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Box(r, GUIContent.none, boxStyle);

        // 名札(ウィンドウの左上にはみ出して配置)
        string nameText = kind == Kind.User ? userName : characterName;
        Vector2 nameSize = nameStyle.CalcSize(new GUIContent(nameText));
        var plate = new Rect(r.x + f * 1.2f, 0f, nameSize.x + f * 1.8f, nameSize.y + f * 0.5f);
        plate.y = r.y - plate.height * 0.55f;

        Color plateColor = kind == Kind.User ? userNamePlateColor : namePlateColor;
        GUI.color = new Color(plateColor.r, plateColor.g, plateColor.b, plateColor.a * alpha);
        GUI.Box(plate, GUIContent.none, plateStyle);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(plate, nameText, nameStyle);

        // 本文
        var inner = new Rect(r.x + f * 1.2f, r.y + f * 1.1f, r.width - f * 2.4f, r.height - f * 1.5f);
        GUI.Label(inner, BuildDisplayText(), textStyle);

        // 文字が出終わったら「▼」を点滅
        bool finished = kind == Kind.Character && visible >= text.Length;
        if (finished && ((int)(Time.unscaledTime * 2f)) % 2 == 0)
        {
            GUI.Label(new Rect(r.xMax - f * 2.6f, r.yMax - f * 2.0f, f * 2f, f * 1.6f), "▼", indicatorStyle);
        }

        // ウィンドウをタップ → 文字送りを飛ばして全文を出す
        Event e = Event.current;
        if (kind == Kind.Character && e.type == EventType.MouseDown && r.Contains(e.mousePosition))
        {
            visible = text.Length;
            if (completedAt < 0f) completedAt = Time.unscaledTime;
            e.Use();
        }

        GUI.color = oldColor;
    }

    /// <summary>
    /// 表示する文字列。まだ出していない部分は「透明」で入れておくことで、
    /// 文字が増えても改行位置が変わらない(ギャルゲーの文字送りと同じ見え方)。
    /// </summary>
    string BuildDisplayText()
    {
        if (kind == Kind.Thinking)
            return new string('.', 1 + (int)(Time.unscaledTime * 2.5f) % 3);

        int n = Mathf.Clamp(Mathf.FloorToInt(visible), 0, text.Length);
        if (n >= text.Length) return text;
        return text.Substring(0, n) + "<color=#00000000>" + text.Substring(n) + "</color>";
    }

    // ------------------------------------------------------------------ スタイルと画像

    void EnsureStyles()
    {
        int fontSize = Mathf.Max(20, Mathf.RoundToInt(Screen.height / Mathf.Max(10f, fontDivisor)));
        if (!styleDirty && textStyle != null && textStyle.fontSize == fontSize) return;
        styleDirty = false;

        if (windowTex != null) Destroy(windowTex);
        if (plateTex != null) Destroy(plateTex);
        windowTex = MakeRoundedTexture(64, WindowRadius, 3, windowColor, borderColor);
        plateTex = MakeRoundedTexture(32, PlateRadius, 2, Color.white, Color.white);

        boxStyle = new GUIStyle { border = new RectOffset(WindowRadius, WindowRadius, WindowRadius, WindowRadius) };
        boxStyle.normal.background = windowTex;

        plateStyle = new GUIStyle { border = new RectOffset(PlateRadius, PlateRadius, PlateRadius, PlateRadius) };
        plateStyle.normal.background = plateTex;

        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            wordWrap = true,
            richText = true,
            alignment = TextAnchor.UpperLeft,
            clipping = TextClipping.Clip,
        };
        textStyle.normal.textColor = Color.white;

        nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(fontSize * 0.85f),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        nameStyle.normal.textColor = Color.white;

        indicatorStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(fontSize * 0.8f),
            alignment = TextAnchor.MiddleCenter,
        };
        indicatorStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
    }

    /// <summary>角丸の長方形の画像をコードでつくる(9分割で伸ばして使う)</summary>
    static Texture2D MakeRoundedTexture(int size, int radius, int borderWidth, Color fill, Color border)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius, size - radius);
                float cy = Mathf.Clamp(py, radius, size - radius);
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));

                float edge = radius - d;                              // ふちまでの距離(内側が正)
                float shape = Mathf.Clamp01(edge + 0.5f);             // ふちをなめらかに
                float borderMix = Mathf.Clamp01(borderWidth - edge + 0.5f);

                Color c = Color.Lerp(fill, border, borderMix);
                c.a *= shape;
                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
