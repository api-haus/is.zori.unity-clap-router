using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Zori.ClapRouter.Editor
{
    public sealed class HostBinaryWindow : EditorWindow
    {
        public const string MenuPath = "CLAP Router/Download clap-ipc Host";

        private const string ShownKey = "Zori.ClapRouter.HostDownloaderShown";
        private const string LatestBase =
            "https://github.com/api-haus/clap-ipc/releases/latest/download/";

        private readonly struct HostModule
        {
            public readonly string Dir;
            public readonly string Exe;
            public readonly string Asset;

            public HostModule(string dir, string exe, string asset)
            {
                Dir = dir;
                Exe = exe;
                Asset = asset;
            }
        }

        private static readonly HostModule[] Modules =
        {
            new HostModule("linux-x86_64", "clap-ipc", "clap-ipc-linux-x86_64.tar.gz"),
            new HostModule("win-x86_64", "clap-ipc.exe", "clap-ipc-win-x86_64.zip"),
            new HostModule("macos", "clap-ipc", "clap-ipc-macos.tar.gz"),
        };

        [MenuItem(MenuPath)]
        public static void Open()
        {
            HostBinaryWindow window = GetWindow<HostBinaryWindow>(true, "clap-ipc Host");
            window.minSize = new Vector2(460f, 190f);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void AutoPrompt()
        {
            if (Application.isBatchMode)
            {
                return;
            }
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(ShownKey, false) || AllInstalled())
                {
                    return;
                }
                SessionState.SetBool(ShownKey, true);
                Open();
            };
        }

        private static string HostRoot() =>
            Path.Combine(Application.streamingAssetsPath, ClapRouterContent.RootFolder, "host");

        private static bool Installed(HostModule module) =>
            File.Exists(Path.Combine(HostRoot(), module.Dir, module.Exe));

        public static bool AllInstalled()
        {
            foreach (HostModule module in Modules)
            {
                if (!Installed(module))
                {
                    return false;
                }
            }
            return true;
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "The clap-ipc host runs the CLAP plugins. It ships per-platform and isn't bundled "
                    + "with the package — download all modules into StreamingAssets so any player "
                    + "(including cross-compiled builds) has its host.",
                MessageType.Info
            );

            foreach (HostModule module in Modules)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(module.Dir, GUILayout.Width(160f));
                EditorGUILayout.LabelField(
                    Installed(module) ? "ready" : "missing",
                    GUILayout.Width(70f)
                );
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            if (
                GUILayout.Button(
                    AllInstalled() ? "Re-download all modules" : "Download all modules"
                )
            )
            {
                DownloadAll();
                Repaint();
            }
        }

        public static void DownloadAll()
        {
            string root = HostRoot();
            Directory.CreateDirectory(root);
            try
            {
                for (int i = 0; i < Modules.Length; i++)
                {
                    HostModule module = Modules[i];
                    EditorUtility.DisplayProgressBar(
                        "clap-ipc",
                        $"Downloading {module.Dir}…",
                        (i + 0.5f) / Modules.Length
                    );
                    Fetch(module, root);
                }
                AssetDatabase.Refresh();
                Debug.Log($"[HostBinaryWindow] host modules installed under {root}");
            }
            catch (WebException e)
                when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                EditorUtility.DisplayDialog(
                    "clap-ipc",
                    $"A host module isn't published yet at\n{LatestBase}",
                    "OK"
                );
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("clap-ipc", $"Download failed: {e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Fetch(HostModule module, string root)
        {
            bool zip = module.Asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            string archive = FileUtil.GetUniqueTempPathInProject() + (zip ? ".zip" : ".tar.gz");
            string extractDir = FileUtil.GetUniqueTempPathInProject() + "-host";
            try
            {
                Directory.CreateDirectory(extractDir);
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "clap-router-unity");
                    client.DownloadFile(LatestBase + module.Asset, archive);
                }

                if (zip)
                {
                    ZipFile.ExtractToDirectory(archive, extractDir);
                }
                else
                {
                    Run("tar", "-xf", archive, "-C", extractDir);
                }

                string binary = Find(extractDir, module.Exe);
                if (binary == null)
                {
                    throw new Exception($"no '{module.Exe}' inside {module.Asset}");
                }

                string destDir = Path.Combine(root, module.Dir);
                Directory.CreateDirectory(destDir);
                string dest = Path.Combine(destDir, module.Exe);
                File.Copy(binary, dest, true);
                MakeExecutable(dest);
            }
            finally
            {
                TryDelete(archive);
                TryDeleteDir(extractDir);
            }
        }

        private static string Find(string root, string exe)
        {
            foreach (string entry in Directory.GetFiles(root, exe, SearchOption.AllDirectories))
            {
                return entry;
            }
            return null;
        }

        private static void Run(string file, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
            {
                info.ArgumentList.Add(arg);
            }
            using Process process = Process.Start(info);
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Exception($"{file} exited {process.ExitCode}: {error}");
            }
        }

        private static void MakeExecutable(string path)
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("chmod") { UseShellExecute = false };
                info.ArgumentList.Add("+x");
                info.ArgumentList.Add(path);
                using Process process = Process.Start(info);
                process?.WaitForExit(3000);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HostBinaryWindow] chmod +x failed for {path}: {e.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }

        private static void TryDeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch { }
        }
    }
}
