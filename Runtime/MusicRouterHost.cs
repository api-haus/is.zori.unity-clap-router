using System;
using System.IO;
using Unity.Logging;
using UnityEngine;

namespace Zori.ClapRouter
{
    public sealed class MusicRouterHost : MonoBehaviour
    {
        [SerializeField] private string hostBinaryPath = "clap-ipc";
        [SerializeField] private string controlSocketPath = "";
        [SerializeField] private string instrumentClapPath = "";
        [SerializeField] private int sampleRate = 48000;
        [SerializeField] private int blockSize = 512;
        [SerializeField] private bool spawnHostProcess = true;
        [SerializeField] private int hostGraceExitMs = 700;
        [SerializeField] private int hostKillWaitMs = 2000;

        private ClapHostConnection _connection;
        private MusicRouterSession _externalSession;
        private int _instrumentTrack = -1;
        private bool _tornDown;

        public MusicRouterSession Session => _connection != null ? _connection.Session : _externalSession;

        public int InstrumentTrack => _instrumentTrack;

        private void Start()
        {
            ResolveHostBinary();

            string socket = string.IsNullOrEmpty(controlSocketPath)
                ? Path.Combine(Path.GetTempPath(), $"clap-router-{Guid.NewGuid():N}.sock")
                : controlSocketPath;

            if (!Connect(socket))
            {
                return;
            }

            MusicRouterSession session = Session;
            if (session != null && !string.IsNullOrEmpty(instrumentClapPath))
            {
                _instrumentTrack = session.CreateTrack();
                if (_instrumentTrack >= 0)
                {
                    session.LoadInstrument(_instrumentTrack, instrumentClapPath);
                }
            }
        }

        private bool Connect(string socket)
        {
            try
            {
                if (spawnHostProcess)
                {
                    ClapHostSettings settings = ClapHostSettings.ForDevice(hostBinaryPath, socket, sampleRate, blockSize);
                    settings.GraceExitMs = hostGraceExitMs;
                    settings.KillWaitMs = hostKillWaitMs;
                    string backendEnv = Environment.GetEnvironmentVariable("MR_BACKEND");
                    if (!string.IsNullOrEmpty(backendEnv))
                    {
                        settings.Backend = backendEnv;
                    }
                    string deviceEnv = Environment.GetEnvironmentVariable("MR_AUDIO_DEVICE");
                    if (!string.IsNullOrEmpty(deviceEnv))
                    {
                        settings.Device = deviceEnv;
                    }
                    _connection = ClapHostConnection.StartWithDeviceFallback(settings,
                        ClapHostSettings.MinDeviceBlock, LogHostLine);
                }
                else
                {
                    _externalSession = new MusicRouterSession(socket);
                }
                return true;
            }
            catch (DllNotFoundException)
            {
                Log.Error("[MusicRouterHost] libclap_ipc_client not found. Stage it into Plugins/linux-x86_64 (macos / win-x86_64) via tools/stage_unity_plugin.sh, or build the clap_ipc_client target.");
                return false;
            }
            catch (MusicRouterConnectException e)
            {
                Log.Error($"[MusicRouterHost] host did not accept a connection on '{socket}': {e.Message}. Check hostBinaryPath ('{hostBinaryPath}') and that the host can open an audio device.");
                return false;
            }
            catch (Exception e)
            {
                Log.Error($"[MusicRouterHost] connect failed: {e.Message}");
                return false;
            }
        }

        private void ResolveHostBinary()
        {
            if (ClapRouterContent.TryGetHostBinary(out string packed))
            {
                hostBinaryPath = packed;
            }
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

        public void SetParam(int track, int destSlot, uint paramId, double value, int offsetFrames = 0)
        {
            Session?.SetParam(track, destSlot, StampNow(offsetFrames), paramId, value);
        }

        private void OnDisable()
        {
            Teardown();
        }

        private void OnDestroy()
        {
            Teardown();
        }

        private void OnApplicationQuit()
        {
            Teardown();
        }

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
