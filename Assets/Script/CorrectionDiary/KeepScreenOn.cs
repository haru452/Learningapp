using UnityEngine;

/// <summary>
/// アプリを開いている間は、画面を消さない (スリープしない)。起動時に自動で設定する (シーンには置かない)。
/// ほかの画面で SleepTimeout.SystemSetting に戻さないこと。
/// </summary>
public static class KeepScreenOn
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }
}
