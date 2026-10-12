using UnityEngine;
using UniVRM10;

/// <summary>
/// VRM 1.0 の「aa」表情を 0〜1 で動かす。
/// VRM モデル(Vrm10Instance が付いたオブジェクト)と同じ階層、または
/// その親に付けるか、Inspector で vrm に直接ドラッグして指定する。
/// </summary>
public class Vrm10MouthTarget : MouthTargetBase
{
    [SerializeField] Vrm10Instance vrm;

    void Awake()
    {
        if (vrm == null) vrm = GetComponentInChildren<Vrm10Instance>();
        if (vrm == null) vrm = FindFirstObjectByType<Vrm10Instance>();
        if (vrm == null) Debug.LogError("Vrm10Instance が見つかりません。VRMモデルをシーンに配置してください。");
    }

    /// <summary>実行時ロードした VRM を後から差し込む用</summary>
    public void SetVrm(Vrm10Instance instance) => vrm = instance;

    public override void SetMouthOpen(float value)
    {
        if (vrm == null || vrm.Runtime == null) return;
        vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa, Mathf.Clamp01(value));
    }
}
