// 配置場所: Assets/Editor/IosPostBuild.cs  (Editor フォルダ内に置くこと)
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// iOSビルド後に Xcode プロジェクトへ以下を自動設定する。
///  - Info.plist: 音声認識 / マイクの利用目的 (未設定だとアプリがクラッシュする)
///  - Speech.framework のリンク
/// </summary>
public static class IosPostBuild
{
    private const string SpeechUsage =
        "This app uses speech recognition to convert your spoken English into text for correction.";
    private const string MicUsage =
        "This app uses the microphone to record your English speech for correction.";

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        // --- Info.plist ---
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromString(File.ReadAllText(plistPath));
        PlistElementDict root = plist.root;

        root.SetString("NSSpeechRecognitionUsageDescription", SpeechUsage);
        // Player Settings で設定済みの場合は上書きしない
        if (!root.values.ContainsKey("NSMicrophoneUsageDescription"))
            root.SetString("NSMicrophoneUsageDescription", MicUsage);

        File.WriteAllText(plistPath, plist.WriteToString());

        // --- Speech.framework ---
        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);

        proj.AddFrameworkToProject(proj.GetUnityFrameworkTargetGuid(), "Speech.framework", false);
        proj.AddFrameworkToProject(proj.GetUnityMainTargetGuid(), "Speech.framework", false);

        proj.WriteToFile(projPath);
    }
}
#endif
