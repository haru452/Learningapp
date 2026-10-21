using UnityEngine;

/// <summary>
/// ログイン画面 (幅 designWidth で作ったもの) を、どの画面サイズでも同じ見た目に拡大・縮小する。
/// ・横幅に合わせて拡大する。画面が横長・短い場合は、高さ (minDesignHeight) に収まるよう縮める
/// ・ノッチなどの安全領域 (Screen.safeArea) の内側に収める
/// この RectTransform の親いっぱいを基準にする。Canvas Scaler の設定に関係なく動く。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class LoginScreenFit : MonoBehaviour
{
    [SerializeField] private float designWidth = 390f;
    [SerializeField] private float minDesignHeight = 800f;
    [Tooltip("ノッチなどを避ける。画面全体に置くときだけオンにする")]
    [SerializeField] private bool useSafeArea = true;

    private RectTransform _rt;
    private Vector2 _lastParentSize;
    private Rect _lastSafeArea;

    private void OnEnable() => Apply(true);
    private void Update() => Apply(false);

    private void Apply(bool force)
    {
        if (_rt == null) _rt = (RectTransform)transform;
        var parent = _rt.parent as RectTransform;
        if (parent == null) return;

        Vector2 size = parent.rect.size;
        if (size.x <= 0f || size.y <= 0f) return;

        Rect safe = Screen.safeArea;
        if (!force && size == _lastParentSize && safe == _lastSafeArea) return;
        _lastParentSize = size;
        _lastSafeArea = safe;

        // 安全領域 (割合) → 親の座標に直す
        float topFrac = 0f, bottomFrac = 0f, leftFrac = 0f, rightFrac = 0f;
        if (useSafeArea && Screen.width > 0 && Screen.height > 0)
        {
            topFrac = Mathf.Clamp01((Screen.height - safe.yMax) / Screen.height);
            bottomFrac = Mathf.Clamp01(safe.y / Screen.height);
            leftFrac = Mathf.Clamp01(safe.x / Screen.width);
            rightFrac = Mathf.Clamp01((Screen.width - safe.xMax) / Screen.width);
        }
        Vector2 area = new Vector2(size.x * (1f - leftFrac - rightFrac), size.y * (1f - topFrac - bottomFrac));
        Vector2 offset = new Vector2(size.x * (leftFrac - rightFrac) * 0.5f, size.y * (bottomFrac - topFrac) * 0.5f);

        float scale = Mathf.Min(area.x / designWidth, area.y / minDesignHeight);
        if (scale <= 0f) return;

        _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot = new Vector2(0.5f, 0.5f);
        _rt.anchoredPosition = offset;
        _rt.sizeDelta = area / scale;
        _rt.localScale = new Vector3(scale, scale, 1f);
    }
}
