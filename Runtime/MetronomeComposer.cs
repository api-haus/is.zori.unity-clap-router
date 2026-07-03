using System.Collections.Generic;

namespace Zori.ClapRouter
{
    public sealed class MetronomeComposer
    {
        private readonly int _track;
        private readonly int _sampleRate;
        private readonly int _blockSize;
        private readonly double _bpm;
        private readonly int _beats;
        private readonly short _key;
        private readonly int _subdivision;

        public MetronomeComposer(
            int track,
            int sampleRate = 48000,
            int blockSize = 512,
            double bpm = 120.0,
            int beats = 16,
            short key = 84,
            int subdivision = 4
        )
        {
            _track = track;
            _sampleRate = sampleRate;
            _blockSize = blockSize;
            _bpm = bpm;
            _beats = beats;
            _key = key;
            _subdivision = subdivision;
        }

        public long StepFrames => (long)System.Math.Round(_sampleRate * 60.0 / _bpm);

        public BeatGrid Grid => new BeatGrid(_sampleRate, _bpm, _subdivision);

        public Composition Compose()
        {
            long step = StepFrames;
            long noteFrames = _blockSize * 3;
            long totalFrames = step * _beats;

            List<MrEvent> events = new List<MrEvent>(2 + _beats * 2);
            events.Add(
                MrEvent.ParamValue(
                    (ushort)_track,
                    MrDest.Instrument,
                    0u,
                    SeededComposer.SixSinesMainLevelParam,
                    1.0
                )
            );

            for (int b = 0; b < _beats; b++)
            {
                long t = b * step;
                int id = b + 1;
                events.Add(MrEvent.NoteOn((ushort)_track, (uint)t, id, 0, 0, _key, 0.95));
                events.Add(MrEvent.NoteOff((ushort)_track, (uint)(t + noteFrames), id, 0, 0, _key));
            }

            return new Composition
            {
                Events = events.ToArray(),
                Grid = Grid,
                SampleRate = _sampleRate,
                BlockSize = _blockSize,
                TotalBlocks = (int)((totalFrames + _blockSize - 1) / _blockSize),
                TotalFrames = totalFrames,
                BassTrack = _track,
                KeysTrack = _track,
                PercTrack = _track,
                EffectSlot = 0,
                ExpectedNoteOnsets = _beats,
                PreRollEndFrame = 0,
            };
        }
    }
}
