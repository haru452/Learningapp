// 2D キャラを uGUI で表示する。元の絵の上に口・目の差分を重ね、まばたきとゆっくりした揺れを入れる。
// 自分の RectTransform の中に、縦横比を保って収まるように表示する。
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class Character2DView : MonoBehaviour
{
    RectTransform body;
    RawImage[] mouth;      // [半開き, 全開]
    RawImage[] eyesClosed; // [左目, 右目]
    BlinkTimer blink;
    float aspect = 1f;     // 幅 / 高さ
    float t;

    public bool HasCharacter => body != null;

    public void Show(LoadedCharacter c)
    {
        Clear();
        aspect = (float)c.json.imageWidth / c.json.imageHeight;

        body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
        body.SetParent(transform, false);
        body.anchorMin = body.anchorMax = new Vector2(0.5f, 0.5f);
        Stretch(NewImage("Base", body, c.baseTex));

        mouth = new RawImage[2];
        for (int i = 0; i < 2; i++) mouth[i] = Patch("Mouth" + (i + 1), c.mouth[i], c.json.mouth.rect);
        eyesClosed = new RawImage[2];
        for (int i = 0; i < 2; i++) eyesClosed[i] = Patch(i == 0 ? "EyeLClosed" : "EyeRClosed", c.eyesClosed[i], c.json.eyes[i].rect);

        blink = new BlinkTimer(() => Random.value);
        t = 0f;
        SetMouthFrame(0);
        Layout();
    }

    /// <summary>0 = 閉じ, 1 = 半開き, 2 = 全開</summary>
    public void SetMouthFrame(int frame)
    {
        if (mouth == null) return;
        mouth[0].enabled = frame == 1;
        mouth[1].enabled = frame == 2;
    }

    void Update()
    {
        if (body == null) return;
        bool closed = blink.Update(Time.deltaTime);
        eyesClosed[0].enabled = closed;
        eyesClosed[1].enabled = closed;
        t += Time.deltaTime;
        Layout();
    }

    /// <summary>縦横比を保って収め、揺れの分だけ上下させる</summary>
    void Layout()
    {
        var area = ((RectTransform)transform).rect.size;
        float h = Mathf.Min(area.y, area.x / aspect);
        body.sizeDelta = new Vector2(h * aspect, h);
        body.anchoredPosition = new Vector2(0f, Character2DLogic.BobOffset(t, h));
    }

    RawImage Patch(string name, Texture2D tex, RectJson topLeftRect)
    {
        var r = Character2DLogic.ToBottomLeft(topLeftRect);
        var img = NewImage(name, body, tex);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(r.x, r.y);
        rt.anchorMax = new Vector2(r.x + r.w, r.y + r.h);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        img.enabled = false;
        return img;
    }

    static RawImage NewImage(string name, Transform parent, Texture2D tex)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(RawImage img)
    {
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void Clear()
    {
        if (body != null) Destroy(body.gameObject);
        body = null;
        mouth = null;
        eyesClosed = null;
    }
}
