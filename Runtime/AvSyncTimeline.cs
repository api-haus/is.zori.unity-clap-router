using System;

namespace Zori.ClapRouter
{
    public readonly struct AvClock
    {
        public readonly long NowFrame;
        public readonly double WallSeconds;

        public AvClock(long nowFrame, double wallSeconds)
        {
            NowFrame = nowFrame;
            WallSeconds = wallSeconds;
        }
    }

    public enum AvSyncMode
    {
        SimulateAhead = 0,
        DeterministicDelay = 1
    }

    public sealed class AvSyncTimeline
    {
        private readonly double _sampleRate;
        private readonly double _deviceLatencySeconds;

        public AvSyncTimeline(double sampleRate, double deviceLatencySeconds)
        {
            _sampleRate = sampleRate <= 0.0 ? 48000.0 : sampleRate;
            _deviceLatencySeconds = deviceLatencySeconds;
        }

        public double DeviceLatencySeconds => _deviceLatencySeconds;

        public static double LatencySecondsFromFrames(long latencyFrames, double sampleRate)
        {
            return latencyFrames / (sampleRate <= 0.0 ? 48000.0 : sampleRate);
        }

        public double AudibleTime(long audioFrame, AvClock clock)
        {
            double secondsUntilRendered = (audioFrame - clock.NowFrame) / _sampleRate;
            return clock.WallSeconds + secondsUntilRendered + _deviceLatencySeconds;
        }

        public double Progress(long audioFrame, double leadSeconds, AvClock clock)
        {
            double audible = AudibleTime(audioFrame, clock);
            if (leadSeconds <= 0.0)
            {
                return clock.WallSeconds >= audible ? 1.0 : 0.0;
            }
            double p = (clock.WallSeconds - (audible - leadSeconds)) / leadSeconds;
            return p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p);
        }

        public double FireTime(long audioFrame, AvClock clock)
        {
            return AudibleTime(audioFrame, clock);
        }

        public bool IsWithinLead(long audioFrame, double leadSeconds, AvClock clock)
        {
            double audible = AudibleTime(audioFrame, clock);
            return clock.WallSeconds >= audible - leadSeconds && clock.WallSeconds <= audible;
        }
    }
}
