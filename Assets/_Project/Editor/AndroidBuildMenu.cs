#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ARLocation.MapboxRoutes.SampleProject.Editor
{
    /// <summary>
    /// Builds Android without opening Build Settings (avoids Unity GUILayout bug in AndroidBuildWindowExtension).
    /// </summary>
    public static class AndroidBuildMenu
    {
        const string DefaultApkName = "AR_PathFinder.apk";

        [MenuItem("AR PathFinder/Build Android APK (bypass Build window)")]
        public static void BuildAndroidApk()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "AR PathFinder",
                    "No scenes are enabled in File → Build Settings.\nEnable at least one scene and try again.",
                    "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Save Android APK",
                "",
                DefaultApkName,
                "apk");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    EditorUtility.DisplayProgressBar("AR PathFinder", "Switching to Android…", 0.2f);
                    if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                            BuildTargetGroup.Android, BuildTarget.Android))
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog(
                            "AR PathFinder",
                            "Could not switch the active platform to Android.",
                            "OK");
                        return;
                    }
                }

                EditorUtility.DisplayProgressBar("AR PathFinder", "Building APK…", 0.5f);

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                EditorUtility.ClearProgressBar();

                if (report.summary.result == BuildResult.Succeeded)
                {
                    EditorUtility.DisplayDialog(
                        "AR PathFinder",
                        $"Build succeeded.\n\n{path}",
                        "OK");
                    EditorUtility.RevealInFinder(path);
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "AR PathFinder",
                        $"Build failed: {report.summary.result}\n\nSee the Console for details.",
                        "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
#endif
