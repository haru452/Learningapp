using UnityEngine;

/// <summary>
/// 「口をどれだけ開くか(0〜1)」を受け取る共通の窓口。
/// VRM 1.0 / VRM 0.x の違いはこのクラスの派生側で吸収する。
/// </summary>
public abstract class MouthTargetBase : MonoBehaviour
{
    /// <param name="value">0 = 閉じる, 1 = 最大に開く</param>
    public abstract void SetMouthOpen(float value);
}
