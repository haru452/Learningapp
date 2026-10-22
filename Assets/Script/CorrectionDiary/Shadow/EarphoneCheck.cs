/// <summary>シャドーイングの前に、イヤホンが付いているかを調べる</summary>
public static partial class EarphoneCheck
{
    // AudioDeviceInfo の種類: 有線ヘッドセット 3、有線ヘッドホン 4、Bluetooth SCO 7、Bluetooth A2DP 8、
    // USB ヘッドセット 22、補聴器 23、BLE ヘッドセット 26 (BLE スピーカー 27 などは含めない)
    public static bool IsEarphone(int deviceType)
    {
        switch (deviceType)
        {
            case 3: case 4: case 7: case 8: case 22: case 23: case 26: return true;
            default: return false;
        }
    }
}
