using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Topic と Repeat の画面 (どちらも "Canvas/Root" に縦に並んだ作り) を、起動時に UiTheme の見た目にする。
/// ・ボタン: 主なボタンは水色に濃い文字、ほかは明るい紺に白い文字 (前はどれも背景とほぼ同じ濃い灰色だった)
/// ・横に並んだボタンの行が余りの高さを取って大きくならないようにし、高さは 48dp 以上にする
/// ・結果の一覧はカードにし、文字は役割ごとの色・大きさにする
/// </summary>
public static class PracticeLayoutStyle
{
    public static void ApplyTopic()
    {
        var root = RootOf("topic screen");
        if (root == null) return;
        Common(root);
        Primary(root, "MainButton");
        Secondary(root, "SkipThinkButton", "RetryButton");
        Text(root, "KindText", UiTheme.CaptionSize, UiTheme.SubText, FontStyles.Bold);
        Text(root, "WeakPointText", UiTheme.CaptionSize, UiTheme.Warning);
        Text(root, "TopicText", UiTheme.HeadingSize, UiTheme.Text, FontStyles.Bold);
        Text(root, "HintText", 28f, UiTheme.SubText);
        Text(root, "TimerText", 80f, UiTheme.Primary, FontStyles.Bold);
        Text(root, "StatusText", 30f, UiTheme.SubText);
    }

    public static void ApplyRepeat()
    {
        var root = RootOf("repeat screen");
        if (root == null) return;
        Common(root);
        Primary(root, "MainButton", "SpeakButton");
        Secondary(root, "AgainButton", "SlowButton");
        Text(root, "HeaderText", UiTheme.HeadingSize, UiTheme.Text, FontStyles.Bold);
        Text(root, "SentenceText", UiTheme.HeadingSize, UiTheme.Text, FontStyles.Bold);
        Text(root, "StatusText", 30f, UiTheme.SubText);
        Text(root, "CreditText", 22f, UiTheme.SubText);
    }

    private static Transform RootOf(string screenName)
    {
        var screen = UiStyle.Find(screenName);
        var root = screen != null ? screen.transform.Find("Canvas/Root") : null;
        if (root == null) Debug.LogWarning("[UiStyle] " + screenName + " was not found. Kept the old look.");
        return root;
    }

    private static void Common(Transform root)
    {
        var canvas = root.parent;
        var background = canvas.Find("Background")?.GetComponent<Image>();
        if (background != null) background.color = UiTheme.Background;

        // 前は画面の上 2 割が空いていたので、上の帯 (右上の設定ボタン) の下まで使う
        var rect = (RectTransform)root;
        rect.anchorMax = new Vector2(rect.anchorMax.x, 0.94f);

        var column = root.GetComponent<VerticalLayoutGroup>();
        if (column != null)
        {
            column.padding = new RectOffset((int)UiTheme.Gutter, (int)UiTheme.Gutter, 24, 32);
            column.spacing = UiTheme.Gap;
        }

        foreach (Transform child in root)
        {
            var le = child.GetComponent<LayoutElement>();
            // 横に並べる行・ボタンは、余りの高さをもらわず、押しやすい高さにする
            if (le != null && (child.GetComponent<HorizontalLayoutGroup>() != null || child.GetComponent<Button>() != null))
            {
                le.flexibleHeight = 0;
                le.preferredHeight = Mathf.Max(le.preferredHeight, UiTheme.ButtonHeight);
            }
            if (child.name == "ResultScroll") UiStyle.Surface(child.gameObject, UiTheme.Surface, outline: true);
        }
    }

    private static void Primary(Transform root, params string[] names)
    {
        foreach (var b in Buttons(root, names)) UiStyle.Primary(b);
    }

    private static void Secondary(Transform root, params string[] names)
    {
        foreach (var b in Buttons(root, names)) UiStyle.Secondary(b);
    }

    private static System.Collections.Generic.IEnumerable<Button> Buttons(Transform root, string[] names) =>
        root.GetComponentsInChildren<Button>(true).Where(b => names.Contains(b.name));

    private static void Text(Transform root, string name, float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var t = root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => x.name == name);
        if (t == null) return;
        t.color = color;
        t.fontStyle = style;
        if (t.enableAutoSizing) t.fontSizeMax = size;
        else t.fontSize = size;
    }
}
