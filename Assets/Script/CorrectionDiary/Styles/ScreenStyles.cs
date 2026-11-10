using UnityEngine;

/// <summary>
/// 画面ごとの組み直し (〜ScreenStyle) を起動時に 1 回ずつ呼ぶ。
/// ModesNav が下のタブを作る前に呼ぶ (並べ直した "Canvas/Root" を、下のタブの上までに合わせてもらうため)。
/// やめるときは Enabled を false にする (シーンは変えていないので、元の見た目に戻る)。
/// </summary>
public static class ScreenStyles
{
    public const bool Enabled = true;
    private static bool _done;

    public static void ApplyAll()
    {
        if (!Enabled || _done) return;
        _done = true;
        Run("Practice", PracticeScreenStyle.Apply);
        Run("Diary", DiaryScreenStyle.Apply);
        Run("Topic", PracticeLayoutStyle.ApplyTopic);
        Run("Repeat", PracticeLayoutStyle.ApplyRepeat);
    }

    // 下のタブを使わない設定のときも動かす
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnLoad() => ApplyAll();

    private static void Run(string name, System.Action apply)
    {
        try { apply(); }
        catch (System.Exception e) { Debug.LogWarning("[UiStyle] could not restyle " + name + ": " + e); }
    }
}
