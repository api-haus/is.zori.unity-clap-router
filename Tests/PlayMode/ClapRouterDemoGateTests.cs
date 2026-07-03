using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Zori.ClapRouter;
using Zori.ClapRouter.Tests.Dsp;
using Debug = UnityEngine.Debug;

namespace Zori.ClapRouter.Tests
{
    public sealed class ClapRouterDemoGateTests
    {
        private const int SvfIndex = 13;
        private const int GainIndex = 4;
        private const int Seed = 20240617;
        private const int SampleRate = 48000;
        private const int Block = 512;

        private string _host;
        private string _sixSines;
        private string _clapPlugins;
        private string _stagedLib;
        private string _tmp;
        private bool _dropRealtime;

        private Process _primaryHost;
        private MusicRouterSession _primarySession;
        private Process _bypassHost;
        private MusicRouterSession _bypassSession;
        private Composition _composition;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ResolvePaths();

            Assert.IsTrue(File.Exists(_stagedLib),
                $"native client library not staged: {_stagedLib}\n" +
                "run tools/stage_unity_plugin.sh before the gate (the .so is a gitignored build output).");
            Assert.IsTrue(File.Exists(_host), $"host binary missing: {_host}");
            Assert.IsTrue(File.Exists(_sixSines), $"SixSines.clap missing: {_sixSines}");
            Assert.IsTrue(File.Exists(_clapPlugins), $"ClapPlugins.clap missing: {_clapPlugins}");

            SpawnHostSession(out _primaryHost, out _primarySession);
            Assert.IsTrue(_primarySession.IsOpen,
                "session did not open — the real native client + host handshake is the load proof " +
                "(a missing/stale .so throws DllNotFoundException from the MusicRouterSession ctor P/Invoke).");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _primarySession?.Dispose();
            _bypassSession?.Dispose();
            KillHost(_primaryHost);
            KillHost(_bypassHost);
            _primarySession = null;
            _bypassSession = null;
            _primaryHost = null;
            _bypassHost = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Conducts_three_track_daw_and_asserts_G1_to_G5()
        {
            string wavWith = Path.Combine(_tmp, "mr-unity-with.wav");
            yield return ConductAndCapture(_primarySession, svfOnKeys: true, wavWith);

            Composition comp = _composition;
            float[] x = WavReader.ReadMono(wavWith, out int sr, out int ch);
            Debug.Log($"[gate] capture with-svf: frames={x.Length} sr={sr} ch={ch}");

            int block = comp.BlockSize;
            int preRollEnd = (int)comp.PreRollEndFrame;

            BandEnergies pre = Analysis.BandRmsDb(x, sr, 0, preRollEnd);
            Debug.Log($"[G1] preroll bass={pre.BassDb:F1} mid={pre.MidDb:F1} high={pre.HighDb:F1} dBFS");
            Assert.Less(pre.BassDb, -70.0, "G1 preroll bass not silent");
            Assert.Less(pre.MidDb, -70.0, "G1 preroll mid not silent");
            Assert.Less(pre.HighDb, -70.0, "G1 preroll high not silent");

            BandEnergies ens = Analysis.BandRmsDb(x, sr, preRollEnd, SampleRate);
            Debug.Log($"[G1] ensemble bass={ens.BassDb:F1} mid={ens.MidDb:F1} high={ens.HighDb:F1} dBFS");
            Assert.Greater(ens.BassDb, -50.0, "G1 ensemble bass band silent");
            Assert.Greater(ens.MidDb, -50.0, "G1 ensemble mid band silent");
            Assert.Greater(ens.HighDb, -50.0, "G1 ensemble high band silent");

            int g4o = (int)comp.PitchGlide.OnsetFrame;
            int g4e = (int)comp.PitchGlide.EndFrame;
            BandEnergies bassSolo = Analysis.BandRmsDb(x, sr, g4o + block, g4e - g4o - 8 * block);
            Debug.Log($"[G1] bassSolo bass={bassSolo.BassDb:F1} dBFS");
            Assert.Greater(bassSolo.BassDb, -50.0, "G1 bass track silent in its solo window");

            int onsets = Analysis.OnsetCount(x, sr, preRollEnd, x.Length, 1024, 512);
            int expected = comp.ExpectedNoteOnsets;
            Debug.Log($"[G2] onsets detected={onsets} expected={expected}");
            Assert.GreaterOrEqual(onsets, (int)(0.55 * expected), "G2 too few onsets detected");
            Assert.LessOrEqual(onsets, (int)(1.8 * expected), "G2 spurious onset storm");

            int bestPeaks = 0;
            for (int c = preRollEnd + 4000; c < preRollEnd + 44000; c += 4000)
            {
                int n = Analysis.SpectralPeaks(x, sr, c, 8192, 240.0, 700.0, 5).Count;
                if (n > bestPeaks)
                {
                    bestPeaks = n;
                }
            }

            Debug.Log($"[G2] max simultaneous keys-band pitches={bestPeaks}");
            Assert.GreaterOrEqual(bestPeaks, 2, "G2 no polyphony (chord) detected");

            int g3s = (int)comp.SampleAccuracy.StartFrame;
            int g3e = (int)comp.SampleAccuracy.EndFrame;
            int g3n = (int)comp.SampleAccuracy.OnsetFrame;
            long onset = Analysis.FirstOnsetSample(x, g3s, g3e, 0.004);
            Debug.Log($"[G3] onset={onset} N={g3n} err={(onset < 0 ? -1 : Math.Abs(onset - g3n))} blockStartErr={Math.Abs(g3s - g3n)}");
            Assert.GreaterOrEqual(onset, 0, "G3 no onset found in solo window");
            Assert.LessOrEqual(Math.Abs(onset - g3n), 64, "G3 onset not sample-accurate");
            Assert.Greater(Math.Abs(g3s - g3n), 64,
                "G3 tolerance non-discriminating — a place-at-block-start host must fail this");

            PitchSweep sweep = Analysis.PitchSweepCents(x, sr, g4o + 2 * block, g4e - 6 * block, 1024, 4096, 60.0, 220.0);
            Debug.Log($"[G4] pitch sweep min={sweep.MinHz:F1}Hz max={sweep.MaxHz:F1}Hz cents={sweep.Cents:F0} hops={sweep.Hops}");
            Assert.GreaterOrEqual(sweep.Cents, 30.0, "G4 pitch bend did not land");

            int g5o = (int)comp.EffectSignature.OnsetFrame;
            int g5e = (int)comp.EffectSignature.EndFrame;
            int keysStart = g5o + block;
            int keysCount = g5e - g5o - 8 * block;
            double keysWithDb = Analysis.RmsDb(x, keysStart, keysCount);

            string wavWithout = Path.Combine(_tmp, "mr-unity-without.wav");
            SpawnHostSession(out _bypassHost, out _bypassSession);
            yield return ConductAndCapture(_bypassSession, svfOnKeys: false, wavWithout);
            _bypassSession.Dispose();
            _bypassSession = null;
            KillHost(_bypassHost);
            _bypassHost = null;

            float[] xw = WavReader.ReadMono(wavWithout, out _, out _);
            double keysWithoutDb = Analysis.RmsDb(xw, keysStart, keysCount);
            double delta = keysWithoutDb - keysWithDb;
            Debug.Log($"[G5] keys solo RMS with-svf={keysWithDb:F1}dB without-svf={keysWithoutDb:F1}dB delta={delta:F1}dB");
            Assert.GreaterOrEqual(delta, 12.0, "G5 svf effect chain did not process audio (bypass matches active)");

            Debug.Log("[gate] G1-G5 all passed");
        }

        private IEnumerator ConductAndCapture(MusicRouterSession session, bool svfOnKeys, string wavPath)
        {
            int bass = session.CreateTrack();
            int keys = session.CreateTrack();
            int perc = session.CreateTrack();
            Assert.GreaterOrEqual(bass, 0, "create bass track");
            Assert.GreaterOrEqual(keys, 0, "create keys track");
            Assert.GreaterOrEqual(perc, 0, "create perc track");

            Assert.AreEqual(MrStatus.Ok, session.LoadInstrument(bass, _sixSines, 0), "load bass instrument");
            Assert.AreEqual(MrStatus.Ok, session.LoadInstrument(keys, _sixSines, 0), "load keys instrument");
            Assert.AreEqual(MrStatus.Ok, session.LoadInstrument(perc, _sixSines, 0), "load perc instrument");

            session.SetParam(bass, MrDest.Instrument, 0, SeededComposer.SixSinesMainLevelParam, 1.0);
            session.SetParam(keys, MrDest.Instrument, 0, SeededComposer.SixSinesMainLevelParam, 0.85);
            session.SetParam(perc, MrDest.Instrument, 0, SeededComposer.SixSinesMainLevelParam, 0.9);

            Assert.AreEqual(MrStatus.Ok, session.InsertEffect(bass, 0, _clapPlugins, SvfIndex), "svf on bass");
            Assert.AreEqual(MrStatus.Ok, session.InsertEffect(bass, 1, _clapPlugins, GainIndex), "gain on bass");
            if (svfOnKeys)
            {
                Assert.AreEqual(MrStatus.Ok, session.InsertEffect(keys, 0, _clapPlugins, SvfIndex), "svf on keys");
            }

            Assert.AreEqual(MrStatus.Ok, session.InsertEffect(perc, 0, _clapPlugins, SvfIndex), "svf on perc");

            Assert.AreEqual(MrStatus.Ok, session.SetTrackGain(bass, 1.0f), "bass track gain");
            Assert.AreEqual(MrStatus.Ok, session.SetTrackGain(keys, 0.95f), "keys track gain");
            Assert.AreEqual(MrStatus.Ok, session.SetTrackGain(perc, 0.85f), "perc track gain");

            _composition = new SeededComposer(Seed, bass, keys, perc, effectSlot: 0, sampleRate: SampleRate,
                blockSize: Block).Compose();

            const int chunk = 25;
            for (int i = 0; i < _composition.Events.Length; i += chunk)
            {
                int n = Math.Min(chunk, _composition.Events.Length - i);
                MrPushResult r = PushChunk(session, _composition.Events, i, n);
                Assert.AreEqual(MrPushResult.Ok, r, $"push chunk at {i}");
                yield return null;
            }

            MrStatus cap = session.RenderCapture((uint)_composition.TotalBlocks, wavPath);
            Assert.AreEqual(MrStatus.Ok, cap, "render capture");
            Assert.IsTrue(File.Exists(wavPath), $"capture wav not written: {wavPath}");
        }

        private static MrPushResult PushChunk(MusicRouterSession session, MrEvent[] events, int start, int count)
        {
            return session.PushBatch(new ReadOnlySpan<MrEvent>(events, start, count), out int _);
        }

        private void SpawnHostSession(out Process host, out MusicRouterSession session)
        {
            string sock = Path.Combine(_tmp, $"mr-unity-{Guid.NewGuid():N}.sock");
            ProcessStartInfo psi = new ProcessStartInfo(_host)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("--backend=capture");
            psi.ArgumentList.Add($"--samplerate={SampleRate}");
            psi.ArgumentList.Add($"--block={Block}");
            psi.ArgumentList.Add($"--control-socket={sock}");
            if (_dropRealtime)
            {
                psi.ArgumentList.Add("--drop-realtime");
            }

            psi.EnvironmentVariables["LD_PRELOAD"] = string.Empty;

            host = Process.Start(psi);
            host.OutputDataReceived += (_, __) => { };
            host.ErrorDataReceived += (_, __) => { };
            host.BeginOutputReadLine();
            host.BeginErrorReadLine();

            for (int i = 0; i < 200 && !File.Exists(sock); i++)
            {
                Thread.Sleep(20);
            }

            session = null;
            for (int i = 0; i < 40 && session == null; i++)
            {
                try
                {
                    session = new MusicRouterSession(sock);
                }
                catch (MusicRouterConnectException)
                {
                    Thread.Sleep(50);
                }
            }

            Assert.IsNotNull(session, $"could not connect to host on {sock}");
        }

        private static void KillHost(Process host)
        {
            if (host == null)
            {
                return;
            }

            try
            {
                if (!host.HasExited && !host.WaitForExit(1500))
                {
                    host.Kill();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ResolvePaths()
        {
            string packageRoot = PackageRoot();
            string repoRoot = Path.GetDirectoryName(packageRoot);

            _stagedLib = Path.Combine(packageRoot, "Plugins", "linux-x86_64", "libclap_ipc_client.so");
            _host = Env("MR_HOST_BIN") ?? Path.Combine(repoRoot, "build", "linux-debug", "bin", "clap-ipc");
            _sixSines = Env("MR_SIXSINES_CLAP") ?? Path.Combine(repoRoot, "tests", "fixtures", "SixSines.clap");
            _clapPlugins = Env("MR_CLAPPLUGINS_CLAP") ?? Path.Combine(repoRoot, "tests", "fixtures", "ClapPlugins.clap");
            _tmp = Env("MR_TMP") ?? Path.GetTempPath();
            _dropRealtime = Env("MR_DROP_REALTIME") == "1";
        }

        private static string Env(string key)
        {
            string v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static string PackageRoot([CallerFilePath] string thisFile = "")
        {
            return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(thisFile)));
        }
    }
}
