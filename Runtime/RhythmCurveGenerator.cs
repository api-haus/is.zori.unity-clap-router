using System;
using System.Collections.Generic;

namespace Zori.ClapRouter
{
    public sealed class RhythmCurveGenerator
    {
        private readonly int _bars;
        private readonly int _beatsPerBar;
        private readonly double _startFrequency;
        private readonly double _minFrequency;
        private readonly double _maxFrequency;
        private readonly double _accel;
        private readonly double _damping;
        private readonly double _maxMutation;

        public RhythmCurveGenerator(
            int bars = 4,
            int beatsPerBar = 4,
            double startFrequency = 1.0,
            double minFrequency = 0.5,
            double maxFrequency = 4.0,
            double accel = 0.35,
            double damping = 0.8,
            double maxMutation = 0.6
        )
        {
            _bars = bars < 1 ? 1 : bars;
            _beatsPerBar = beatsPerBar < 1 ? 1 : beatsPerBar;
            _startFrequency = startFrequency;
            _minFrequency = minFrequency;
            _maxFrequency = maxFrequency;
            _accel = accel;
            _damping = damping;
            _maxMutation = maxMutation;
        }

        public RhythmPattern Generate(ulong seed)
        {
            DeterministicRandom rng = new DeterministicRandom(seed);
            int spanBeats = _bars * _beatsPerBar;
            int totalTicks = spanBeats * NoteValues.TicksPerBeat;

            List<int> onsets = new List<int>(spanBeats * 4);
            List<NoteValue> values = new List<NoteValue>(spanBeats * 4);
            double frequency = _startFrequency;
            double mutation = 0.0;
            int tick = NoteValues.TicksPerBeat;
            int guard = spanBeats * 8;

            while (tick < totalTicks && guard-- > 0)
            {
                NoteValue value = NearestSubdivision(frequency);
                onsets.Add(tick);
                values.Add(value);
                tick += NoteValues.Ticks(value);

                mutation += (rng.NextDouble() * 2.0 - 1.0) * _accel;
                mutation = Clamp(mutation * _damping, -_maxMutation, _maxMutation);
                frequency += mutation;
                if (frequency < _minFrequency)
                {
                    frequency = _minFrequency;
                    mutation = Math.Abs(mutation);
                }
                else if (frequency > _maxFrequency)
                {
                    frequency = _maxFrequency;
                    mutation = -Math.Abs(mutation);
                }
            }

            return RhythmPattern.FromOnsets(onsets, values, totalTicks);
        }

        private static NoteValue NearestSubdivision(double frequency) =>
            frequency < 0.707 ? NoteValue.Half
            : frequency < 1.414 ? NoteValue.Quarter
            : frequency < 2.828 ? NoteValue.Eighth
            : NoteValue.Sixteenth;

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : (value > max ? max : value);
    }
}
