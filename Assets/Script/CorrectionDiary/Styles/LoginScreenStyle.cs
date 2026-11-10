using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 設定画面のログイン (Sign in / Create account / Sign out) を、起動時に UiTheme の見た目にする (シーンは変えない)。
/// 前は白い半透明の箱に白い入力欄・白いボタンで、ほかの画面 (紺 + 水色) と合っていなかった。
/// "Login Form" の中は LoginScreenFit で拡大して表示するので、文字の大きさ・並びは変えず、色と形だけを変える。
/// </summary>
public static class LoginScreenStyle
{
    public static void Apply()
    {
        var form = UiStyle.Find("Login Form");
        var panel = form != null ? form.transform.parent : null;
        var canvas = panel != null ? panel.parent : null;
        if (canvas == null)
        {
            Debug.LogWarning("[UiStyle] Login screen was not found. Kept the old look.");
            return;
        }

        // 箱: ほかの画面のカードと同じ紺に水色の縁
        Card(panel);
        Card(canvas.Find("Panel (1)"));   // ログイン中に出る箱 (Sign out)

        // 画面の題・説明
        Paint(canvas.Find("tezxt"), UiTheme.Text, FontStyles.Bold);
        Paint(panel.Find("Text (TMP)"), UiTheme.SubText);

        // 入力欄と、その上の見出し
        foreach (var label in new[] { "Email Label", "Password Label" })
            Paint(FindIn(form.transform, label), UiTheme.SubText, FontStyles.Bold);
        foreach (var input in form.GetComponentsInChildren<TMP_InputField>(true)) StyleInput(input);

        // 文字だけのボタン (パスワードを見る・忘れたとき) は水色
        Paint(FindIn(form.transform, "Forgot Password"), UiTheme.Primary);
        var show = FindIn(form.transform, "Show Password");
        if (show != null)
            foreach (var t in show.GetComponentsInChildren<TMP_Text>(true)) Paint(t.transform, UiTheme.Primary, FontStyles.Bold);

        // Remember this device のチェック
        var toggle = FindIn(form.transform, "Remember Toggle");
        if (toggle != null)
        {
            var box = toggle.Find("Background");
            if (box != null) UiStyle.Surface(box.gameObject, UiTheme.SurfaceRaised, outline: true);
            var check = box != null ? box.Find("Checkmark")?.GetComponent<Image>() : null;
            if (check != null) check.color = UiTheme.Primary;
            Paint(toggle.Find("Label"), UiTheme.Text);
        }

        // ボタン: Sign In は水色、ほかは明るい紺
        Button(FindIn(form.transform, "Sign In Button"), UiTheme.Primary, UiTheme.OnPrimary);
        Button(FindIn(form.transform, "Create Account Button"), UiTheme.SurfaceRaised, UiTheme.Text);
        Paint(FindIn(form.transform, "Create Account Hint"), UiTheme.SubText);
        Button(FindIn(canvas, "sign out button (2)"), UiTheme.SurfaceRaised, UiTheme.Text);
        Paint(FindIn(canvas, "yemp1"), UiTheme.Text);   // Signed in as ...
        Button(canvas.Find("back button"), UiTheme.SurfaceRaised, UiTheme.Text);
    }

    private static void Card(Transform t)
    {
        if (t != null) UiStyle.Surface(t.gameObject, UiTheme.Surface, outline: true);
    }

    private static void StyleInput(TMP_InputField input)
    {
        UiStyle.Surface(input.gameObject, UiTheme.Background, outline: true);
        if (input.textComponent != null) input.textComponent.color = UiTheme.Text;
        if (input.placeholder != null) input.placeholder.color = UiTheme.SubText;
        input.customCaretColor = true;
        input.caretColor = UiTheme.Primary;
        input.selectionColor = UiTheme.Hex("#22D3EE", 0.35f);
    }

    private static void Button(Transform t, Color background, Color label)
    {
        if (t != null) UiStyle.Button(t.GetComponent<Button>(), background, label, keepTextSize: true);
    }

    private static void Paint(Transform t, Color color, FontStyles style = FontStyles.Normal)
    {
        var text = t != null ? t.GetComponent<TMP_Text>() : null;
        if (text == null) return;
        text.color = color;
        if (style != FontStyles.Normal) text.fontStyle |= style;
    }

    /// <summary>下のどこかにある、名前の合う物 (隠れている物も)</summary>
    private static Transform FindIn(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
