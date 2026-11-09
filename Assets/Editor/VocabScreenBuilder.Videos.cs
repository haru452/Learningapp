using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 単語帳の「Videos」ボタンと動画の画面を作る (VocabScreenBuilder の続き)。
/// Create Vocabulary Screen と Update Vocabulary Screen の両方から呼ばれ、何度実行しても重複しない。
/// 動画の画面は Today の中、カードの領域 (CardArea) の後ろに置く (動画を開くとカードの領域は隠れる)。
/// </summary>
public static partial class VocabScreenBuilder
{
    private const string VideosUndo = "Update Vocabulary Screen";

    /// <summary>
    /// Videos ボタン・動画の画面・VocabManager の欄を足す。足りない物だけ作る。
    /// 何かを作る・割り当てたら 1 以上、何もしなければ 0 を返す
    /// </summary>
    public static int EnsureVideos(Transform screen)
    {
        var manager = screen.GetComponentInChildren<VocabManager>(true);
        var today = FindChild(screen, "TodayPanel");
        if (manager == null || today == null) return 0;
        int changed = 0;

        // ボタン: Sound / Say it と同じ行
        var voiceRow = FindChild(today, "VoiceRow");
        Transform buttonT = FindChild(today, "VideosButton");
        if (buttonT == null && voiceRow != null)
        {
            var (button, _) = CreateButton("VideosButton", voiceRow, "Videos");
            Undo.RegisterCreatedObjectUndo(button.gameObject, VideosUndo);
            Layout(button.gameObject, flexibleWidth: 1);
            buttonT = button.transform;
            changed++;
        }

        // 画面: カード (CardArea) のすぐ後ろ
        Transform panelT = FindChild(today, "VideosPanel");
        if (panelT == null)
        {
            panelT = BuildVideosPanel(today).transform;
            Undo.RegisterCreatedObjectUndo(panelT.gameObject, VideosUndo);
            var card = FindChild(today, "CardArea");
            if (card != null) panelT.SetSiblingIndex(card.GetSiblingIndex() + 1);
            changed++;
        }

        // VocabManager の欄 (空のものだけ入れる)
        var player = manager.GetComponent<YouTubePlayerView>();
        if (player == null) { player = Undo.AddComponent<YouTubePlayerView>(manager.gameObject); changed++; }
        var so = new SerializedObject(manager);
        changed += Assign(so, "videosButton", buttonT != null ? buttonT.GetComponent<Button>() : null);
        changed += Assign(so, "videosPanel", panelT.gameObject);
        changed += Assign(so, "videoArea", FindChild(panelT, "VideoArea") as RectTransform);
        changed += Assign(so, "videoSentence", TextOf(panelT, "VideoSentence"));
        changed += Assign(so, "videoChannel", TextOf(panelT, "VideoChannel"));
        changed += Assign(so, "videoCount", TextOf(panelT, "VideoCount"));
        changed += Assign(so, "videoReplay", ButtonOf(panelT, "VideoReplay"));
        changed += Assign(so, "videoNext", ButtonOf(panelT, "VideoNext"));
        changed += Assign(so, "videoClose", ButtonOf(panelT, "VideoClose"));
        changed += Assign(so, "videoPlayer", player);
        so.ApplyModifiedPropertiesWithoutUndo();
        if (changed > 0) EditorUtility.SetDirty(manager);
        return changed;
    }

    private static GameObject BuildVideosPanel(Transform today)
    {
        // 縦は子の高さを自分で決める (VideoArea の高さは AspectRatioFitter が決めるので、グループに任せない)
        var panel = CreateBox("VideosPanel", today, vertical: true, spacing: 10);
        var group = panel.GetComponent<VerticalLayoutGroup>();
        group.childControlHeight = false;
        group.childForceExpandHeight = false;
        Layout(panel, flexibleHeight: 1);

        // 動画の枠。上に何も重ねない (WebView が UI の上に描かれる)
        var area = new GameObject("VideoArea", typeof(RectTransform));
        area.transform.SetParent(panel.transform, false);
        var fitter = area.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        fitter.aspectRatio = 16f / 9f;

        var sentence = CreateText("VideoSentence", panel.transform, "", 34, TextAlignmentOptions.TopLeft);
        sentence.enableAutoSizing = true;
        sentence.fontSizeMin = 22;
        sentence.fontSizeMax = 34;
        sentence.raycastTarget = false;
        FixedHeight(sentence.gameObject, 200);

        var info = CreateBox("VideoInfoRow", panel.transform, vertical: false, spacing: 12);
        FixedHeight(info, 44);
        var channel = CreateText("VideoChannel", info.transform, "", 26, TextAlignmentOptions.Left);
        channel.color = SubTextColor;
        channel.raycastTarget = false;
        channel.overflowMode = TextOverflowModes.Ellipsis;
        Layout(channel.gameObject, flexibleWidth: 1);
        var count = CreateText("VideoCount", info.transform, "", 26, TextAlignmentOptions.Right);
        count.color = SubTextColor;
        count.raycastTarget = false;
        Layout(count.gameObject, flexibleWidth: 0);
        count.gameObject.GetComponent<LayoutElement>().minWidth = 140;

        var buttons = CreateBox("VideoButtons", panel.transform, vertical: false, spacing: 12);
        FixedHeight(buttons, 76);
        CreateButton("VideoReplay", buttons.transform, "Replay");
        CreateButton("VideoNext", buttons.transform, "Next");
        CreateButton("VideoClose", buttons.transform, "Close");

        panel.SetActive(false);
        return panel;
    }

    // 高さを固定する (親は縦の大きさを制御しないので RectTransform に直接入れる)
    private static void FixedHeight(GameObject go, float height)
    {
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
    }

    private static Transform FindChild(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != root && t.name == name);

    private static TMP_Text TextOf(Transform root, string name)
    {
        var t = FindChild(root, name);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static Button ButtonOf(Transform root, string name)
    {
        var t = FindChild(root, name);
        return t != null ? t.GetComponent<Button>() : null;
    }

    // 欄が空で、入れるものがあるときだけ入れる。入れたら 1
    private static int Assign(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogError("[VocabScreenBuilder] no field: " + field); return 0; }
        if (value == null || p.objectReferenceValue != null) return 0;
        p.objectReferenceValue = value;
        return 1;
    }
}
