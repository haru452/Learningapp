using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// メニュー「Tools > Talk2D > 確認用シーンを作る」で、2D キャラの確認用シーン
/// (Assets/Scenes/Character2DTest.unity) を新しく作って保存する。今開いているシーンは変えない。
///
/// 中身: Canvas の中に Character2DView + Sprite2DMouthTarget + PseudoLipSync、OnGUI の Character2DTestUI。
/// キャラは StreamingAssets/Characters/ (CharacterMaker の書き出し先) から読む。
/// </summary>
public static class Character2DTestSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Character2DTest.unity";

    [MenuItem("Tools/Talk2D/確認用シーンを作る")]
    static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Talk2D", ScenePath + " はもうあります。作り直しますか？", "作り直す", "やめる"))
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.11f, 0.18f);
        }

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // キャラを置く場所: 上のボタンと下の「話す」ボタンを避ける
        var charGo = new GameObject("Character2D", typeof(RectTransform));
        var rt = charGo.GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = new Vector2(0.05f, 0.15f);
        rt.anchorMax = new Vector2(0.95f, 0.75f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var view = charGo.AddComponent<Character2DView>();
        var mouth = charGo.AddComponent<Sprite2DMouthTarget>();
        SetRef(mouth, "view", view);
        var lipSync = charGo.AddComponent<PseudoLipSync>();
        SetRef(lipSync, "mouth", mouth);

        var ui = new GameObject("Character2DTestUI").AddComponent<Character2DTestUI>();
        SetRef(ui, "view", view);
        SetRef(ui, "lipSync", lipSync);

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Talk2D",
            ScenePath + " を作りました。\n再生すると、StreamingAssets/Characters/ のキャラを選べます。", "OK");
    }

    static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
