using System;
using System.IO;
using Unity.Logging;
using UnityEngine;

namespace Zori.ClapRouter
{
    public sealed class MusicRouterHost : MonoBehaviour
    {
        private const int DefaultSampleRate = 48000;
        private const int DefaultBlockSize = 512;
        private const int GraceExitMs = 700;
        private const int KillWaitMs = 2000;

        private ClapHostConnection _connection;
        private MusicRouterSession _externalSession;
        private bool _tornDown;

        public MusicRouterSession Session =>
            _connection != null ? _connection.Session : _externalSession;

        private void Start()
        {
            string socket = Path.Combine(
                Path.GetTempPath(),
                $"clap-router-{Guid.NewGuid():N}.sock"
            );
            Connect(socket);
        }

        private void Connect(string socket)
        {
            string host = ResolveHostBinary();
            if (!File.Exists(host))
            {
                Log.Error(
                    $"[MusicRouterHost] clap-ipc host not found at '{host}'. Run 'CLAP Router ▸ Download clap-ipc Host' to fetch the host modules into StreamingAssets."
                );
                return;
            }

            try
            {
                ClapHostSettings settings = ClapHostSettings.ForDevice(
                    host,
                    socket,
                    EnvInt("MR_SAMPLERATE", DefaultSampleRate),
                    EnvInt("MR_BLOCK", DefaultBlockSize)
                );
                settings.GraceExitMs = GraceExitMs;
                settings.KillWaitMs = KillWaitMs;
                string backend = Environment.GetEnvironmentVariable("MR_BACKEND");
                if (!string.IsNullOrEmpty(backend))
                {
                    settings.Backend = backend;
                }
                string device = Environment.GetEnvironmentVariable("MR_AUDIO_DEVICE");
                if (!string.IsNullOrEmpty(device))
                {
                    settings.Device = device;
                }
                _connection = ClapHostConnection.StartWithDeviceFallback(
                    settings,
                    ClapHostSettings.MinDeviceBlock,
                    LogHostLine
                );
            }
            catch (DllNotFoundException)
            {
                Log.Error(
                    "[MusicRouterHost] libclap_ipc_client not found. In this repo run tools/stage_unity_plugin.sh; a shipped package downloads it on import."
                );
            }
            catch (MusicRouterConnectException e)
            {
                Log.Error(
                    $"[MusicRouterHost] host did not accept a connection on '{socket}': {e.Message}. The clap-ipc host could not be launched — run 'CLAP Router ▸ Download clap-ipc Host' to (re)install the host modules."
                );
            }
            catch (Exception e)
            {
                Log.Error($"[MusicRouterHost] connect failed: {e.Message}");
            }
        }

        private static string ResolveHostBinary()
        {
            string exe = IsWindows() ? "clap-ipc.exe" : "clap-ipc";
            return Path.Combine(
                Application.streamingAssetsPath,
                ClapRouterContent.RootFolder,
                "host",
                ClapRouterContent.PlatformDir,
                exe
            );
        }

        private static bool IsWindows() =>
            Application.platform == RuntimePlatform.WindowsPlayer
            || Application.platform == RuntimePlatform.WindowsEditor;

        private static int EnvInt(string key, int fallback)
        {
            string value = Environment.GetEnvironmentVariable(key);
            return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : fallback;
        }

        public uint StampNow(int offsetFrames = 0)
        {
            MusicRouterSession session = Session;
            if (session == null)
            {
                return 0u;
            }
            long frame = (long)session.NowFrame + session.LookaheadFrames + offsetFrames;
            return (uint)Math.Max(0, frame);
        }

        public void NoteOn(int track, int noteId, short key, double velocity, int offsetFrames = 0)
        {
            Session?.NoteOn(track, StampNow(offsetFrames), noteId, 0, 0, key, velocity);
        }

        public void NoteOff(int track, int noteId, short key, int offsetFrames = 0)
        {
            Session?.NoteOff(track, StampNow(offsetFrames), noteId, 0, 0, key);
        }

        public void PitchBend(int track, int noteId, double semitones, int offsetFrames = 0)
        {
            Session?.PitchBend(track, StampNow(offsetFrames), noteId, semitones);
        }

        public void SetParam(
            int track,
            int destSlot,
            uint paramId,
            double value,
            int offsetFrames = 0
        )
        {
            Session?.SetParam(track, destSlot, StampNow(offsetFrames), paramId, value);
        }

        private void OnDisable() => Teardown();

        private void OnDestroy() => Teardown();

        private void OnApplicationQuit() => Teardown();

        private void Teardown()
        {
            if (_tornDown)
            {
                return;
            }
            _tornDown = true;

            _connection?.Dispose();
            _connection = null;
            _externalSession?.Dispose();
            _externalSession = null;
        }

        private static void LogHostLine(string line)
        {
            Log.Debug($"[clap-ipc/host] {line}");
        }
    }
}
