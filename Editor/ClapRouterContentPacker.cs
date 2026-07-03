using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Zori.ClapRouter.Editor
{
    public sealed class ClapRouterContentPacker : IPostprocessBuildWithReport
    {
        private enum SlotKind
        {
            Instrument,
            Effect
        }

        private const string SurgeSourceRef =
            "Surge XT 1.3.4 — https://github.com/surge-synthesizer/releases-xt/releases/tag/1.3.4 " +
            "(surge-src-1.3.4.tar.gz); repo https://github.com/surge-synthesizer/surge";

        private sealed class PackSource
        {
            public string Id;
            public string DisplayName;
            public string License;
            public SlotKind Kind;
            public string ClapRepoPath;
            public string ClapEnvVar;
            public string SourceRepoPath;
            public string SourceRefEnvVar;
            public string SourceRefDefault;
        }

        private static readonly PackSource[] Sources =
        {
            new PackSource
            {
                Id = "six-sines",
                DisplayName = "Six Sines",
                License = "GPL-3.0",
                Kind = SlotKind.Instrument,
                ClapRepoPath = "tests/fixtures/SixSines.clap",
                ClapEnvVar = "MR_SIXSINES_CLAP",
                SourceRepoPath = "third_party/six-sines",
                SourceRefEnvVar = "MR_SIXSINES_REF",
                SourceRefDefault = "https://github.com/baconpaul/six-sines"
            },
            new PackSource
            {
                Id = "surge-xt",
                DisplayName = "Surge XT",
                License = "GPL-3.0",
                Kind = SlotKind.Instrument,
                ClapRepoPath = "artifacts/surge/linux-x86_64/Surge XT.clap",
                ClapEnvVar = "MR_SURGE_CLAP",
                SourceRepoPath = "",
                SourceRefEnvVar = "MR_SURGE_REF",
                SourceRefDefault = SurgeSourceRef
            },
            new PackSource
            {
                Id = "surge-xt-fx",
                DisplayName = "Surge XT Effects",
                License = "GPL-3.0",
                Kind = SlotKind.Effect,
                ClapRepoPath = "artifacts/surge/linux-x86_64/Surge XT Effects.clap",
                ClapEnvVar = "MR_SURGE_FX_CLAP",
                SourceRepoPath = "",
                SourceRefEnvVar = "MR_SURGE_REF",
                SourceRefDefault = SurgeSourceRef
            },
            new PackSource
            {
                Id = "clap-plugins",
                DisplayName = "CLAP Plugins (svf + gain)",
                License = "MIT",
                Kind = SlotKind.Effect,
                ClapRepoPath = "tests/fixtures/ClapPlugins.clap",
                ClapEnvVar = "MR_CLAPPLUGINS_CLAP",
                SourceRepoPath = "third_party/clap-plugins",
                SourceRefEnvVar = "MR_CLAPPLUGINS_REF",
                SourceRefDefault = "https://github.com/free-audio/clap-plugins"
            }
        };

        private static readonly HashSet<string> SourceSkip = new HashSet<string>
        {
            ".git", "build", "builds", "build-clap", "Library", "Temp", "obj", ".vs", ".idea"
        };

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneLinux64)
            {
                return;
            }

            const string platformDir = "linux-x86_64";
            string repo = RepoRoot();
            string root = StreamingRoot(report.summary.outputPath);
            bool packSource = Environment.GetEnvironmentVariable("MR_PACK_SOURCE") == "1";

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
            Directory.CreateDirectory(root);

            ClapContentCatalog catalog = new ClapContentCatalog { platform = platformDir };

            string host = Environment.GetEnvironmentVariable("MR_HOST_BIN");
            if (string.IsNullOrEmpty(host))
            {
                host = Path.Combine(repo, "build", "linux-debug", "bin", "clap-ipc");
            }
            if (!File.Exists(host))
            {
                throw new BuildFailedException(
                    $"[ClapRouterContentPacker] host binary missing: {host} " +
                    "(cmake --build build/linux-debug --target clap-ipc)");
            }
            string hostRel = Path.Combine("host", platformDir, "clap-ipc");
            CopyFile(host, Path.Combine(root, hostRel));
            MakeExecutable(Path.Combine(root, hostRel));
            catalog.hostBinary = ToPosix(hostRel);

            string hostSourceRef = Environment.GetEnvironmentVariable("MR_HOST_SRC_REF");
            if (string.IsNullOrEmpty(hostSourceRef))
            {
                hostSourceRef = "https://github.com/api-haus/clap-ipc";
            }

            List<ClapArtifactEntry> instruments = new List<ClapArtifactEntry>();
            List<ClapArtifactEntry> effects = new List<ClapArtifactEntry>();

            foreach (PackSource source in Sources)
            {
                string clap = Environment.GetEnvironmentVariable(source.ClapEnvVar);
                if (string.IsNullOrEmpty(clap))
                {
                    clap = Path.Combine(repo, source.ClapRepoPath);
                }
                if (!File.Exists(clap) && !Directory.Exists(clap))
                {
                    throw new BuildFailedException(
                        $"[ClapRouterContentPacker] {source.Id} .clap missing: {clap}");
                }

                string clapName = Path.GetFileName(clap.TrimEnd('/'));
                string clapRel = Path.Combine("clap", platformDir, clapName);
                CopyPath(clap, Path.Combine(root, clapRel));

                string sourceRel = "";
                if (packSource && !string.IsNullOrEmpty(source.SourceRepoPath))
                {
                    string src = Path.Combine(repo, source.SourceRepoPath);
                    if (Directory.Exists(src))
                    {
                        sourceRel = Path.Combine("src", source.Id);
                        CopyDirectory(src, Path.Combine(root, sourceRel), SourceSkip);
                    }
                }

                string sourceRef = Environment.GetEnvironmentVariable(source.SourceRefEnvVar);
                if (string.IsNullOrEmpty(sourceRef))
                {
                    sourceRef = source.SourceRefDefault;
                }

                ClapArtifactEntry entry = new ClapArtifactEntry
                {
                    id = source.Id,
                    displayName = source.DisplayName,
                    license = source.License,
                    clapPath = ToPosix(clapRel),
                    sourcePath = ToPosix(sourceRel),
                    sourceRef = sourceRef
                };
                (source.Kind == SlotKind.Instrument ? instruments : effects).Add(entry);
            }

            catalog.instruments = instruments.ToArray();
            catalog.effects = effects.ToArray();
            File.WriteAllText(
                Path.Combine(root, ClapRouterContent.CatalogFile),
                JsonUtility.ToJson(catalog, true));
            File.WriteAllText(
                Path.Combine(root, "SOURCE-OFFER.txt"),
                BuildSourceOffer(catalog, hostSourceRef));

            Debug.Log(
                $"[ClapRouterContentPacker] packed {instruments.Count} instruments + {effects.Count} effects + host " +
                $"into {root} (in-bundle source={(packSource ? "included" : "written offer")})");
        }

        private static string BuildSourceOffer(ClapContentCatalog catalog, string hostSourceRef)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Corresponding Source — GPL-3.0 sections 3 and 6");
            sb.AppendLine();
            sb.AppendLine("This software bundles the GPL-3.0 programs listed below. Each runs in a");
            sb.AppendLine("separate process and speaks to the game only across an arm's-length IPC");
            sb.AppendLine("boundary (mere aggregation). The complete corresponding source for each,");
            sb.AppendLine("matching the exact version distributed here, is available at the location");
            sb.AppendLine("shown. For three years from receipt, the distributor will also provide that");
            sb.AppendLine("source on request.");
            sb.AppendLine();
            sb.AppendLine($"  clap-ipc (audio host)      GPL-3.0   {hostSourceRef}");
            foreach (ClapArtifactEntry e in AllEntries(catalog))
            {
                if (e != null && e.license == "GPL-3.0")
                {
                    sb.AppendLine($"  {e.displayName,-24} GPL-3.0   {e.sourceRef}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("MIT components (CLAP Plugins svf/gain, music-router, clap-ipc-client, and");
            sb.AppendLine("the Unity bindings) retain their MIT license and notices.");
            return sb.ToString();
        }

        private static IEnumerable<ClapArtifactEntry> AllEntries(ClapContentCatalog catalog)
        {
            foreach (ClapArtifactEntry e in catalog.instruments)
            {
                yield return e;
            }
            foreach (ClapArtifactEntry e in catalog.effects)
            {
                yield return e;
            }
        }

        private static string StreamingRoot(string outputPath)
        {
            string dir = Path.GetDirectoryName(outputPath);
            string data = Path.Combine(dir, PlayerSettings.productName + "_Data");
            return Path.Combine(data, "StreamingAssets", ClapRouterContent.RootFolder);
        }

        private static void CopyFile(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
        }

        private static void CopyPath(string src, string dst)
        {
            if (Directory.Exists(src))
            {
                CopyDirectory(src, dst, null);
            }
            else
            {
                CopyFile(src, dst);
            }
        }

        private static void CopyDirectory(string src, string dst, HashSet<string> skipNames)
        {
            Directory.CreateDirectory(dst);
            foreach (string dir in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(dir);
                if (skipNames != null && skipNames.Contains(name))
                {
                    continue;
                }
                CopyDirectory(dir, Path.Combine(dst, name), skipNames);
            }
            foreach (string file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            }
        }

        private static void MakeExecutable(string path)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("chmod", $"+x \"{path}\"")
                {
                    UseShellExecute = false
                };
                using (Process p = Process.Start(info))
                {
                    p?.WaitForExit(3000);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClapRouterContentPacker] chmod +x failed for {path}: {e.Message}");
            }
        }

        private static string ToPosix(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        private static string RepoRoot([CallerFilePath] string thisFile = "")
        {
            string package = Path.GetDirectoryName(Path.GetDirectoryName(thisFile));
            return Path.GetDirectoryName(package);
        }
    }
}
