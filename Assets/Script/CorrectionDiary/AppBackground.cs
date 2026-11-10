using UnityEngine;

/// <summary>
/// どの画面でも、後ろに透けて見える色をアプリ共通の紺 (UiTheme.Background) にする。
/// 画面の背景は半透明なので、カメラの背景色 (前は明るい青) がそのまま見えていた。起動時に自動で設定する。
/// </summary>
public static class AppBackground
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply()
    {
        foreach (var cam in Object.FindObjectsOfType<Camera>(true))
        {
            if (cam.clearFlags != CameraClearFlags.SolidColor && cam.clearFlags != CameraClearFlags.Skybox) continue;
            if (cam.targetTexture != null) continue;   // 3D の顔などを写す別のカメラは変えない
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UiTheme.Background;
        }
    }
}
