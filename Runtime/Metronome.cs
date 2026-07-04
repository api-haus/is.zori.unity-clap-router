using System;

namespace Zori.ClapRouter
{
    public sealed class Metronome : IDisposable
    {
        private const int ScheduleAheadBeats = 4;

        private readonly MusicRouterSession _session;
        private readonly int _track;
        private readonly int _beatsPerBar;
        private readonly short _accentKey;
        private readonly short _beatKey;
        private readonly double _framesPerBeat;
        private readonly long _clickFrames;
        private readonly long _origin;
        private long _nextBeat;
        private bool _disposed;

        public Metronome(
            MusicRouterSession session,
            string instrumentClapPath,
            uint pluginIndex,
            double bpm,
            long originFrame,
            int beatsPerBar = 4,
            short accentKey = 84,
            short beatKey = 77,
            double clickSeconds = 0.05,
            float gain = 0.6f
        )
        {
            _session = session;
            _track = session.CreateTrack();
            if (_track >= 0)
            {
                session.LoadInstrument(_track, instrumentClapPath, pluginIndex);
                session.SetTrackGain(_track, gain);
            }
            _beatsPerBar = Math.Max(1, beatsPerBar);
            _accentKey = accentKey;
            _beatKey = beatKey;
            _framesPerBeat = 60.0 / bpm * session.SampleRate;
            _clickFrames = Math.Max(1, (long)(clickSeconds * session.SampleRate));
            _origin = originFrame;
        }

        public bool Active => !_disposed && _track >= 0;

        public void Pump(long nowFrame)
        {
            if (!Active)
            {
                return;
            }
            long horizon = nowFrame + (long)(_framesPerBeat * ScheduleAheadBeats);
            while (BeatFrame(_nextBeat) <= horizon)
            {
                long onset = BeatFrame(_nextBeat);
                if (onset >= nowFrame)
                {
                    short key = _nextBeat % _beatsPerBar == 0 ? _accentKey : _beatKey;
                    int id = unchecked((int)(_nextBeat & 0x3FFFFFFF)) + 1;
                    _session.NoteOn(_track, (uint)onset, id, 0, 0, key, 0.9);
                    _session.NoteOff(_track, (uint)(onset + _clickFrames), id, 0, 0, key);
                }
                _nextBeat++;
            }
        }

        private long BeatFrame(long beat) => _origin + (long)Math.Round(beat * _framesPerBeat);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_track >= 0)
            {
                _session.DestroyTrack(_track);
            }
        }
    }
}
