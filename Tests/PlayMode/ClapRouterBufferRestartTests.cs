using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Zori.ClapRouter;
using Zori.ClapRouter.Tests.Dsp;
using Debug = UnityEngine.Debug;

namespace Zori.ClapRouter.Tests
{
    public sealed class ClapRouterBufferRestartTests
    {
        private const int SampleRate = 48000;
        private const int CaptureBlocks = 240;
        private const double SilenceFloorDb = -50.0;

        private static readonly int[] BlockSizes = { 256, 512 };
        private const int Restarts = 3;

        private string _host;
        private string _sixSines;

        [OneTimeSetUp]
        public void ResolvePaths()
        {
            string repoRoot = RepoRoot();
            _host = Env("MR_HOST_BIN") ?? Path.Combine(repoRoot, "build", "linux-debug", "bin", "clap-ipc");
            _sixSines = Env("MR_SIXSINES_CLAP") ?? Path.Combine(repoRoot, "tests", "fixtures", "SixSines.clap");
            Assert.IsTrue(File.Exists(_host), $"host binary missing: {_host}");
            Assert.IsTrue(Directory.Exists(_sixSines) || File.Exists(_sixSines), $"SixSines.clap missing: {_sixSines}");
        }

        [UnityTest]
        public IEnumerator Device_streams_across_buffer_sizes_and_restarts()
        {
            foreach (int block in BlockSizes)
            {
                for (int run = 1; run <= Restarts; run++)
                {
                    ClapHostConnection connection = ClapHostConnection.StartWithDeviceFallback(
                        DeviceSettings(block), ClapHostSettings.MinDeviceBlock, Log);
                    MusicRouterSession session = connection.Session;
                    Assert.IsTrue(session.IsOpen, $"block {block} run {run}: session did not open");

                    int track = session.CreateTrack();
                    Assert.GreaterOrEqual(track, 0, $"block {block} run {run}: track rejected");
                    Assert.AreEqual(MrStatus.Ok, session.LoadInstrument(track, _sixSines, 0));
                    uint stamp = (uint)(session.NowFrame + session.LookaheadFrames);
                    session.SetParam(track, (int)MrDest.Instrument, stamp, SeededComposer.SixSinesMainLevelParam, 1.0);
                    session.NoteOn(track, stamp, 1, 0, 0, 60, 0.9);

                    ulong before = session.NowFrame;
                    yield return new WaitForSeconds(0.8f);
                    ulong after = session.NowFrame;
                    connection.Dispose();

                    long advanced = (long)(after - before);
                    Debug.Log($"[buffer-gate] device block={block} run={run} NowFrame advanced {advanced} frames");
                    Assert.Greater(advanced, SampleRate / 4,
                        $"block {block} run {run}: NowFrame advanced only {advanced} frames in 0.8s — the device "
                        + "callback stalled (freeze). Expected near-realtime advance.");
                    yield return null;
                }
            }
        }

        [UnityTest]
        public IEnumerator Device_below_minimum_block_is_refused_by_core()
        {
            Assert.Throws<MusicRouterConnectException>(
                () => ClapHostConnection.Start(DeviceSettings(256), Log).Dispose(),
                "a device block below the minimum must be refused by the host, not silently accepted");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_is_non_silent_across_buffer_sizes()
        {
            foreach (int block in BlockSizes)
            {
                string wav = Path.Combine(Path.GetTempPath(), $"mr-buffer-{block}-{Guid.NewGuid():N}.wav");
                using (ClapHostConnection connection = ClapHostConnection.Start(CaptureSettings(block), Log))
                {
                    MusicRouterSession session = connection.Session;
                    Assert.IsTrue(session.IsOpen, $"capture block {block}: session did not open");

                    int track = session.CreateTrack();
                    Assert.GreaterOrEqual(track, 0);
                    Assert.AreEqual(MrStatus.Ok, session.LoadInstrument(track, _sixSines, 0));
                    session.SetParam(track, (int)MrDest.Instrument, 0, SeededComposer.SixSinesMainLevelParam, 1.0);
                    session.NoteOn(track, (uint)(4 * block), 1, 0, 0, 60, 0.9);
                    session.NoteOff(track, (uint)(CaptureBlocks * block - 4 * block), 1, 0, 0, 60);

                    Assert.AreEqual(MrStatus.Ok, session.RenderCapture((uint)CaptureBlocks, wav));
                }

                Assert.IsTrue(File.Exists(wav), $"capture block {block}: no wav written");
                float[] x = WavReader.ReadMono(wav, out int sr, out int ch);
                double db = Analysis.RmsDb(x, 0, x.Length);
                Debug.Log($"[buffer-gate] capture block={block} frames={x.Length} sr={sr} ch={ch} rms={db:F1} dBFS");
                Assert.Greater(db, SilenceFloorDb, $"capture block {block}: silent ({db:F1} dBFS)");
                yield return null;
            }
        }

        private ClapHostSettings DeviceSettings(int block)
        {
            ClapHostSettings settings = ClapHostSettings.ForDevice(_host, NewSocket(), SampleRate, block);
            return settings;
        }

        private ClapHostSettings CaptureSettings(int block)
        {
            return new ClapHostSettings
            {
                BinaryPath = _host,
                Backend = "capture",
                SampleRate = SampleRate,
                BlockSize = block,
                SocketPath = NewSocket(),
                Device = null,
                DropRealtime = false,
                GraceExitMs = 2000,
                KillWaitMs = 2000
            };
        }

        private static string NewSocket()
        {
            return Path.Combine(Path.GetTempPath(), $"mr-buffer-{Guid.NewGuid():N}.sock");
        }

        private static void Log(string line)
        {
            Debug.Log($"[clap-ipc/host] {line}");
        }

        private static string Env(string key)
        {
            string v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static string RepoRoot([CallerFilePath] string thisFile = "")
        {
            return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(thisFile))));
        }
    }
}
