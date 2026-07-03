using System;
using System.IO;
using Unity.Logging;
using UnityEngine;

namespace Zori.ClapRouter
{
    public enum ClapArtifactKind
    {
        Instrument = 0,
        Effect = 1
    }

    [Serializable]
    public sealed class ClapArtifactEntry
    {
        public string id;
        public string displayName;
        public string license;
        public string clapPath;
        public string sourcePath;
        public string sourceRef;
    }

    [Serializable]
    public sealed class ClapContentCatalog
    {
        public string schema = "clap-router/1";
        public string platform;
        public string hostBinary;
        public ClapArtifactEntry[] instruments = Array.Empty<ClapArtifactEntry>();
        public ClapArtifactEntry[] effects = Array.Empty<ClapArtifactEntry>();
    }

    public static class ClapRouterContent
    {
        public const string RootFolder = "clap-router";
        public const string CatalogFile = "catalog.json";

        private static bool _loaded;
        private static string _root;
        private static ClapContentCatalog _catalog;

        public static bool IsPacked
        {
            get
            {
                EnsureLoaded();
                return _catalog != null;
            }
        }

        public static ClapContentCatalog Catalog
        {
            get
            {
                EnsureLoaded();
                return _catalog;
            }
        }

        public static string PlatformDir
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        return "win-x86_64";
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.OSXEditor:
                        return "macos";
                    default:
                        return "linux-x86_64";
                }
            }
        }

        public static bool TryGetHostBinary(out string path)
        {
            EnsureLoaded();
            if (_catalog != null && !string.IsNullOrEmpty(_catalog.hostBinary))
            {
                path = Path.Combine(_root, _catalog.hostBinary);
                if (File.Exists(path))
                {
                    return true;
                }
                Log.Warning($"[ClapRouterContent] packed host binary missing: {path}");
            }
            path = null;
            return false;
        }

        public static bool TryGetClap(string id, out string path)
        {
            EnsureLoaded();
            path = null;
            ClapArtifactEntry entry = Find(id);
            if (entry == null || string.IsNullOrEmpty(entry.clapPath))
            {
                return false;
            }
            path = Path.Combine(_root, entry.clapPath);
            return File.Exists(path) || Directory.Exists(path);
        }

        public static ClapArtifactEntry Find(string id)
        {
            EnsureLoaded();
            if (_catalog == null || string.IsNullOrEmpty(id))
            {
                return null;
            }
            foreach (ClapArtifactEntry e in _catalog.instruments)
            {
                if (e != null && e.id == id)
                {
                    return e;
                }
            }
            foreach (ClapArtifactEntry e in _catalog.effects)
            {
                if (e != null && e.id == id)
                {
                    return e;
                }
            }
            return null;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            _root = Path.Combine(Application.streamingAssetsPath, RootFolder);
            string catalogPath = Path.Combine(_root, CatalogFile);
            if (!File.Exists(catalogPath))
            {
                return;
            }
            try
            {
                _catalog = JsonUtility.FromJson<ClapContentCatalog>(File.ReadAllText(catalogPath));
                Log.Info($"[ClapRouterContent] packed catalog loaded: {_catalog.instruments.Length} instruments, {_catalog.effects.Length} effects (platform {_catalog.platform})");
            }
            catch (Exception e)
            {
                _catalog = null;
                Log.Error($"[ClapRouterContent] catalog parse failed at {catalogPath}: {e.Message}");
            }
        }
    }
}
