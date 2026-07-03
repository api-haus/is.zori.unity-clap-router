using System;
using System.Diagnostics;

namespace Zori.ClapRouter
{
    public struct ClapHostSettings
    {
        public const int MinDeviceBlock = 512;

        public string BinaryPath;
        public string Backend;
        public int SampleRate;
        public int BlockSize;
        public string SocketPath;
        public string Device;
        public bool DropRealtime;
        public int GraceExitMs;
        public int KillWaitMs;

        public static ClapHostSettings ForDevice(string binaryPath, string socketPath, int sampleRate = 48000,
            int blockSize = 512)
        {
            return new ClapHostSettings
            {
                BinaryPath = binaryPath,
                Backend = "device",
                SampleRate = sampleRate,
                BlockSize = blockSize,
                SocketPath = socketPath,
                Device = null,
                DropRealtime = false,
                GraceExitMs = 700,
                KillWaitMs = 2000
            };
        }
    }

    public sealed class ClapHostProcess : IDisposable
    {
        private Process _process;
        private readonly int _graceExitMs;
        private readonly int _killWaitMs;
        private readonly Action<string> _log;

        private ClapHostProcess(Process process, int graceExitMs, int killWaitMs, Action<string> log)
        {
            _process = process;
            _graceExitMs = graceExitMs;
            _killWaitMs = killWaitMs;
            _log = log;
        }

        public bool HasExited => _process == null || _process.HasExited;

        public static ClapHostProcess Spawn(ClapHostSettings settings, Action<string> log)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = settings.BinaryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.Environment.Remove("LD_PRELOAD");
            info.ArgumentList.Add($"--backend={settings.Backend}");
            info.ArgumentList.Add($"--samplerate={settings.SampleRate}");
            info.ArgumentList.Add($"--block={settings.BlockSize}");
            info.ArgumentList.Add($"--control-socket={settings.SocketPath}");
            if (!string.IsNullOrEmpty(settings.Device))
            {
                info.ArgumentList.Add($"--device={settings.Device}");
            }
            if (settings.DropRealtime)
            {
                info.ArgumentList.Add("--drop-realtime");
            }

            Process process = Process.Start(info);
            if (process == null)
            {
                throw new InvalidOperationException($"failed to start clap-ipc host '{settings.BinaryPath}'");
            }

            process.OutputDataReceived += (_, __) => { };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null && e.Data.Contains("[clap-ipc]"))
                {
                    log?.Invoke(e.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return new ClapHostProcess(process, settings.GraceExitMs, settings.KillWaitMs, log);
        }

        public void Dispose()
        {
            Process process = _process;
            _process = null;
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited && !process.WaitForExit(Math.Max(0, _graceExitMs)))
                {
                    _log?.Invoke($"[ClapHostProcess] host up after {_graceExitMs}ms grace; killing.");
                    process.Kill();
                    process.WaitForExit(Math.Max(0, _killWaitMs));
                }
            }
            catch (Exception e)
            {
                _log?.Invoke($"[ClapHostProcess] stop threw: {e.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
