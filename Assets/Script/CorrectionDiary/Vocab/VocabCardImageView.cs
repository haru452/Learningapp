using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 単語カードの裏で、Sound / Say it / Videos の行の代わりに出す画像 (Pixabay)。
/// 部品は起動時にここで作る (シーンは変えない)。画像は端末に保存し、表のうちに先に取っておく。
/// 画像が合わないときは「Hide」でその単語だけ隠せる (細い「Show image」の行になり、押すとまた出す)。
/// </summary>
public class VocabCardImageView
{
    private const float Height = 360f, HiddenHeight = 80f;
    private static readonly Color ButtonColor = new Color32(0x1F, 0x24, 0x40, 0xE6);
    private static readonly Color BoxColor = new Color32(0x11, 0x18, 0x27, 0xFF);

    private readonly PixabayClient _client;
    private readonly GameObject _box;
    private readonly RawImage _picture;
    private readonly AspectRatioFitter _fitter;
    private readonly LayoutElement _layout;
    private readonly GameObject _credit, _hideButton, _showButton;
    private readonly Dictionary<string, Task<byte[]>> _inFlight = new Dictionary<string, Task<byte[]>>();
    private Texture2D _texture;
    private string _shownWord;
    private string _wantedWord;   // 今出したい単語 (Hide で null)

    /// <param name="cardArea">カードの領域 (縦に並ぶ)</param>
    /// <param name="voiceRow">Sound / Say it の行。この場所に画像を置く</param>
    /// <param name="onTap">画像を押したとき (カードをめくり直す)</param>
    public VocabCardImageView(GameObject cardArea, Transform voiceRow, PixabayClient client, TMP_FontAsset font, Action onTap)
    {
        _client = client;
        _box = NewUi("CardImage", cardArea.transform);
        _box.transform.SetSiblingIndex(voiceRow.GetSiblingIndex());
        _layout = _box.AddComponent<LayoutElement>();
        var bg = _box.AddComponent<Image>();
        bg.color = BoxColor;
        var button = _box.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onTap?.Invoke());
        _box.AddComponent<RectMask2D>();

        var pic = NewUi("Picture", _box.transform);
        _picture = pic.AddComponent<RawImage>();
        _picture.raycastTarget = false;
        _fitter = pic.AddComponent<AspectRatioFitter>();
        _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        // Pixabay の決まり: どこから取った画像かを出す
        var credit = NewUi("Credit", _box.transform).AddComponent<TextMeshProUGUI>();
        if (font != null) credit.font = font;
        credit.text = "Pixabay";
        credit.fontSize = 22;
        credit.color = new Color(1f, 1f, 1f, 0.7f);
        credit.alignment = TextAlignmentOptions.BottomRight;
        credit.raycastTarget = false;
        var cr = (RectTransform)credit.transform;
        cr.anchorMin = Vector2.zero;
        cr.anchorMax = Vector2.one;
        cr.offsetMin = new Vector2(12f, 8f);
        cr.offsetMax = new Vector2(-12f, -8f);
        _credit = credit.gameObject;

        // 右上の「Hide」: この単語だけ画像を隠す。隠したら全体が「Show image」の細い行になる
        _hideButton = SmallButton("Hide", _box.transform, font, "Hide", () => SetHidden(true));
        var hr = (RectTransform)_hideButton.transform;
        hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(1f, 1f);
        hr.anchoredPosition = new Vector2(-12f, -12f);
        hr.sizeDelta = new Vector2(120f, 56f);
        _showButton = SmallButton("ShowImage", _box.transform, font, "Show image", () => SetHidden(false));

        _box.SetActive(false);
    }

    /// <summary>裏になった: 画像があれば出す (無ければ何も出さない)</summary>
    public async void Show(string word)
    {
        _wantedWord = word;
        if (VocabImageStore.IsHidden(word)) { SetLayout(hidden: true); _box.SetActive(true); return; }
        SetLayout(hidden: false);
        if (_shownWord == word && _texture != null) { _box.SetActive(true); return; }
        _box.SetActive(false);
        byte[] data = await GetAsync(word);
        if (_wantedWord != word || data == null) return;
        if (!Display(word, data)) return;
        _box.SetActive(true);
    }

    /// <summary>表になった・カードを離れた: 隠す (次にめくったとき用に先に取っておく)</summary>
    public void Hide(string preloadWord = null)
    {
        _wantedWord = null;
        _box.SetActive(false);
        if (!string.IsNullOrEmpty(preloadWord) && !VocabImageStore.IsHidden(preloadWord)) _ = GetAsync(preloadWord);
    }

    private void SetHidden(bool hidden)
    {
        string word = _wantedWord;
        if (string.IsNullOrEmpty(word)) return;
        VocabImageStore.SetHidden(word, hidden);
        Show(word);
    }

    // 画像あり: 高さ 360、画像・出典・Hide。隠した: 高さ 80、Show image だけ
    private void SetLayout(bool hidden)
    {
        _layout.preferredHeight = _layout.minHeight = hidden ? HiddenHeight : Height;
        _picture.gameObject.SetActive(!hidden);
        _credit.SetActive(!hidden);
        _hideButton.SetActive(!hidden);
        _showButton.SetActive(hidden);
    }

    private static GameObject SmallButton(string name, Transform parent, TMP_FontAsset font, string text, Action onClick)
    {
        var go = NewUi(name, parent);
        go.AddComponent<Image>().color = ButtonColor;
        go.AddComponent<Button>().onClick.AddListener(() => onClick());
        var label = NewUi("Label", go.transform).AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = 26;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return go;
    }

    /// <summary>保存 → Pixabay の順で画像のデータを取る。見つからない・失敗したら null</summary>
    private Task<byte[]> GetAsync(string word)
    {
        if (string.IsNullOrEmpty(word)) return Task.FromResult<byte[]>(null);
        if (_inFlight.TryGetValue(word, out var running)) return running;
        var task = FetchAsync(word);
        if (!task.IsCompleted) _inFlight[word] = task;
        return task;
    }

    private async Task<byte[]> FetchAsync(string word)
    {
        try
        {
            var stored = VocabImageStore.Load(word);
            if (stored != null) return stored;
            if (VocabImageStore.IsKnownNone(word, DateTime.UtcNow) || _client == null || !_client.IsConfigured) return null;
            byte[] data = await _client.GetImageAsync(word);
            if (data == null || data.Length == 0) { VocabImageStore.SaveNone(word, DateTime.UtcNow); return null; }
            VocabImageStore.Save(word, data);
            return data;
        }
        catch (Exception e)
        {
            // 通信できない・キーの間違いなど。「画像なし」とは保存しないので、あとでまた探す
            Debug.LogWarning("[VocabImage] could not get an image for \"" + word + "\": " + e.Message);
            return null;
        }
        finally
        {
            _inFlight.Remove(word);
        }
    }

    private bool Display(string word, byte[] data)
    {
        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(data))
        {
            UnityEngine.Object.Destroy(tex);
            return false;
        }
        if (_texture != null) UnityEngine.Object.Destroy(_texture);
        _texture = tex;
        _shownWord = word;
        _picture.texture = tex;
        _fitter.aspectRatio = tex.height > 0 ? (float)tex.width / tex.height : 1f;
        return true;
    }

    private static GameObject NewUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return go;
    }
}
