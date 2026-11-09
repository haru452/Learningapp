using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 単語帳画面のボタンや行を小さくして、カード (タップでめくる所) を大きくする。
/// すでに作った画面には Tools → Vocabulary → Update Vocabulary Screen で当てる
/// (やめた Add タブ・Add 画面が残っていれば消す)。
/// 名前で探すので、VocabScreenBuilder で付けた名前を変えたらここも直す。
/// </summary>
public static class VocabLayoutTuner
{
    private const string UndoName = "Update Vocabulary Screen";
    private const float ButtonFontMax = 28f;
    private const float TabFontMax = 26f;
    private const float CardMinHeight = 600f;

    // 名前 → 高さ (preferredHeight)
    private static readonly Dictionary<string, float> Heights = new Dictionary<string, float>
    {
        // 上
        ["HeaderText"] = 40, ["SubTabs"] = 60,
        // Today
        ["ProgressText"] = 36, ["LevelPickRow"] = 80, ["VoiceRow"] = 76, ["RatingRow"] = 84,
        ["RatingHint"] = 32, ["SummaryButtons"] = 70, ["StartButton"] = 80,
        // Words
        ["StatusRow"] = 64, ["FilterRow"] = 60, ["WordRowTemplate"] = 80, ["DialogButton0"] = 70,
        ["DialogButton1"] = 70, ["DialogButton2"] = 70,
    };

    [MenuItem("Tools/Vocabulary/Update Vocabulary Screen")]
    private static void ApplyFromMenu()
    {
        var manager = Object.FindObjectsOfType<VocabManager>(true).FirstOrDefault();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Vocabulary", "このシーンに単語帳の画面がありません。先に Create Vocabulary Screen を実行してください。", "OK");
            return;
        }
        Transform screen = manager.transform.parent;   // VocabManager は "vocab screen" の直下にある
        if (screen == null)
        {
            EditorUtility.DisplayDialog("Vocabulary", "単語帳の画面の場所がわかりませんでした。", "OK");
            return;
        }

        Undo.SetCurrentGroupName(UndoName);
        int group = Undo.GetCurrentGroup();
        int removed = RemoveAddTab(screen.gameObject);
        int changed = Apply(screen.gameObject);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);

        EditorUtility.DisplayDialog("Vocabulary",
            $"{changed} か所の大きさを変えました。" + (removed > 0 ? "\nAdd タブと Add 画面を消しました。" : "") +
            "\n確認したら Ctrl+S で保存してください (Ctrl+Z で元に戻せます)。", "OK");
    }

    /// <summary>画面の下にある行・ボタンの大きさを変える。変えた数を返す</summary>
    public static int Apply(GameObject screen)
    {
        int changed = 0;
        foreach (Transform t in screen.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (Heights.TryGetValue(n, out float h)) changed += SetHeight(t.gameObject, h);
            else if (n == "Card") changed += SetCard(t.gameObject);
            else if (n.EndsWith("Row") && t.parent != null && t.parent.name == "SettingsPanel") changed += SetHeight(t.gameObject, 64);
            else if (n.EndsWith("Title") && t.parent != null && t.parent.name == "SettingsPanel") changed += SetHeight(t.gameObject, 36);
        }

        foreach (var button in screen.GetComponentsInChildren<Button>(true))
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null || !label.enableAutoSizing) continue;
            bool isTab = button.transform.parent != null && button.transform.parent.name == "SubTabs";
            Undo.RecordObject(label, UndoName);
            label.fontSizeMax = isTab ? TabFontMax : ButtonFontMax;
            changed++;
        }
        changed += VocabStyler.Apply(screen);   // 色・形 (Stitch のデザイン)
        return changed;
    }

    // Add はやめたので、前に作った画面に残っているボタンと画面を消す
    private static int RemoveAddTab(GameObject screen)
    {
        var old = screen.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "AddPanel" || (t.name == "AddTab" && t.parent != null && t.parent.name == "SubTabs"))
            .Select(t => t.gameObject).ToList();
        foreach (var go in old) Undo.DestroyObjectImmediate(go);
        return old.Count;
    }

    private static int SetHeight(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) return 0;
        Undo.RecordObject(le, UndoName);
        le.preferredHeight = height;
        return 1;
    }

    private static int SetCard(GameObject go)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null) return 0;
        Undo.RecordObject(le, UndoName);
        le.minHeight = CardMinHeight;
        le.flexibleHeight = 1;
        return 1;
    }
}
