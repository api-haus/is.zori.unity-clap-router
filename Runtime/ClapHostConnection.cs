using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Zori.ClapRouter
{
    public sealed class ClapHostConnection : IDisposable
    {
        private ClapHostProcess _process;
        private MusicRouterSession _session;
        private readonly Action<string> _log;

        private ClapHostConnection(ClapHostProcess process, MusicRouterSession session, Action<string> log)
        {
            _process = process;
            _session = session;
            _log = log;
        }

        public MusicRouterSession Session => _session;

        public static ClapHostConnection Start(ClapHostSettings settings, Action<string> log)
        {
            ClapHostProcess process = ClapHostProcess.Spawn(settings, log);
            try
            {
                WaitForSocketOrExit(process, settings.SocketPath);
                if (process.HasExited)
                {
                    throw new MusicRouterConnectException(settings.SocketPath,
                        "host process exited before accepting a connection (see the host log — e.g. a device "
                        + "block below the minimum is refused by the host)");
                }
                MusicRouterSession session = new MusicRouterSession(settings.SocketPath);
                return new ClapHostConnection(process, session, log);
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }

        public static ClapHostConnection StartWithDeviceFallback(ClapHostSettings settings, int fallbackBlock,
            Action<string> log)
        {
            try
            {
                return Start(settings, log);
            }
            catch (Exception e) when (settings.BlockSize < fallbackBlock)
            {
                log?.Invoke($"[ClapHostConnection] device block {settings.BlockSize} was rejected "
                    + $"({e.Message}); falling back to block {fallbackBlock}.");
                settings.BlockSize = fallbackBlock;
                return Start(settings, log);
            }
        }

        private static void WaitForSocketOrExit(ClapHostProcess process, string socketPath)
        {
            for (int i = 0; i < 250 && !process.HasExited && !File.Exists(socketPath); i++)
            {
                Thread.Sleep(20);
            }
        }

        public void Dispose()
        {
            Stopwatch sw = Stopwatch.StartNew();

            MusicRouterSession session = _session;
            _session = null;
            session?.Dispose();
            _log?.Invoke($"[ClapHostConnection] session disposed at {sw.ElapsedMilliseconds}ms; "
                + "host should self-exit on socket close.");

            ClapHostProcess process = _process;
            _process = null;
            process?.Dispose();
            _log?.Invoke($"[ClapHostConnection] host stopped at {sw.ElapsedMilliseconds}ms.");
        }
    }
}
