using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// メニュー「Tools > Login > Build Login Form」で、設定画面のログイン欄を作り直し、AuthPanel につなぎ直す。
///
/// 見た目は、ほかの画面と同じ Unity 標準の部品 (白いボタン・標準の入力欄・同じフォント) にそろえる。
/// - もとのログイン用 Panel (背景と説明文) はそのまま使い、その中に入力欄とボタンを並べる
/// - 追加の機能: パスワードの表示切替 (Show/Hide)、Remember this device、Forgot password?
/// - もとの入力欄・ボタンは消さずに非表示にするだけ (Ctrl+Z で全部元に戻せる)
/// - もう一度実行すると作り直す。前のバージョンで作った「Login Screen (styled)」も消す
/// シーンの保存 (Ctrl+S) は自分で行う。
/// </summary>
public static class LoginScreenBuilder
{
    private const string FormName = "Login Form";
    private static readonly string[] OldBuiltNames = { "Login Screen (styled)" };

    // 部品の大きさは、ほかの画面の部品と同じ基準 (Unity 標準の 160x30 のボタンなど)。
    // 画面に合わせて、まとめて拡大する (スマホの横幅 1080 なら3倍。ほかの画面の部品と同じ倍率)
    private const float DesignWidth = 360f;
    private const float TopSpace = 64f;          // 上にある説明文の分だけ空ける
    private const float ColumnWidth = 260f;

    private static readonly Color TextColor = Color.white;
    private static readonly Color DarkText = new Color(0.196f, 0.196f, 0.196f, 1f);   // 標準ボタンの文字色
    private static readonly Color SubText = new Color(0.85f, 0.85f, 0.85f, 1f);

    private static Sprite _uiSprite, _inputSprite, _checkmark;
    private static TMP_FontAsset _font;

    [MenuItem("Tools/Login/Build Login Form")]
    private static void Build()
    {
        var panels = Object.FindObjectsOfType<AuthPanel>(true);
        if (panels.Length == 0)
        {
            EditorUtility.DisplayDialog("Login Form", "シーンに AuthPanel が見つかりません。SampleScene を開いてから実行してください。", "OK");
            return;
        }
        AuthPanel main = panels.FirstOrDefault(p => p.GetComponent<SupabaseService>() != null)
                         ?? panels.OrderByDescending(CountRefs).First();

        // 前回作ったもの (このツールの古いバージョンも含む)
        var previous = FindAllInScene(FormName).Concat(OldBuiltNames.SelectMany(FindAllInScene)).Distinct().ToList();

        // もとの入力欄 (どれかの AuthPanel が、まだ指しているもの) から、置き場所の Panel を決める
        GameObject oldSignedOut = null;
        Component oldEmail = null, oldPassword = null;
        foreach (var p in panels.OrderByDescending(p => p == main))
        {
            var so = new SerializedObject(p);
            var g = so.FindProperty("signedOutGroup").objectReferenceValue as GameObject;
            var e = so.FindProperty("emailInput").objectReferenceValue as Component;
            var pw = so.FindProperty("passwordInput").objectReferenceValue as Component;
            bool built = previous.Any(r => (g != null && g.transform.IsChildOf(r.transform))
                                        || (e != null && e.transform.IsChildOf(r.transform)));
            if (built) continue;
            if (oldSignedOut == null) oldSignedOut = g;
            if (oldEmail == null) oldEmail = e;
            if (oldPassword == null) oldPassword = pw;
        }

        Transform container = oldEmail != null ? oldEmail.transform.parent
            : oldSignedOut != null ? oldSignedOut.transform.parent
            : null;
        if (container == null)
        {
            EditorUtility.DisplayDialog("Login Form",
                "もとのログイン欄 (メール入力欄) が見つからないため、置き場所が決められません。", "OK");
            return;
        }

        LoadAssets(container);

        Undo.SetCurrentGroupName("Build Login Form");
        int undoGroup = Undo.GetCurrentGroup();
        var report = new List<string>();

        foreach (var old in previous)
        {
            report.Add("前に作った「" + old.name + "」を消して作り直しました");
            Undo.DestroyObjectImmediate(old);
        }

        var ui = BuildForm(container);
        Undo.RegisterCreatedObjectUndo(ui.Root, "Create login form");

        // ---- AuthPanel をつなぎ直す ----
        var mso = new SerializedObject(main);
        if (mso.FindProperty("supabase").objectReferenceValue == null)
            mso.FindProperty("supabase").objectReferenceValue = Object.FindObjectsOfType<SupabaseService>(true).FirstOrDefault();
        mso.FindProperty("signedOutGroup").objectReferenceValue = ui.Root;
        mso.FindProperty("emailInput").objectReferenceValue = ui.Email;
        mso.FindProperty("passwordInput").objectReferenceValue = ui.Password;
        mso.FindProperty("signInButton").objectReferenceValue = ui.SignIn;
        mso.FindProperty("signUpButton").objectReferenceValue = ui.CreateAccount;
        mso.FindProperty("statusText").objectReferenceValue = ui.Status;
        mso.FindProperty("statusInfoColor").colorValue = TextColor;
        mso.FindProperty("statusErrorColor").colorValue = new Color(1f, 0.6f, 0.6f, 1f);
        mso.FindProperty("showPasswordButton").objectReferenceValue = ui.ShowPassword;
        mso.FindProperty("passwordHiddenIcon").objectReferenceValue = ui.ShowLabel;
        mso.FindProperty("passwordVisibleIcon").objectReferenceValue = ui.HideLabel;
        mso.FindProperty("rememberToggle").objectReferenceValue = ui.Remember;
        mso.FindProperty("forgotPasswordButton").objectReferenceValue = ui.Forgot;
        mso.ApplyModifiedProperties();

        // ---- 同じボタンを2回処理しないよう、ほかの AuthPanel は止める ----
        foreach (var p in panels)
        {
            if (p == main || !p.enabled) continue;
            Undo.RecordObject(p, "Disable duplicate AuthPanel");
            p.enabled = false;
            report.Add("ほかの AuthPanel をオフにしました: " + PathOf(p.transform));
        }

        // ---- もとの入力欄・ボタンを隠す (消さない) ----
        foreach (var go in new[] { oldSignedOut, oldEmail != null ? oldEmail.gameObject : null, oldPassword != null ? oldPassword.gameObject : null })
        {
            if (go == null || !go.activeSelf || go.transform.IsChildOf(ui.Root.transform)) continue;
            Undo.RecordObject(go, "Hide old login UI");
            go.SetActive(false);
            report.Add("もとの部品を非表示にしました: " + PathOf(go.transform));
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(ui.Root.scene);
        Selection.activeGameObject = ui.Root;

        EditorUtility.DisplayDialog("Login Form",
            "ログイン欄を作りました: " + PathOf(ui.Root.transform) + "\n\n" +
            string.Join("\n", report.Select(r => "・" + r)) +
            "\n\nCtrl+S でシーンを保存してください。元に戻すときは Ctrl+Z。", "OK");
    }

    private static int CountRefs(AuthPanel p)
    {
        var so = new SerializedObject(p);
        int n = 0;
        var it = so.GetIterator();
        while (it.NextVisible(true))
            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null) n++;
        return n;
    }

    // ==================================================================
    // Form
    // ==================================================================
    private class Built
    {
        public GameObject Root;
        public TMP_InputField Email, Password;
        public Button SignIn, CreateAccount, ShowPassword, Forgot;
        public GameObject ShowLabel, HideLabel;
        public Toggle Remember;
        public TMP_Text Status;
    }

    private static Built BuildForm(Transform container)
    {
        var b = new Built();

        // Panel いっぱいに置き、幅 360 の基準で拡大する
        var root = Node(FormName, container);
        Stretch(root);
        b.Root = root.gameObject;

        var content = Node("Content", root);
        var fit = content.gameObject.AddComponent<LoginScreenFit>();
        var fitSo = new SerializedObject(fit);
        fitSo.FindProperty("designWidth").floatValue = DesignWidth;
        fitSo.FindProperty("minDesignHeight").floatValue = 330f;
        fitSo.FindProperty("useSafeArea").boolValue = false;
        fitSo.ApplyModifiedPropertiesWithoutUndo();

        var column = Node("Column", content);
        column.anchorMin = new Vector2(0.5f, 0f);
        column.anchorMax = new Vector2(0.5f, 1f);
        column.pivot = new Vector2(0.5f, 1f);
        column.sizeDelta = new Vector2(ColumnWidth, 0f);
        column.anchoredPosition = Vector2.zero;
        var v = column.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(0, 0, (int)TopSpace, 0);
        v.spacing = 3f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        // Email
        Row(Label(column, "Email Label", "Email", 14f, TextColor, TextAlignmentOptions.BottomLeft), 18f);
        b.Email = Input(column, "Email Input", "name@example.com", TMP_InputField.ContentType.EmailAddress, 0f);
        Spacer(column, 4f);

        // Password + Forgot password?
        var pwRow = Node("Password Row", column);
        Row(pwRow, 18f);
        var pwLabel = Label(pwRow, "Password Label", "Password", 14f, TextColor, TextAlignmentOptions.BottomLeft);
        Stretch(pwLabel.rectTransform);
        var forgot = Label(pwRow, "Forgot Password", "<u>Forgot password?</u>", 11f, SubText, TextAlignmentOptions.BottomRight);
        forgot.richText = true;
        forgot.raycastTarget = true;
        Stretch(forgot.rectTransform);
        forgot.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        b.Forgot = forgot.gameObject.AddComponent<Button>();
        b.Forgot.targetGraphic = forgot;

        b.Password = Input(column, "Password Input", "Password", TMP_InputField.ContentType.Password, 44f);
        var show = Node("Show Password", b.Password.transform);
        show.anchorMin = new Vector2(1f, 0f);
        show.anchorMax = new Vector2(1f, 1f);
        show.pivot = new Vector2(1f, 0.5f);
        show.sizeDelta = new Vector2(42f, 0f);
        show.anchoredPosition = Vector2.zero;
        var showHit = show.gameObject.AddComponent<Image>();
        showHit.color = new Color(0f, 0f, 0f, 0f);
        b.ShowPassword = show.gameObject.AddComponent<Button>();
        b.ShowPassword.targetGraphic = showHit;
        var showLabel = Label(show, "Show", "Show", 11f, DarkText, TextAlignmentOptions.Center);
        Stretch(showLabel.rectTransform);
        var hideLabel = Label(show, "Hide", "Hide", 11f, DarkText, TextAlignmentOptions.Center);
        Stretch(hideLabel.rectTransform);
        hideLabel.gameObject.SetActive(false);
        b.ShowLabel = showLabel.gameObject;
        b.HideLabel = hideLabel.gameObject;
        Spacer(column, 2f);

        // Remember this device (Unity 標準のトグルと同じ作り)
        b.Remember = Toggle(column, "Remember Toggle", "Remember this device");
        Spacer(column, 4f);

        // Sign In / 状態表示 / Create Account
        b.SignIn = StandardButton(column, "Sign In Button", "Sign In", 30f, 18f);
        b.Status = Label(column, "Status", "", 11f, TextColor, TextAlignmentOptions.Top, wrap: true);
        Row(b.Status, 26f);

        Row(Label(column, "Create Account Hint", "Don't have an account?", 11f, SubText, TextAlignmentOptions.Bottom), 16f);
        b.CreateAccount = StandardButton(column, "Create Account Button", "Create Account", 26f, 14f);
        return b;
    }

    // ==================================================================
    // Pieces (Unity 標準の UI と同じ見た目)
    // ==================================================================
    private static TMP_InputField Input(RectTransform parent, string name, string placeholder,
        TMP_InputField.ContentType type, float rightPad)
    {
        var rt = Node(name, parent);
        Row(rt, 30f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = _inputSprite;
        bg.type = Image.Type.Sliced;
        bg.color = Color.white;

        var area = Node("Text Area", rt);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(10f, 6f);
        area.offsetMax = new Vector2(-(10f + rightPad), -7f);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = Label(area, "Placeholder", placeholder, 14f, new Color(0.196f, 0.196f, 0.196f, 0.5f), TextAlignmentOptions.Left);
        ph.fontStyle = FontStyles.Italic;
        Stretch(ph.rectTransform);
        var text = Label(area, "Text", "", 14f, DarkText, TextAlignmentOptions.Left);
        Stretch(text.rectTransform);

        var input = rt.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = ph;
        input.fontAsset = _font;
        input.pointSize = 14f;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = type;
        input.richText = false;
        return input;
    }

    private static Button StandardButton(RectTransform parent, string name, string label, float height, float fontSize)
    {
        var rt = Node(name, parent);
        Row(rt, height);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = _uiSprite;
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        var text = Label(rt, "Text (TMP)", label, fontSize, DarkText, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        return button;
    }

    private static Toggle Toggle(RectTransform parent, string name, string label)
    {
        var rt = Node(name, parent);
        Row(rt, 20f);

        var bg = Node("Background", rt);
        bg.anchorMin = bg.anchorMax = new Vector2(0f, 0.5f);
        bg.pivot = new Vector2(0f, 0.5f);
        bg.anchoredPosition = Vector2.zero;
        bg.sizeDelta = new Vector2(20f, 20f);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = _uiSprite;
        bgImg.type = Image.Type.Sliced;

        var check = Node("Checkmark", bg);
        check.anchorMin = check.anchorMax = new Vector2(0.5f, 0.5f);
        check.sizeDelta = new Vector2(20f, 20f);
        var checkImg = check.gameObject.AddComponent<Image>();
        checkImg.sprite = _checkmark;

        var text = Label(rt, "Label", label, 14f, TextColor, TextAlignmentOptions.Left);
        var trt = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(26f, 0f);
        trt.offsetMax = Vector2.zero;

        var toggle = rt.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = checkImg;
        toggle.isOn = true;
        return toggle;
    }

#pragma warning disable 618 // enableWordWrapping (TextMeshPro 3.0)
    private static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color,
        TextAlignmentOptions align, bool wrap = false)
    {
        var rt = Node(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.enableWordWrapping = wrap;
        t.raycastTarget = false;
        return t;
    }
#pragma warning restore 618

    private static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Row(Component c, float height)
    {
        var le = c.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }

    private static void Spacer(RectTransform parent, float height) => Row(Node("Space", parent), height);

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ==================================================================
    // Assets
    // ==================================================================
    private static void LoadAssets(Transform near)
    {
        _uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        _inputSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
        _checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");

        // ほかの文字と同じフォントを使う (同じ Canvas にある文字のフォント → なければ TextMeshPro の標準)
        var canvas = near.GetComponentInParent<Canvas>(true);
        _font = canvas != null
            ? canvas.GetComponentsInChildren<TMP_Text>(true).Select(t => t.font).FirstOrDefault(f => f != null)
            : null;
        if (_font == null) _font = TMP_Settings.defaultFontAsset;
    }

    // ==================================================================
    // Misc
    // ==================================================================
    private static List<GameObject> FindAllInScene(string name)
    {
        return Object.FindObjectsOfType<RectTransform>(true)
            .Where(t => t.name == name && t.gameObject.scene.IsValid())
            .Select(t => t.gameObject)
            .ToList();
    }

    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
