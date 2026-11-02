using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// メニュー「Tools > Vocabulary > Create Vocabulary Screen」で、単語帳の画面 (vocab screen) を今のシーンに組み立てる。
///
/// - リピーティングの画面 (repeat screen) と同じ作り。その隣に置く
/// - Diary のタブボタンを複製して「Vocab」ボタンを作り、Repeat の右に並べる
/// - ほかの画面へ切り替えるボタンには「vocab screen も隠す」を追加する
/// - 画面の中は Today / Words / Add / Settings の 4 つ (VocabManager が切り替える)
///
/// Ctrl+Z で元に戻せる。シーンの保存 (Ctrl+S) は自分で行う。
/// (部品の作り方とタブの処理は RepeatScreenBuilder と同じ)
/// </summary>
public static class VocabScreenBuilder
{
    private const string ScreenName = "vocab screen";
    private const string ManagerName = "vocab manager";
    private const string TabLabel = "Vocab";
    private const string Title = "Vocabulary";

    private static readonly Color PanelColor = new Color(0.176f, 0.176f, 0.176f, 0.392f);
    private static readonly Color ButtonColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    private static readonly Color CardColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
    private static readonly Color SubTextColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    [MenuItem("Tools/Vocabulary/Create Vocabulary Screen")]
    private static void Create()
    {
        if (Object.FindObjectsOfType<VocabManager>(true).Length > 0)
        {
            EditorUtility.DisplayDialog(Title,
                "このシーンにはすでに VocabManager があります。\n作り直す場合は、先に「" + ScreenName + "」を削除してください。", "OK");
            return;
        }

        var diary = Object.FindObjectsOfType<DiaryManager>(true).FirstOrDefault();
        if (diary == null || diary.transform.parent == null)
        {
            EditorUtility.DisplayDialog(Title,
                "DiaryManager が見つかりません。Diary 画面のあるシーン (SampleScene) を開いてから実行してください。", "OK");
            return;
        }

        Undo.SetCurrentGroupName("Create Vocabulary Screen");
        int undoGroup = Undo.GetCurrentGroup();

        Transform diaryScreen = diary.transform.parent;
        // リピーティングの画面があれば、その隣に置く (なければ Diary の隣)
        var repeat = Object.FindObjectsOfType<RepeatPracticeManager>(true).FirstOrDefault();
        Transform after = repeat != null && repeat.transform.parent != null ? repeat.transform.parent : diaryScreen;

        GameObject screen = BuildScreen(diaryScreen, after);
        string navResult = SetUpNavigation(diaryScreen.gameObject, screen);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(screen.scene);
        Selection.activeGameObject = screen;

        EditorUtility.DisplayDialog(Title,
            "「" + ScreenName + "」を作りました。\n\n" + navResult +
            "\n\n確認したら Ctrl+S でシーンを保存してください (Ctrl+Z で元に戻せます)。", "OK");
    }

    // ------------------------------------------------------------
    // 画面
    // ------------------------------------------------------------
    private static GameObject BuildScreen(Transform diaryScreen, Transform after)
    {
        var screen = new GameObject(ScreenName);
        Undo.RegisterCreatedObjectUndo(screen, "Create Vocabulary Screen");
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

        var root = CreateBox("Root", canvas, vertical: true, spacing: 10);
        Stretch((RectTransform)root.transform, 0f, 0.02f, 1f, 0.78f);
        root.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(24, 24, 12, 12);

        var header = CreateText("HeaderText", root.transform, "Vocabulary", 34, TextAlignmentOptions.Center);
        header.color = SubTextColor;
        Layout(header.gameObject, preferredHeight: 50);

        // 上の 4 つの切り替え
        var tabRow = CreateBox("SubTabs", root.transform, vertical: false, spacing: 10);
        Layout(tabRow, preferredHeight: 90);
        var (todayTab, _) = CreateButton("TodayTab", tabRow.transform, "Today");
        var (wordsTab, _) = CreateButton("WordsTab", tabRow.transform, "Words");
        var (addTab, _) = CreateButton("AddTab", tabRow.transform, "Add");
        var (settingsTab, _) = CreateButton("SettingsTab", tabRow.transform, "Settings");

        var m = new Dictionary<string, Object>
        {
            ["todayTab"] = todayTab, ["wordsTab"] = wordsTab, ["addTab"] = addTab, ["settingsTab"] = settingsTab,
        };
        var arrays = new Dictionary<string, Object[]>();

        BuildToday(root.transform, m, arrays);
        BuildWords(root.transform, m, arrays);
        BuildAdd(root.transform, m);
        BuildSettings(root.transform, m, arrays);

        var status = CreateText("StatusText", root.transform, "", 28, TextAlignmentOptions.Center);
        status.enableAutoSizing = true;
        status.fontSizeMin = 18;
        status.fontSizeMax = 30;
        Layout(status.gameObject, preferredHeight: 70);
        m["statusText"] = status;

        var loading = CreateText("LoadingIndicator", root.transform, "Loading...", 32, TextAlignmentOptions.Center);
        loading.raycastTarget = false;
        var le = loading.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        Stretch(loading.rectTransform, 0f, 0.5f, 1f, 0.5f);
        loading.rectTransform.sizeDelta = new Vector2(0f, 80f);
        loading.gameObject.SetActive(false);
        m["loadingIndicator"] = loading.gameObject;

        var manager = new GameObject(ManagerName);
        manager.transform.SetParent(screen.transform, false);
        manager.AddComponent<AudioSource>();
        m["audioPlayer"] = manager.AddComponent<VocabAudio>();
        m["recorder"] = manager.AddComponent<AudioRecorder>();
        m["nativeStt"] = manager.AddComponent<NativeSpeechRecognizer>();
        var vocab = manager.AddComponent<VocabManager>();
        m["supabase"] = Object.FindObjectsOfType<SupabaseService>(true).FirstOrDefault();

        var so = new SerializedObject(vocab);
        foreach (var kv in m)
        {
            var p = so.FindProperty(kv.Key);
            if (p == null) { Debug.LogError("[VocabScreenBuilder] no field: " + kv.Key); continue; }
            p.objectReferenceValue = kv.Value;
        }
        foreach (var kv in arrays)
        {
            var p = so.FindProperty(kv.Key);
            if (p == null) { Debug.LogError("[VocabScreenBuilder] no field: " + kv.Key); continue; }
            p.arraySize = kv.Value.Length;
            for (int i = 0; i < kv.Value.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = kv.Value[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        screen.SetActive(false);   // タブボタンで開く
        return screen;
    }

    // ---------------- Today ----------------
    private static void BuildToday(Transform root, Dictionary<string, Object> m, Dictionary<string, Object[]> arrays)
    {
        var panel = CreateBox("TodayPanel", root, vertical: true, spacing: 10);
        Layout(panel, flexibleHeight: 1);
        m["todayPanel"] = panel;

        var progress = CreateText("ProgressText", panel.transform, "", 30, TextAlignmentOptions.Center);
        progress.color = SubTextColor;
        Layout(progress.gameObject, preferredHeight: 50);
        m["progressText"] = progress;

        var levelRow = CreateBox("LevelPickRow", panel.transform, vertical: false, spacing: 12);
        Layout(levelRow, preferredHeight: 110);
        m["levelPickRow"] = levelRow;
        arrays["levelPickButtons"] = new[] { "A1", "A2", "B1", "B2" }
            .Select(l => (Object)CreateButton("Level" + l, levelRow.transform, l).Item1).ToArray();

        // カード (タップでめくる。裏は長いので縦スクロール)
        var cardArea = CreateBox("CardArea", panel.transform, vertical: true, spacing: 10);
        Layout(cardArea, flexibleHeight: 1);
        m["cardArea"] = cardArea;
        var (cardImage, cardContent) = CreateScroll("Card", cardArea.transform, CardColor);
        Layout(cardImage.gameObject, minHeight: 300, flexibleHeight: 1);
        var cardButton = cardImage.gameObject.AddComponent<Button>();
        cardButton.targetGraphic = cardImage;
        m["cardButton"] = cardButton;
        var cardText = CreateText("CardText", cardContent, "", 36, TextAlignmentOptions.Top);
        cardText.raycastTarget = false;
        m["cardText"] = cardText;

        var voiceRow = CreateBox("VoiceRow", cardArea.transform, vertical: false, spacing: 16);
        Layout(voiceRow, preferredHeight: 100);
        var (sound, _) = CreateButton("SoundButton", voiceRow.transform, "Sound");
        var (say, sayLabel) = CreateButton("SayButton", voiceRow.transform, "Say it");
        Layout(say.gameObject, flexibleWidth: 2);
        m["soundButton"] = sound;
        m["sayButton"] = say;
        m["sayLabel"] = sayLabel;

        var ratingRow = CreateBox("RatingRow", panel.transform, vertical: false, spacing: 10);
        Layout(ratingRow, preferredHeight: 120);
        m["ratingRow"] = ratingRow;
        var ratings = new[] { "Didn't know", "Hard", "Knew it", "Easy" }
            .Select((t, i) => CreateButton("Rating" + (i + 1), ratingRow.transform, t)).ToList();
        arrays["ratingButtons"] = ratings.Select(r => (Object)r.Item1).ToArray();
        arrays["ratingLabels"] = ratings.Select(r => (Object)r.Item2).ToArray();

        var hint = CreateText("RatingHint", panel.transform, "", 24, TextAlignmentOptions.Center);
        hint.color = SubTextColor;
        Layout(hint.gameObject, preferredHeight: 40);
        m["ratingHint"] = hint;

        // まとめ
        var (summaryImage, summaryContent) = CreateScroll("SummaryScroll", panel.transform, new Color(0f, 0f, 0f, 0.25f));
        Layout(summaryImage.gameObject, minHeight: 300, flexibleHeight: 1);
        m["summaryScroll"] = summaryImage.gameObject;
        m["summaryContent"] = summaryContent;
        var rowTemplate = CreateBox("SummaryRowTemplate", summaryContent, vertical: true, spacing: 6);
        var rowText = CreateText("Text", rowTemplate.transform, "", 30, TextAlignmentOptions.TopLeft);
        rowText.raycastTarget = false;
        var rowButtons = CreateBox("Buttons", rowTemplate.transform, vertical: false, spacing: 12);
        Layout(rowButtons, preferredHeight: 70);
        CreateButton("MyVoice", rowButtons.transform, "My voice");
        CreateButton("Model", rowButtons.transform, "Model");
        rowTemplate.SetActive(false);
        m["summaryRowTemplate"] = rowTemplate;

        var summaryButtons = CreateBox("SummaryButtons", panel.transform, vertical: false, spacing: 10);
        Layout(summaryButtons, preferredHeight: 90);
        var (rescore, _) = CreateButton("RescoreButton", summaryButtons.transform, "Score again");
        var (weak, _) = CreateButton("WeakButton", summaryButtons.transform, "Practice weak words");
        var (levelUp, levelUpLabel) = CreateButton("LevelUpButton", summaryButtons.transform, "Level up?");
        m["rescoreButton"] = rescore;
        m["weakButton"] = weak;
        m["levelUpButton"] = levelUp;
        m["levelUpLabel"] = levelUpLabel;

        var (start, startLabel) = CreateButton("StartButton", panel.transform, "Start");
        Layout(start.gameObject, preferredHeight: 110);
        m["startButton"] = start;
        m["startLabel"] = startLabel;
    }

    // ---------------- Words ----------------
    private static void BuildWords(Transform root, Dictionary<string, Object> m, Dictionary<string, Object[]> arrays)
    {
        var panel = CreateBox("WordsPanel", root, vertical: true, spacing: 10);
        Layout(panel, flexibleHeight: 1);
        panel.SetActive(false);
        m["wordsPanel"] = panel;

        var toggles = CreateBox("StatusRow", panel.transform, vertical: false, spacing: 12);
        Layout(toggles, preferredHeight: 90);
        m["learningToggle"] = CreateButton("LearningToggle", toggles.transform, "Learning").Item1;
        m["knownToggle"] = CreateButton("KnownToggle", toggles.transform, "Known").Item1;

        var filters = CreateBox("FilterRow", panel.transform, vertical: false, spacing: 8);
        Layout(filters, preferredHeight: 80);
        arrays["filterButtons"] = new[] { "All", "A1", "A2", "B1", "B2", "—" }
            .Select((f, i) => (Object)CreateButton("Filter" + i, filters.transform, f).Item1).ToArray();

        var (listImage, listContent) = CreateScroll("WordList", panel.transform, new Color(0f, 0f, 0f, 0.25f));
        Layout(listImage.gameObject, minHeight: 300, flexibleHeight: 1);
        m["wordsContent"] = listContent;
        var (rowButton, rowLabel) = CreateButton("WordRowTemplate", listContent, "");
        rowLabel.alignment = TextAlignmentOptions.Left;
        rowLabel.enableAutoSizing = false;
        rowLabel.fontSize = 30;
        Layout(rowButton.gameObject, preferredHeight: 110);
        rowButton.gameObject.SetActive(false);
        m["wordRowTemplate"] = rowButton.gameObject;

        // 行をタップしたときの操作 (パネルの上に重ねる)
        var dialog = CreateImage("Dialog", panel.transform, new Color(0.05f, 0.05f, 0.05f, 0.95f));
        dialog.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch(dialog.rectTransform, 0.05f, 0.25f, 0.95f, 0.75f);
        var dvlg = dialog.gameObject.AddComponent<VerticalLayoutGroup>();
        dvlg.padding = new RectOffset(30, 30, 30, 30);
        dvlg.spacing = 16;
        dvlg.childControlWidth = dvlg.childControlHeight = true;
        dvlg.childForceExpandWidth = true;
        dvlg.childForceExpandHeight = false;
        var dialogText = CreateText("DialogText", dialog.transform, "", 34, TextAlignmentOptions.Center);
        Layout(dialogText.gameObject, preferredHeight: 100);
        var dialogButtons = Enumerable.Range(1, 3).Select(i =>
        {
            var b = CreateButton("DialogButton" + i, dialog.transform, "");
            Layout(b.Item1.gameObject, preferredHeight: 90);
            return b;
        }).ToList();
        dialog.gameObject.SetActive(false);
        m["dialogPanel"] = dialog.gameObject;
        m["dialogText"] = dialogText;
        arrays["dialogButtons"] = dialogButtons.Select(b => (Object)b.Item1).ToArray();
        arrays["dialogLabels"] = dialogButtons.Select(b => (Object)b.Item2).ToArray();
    }

    // ---------------- Add ----------------
    private static void BuildAdd(Transform root, Dictionary<string, Object> m)
    {
        var panel = CreateBox("AddPanel", root, vertical: true, spacing: 10);
        Layout(panel, flexibleHeight: 1);
        panel.SetActive(false);
        m["addPanel"] = panel;

        var searchRow = CreateBox("SearchRow", panel.transform, vertical: false, spacing: 12);
        Layout(searchRow, preferredHeight: 90);
        var search = CreateInput("SearchInput", searchRow.transform, "Word to look up");
        Layout(search.gameObject, flexibleWidth: 3);
        m["searchInput"] = search;
        m["searchButton"] = CreateButton("SearchButton", searchRow.transform, "Search").Item1;

        var (resultImage, resultContent) = CreateScroll("ResultScroll", panel.transform, new Color(0f, 0f, 0f, 0.25f));
        Layout(resultImage.gameObject, minHeight: 240, flexibleHeight: 1);
        var result = CreateText("ResultText", resultContent, "", 30, TextAlignmentOptions.TopLeft);
        m["resultText"] = result;
        var suggestions = CreateBox("Suggestions", resultContent, vertical: true, spacing: 8);
        m["suggestionContent"] = suggestions.transform;
        var (sugButton, _) = CreateButton("SuggestionTemplate", suggestions.transform, "");
        Layout(sugButton.gameObject, preferredHeight: 80);
        sugButton.gameObject.SetActive(false);
        m["suggestionTemplate"] = sugButton.gameObject;

        var resultRow = CreateBox("ResultButtons", panel.transform, vertical: false, spacing: 12);
        Layout(resultRow, preferredHeight: 90);
        m["addSoundButton"] = CreateButton("AddSoundButton", resultRow.transform, "Sound").Item1;
        var (addResult, _) = CreateButton("AddResultButton", resultRow.transform, "Add to my words");
        Layout(addResult.gameObject, flexibleWidth: 2);
        m["addResultButton"] = addResult;

        var manualTitle = CreateText("ManualTitle", panel.transform, "Add by hand", 28, TextAlignmentOptions.Left);
        manualTitle.color = SubTextColor;
        Layout(manualTitle.gameObject, preferredHeight: 44);
        var word = CreateInput("ManualWordInput", panel.transform, "Word");
        Layout(word.gameObject, preferredHeight: 80);
        m["manualWordInput"] = word;
        var meaning = CreateInput("ManualMeaningInput", panel.transform, "My meaning (Japanese is OK)");
        Layout(meaning.gameObject, preferredHeight: 80);
        m["manualMeaningInput"] = meaning;
        var (manualAdd, _) = CreateButton("ManualAddButton", panel.transform, "Add");
        Layout(manualAdd.gameObject, preferredHeight: 90);
        m["manualAddButton"] = manualAdd;
    }

    // ---------------- Settings ----------------
    private static void BuildSettings(Transform root, Dictionary<string, Object> m, Dictionary<string, Object[]> arrays)
    {
        var panel = CreateBox("SettingsPanel", root, vertical: true, spacing: 12);
        Layout(panel, flexibleHeight: 1);
        panel.SetActive(false);
        m["settingsPanel"] = panel;

        Object[] Row(string title, string name, string[] labels)
        {
            var t = CreateText(name + "Title", panel.transform, title, 28, TextAlignmentOptions.Left);
            t.color = SubTextColor;
            Layout(t.gameObject, preferredHeight: 44);
            var row = CreateBox(name + "Row", panel.transform, vertical: false, spacing: 10);
            Layout(row, preferredHeight: 90);
            return labels.Select((l, i) => (Object)CreateButton(name + i, row.transform, l).Item1).ToArray();
        }

        arrays["settingsLevelButtons"] = Row("My level", "Level", new[] { "A1", "A2", "B1", "B2" });
        arrays["sizeButtons"] = Row("Words per session", "Size", new[] { "5 words", "10 words", "15 words", "20 words" });
        arrays["retentionButtons"] = Row("Target recall", "Retention", new[] { "80%", "85%", "90%", "95%" });

        var credit = CreateText("CreditText", panel.transform, "", 22, TextAlignmentOptions.TopLeft);
        credit.color = SubTextColor;
        Layout(credit.gameObject, minHeight: 120, flexibleHeight: 1);
        m["creditText"] = credit;
    }

    // ------------------------------------------------------------
    // タブボタンと画面の切り替え (RepeatScreenBuilder と同じ)
    // ------------------------------------------------------------
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

    private static string SetUpNavigation(GameObject diaryScreen, GameObject vocabScreen)
    {
        var buttons = Object.FindObjectsOfType<Button>(true)
            .Where(b => b.gameObject.scene == vocabScreen.scene && !b.transform.IsChildOf(vocabScreen.transform))
            .ToList();

        Button diaryTab = buttons.FirstOrDefault(b => GetSetActiveCalls(b).Any(c => c.target == diaryScreen && c.value));
        if (diaryTab == null)
            return "Diary 画面を開くボタンが見つからなかったので、タブボタンは作っていません。";

        // Diary タブと「Diary 画面を隠す」ボタン (Summary・Topic・Repeat タブなど) には、「vocab screen も隠す」を追加する
        int updated = 0;
        foreach (var b in buttons)
        {
            if (b != diaryTab && !GetSetActiveCalls(b).Any(c => c.target == diaryScreen && !c.value)) continue;
            Undo.RecordObject(b, "Create Vocabulary Screen");
            UnityEventTools.AddBoolPersistentListener(b.onClick, vocabScreen.SetActive, false);
            EditorUtility.SetDirty(b);
            updated++;
        }

        var tabGo = Object.Instantiate(diaryTab.gameObject, diaryTab.transform.parent);
        Undo.RegisterCreatedObjectUndo(tabGo, "Create Vocabulary Screen");
        tabGo.name = TabLabel;
        Transform TabNamed(string n) => diaryTab.transform.parent.Cast<Transform>()
            .FirstOrDefault(t => t != tabGo.transform && t.name == n && t.GetComponent<Button>() != null);
        Transform leftOfNew = TabNamed("Repeat") ?? TabNamed("Topic") ?? TabNamed("Summary") ?? diaryTab.transform;
        tabGo.transform.SetSiblingIndex(leftOfNew.GetSiblingIndex() + 1);
        var label = tabGo.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = TabLabel;
        else
        {
            var legacy = tabGo.GetComponentInChildren<Text>(true);
            if (legacy != null) legacy.text = TabLabel;
        }

        var tab = tabGo.GetComponent<Button>();
        var diaryCalls = GetSetActiveCalls(diaryTab);
        for (int i = tab.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(tab.onClick, i);
        UnityEventTools.AddBoolPersistentListener(tab.onClick, vocabScreen.SetActive, true);
        foreach (var c in diaryCalls)
        {
            if (c.target == vocabScreen) continue;
            UnityEventTools.AddBoolPersistentListener(tab.onClick, c.target.SetActive, false);
        }

        string layout = RearrangeTabs(leftOfNew.GetComponent<Button>(), tab)
            ? "タブを並べ直しました。"
            : "タブの並べ直しはできなかったので、「" + TabLabel + "」ボタンの位置を手で調整してください。";

        return $"「{TabLabel}」タブを追加しました。{layout}\n" +
               $"ほかの画面へ切り替えるボタン {updated} 個 (Diary タブを含む) に「{ScreenName} を隠す」を追加しました。";
    }

    private struct SetActiveCall
    {
        public GameObject target;
        public bool value;
    }

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

    private static bool RearrangeTabs(Button leftTab, Button newTab)
    {
        var leftRect = (RectTransform)leftTab.transform;
        var row = leftTab.transform.parent.Cast<Transform>()
            .Where(t => t != newTab.transform && t.GetComponent<Button>() != null && t is RectTransform)
            .Cast<RectTransform>()
            .Where(r => Mathf.Abs(r.anchoredPosition.y - leftRect.anchoredPosition.y) < 1f
                        && r.anchorMin == leftRect.anchorMin && r.anchorMax == leftRect.anchorMax)
            .OrderBy(r => r.anchoredPosition.x)
            .ToList();
        if (row.Count < 2) return false;

        float Width(RectTransform r) => r.sizeDelta.x * r.localScale.x;
        float left = row.Min(r => r.anchoredPosition.x - Width(r) / 2f);
        float right = row.Max(r => r.anchoredPosition.x + Width(r) / 2f);
        float fill = row.Average(Width) / ((right - left) / row.Count);

        row.Insert(row.IndexOf(leftRect) + 1, (RectTransform)newTab.transform);
        float slot = (right - left) / row.Count;
        for (int i = 0; i < row.Count; i++)
        {
            var r = row[i];
            Undo.RecordObject(r, "Create Vocabulary Screen");
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
    private static GameObject CreateBox(string name, Transform parent, bool vertical, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        HorizontalOrVerticalLayoutGroup g = vertical
            ? (HorizontalOrVerticalLayoutGroup)go.AddComponent<VerticalLayoutGroup>()
            : go.AddComponent<HorizontalLayoutGroup>();
        g.spacing = spacing;
        g.childAlignment = TextAnchor.UpperCenter;
        g.childControlWidth = g.childControlHeight = true;
        g.childForceExpandWidth = true;
        g.childForceExpandHeight = !vertical;
        return go;
    }

    // 縦スクロールの枠 (Image + ScrollRect)。中身を入れる Content を返す
    private static (Image, Transform) CreateScroll(string name, Transform parent, Color color)
    {
        var image = CreateImage(name, parent, color);
        image.gameObject.AddComponent<RectMask2D>();
        var scroll = image.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;
        scroll.viewport = image.rectTransform;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(image.transform, false);
        var rect = (RectTransform)content.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 12;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = rect;
        return (image, content.transform);
    }

    private static TMP_InputField CreateInput(string name, Transform parent, string placeholder)
    {
        var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = name;
        go.transform.SetParent(parent, false);
        var input = go.GetComponent<TMP_InputField>();
        input.lineType = TMP_InputField.LineType.SingleLine;
        if (input.textComponent != null) input.textComponent.fontSize = 32;
        if (input.placeholder is TMP_Text ph)
        {
            ph.text = placeholder;
            ph.fontSize = 30;
        }
        return input;
    }

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

        var label = CreateText("Text (TMP)", image.transform, text, 32, TextAlignmentOptions.Center);
        label.enableAutoSizing = true;
        label.fontSizeMin = 16;
        label.fontSizeMax = 34;
        label.raycastTarget = false;
        Stretch(label.rectTransform, 0f, 0f, 1f, 1f);
        label.rectTransform.offsetMin = new Vector2(12f, 6f);
        label.rectTransform.offsetMax = new Vector2(-12f, -6f);
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
