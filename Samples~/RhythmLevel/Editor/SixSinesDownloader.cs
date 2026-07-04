#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Zori.ClapRouter.Samples.Editor
{
    public sealed class SixSinesDownloader : EditorWindow
    {
        public const string MenuPath = "CLAP Router/Download Six Sines";

        private const string DeviceName = "SixSines";
        private const string ShownKey = "Zori.ClapRouter.SixSinesDownloaderShown";
        private const string LatestBase =
            "https://github.com/api-haus/clap-ipc/releases/latest/download/";
        private const string RelativeRoot = "StreamingAssets/clap-router/devices/" + DeviceName;

        private readonly struct Module
        {
            public readonly ClapTargetPlatform Platform;
            public readonly string Dir;
            public readonly string Asset;
            public readonly bool Zip;

            public Module(ClapTargetPlatform platform, string dir, string asset, bool zip)
            {
                Platform = platform;
                Dir = dir;
                Asset = asset;
                Zip = zip;
            }

            public string RelativePath => $"Assets/{RelativeRoot}/{Dir}/SixSines.clap";

            public string AbsolutePath =>
                Path.Combine(Application.dataPath, RelativeRoot, Dir, "SixSines.clap");
        }

        private static readonly Module[] Modules =
        {
            new Module(
                ClapTargetPlatform.Linux,
                "linux-x86_64",
                "SixSines-linux-x86_64.tar.gz",
                false
            ),
            new Module(ClapTargetPlatform.Windows, "win-x86_64", "SixSines-win-x86_64.zip", true),
            new Module(ClapTargetPlatform.MacOS, "macos", "SixSines-macos.tar.gz", false),
        };

        [MenuItem(MenuPath)]
        public static void Open()
        {
            SixSinesDownloader window = GetWindow<SixSinesDownloader>(true, "Six Sines");
            window.minSize = new Vector2(560f, 200f);
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void AutoOpenOnImport()
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

        private static bool Installed(Module module) =>
            File.Exists(module.AbsolutePath) || Directory.Exists(module.AbsolutePath);

        private static bool AllInstalled()
        {
            foreach (Module module in Modules)
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
                "Six Sines is a GPL-3.0 CLAP synth (baconpaul/six-sines, built with JUCE). "
                    + "This downloads all platform modules into StreamingAssets and points the "
                    + "SixSines device asset at them, so any player — including cross-compiled builds — has it.",
                MessageType.Info
            );

            foreach (Module module in Modules)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(module.Dir, GUILayout.Width(150f));
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
            try
            {
                for (int i = 0; i < Modules.Length; i++)
                {
                    Module module = Modules[i];
                    EditorUtility.DisplayProgressBar(
                        "Six Sines",
                        $"Downloading {module.Dir}…",
                        (i + 0.5f) / Modules.Length
                    );
                    Fetch(module);
                }
                AssetDatabase.Refresh();
                PointDeviceAtModules();
                Debug.Log("[SixSinesDownloader] modules installed and SixSines device updated");
            }
            catch (WebException e)
                when ((e.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                EditorUtility.DisplayDialog(
                    "Six Sines",
                    $"A module isn't published yet at\n{LatestBase}",
                    "OK"
                );
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Six Sines", $"Download failed: {e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Fetch(Module module)
        {
            string archive =
                FileUtil.GetUniqueTempPathInProject() + (module.Zip ? ".zip" : ".tar.gz");
            string extractDir = FileUtil.GetUniqueTempPathInProject() + "-clap";
            try
            {
                Directory.CreateDirectory(extractDir);
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "clap-router-unity");
                    client.DownloadFile(LatestBase + module.Asset, archive);
                }

                if (module.Zip)
                {
                    ZipFile.ExtractToDirectory(archive, extractDir);
                }
                else
                {
                    Run("tar", "-xf", archive, "-C", extractDir);
                }

                string clap = FindClap(extractDir);
                if (clap == null)
                {
                    throw new Exception($"no .clap inside {module.Asset}");
                }

                string destDir = Path.GetDirectoryName(module.AbsolutePath);
                if (Directory.Exists(destDir))
                {
                    Directory.Delete(destDir, true);
                }
                Directory.CreateDirectory(destDir);
                Copy(clap, module.AbsolutePath);
            }
            finally
            {
                TryDelete(archive);
                TryDeleteDir(extractDir);
            }
        }

        private static void PointDeviceAtModules()
        {
            ClapDeviceDefinition device = FindDevice();
            if (device == null)
            {
                return;
            }
            SerializedObject serialized = new SerializedObject(device);
            SerializedProperty binaries = serialized.FindProperty("binaries");
            foreach (Module module in Modules)
            {
                SerializedProperty row = null;
                for (int i = 0; i < binaries.arraySize; i++)
                {
                    SerializedProperty element = binaries.GetArrayElementAtIndex(i);
                    if (
                        element.FindPropertyRelative("platform").enumValueIndex
                        == (int)module.Platform
                    )
                    {
                        row = element;
                        break;
                    }
                }
                if (row == null)
                {
                    binaries.arraySize++;
                    row = binaries.GetArrayElementAtIndex(binaries.arraySize - 1);
                    row.FindPropertyRelative("platform").enumValueIndex = (int)module.Platform;
                }
                row.FindPropertyRelative("path").stringValue = module.RelativePath;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(device);
            AssetDatabase.SaveAssetIfDirty(device);
        }

        private static ClapDeviceDefinition FindDevice()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ClapDeviceDefinition"))
            {
                ClapDeviceDefinition device = AssetDatabase.LoadAssetAtPath<ClapDeviceDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid)
                );
                if (device != null && device.name == DeviceName)
                {
                    return device;
                }
            }
            return null;
        }

        private static string FindClap(string root)
        {
            foreach (
                string entry in Directory.GetFileSystemEntries(
                    root,
                    "*.clap",
                    SearchOption.AllDirectories
                )
            )
            {
                return entry;
            }
            return null;
        }

        private static void Copy(string source, string dest)
        {
            if (Directory.Exists(source))
            {
                CopyDirectory(source, dest);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(source, dest, true);
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (
                string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)
            )
            {
                Directory.CreateDirectory(dir.Replace(source, dest));
            }
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(source, dest), true);
            }
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
#endif
