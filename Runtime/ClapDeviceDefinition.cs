using System;
using System.IO;
using UnityEngine;

namespace Zori.ClapRouter
{
    public enum ClapDeviceRole
    {
        Instrument = 0,
        Effect = 1,
    }

    public enum ClapTargetPlatform
    {
        Linux = 0,
        Windows = 1,
        MacOS = 2,
        Android = 3,
    }

    [CreateAssetMenu(fileName = "ClapDevice", menuName = "CLAP Router/CLAP Device", order = 200)]
    public sealed class ClapDeviceDefinition : ScriptableObject
    {
        [Serializable]
        public struct PlatformBinary
        {
            public ClapTargetPlatform platform;
            public string path;
        }

        [SerializeField]
        private string displayName = "";

        [SerializeField]
        private ClapDeviceRole role = ClapDeviceRole.Instrument;

        [SerializeField]
        private uint pluginIndex = 0;

        [SerializeField]
        private PlatformBinary[] binaries = Array.Empty<PlatformBinary>();

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public ClapDeviceRole Role => role;

        public uint PluginIndex => pluginIndex;

        public PlatformBinary[] Binaries => binaries;

        public static ClapTargetPlatform CurrentPlatform =>
            Application.platform switch
            {
                RuntimePlatform.WindowsPlayer => ClapTargetPlatform.Windows,
                RuntimePlatform.WindowsEditor => ClapTargetPlatform.Windows,
                RuntimePlatform.OSXPlayer => ClapTargetPlatform.MacOS,
                RuntimePlatform.OSXEditor => ClapTargetPlatform.MacOS,
                RuntimePlatform.Android => ClapTargetPlatform.Android,
                _ => ClapTargetPlatform.Linux,
            };

        public bool TryResolvePath(out string path) => TryResolvePath(CurrentPlatform, out path);

        public bool TryResolvePath(ClapTargetPlatform platform, out string path)
        {
            foreach (PlatformBinary binary in binaries)
            {
                if (binary.platform == platform && !string.IsNullOrEmpty(binary.path))
                {
                    path = Resolve(binary.path);
                    return true;
                }
            }
            path = null;
            return false;
        }

        private static string Resolve(string configured)
        {
            if (Path.IsPathRooted(configured))
            {
                return configured;
            }
            string relative = configured.Replace('\\', '/');
            if (relative.StartsWith("./"))
            {
                relative = relative.Substring(2);
            }
            if (relative.StartsWith("Assets/"))
            {
                relative = relative.Substring("Assets/".Length);
            }
            return Path.Combine(Application.dataPath, relative);
        }

        public bool ExistsForCurrentPlatform() =>
            TryResolvePath(out string path)
            && !string.IsNullOrEmpty(path)
            && (File.Exists(path) || Directory.Exists(path));
    }
}
