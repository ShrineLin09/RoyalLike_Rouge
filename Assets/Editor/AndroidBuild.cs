using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace MatchRogue.Editor
{
    public static class AndroidBuild
    {
        private const string ApkPath = "Builds/Android/MatchRogue.apk";

        [MenuItem("Match Rogue/Build Android APK")]
        public static void BuildApk()
        {
            ApplyAndroidPlayerSettings();
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Prototype.unity" },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Android APK build failed: {report.summary.result}");
            }
        }

        public static void ApplyAndroidPlayerSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.company.matchrogue");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        }
    }
}
