using System;
using System.Collections.Generic;

namespace Zori.ClapRouter
{
    public sealed class RhythmLevelComposer
    {
        private readonly int _track;
        private readonly RhythmPattern _pattern;
        private readonly int _sampleRate;
        private readonly int _blockSize;
        private readonly double _bpm;
        private readonly int _subdivision;
        private readonly double _preTolerance;
        private readonly double _postTolerance;
        private readonly short _key;

        public RhythmLevelComposer(
            int track,
            RhythmPattern pattern,
            int sampleRate = 48000,
            int blockSize = 512,
            double bpm = 120.0,
            int subdivision = 4,
            double preTolerance = 0.35,
            double postTolerance = 0.25,
            short key = 84
        )
        {
            _track = track;
            _pattern = pattern;
            _sampleRate = sampleRate;
            _blockSize = blockSize;
            _bpm = bpm;
            _subdivision = subdivision;
            _preTolerance = preTolerance;
            _postTolerance = postTolerance;
            _key = key;
        }

        public BeatGrid Grid => new BeatGrid(_sampleRate, _bpm, _subdivision);

        public RhythmSequence Sequence =>
            new RhythmSequence(_pattern, Grid, _preTolerance, _postTolerance);

        public Composition Compose()
        {
            BeatGrid grid = Grid;
            RhythmSequence sequence = Sequence;
            double framesPerBeat = grid.FramesPerBeat;
            long span = (long)
                Math.Round(_pattern.TotalTicks * framesPerBeat / NoteValues.TicksPerBeat);

            List<MrEvent> events = new List<MrEvent>(2 + _pattern.Count * 2);
            events.Add(
                MrEvent.ParamValue(
                    (ushort)_track,
                    MrDest.Instrument,
                    0u,
                    SeededComposer.SixSinesMainLevelParam,
                    1.0
                )
            );

            for (int i = 0; i < _pattern.Count; i++)
            {
                long onset = sequence.LocalOnset(i);
                long nextOnset = i + 1 < _pattern.Count ? sequence.LocalOnset(i + 1) : span;
                long gap = nextOnset - onset;
                long noteFrames = Math.Max(
                    _blockSize,
                    Math.Min(_blockSize * 3, gap - _blockSize / 2)
                );
                int id = i + 1;
                events.Add(MrEvent.NoteOn((ushort)_track, (uint)onset, id, 0, 0, _key, 0.9));
                events.Add(
                    MrEvent.NoteOff((ushort)_track, (uint)(onset + noteFrames), id, 0, 0, _key)
                );
            }

            return new Composition
            {
                Events = events.ToArray(),
                Grid = grid,
                Sequence = sequence,
                SampleRate = _sampleRate,
                BlockSize = _blockSize,
                TotalBlocks = (int)((span + _blockSize - 1) / _blockSize),
                TotalFrames = span,
                BassTrack = _track,
                KeysTrack = _track,
                PercTrack = _track,
                EffectSlot = 0,
                ExpectedNoteOnsets = _pattern.Count,
                PreRollEndFrame = 0,
            };
        }
    }
}
