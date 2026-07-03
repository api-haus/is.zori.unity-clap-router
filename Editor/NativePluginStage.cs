using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace Zori.ClapRouter.Editor
{
    public static class NativePluginStage
    {
        public static bool TryResolve(out string path, out string message)
        {
            string dir = PackageRoot();
            string relative = PlatformRelativePath();
            path = Path.GetFullPath(Path.Combine(dir, relative));

            if (File.Exists(path))
            {
                message = $"native client staged: {path}";
                return true;
            }

            message =
                $"native client library missing: {path}\n" +
                "Run tools/stage_unity_plugin.sh (or the CMake unity_plugin_stage target) to build and copy " +
                "libclap_ipc_client into Plugins/ before running the demo gate.";
            return false;
        }

        [MenuItem("Zori/CLAP Router/Verify Native Plugin Staged")]
        public static void VerifyMenu()
        {
            if (TryResolve(out _, out string message))
            {
                Debug.Log($"[NativePluginStage] {message}");
            }
            else
            {
                Debug.LogError($"[NativePluginStage] {message}");
            }
        }

        private static string PlatformRelativePath()
        {
#if UNITY_EDITOR_WIN
            return Path.Combine("Plugins", "win-x86_64", "clap_ipc_client.dll");
#elif UNITY_EDITOR_OSX
            return Path.Combine("Plugins", "macos", "clap_ipc_client.dylib");
#else
            return Path.Combine("Plugins", "linux-x86_64", "libclap_ipc_client.so");
#endif
        }

        private static string PackageRoot([CallerFilePath] string thisFile = "")
        {
            return Path.GetDirectoryName(Path.GetDirectoryName(thisFile));
        }
    }
}
