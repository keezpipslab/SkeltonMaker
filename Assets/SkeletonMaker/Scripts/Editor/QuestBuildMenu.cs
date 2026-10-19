using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// SkeletonMaker > Build Quest Standalone: builds the No Raymarch scene as
    /// an app for a standalone Quest, the second performer next to the PC on
    /// Link. Only that scene goes in, whatever the Build Settings list says.
    ///
    /// Made from the Android copy of the project (a git worktree kept on the
    /// Android build target), never from the Link project, so that one stays
    /// on Windows: switching it makes Editor Play over Link run with the
    /// Android settings. From the command line:
    /// unity build &lt;copy&gt; --target Android --execute-method SkeletonMaker.QuestBuildMenu.Build
    /// </summary>
    public static class QuestBuildMenu
    {
        private const string ScenePath = "Assets/SkeletonMaker/Scenes/SkeletonBuilder No Raymarch.unity";
        private const string ApkPath = "Builds/Quest/SkeletonMaker.apk";

        [MenuItem("SkeletonMaker/Build Quest Standalone")]
        private static void BuildFromMenu()
        {
            if (OnAndroid()) Build();
        }

        [MenuItem("SkeletonMaker/Build And Run Quest Standalone")]
        private static void BuildAndRunFromMenu()
        {
            if (OnAndroid()) BuildWith(BuildOptions.AutoRunPlayer);
        }

        // File > Build And Run would build the Build Settings list (SkeletonBuilder,
        // raymarch and all); these build the No Raymarch scene only.
        private static bool OnAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) return true;
            EditorUtility.DisplayDialog("Build Quest Standalone",
                "This project is on " + EditorUserBuildSettings.activeBuildTarget + ". Build the Quest app from the Android copy of the project, so this one stays on Windows for Link.", "OK");
            return false;
        }

        /// <summary>Builds the apk. Exits with 1 when it fails in batch mode.</summary>
        public static void Build() => BuildWith(BuildOptions.None);

        /// <summary>AutoRunPlayer installs it on the Quest on USB and starts it.</summary>
        public static void BuildWith(BuildOptions extra)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = extra,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"QuestBuildMenu: built {ApkPath} ({report.summary.totalSize / (1024 * 1024)} MB).");
                return;
            }

            Debug.LogError($"QuestBuildMenu: build {report.summary.result}, {report.summary.totalErrors} errors.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
