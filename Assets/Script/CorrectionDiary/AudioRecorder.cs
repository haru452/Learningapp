using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Microphone クラスによる録音・停止と、WAV(16bit PCM)バイト列への変換を行う。
/// </summary>
public class AudioRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    [Tooltip("サンプリングレート。音声認識用途なら16kHzで十分")]
    [SerializeField] private int sampleRate = 16000;

    [Tooltip("最大録音秒数。超えると自動停止イベントが発火する")]
    [SerializeField] private int maxSeconds = 60;

    public bool IsRecording { get; private set; }

    /// <summary>最大録音時間に達したとき</summary>
    public event Action OnMaxDurationReached;

    private AudioClip _clip;
    private float _startTime;

    private void Update()
    {
        if (IsRecording && Time.realtimeSinceStartup - _startTime >= maxSeconds)
        {
            OnMaxDurationReached?.Invoke();
        }
    }

    // ------------------------------------------------------------
    // Permission
    // ------------------------------------------------------------
    public async Task<bool> RequestPermissionAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            return true;

        var tcs = new TaskCompletionSource<bool>();
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => tcs.TrySetResult(true);
        callbacks.PermissionDenied += _ => tcs.TrySetResult(false);
        callbacks.PermissionDeniedAndDontAskAgain += _ => tcs.TrySetResult(false);
        Permission.RequestUserPermission(Permission.Microphone, callbacks);
        return await tcs.Task;
#elif UNITY_IOS && !UNITY_EDITOR
        if (Application.HasUserAuthorization(UserAuthorization.Microphone))
            return true;

        var op = Application.RequestUserAuthorization(UserAuthorization.Microphone);
        while (!op.isDone)
            await Task.Yield();
        return Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
        await Task.Yield();
        return Microphone.devices.Length > 0;
#endif
    }

    // ------------------------------------------------------------
    // Start / Stop
    // ------------------------------------------------------------
    public bool StartRecording()
    {
        if (IsRecording) return false;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[AudioRecorder] No microphone device found.");
            return false;
        }

        _clip = Microphone.Start(null, false, maxSeconds, sampleRate);
        if (_clip == null)
        {
            Debug.LogWarning("[AudioRecorder] Microphone.Start failed.");
            return false;
        }

        _startTime = Time.realtimeSinceStartup;
        IsRecording = true;
        return true;
    }

    /// <summary>録音を停止し、WAVバイト列を返す。録音できていない場合は null。</summary>
    public byte[] StopRecording()
    {
        if (!IsRecording) return null;

        int position = Microphone.GetPosition(null);
        Microphone.End(null);
        IsRecording = false;

        if (_clip == null || position <= 0) return null;

        var samples = new float[position * _clip.channels];
        _clip.GetData(samples, 0);

        byte[] wav = WavEncoder.FromSamples(samples, _clip.channels, _clip.frequency);

        Destroy(_clip);
        _clip = null;
        return wav;
    }
}

/// <summary>float PCM → 16bit PCM WAV 変換</summary>
public static class WavEncoder
{
    public static byte[] FromSamples(float[] samples, int channels, int sampleRate)
    {
        const int headerSize = 44;
        int dataSize = samples.Length * 2;

        using (var ms = new MemoryStream(headerSize + dataSize))
        using (var bw = new BinaryWriter(ms))
        {
            // RIFF header
            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataSize);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });

            // fmt chunk
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16);                          // chunk size
            bw.Write((short)1);                    // PCM
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * channels * 2);   // byte rate
            bw.Write((short)(channels * 2));       // block align
            bw.Write((short)16);                   // bits per sample

            // data chunk
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataSize);

            foreach (float f in samples)
            {
                short s = (short)Mathf.Clamp(Mathf.RoundToInt(f * short.MaxValue),
                                             short.MinValue, short.MaxValue);
                bw.Write(s);
            }

            bw.Flush();
            return ms.ToArray();
        }
    }
}
