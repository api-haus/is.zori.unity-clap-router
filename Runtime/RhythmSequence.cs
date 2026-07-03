using System;

namespace Zori.ClapRouter
{
    public sealed class RhythmSequence
    {
        private readonly long[] _localOnsets;
        private readonly NoteValue[] _values;
        private readonly long _span;
        private readonly long _origin;
        private readonly double _sampleRate;
        private readonly double _preTolerance;
        private readonly double _postTolerance;
        private readonly int _beats;

        public RhythmSequence(
            RhythmPattern pattern,
            BeatGrid grid,
            double preTolerance = 0.35,
            double postTolerance = 0.25,
            long origin = 0
        )
        {
            double framesPerBeat = grid.FramesPerBeat;
            int count = pattern.Count;
            long[] onsets = new long[count];
            for (int i = 0; i < count; i++)
            {
                onsets[i] = (long)
                    Math.Round(pattern.OnsetTicks[i] * framesPerBeat / NoteValues.TicksPerBeat);
            }

            _localOnsets = onsets;
            _values = (NoteValue[])pattern.Notes.Clone();
            _span = (long)Math.Round(pattern.TotalTicks * framesPerBeat / NoteValues.TicksPerBeat);
            _origin = origin;
            _sampleRate = grid.SampleRate;
            _preTolerance = preTolerance;
            _postTolerance = postTolerance;
            _beats = pattern.TotalBeats;
        }

        private RhythmSequence(
            long[] localOnsets,
            NoteValue[] values,
            long span,
            double sampleRate,
            double preTolerance,
            double postTolerance,
            int beats,
            long origin
        )
        {
            _localOnsets = localOnsets;
            _values = values;
            _span = span;
            _sampleRate = sampleRate;
            _preTolerance = preTolerance;
            _postTolerance = postTolerance;
            _beats = beats;
            _origin = origin;
        }

        public long Span => _span;

        public long Origin => _origin;

        public int Count => _localOnsets.Length;

        public int Beats => _beats;

        public double FramesPerBeat => _beats > 0 ? (double)_span / _beats : _span;

        public double PreTolerance => _preTolerance;

        public double PostTolerance => _postTolerance;

        public long LocalOnset(int index) => _localOnsets[index];

        public NoteValue ValueAt(int index) => _values[index];

        public long AbsoluteOnset(long loop, int index) =>
            _origin + loop * _span + _localOnsets[index];

        public long PrevGap(int index)
        {
            int n = _localOnsets.Length;
            return index > 0
                ? _localOnsets[index] - _localOnsets[index - 1]
                : _localOnsets[0] + _span - _localOnsets[n - 1];
        }

        public long PreWindowFrames(int index) => (long)(_preTolerance * PrevGap(index));

        public long PostWindowFrames(int index) => (long)(_postTolerance * PrevGap(index));

        public RhythmSequence WithOrigin(long origin) =>
            new RhythmSequence(
                _localOnsets,
                _values,
                _span,
                _sampleRate,
                _preTolerance,
                _postTolerance,
                _beats,
                origin
            );

        public long LoopOf(long frame) => FloorDiv(frame - _origin, _span);

        public long NextOnsetAtOrAfter(long frame)
        {
            long rel = frame - _origin;
            long loop = FloorDiv(rel, _span);
            long local = rel - loop * _span;
            int lo = LowerBound(local);
            if (lo < _localOnsets.Length)
            {
                return _origin + loop * _span + _localOnsets[lo];
            }
            return _origin + (loop + 1) * _span + _localOnsets[0];
        }

        public QuantizedHit Quantize(long judgeFrame, long earliestFrame)
        {
            int n = _localOnsets.Length;
            long rel = judgeFrame - _origin;
            long loop = FloorDiv(rel, _span);
            long local = rel - loop * _span;
            long baseAbs = _origin + loop * _span;
            int iNext = LowerBound(local);

            long nextLocal = iNext < n ? _localOnsets[iNext] : _localOnsets[0] + _span;
            int nextIndex = iNext < n ? iNext : 0;
            long prevLocal = iNext - 1 >= 0 ? _localOnsets[iNext - 1] : _localOnsets[n - 1] - _span;
            int prevIndex = iNext - 1 >= 0 ? iNext - 1 : n - 1;

            long upcoming = baseAbs + nextLocal;
            long passed = baseAbs + prevLocal;
            long interval = upcoming - passed;
            long distToPassed = judgeFrame - passed;
            long distToUpcoming = upcoming - judgeFrame;
            double postEdge = _postTolerance * PrevGap(prevIndex);
            double preEdge = _preTolerance * interval;
            bool inPost = distToPassed <= postEdge;
            bool inPre = distToUpcoming <= preEdge;

            long onset;
            int index;
            bool within;
            if ((inPost && inPre && distToPassed <= distToUpcoming) || (inPost && !inPre))
            {
                onset = passed;
                index = prevIndex;
                within = inPost;
            }
            else if (inPre)
            {
                onset = upcoming;
                index = nextIndex;
                within = true;
            }
            else if (distToPassed <= distToUpcoming)
            {
                onset = passed;
                index = prevIndex;
                within = false;
            }
            else
            {
                onset = upcoming;
                index = nextIndex;
                within = false;
            }

            long errorFrames = judgeFrame - onset;
            double edge = errorFrames < 0 ? preEdge : postEdge;
            double accuracy =
                !within ? 0.0
                : edge <= 0.0 ? 1.0
                : 1.0 - Math.Min(1.0, Math.Abs(errorFrames) / edge);

            long scheduled = onset >= earliestFrame ? onset : NextOnsetAtOrAfter(earliestFrame);

            return new QuantizedHit(
                judgeFrame,
                index,
                onset,
                scheduled,
                errorFrames,
                1000.0 * errorFrames / _sampleRate,
                accuracy,
                scheduled != onset,
                within
            );
        }

        private int LowerBound(long value)
        {
            int lo = 0;
            int hi = _localOnsets.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_localOnsets[mid] < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if (a % b != 0 && (a < 0) != (b < 0))
            {
                q--;
            }
            return q;
        }
    }
}
