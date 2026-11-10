using TMPro;
using UnityEngine;

/// <summary>
/// いくつかの画面で 1 つの選択肢 (Diary と Practice の添削エンジン) を共有するための置き場所。
/// この置き場所のある画面が開くたびに、選択肢をここへ移す (選んだ内容は同じ部品なので共有のまま)。
/// </summary>
public class DropdownSlot : MonoBehaviour
{
    private TMP_Dropdown _dropdown;

    public void Attach(TMP_Dropdown dropdown)
    {
        _dropdown = dropdown;
        if (isActiveAndEnabled) Take();
    }

    private void OnEnable() => Take();

    private void Take()
    {
        if (_dropdown == null || _dropdown.transform.parent == transform) return;
        _dropdown.Hide();   // 開いていた一覧は閉じる
        _dropdown.transform.SetParent(transform, false);
        UiStyle.Stretch(_dropdown.transform);
        _dropdown.gameObject.SetActive(true);
    }
}
