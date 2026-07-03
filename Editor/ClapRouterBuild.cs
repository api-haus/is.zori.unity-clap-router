using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Zori.ClapRouter.Editor
{
    public static class ClapRouterBuild
    {
        [MenuItem("Zori/CLAP Router/Build Linux Player")]
        public static void BuildLinuxMenu()
        {
            BuildLinux();
        }

        public static void BuildLinux()
        {
            string outDir = Environment.GetEnvironmentVariable("MR_PLAYER_OUT");
            if (string.IsNullOrEmpty(outDir))
            {
                outDir = Path.Combine(RepoRoot(), "build", "player-linux");
            }
            Directory.CreateDirectory(outDir);
            string exe = Path.Combine(outDir, PlayerSettings.productName + ".x86_64");

            BuildPlayerOptions opts = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = exe,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary summary = report.summary;
            bool ok = summary.result == BuildResult.Succeeded;
            if (ok)
            {
                Debug.Log($"[ClapRouterBuild] SUCCESS -> {exe} ({summary.totalSize} bytes, {summary.totalWarnings} warnings)");
            }
            else
            {
                Debug.LogError($"[ClapRouterBuild] FAILED: {summary.result} ({summary.totalErrors} errors)");
            }

            if (IsBatch())
            {
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        private static string[] EnabledScenes()
        {
            List<string> scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }
            return scenes.ToArray();
        }

        private static bool IsBatch()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode") >= 0;
        }

        private static string RepoRoot([CallerFilePath] string thisFile = "")
        {
            string package = Path.GetDirectoryName(Path.GetDirectoryName(thisFile));
            return Path.GetDirectoryName(package);
        }
    }
}
