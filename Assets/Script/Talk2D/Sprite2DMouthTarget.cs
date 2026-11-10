using UnityEngine;

/// <summary>
/// 口の開き (0〜1) を、2D キャラの口の差分 (閉じ・半開き・全開) に切り替える。
/// PseudoLipSync から呼ばれる。VRM 用の Vrm10MouthTarget の 2D 版。
/// </summary>
public class Sprite2DMouthTarget : MouthTargetBase
{
    [SerializeField] Character2DView view;

    void Awake()
    {
        if (view == null) view = GetComponent<Character2DView>();
    }

    public override void SetMouthOpen(float value)
    {
        if (view != null) view.SetMouthFrame(Character2DLogic.MouthFrame(value));
    }
}
