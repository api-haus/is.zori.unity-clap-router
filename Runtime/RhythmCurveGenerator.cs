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
        private readonly double _restChance;
        private readonly int _maxSixteenthRun;
        private readonly int _maxRestRun;

        public RhythmCurveGenerator(
            int bars = 4,
            int beatsPerBar = 4,
            double startFrequency = 1.0,
            double minFrequency = 0.5,
            double maxFrequency = 4.0,
            double accel = 0.35,
            double damping = 0.8,
            double maxMutation = 0.6,
            double restChance = 0.4,
            int maxSixteenthRun = 4,
            int maxRestRun = 1
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
            _restChance = restChance;
            _maxSixteenthRun = maxSixteenthRun < 1 ? 1 : maxSixteenthRun;
            _maxRestRun = maxRestRun < 1 ? 1 : maxRestRun;
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
            int sixteenthRun = 0;
            int restRun = 0;
            bool first = true;
            int guard = spanBeats * 8;

            while (tick < totalTicks && guard-- > 0)
            {
                NoteValue value = NearestSubdivision(frequency);
                if (value == NoteValue.Sixteenth && sixteenthRun >= _maxSixteenthRun)
                {
                    value = NoteValue.Eighth;
                    frequency = Math.Min(frequency, 2.0);
                    mutation = -Math.Abs(mutation);
                }

                bool rest = !first && restRun < _maxRestRun && rng.NextDouble() < _restChance;
                if (rest)
                {
                    restRun++;
                    sixteenthRun = 0;
                }
                else
                {
                    onsets.Add(tick);
                    values.Add(value);
                    restRun = 0;
                    sixteenthRun = value == NoteValue.Sixteenth ? sixteenthRun + 1 : 0;
                }
                first = false;

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
