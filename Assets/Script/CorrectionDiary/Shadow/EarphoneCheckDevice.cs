using System;
using UnityEngine;

public static partial class EarphoneCheck
{
    /// <summary>
    /// 音の出力先にイヤホンがあるか。調べられない (エディタ・iOS・エラー) ときは「付いている」とみなす。
    /// Android: AudioManager.getDevices(GET_DEVICES_OUTPUTS = 2) の種類を IsEarphone で調べる
    /// </summary>
    public static bool HasEarphones()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
            {
                AndroidJavaObject[] devices = audioManager.Call<AndroidJavaObject[]>("getDevices", 2);
                bool found = false;
                if (devices != null)
                {
                    foreach (var d in devices)
                    {
                        if (d == null) continue;
                        if (IsEarphone(d.Call<int>("getType"))) found = true;
                        d.Dispose();
                    }
                }
                return found;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Shadow] could not check earphones: " + e.Message);
            return true;
        }
#else
        return true;
#endif
    }
}
