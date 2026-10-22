using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// メニュー「Tools > Repeat Practice > Create Repeat Screen」で、リピーティング練習の画面 (repeat screen) を今のシーンに組み立てる。
///
/// - お題スピーキングの画面 (topic screen) と同じ作り。その隣に置く
/// - Diary のタブボタンを複製して「Repeat」ボタンを作り、Topic の右に並べる
/// - ほかの画面へ切り替えるボタンには「repeat screen も隠す」を追加する
///
/// Ctrl+Z で元に戻せる。シーンの保存 (Ctrl+S) は自分で行う。
/// (部品の作り方とタブの処理は TopicScreenBuilder と同じ)
/// </summary>
public static class RepeatScreenBuilder
{
    private const string ScreenName = "repeat screen";
    private const string ManagerName = "repeat manager";
    private const string TabLabel = "Repeat";

    private static readonly Color PanelColor = new Color(0.176f, 0.176f, 0.176f, 0.392f);
    private static readonly Color ButtonColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    private static readonly Color SubTextColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    [MenuItem("Tools/Repeat Practice/Create Repeat Screen")]
    private static void Create()
    {
        if (Object.FindObjectsOfType<RepeatPracticeManager>(true).Length > 0)
        {
            EditorUtility.DisplayDialog("Repeat Practice",
                "このシーンにはすでに RepeatPracticeManager があります。\n作り直す場合は、先に「" + ScreenName + "」を削除してください。",
                "OK");
            return;
        }

        var diary = Object.FindObjectsOfType<DiaryManager>(true).FirstOrDefault();
        if (diary == null || diary.transform.parent == null)
        {
            EditorUtility.DisplayDialog("Repeat Practice",
                "DiaryManager が見つかりません。Diary 画面のあるシーン (SampleScene) を開いてから実行してください。", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Create Repeat Screen");
        int undoGroup = Undo.GetCurrentGroup();

        Transform diaryScreen = diary.transform.parent;
        // お題の画面があれば、その隣に置く (なければ要約練習、それもなければ Diary の隣)
        var topic = Object.FindObjectsOfType<TopicPracticeManager>(true).FirstOrDefault();
        var summary = Object.FindObjectsOfType<SummaryPracticeManager>(true).FirstOrDefault();
        Transform after = topic != null && topic.transform.parent != null ? topic.transform.parent
            : summary != null && summary.transform.parent != null ? summary.transform.parent : diaryScreen;

        GameObject screen = BuildScreen(diaryScreen, after);
        string navResult = SetUpNavigation(diaryScreen.gameObject, screen);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(screen.scene);
        Selection.activeGameObject = screen;

        EditorUtility.DisplayDialog("Repeat Practice",
            "「" + ScreenName + "」を作りました。\n\n" + navResult +
            "\n\n確認したら Ctrl+S でシーンを保存してください (Ctrl+Z で元に戻せます)。", "OK");
    }

    // ------------------------------------------------------------
    // 画面
    // ------------------------------------------------------------
    private static GameObject BuildScreen(Transform diaryScreen, Transform after)
    {
        var screen = new GameObject(ScreenName);
        Undo.RegisterCreatedObjectUndo(screen, "Create Repeat Screen");
        screen.transform.SetParent(diaryScreen.parent, false);
        screen.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);

        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(screen.transform, false);
        canvasGo.layer = LayerMask.NameToLayer("UI");
        CopyCanvasSettings(diaryScreen.GetComponentInChildren<Canvas>(true), canvasGo);
        Transform canvas = canvasGo.transform;

        var bg = CreateImage("Background", canvas, PanelColor);
        bg.raycastTarget = false;
        Stretch(bg.rectTransform, 0f, 0f, 1f, 1f);

        var root = new GameObject("Root", typeof(RectTransform));
        root.transform.SetParent(canvas, false);
        Stretch((RectTransform)root.transform, 0f, 0.02f, 1f, 0.78f);
        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 16, 16);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var header = CreateText("HeaderText", root.transform, "Repeat", 34, TextAlignmentOptions.Center);
        header.color = SubTextColor;
        Layout(header.gameObject, preferredHeight: 55);

        // 英文 (言い返した後だけ、枠で囲んで出す)
        var sentence = CreateText("SentenceText", root.transform, "", 44, TextAlignmentOptions.Center);
        sentence.fontStyle = FontStyles.Bold;
        sentence.enableAutoSizing = true;
        sentence.fontSizeMin = 24;
        sentence.fontSizeMax = 44;
        Layout(sentence.gameObject, preferredHeight: 200);

        var status = CreateText("StatusText", root.transform, "", 32, TextAlignmentOptions.Center);
        status.enableAutoSizing = true;
        status.fontSizeMin = 20;
        status.fontSizeMax = 34;
        Layout(status.gameObject, preferredHeight: 90);

        // 聞き直し・言う
        var listenRow = new GameObject("ListenRow", typeof(RectTransform));
        listenRow.transform.SetParent(root.transform, false);
        var lhlg = listenRow.AddComponent<HorizontalLayoutGroup>();
        lhlg.spacing = 16;
        lhlg.childControlWidth = lhlg.childControlHeight = true;
        lhlg.childForceExpandWidth = lhlg.childForceExpandHeight = true;
        Layout(listenRow, preferredHeight: 110);
        var (again, _) = CreateButton("AgainButton", listenRow.transform, "Again");
        var (slow, _) = CreateButton("SlowButton", listenRow.transform, "Slow");
        var (speak, speakLabel) = CreateButton("SpeakButton", listenRow.transform, "Speak");
        Layout(speak.gameObject, flexibleWidth: 2);

        var (mainButton, mainLabel) = CreateButton("MainButton", root.transform, "Start");
        Layout(mainButton.gameObject, preferredHeight: 110);

        // 結果・まとめ (長くなるので縦スクロール)
        var scrollImage = CreateImage("ResultScroll", root.transform, new Color(0f, 0f, 0f, 0.25f));
        Layout(scrollImage.gameObject, minHeight: 260, flexibleHeight: 5);
        scrollImage.gameObject.AddComponent<RectMask2D>();
        var scroll = scrollImage.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;
        scroll.viewport = scrollImage.rectTransform;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(scrollImage.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
        var cvlg = content.AddComponent<VerticalLayoutGroup>();
        cvlg.padding = new RectOffset(20, 20, 20, 20);
        cvlg.spacing = 16;
        cvlg.childControlWidth = cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = true;
        cvlg.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;

        var result = CreateText("ResultText", content.transform, "", 32, TextAlignmentOptions.TopLeft);

        var loading = CreateText("LoadingIndicator", scrollImage.transform, "Loading...", 36, TextAlignmentOptions.Center);
        loading.raycastTarget = false;
        Stretch(loading.rectTransform, 0f, 1f, 1f, 1f);
        loading.rectTransform.pivot = new Vector2(0.5f, 1f);
        loading.rectTransform.sizeDelta = new Vector2(0f, 80f);
        loading.gameObject.SetActive(false);

        var credit = CreateText("CreditText", root.transform, "", 20, TextAlignmentOptions.Center);
        credit.color = SubTextColor;
        credit.enableAutoSizing = true;
        credit.fontSizeMin = 14;
        credit.fontSizeMax = 20;
        Layout(credit.gameObject, preferredHeight: 40);

        var manager = new GameObject(ManagerName);
        manager.transform.SetParent(screen.transform, false);
        manager.AddComponent<AudioSource>();
        var audioPlayer = manager.AddComponent<RepeatAudioPlayer>();
        var practice = manager.AddComponent<RepeatPracticeManager>();

        var so = new SerializedObject(practice);
        so.FindProperty("mainButton").objectReferenceValue = mainButton;
        so.FindProperty("mainButtonLabel").objectReferenceValue = mainLabel;
        so.FindProperty("againButton").objectReferenceValue = again;
        so.FindProperty("slowButton").objectReferenceValue = slow;
        so.FindProperty("speakButton").objectReferenceValue = speak;
        so.FindProperty("speakButtonLabel").objectReferenceValue = speakLabel;
        so.FindProperty("headerText").objectReferenceValue = header;
        so.FindProperty("sentenceText").objectReferenceValue = sentence;
        so.FindProperty("resultText").objectReferenceValue = result;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("creditText").objectReferenceValue = credit;
        so.FindProperty("loadingIndicator").objectReferenceValue = loading.gameObject;
        so.FindProperty("player").objectReferenceValue = audioPlayer;
        so.FindProperty("supabase").objectReferenceValue = Object.FindObjectsOfType<SupabaseService>(true).FirstOrDefault();
        so.ApplyModifiedPropertiesWithoutUndo();

        screen.SetActive(false);   // タブボタンで開く
        return screen;
    }

    private static void CopyCanvasSettings(Canvas source, GameObject target)
    {
        var canvas = target.AddComponent<Canvas>();
        var scaler = target.AddComponent<CanvasScaler>();
        var raycaster = target.AddComponent<GraphicRaycaster>();
        if (source == null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return;
        }

        CopyValues(source, canvas);
        CopyValues(source.GetComponent<CanvasScaler>(), scaler);
        CopyValues(source.GetComponent<GraphicRaycaster>(), raycaster);
    }

    private static void CopyValues(Component source, Component target)
    {
        if (source == null) return;
        if (UnityEditorInternal.ComponentUtility.CopyComponent(source))
            UnityEditorInternal.ComponentUtility.PasteComponentValues(target);
    }

    // ------------------------------------------------------------
    // タブボタンと画面の切り替え
    // ------------------------------------------------------------
    private static string SetUpNavigation(GameObject diaryScreen, GameObject repeatScreen)
    {
        var buttons = Object.FindObjectsOfType<Button>(true)
            .Where(b => b.gameObject.scene == repeatScreen.scene)
            .ToList();

        // Diary 画面を表示するボタン = Diary のタブ
        Button diaryTab = buttons.FirstOrDefault(b => GetSetActiveCalls(b).Any(c => c.target == diaryScreen && c.value));
        if (diaryTab == null)
            return "⚠ Diary 画面を開くボタンが見つからなかったので、タブボタンは作っていません。";

        // Diary タブと「Diary 画面を隠す」ボタン (Summary・Topic タブなど) には、「repeat screen も隠す」を追加する
        int updated = 0;
        foreach (var b in buttons)
        {
            if (b != diaryTab && !GetSetActiveCalls(b).Any(c => c.target == diaryScreen && !c.value)) continue;

            Undo.RecordObject(b, "Create Repeat Screen");
            UnityEventTools.AddBoolPersistentListener(b.onClick, repeatScreen.SetActive, false);
            EditorUtility.SetDirty(b);
            updated++;
        }

        // Diary のタブを複製して Repeat タブを作る
        var tabGo = Object.Instantiate(diaryTab.gameObject, diaryTab.transform.parent);
        Undo.RegisterCreatedObjectUndo(tabGo, "Create Repeat Screen");
        tabGo.name = TabLabel;
        // Topic タブの右に置く (なければ Summary、それもなければ Diary の右)
        Transform TabNamed(string n) => diaryTab.transform.parent.Cast<Transform>()
            .FirstOrDefault(t => t != tabGo.transform && t.name == n && t.GetComponent<Button>() != null);
        Transform leftOfNew = TabNamed("Topic") ?? TabNamed("Summary") ?? diaryTab.transform;
        tabGo.transform.SetSiblingIndex(leftOfNew.GetSiblingIndex() + 1);
        var label = tabGo.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = TabLabel;
        else
        {
            var legacy = tabGo.GetComponentInChildren<Text>(true);
            if (legacy != null) legacy.text = TabLabel;
        }

        // クリック時の動作: Diary タブと同じものを隠し、Diary タブが出すもの (Diary 画面・添削エンジン選択など) は隠す
        var tab = tabGo.GetComponent<Button>();
        var diaryCalls = GetSetActiveCalls(diaryTab);
        for (int i = tab.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(tab.onClick, i);

        UnityEventTools.AddBoolPersistentListener(tab.onClick, repeatScreen.SetActive, true);
        // (Diary タブには上で「repeat screen を隠す」を足したので、それは写さない。写すと Repeat タブが自分の画面を隠してしまう)
        foreach (var c in diaryCalls)
        {
            if (c.target == repeatScreen) continue;
            UnityEventTools.AddBoolPersistentListener(tab.onClick, c.target.SetActive, false);
        }

        string layout = RearrangeTabs(leftOfNew.GetComponent<Button>(), tab)
            ? "タブを並べ直しました。"
            : "⚠ タブの並べ直しはできなかったので、「" + TabLabel + "」ボタンの位置を手で調整してください。";

        return $"「{TabLabel}」タブを追加しました。{layout}\n" +
               $"ほかの画面へ切り替えるボタン {updated} 個 (Diary タブを含む) に「{ScreenName} を隠す」を追加しました。";
    }

    private struct SetActiveCall
    {
        public GameObject target;
        public bool value;
    }

    // ボタンの OnClick に登録された GameObject.SetActive(bool) の一覧
    private static List<SetActiveCall> GetSetActiveCalls(Button button)
    {
        var result = new List<SetActiveCall>();
        var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
        if (calls == null) return result;

        for (int i = 0; i < calls.arraySize; i++)
        {
            var call = calls.GetArrayElementAtIndex(i);
            if (call.FindPropertyRelative("m_MethodName").stringValue != "SetActive") continue;
            if (!(call.FindPropertyRelative("m_Target").objectReferenceValue is GameObject target)) continue;

            result.Add(new SetActiveCall
            {
                target = target,
                value = call.FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue
            });
        }
        return result;
    }

    // 横一列に並んだタブ (左隣のタブと同じ高さにあるボタン) を、今と同じ幅の中に等間隔で並べ直す
    private static bool RearrangeTabs(Button diaryTab, Button newTab)
    {
        var diaryRect = (RectTransform)diaryTab.transform;
        var row = diaryTab.transform.parent.Cast<Transform>()
            .Where(t => t != newTab.transform && t.GetComponent<Button>() != null && t is RectTransform)
            .Cast<RectTransform>()
            .Where(r => Mathf.Abs(r.anchoredPosition.y - diaryRect.anchoredPosition.y) < 1f
                        && r.anchorMin == diaryRect.anchorMin && r.anchorMax == diaryRect.anchorMax)
            .OrderBy(r => r.anchoredPosition.x)
            .ToList();
        if (row.Count < 2) return false;

        float Width(RectTransform r) => r.sizeDelta.x * r.localScale.x;
        float left = row.Min(r => r.anchoredPosition.x - Width(r) / 2f);
        float right = row.Max(r => r.anchoredPosition.x + Width(r) / 2f);
        float fill = row.Average(Width) / ((right - left) / row.Count);   // ボタンの幅 / 1つ分の枠 (すき間の割合を保つ)

        // 左隣のタブのすぐ右に入れる
        row.Insert(row.IndexOf(diaryRect) + 1, (RectTransform)newTab.transform);
        float slot = (right - left) / row.Count;

        for (int i = 0; i < row.Count; i++)
        {
            var r = row[i];
            Undo.RecordObject(r, "Create Repeat Screen");
            var scale = r.localScale;
            scale.x = slot * fill / r.sizeDelta.x;
            r.localScale = scale;
            r.anchoredPosition = new Vector2(left + slot * (i + 0.5f), r.anchoredPosition.y);
        }
        return true;
    }

    // ------------------------------------------------------------
    // UI の部品
    // ------------------------------------------------------------
    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        image.type = Image.Type.Sliced;
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        return tmp;
    }

    private static (Button, TMP_Text) CreateButton(string name, Transform parent, string text)
    {
        var image = CreateImage(name, parent, ButtonColor);
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        var label = CreateText("Text (TMP)", image.transform, text, 36, TextAlignmentOptions.Center);
        label.enableAutoSizing = true;
        label.fontSizeMin = 18;
        label.fontSizeMax = 40;
        label.raycastTarget = false;
        Stretch(label.rectTransform, 0f, 0f, 1f, 1f);
        label.rectTransform.offsetMin = new Vector2(12f, 8f);
        label.rectTransform.offsetMax = new Vector2(-12f, -8f);
        return (button, label);
    }

    private static void Stretch(RectTransform r, float minX, float minY, float maxX, float maxY)
    {
        r.anchorMin = new Vector2(minX, minY);
        r.anchorMax = new Vector2(maxX, maxY);
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static void Layout(GameObject go, float minHeight = -1, float preferredHeight = -1,
                               float flexibleHeight = -1, float flexibleWidth = -1)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.preferredHeight = preferredHeight;
        le.flexibleHeight = flexibleHeight;
        le.flexibleWidth = flexibleWidth;
    }
}
