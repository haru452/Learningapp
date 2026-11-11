using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 日本語の文字 (Gemini の日本語のアドバイスなど) が ■ にならないように、起動時に日本語のフォントを
/// TextMesh Pro 全体の「見つからない文字の代わり」(fallback) に足す。
/// フォント: Assets/Resources/Fonts/NotoSansJP-Regular.otf (SIL Open Font License。同じ所の NotoSansJP-LICENSE.txt)。
/// 文字の絵は使った文字だけを、その場で作る (Dynamic)
/// </summary>
public static class JapaneseFont
{
    public const string ResourcePath = "Fonts/NotoSansJP-Regular";

    private static TMP_FontAsset _asset;

    public static TMP_FontAsset Asset => _asset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (_asset != null) return;
        var font = Resources.Load<Font>(ResourcePath);
        if (font == null) { Debug.LogWarning("[JapaneseFont] " + ResourcePath + " is missing. Japanese text shows as boxes."); return; }
        _asset = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        if (_asset == null) { Debug.LogWarning("[JapaneseFont] could not make the font asset."); return; }
        _asset.name = "NotoSansJP (runtime)";
        var list = TMP_Settings.fallbackFontAssets;
        if (list == null) { Debug.LogWarning("[JapaneseFont] TMP Settings is missing."); return; }
        if (!list.Contains(_asset)) list.Add(_asset);
    }
}
