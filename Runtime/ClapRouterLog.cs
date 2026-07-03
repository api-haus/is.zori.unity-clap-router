using System;
using System.IO;
using Unity.Logging;
using Unity.Logging.Sinks;
using UnityEngine;

namespace Zori.ClapRouter
{
    public static class ClapRouterLog
    {
        public const string LevelEnvVar = "MR_LOG_LEVEL";

        private static readonly object Gate = new object();
        private static LogLevel _minimumLevel = ResolveInitialLevel();
        private static bool _configured;

        public static LogLevel MinimumLevel => _minimumLevel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            lock (Gate)
            {
                if (_configured)
                {
                    return;
                }
                Apply(_minimumLevel);
            }
        }

        public static void SetMinimumLevel(LogLevel level)
        {
            lock (Gate)
            {
                _minimumLevel = level;
                Apply(level);
            }
        }

        private static void Apply(LogLevel level)
        {
            LoggerConfig config = new LoggerConfig()
                .MinimumLevel.Set(level)
                .CaptureStacktrace(false)
                .OutputTemplate("{Timestamp} | {Level} | {Message}");

#if UNITY_EDITOR
            config = config.WriteTo.UnityEditorConsole(minLevel: level);
#else
            config = config
                .WriteTo.StdOut(minLevel: level)
                .WriteTo.File(LogFilePath(), minLevel: level);
#endif

            Log.Logger = new Unity.Logging.Logger(config);
            _configured = true;
        }

        private static string LogFilePath()
        {
            return Path.Combine(Application.persistentDataPath, "clap-router.log");
        }

        private static LogLevel ResolveInitialLevel()
        {
            string raw = Environment.GetEnvironmentVariable(LevelEnvVar);
            if (!string.IsNullOrEmpty(raw) && Enum.TryParse(raw, ignoreCase: true, out LogLevel parsed))
            {
                return parsed;
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            return LogLevel.Debug;
#else
            return LogLevel.Info;
#endif
        }
    }
}
