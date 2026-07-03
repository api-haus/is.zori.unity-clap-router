using System;

namespace Zori.ClapRouter
{
    public readonly struct BeatGrid
    {
        public readonly double SampleRate;
        public readonly double Bpm;
        public readonly int StepsPerBeat;
        public readonly long OriginFrame;

        public BeatGrid(double sampleRate, double bpm, int stepsPerBeat = 4, long originFrame = 0)
        {
            SampleRate = sampleRate > 0.0 ? sampleRate : 48000.0;
            Bpm = bpm > 0.0 ? bpm : 120.0;
            StepsPerBeat = stepsPerBeat > 0 ? stepsPerBeat : 1;
            OriginFrame = originFrame;
        }

        public bool IsValid => SampleRate > 0.0 && Bpm > 0.0 && StepsPerBeat > 0;

        public double FramesPerBeat => SampleRate * 60.0 / Bpm;

        public double FramesPerStep => FramesPerBeat / StepsPerBeat;

        public BeatGrid WithOrigin(long originFrame) =>
            new BeatGrid(SampleRate, Bpm, StepsPerBeat, originFrame);

        public long FrameOfStep(long step) => OriginFrame + (long)Math.Round(step * FramesPerStep);

        public long NearestStep(long frame) =>
            (long)Math.Round((frame - OriginFrame) / FramesPerStep);

        public long StepFloor(long frame) =>
            (long)Math.Floor((frame - OriginFrame) / FramesPerStep);

        public long StepCeil(long frame) =>
            (long)Math.Ceiling((frame - OriginFrame) / FramesPerStep);

        public long SnapNearest(long frame) => FrameOfStep(NearestStep(frame));

        public long SnapFloor(long frame) => FrameOfStep(StepFloor(frame));

        public long SnapCeil(long frame) => FrameOfStep(StepCeil(frame));

        public long ErrorToNearest(long frame) => frame - SnapNearest(frame);

        public double Phase(long frame)
        {
            double steps = (frame - OriginFrame) / FramesPerStep;
            return steps - Math.Floor(steps);
        }

        public QuantizedHit Quantize(long inputFrame, long earliestFrame)
        {
            long step = NearestStep(inputFrame);
            long intendedFrame = FrameOfStep(step);
            long scheduledFrame =
                intendedFrame >= earliestFrame ? intendedFrame : SnapCeil(earliestFrame);
            long errorFrames = inputFrame - intendedFrame;
            double halfStep = FramesPerStep * 0.5;
            double accuracy =
                halfStep > 0.0 ? 1.0 - Math.Min(1.0, Math.Abs(errorFrames) / halfStep) : 1.0;

            return new QuantizedHit(
                inputFrame,
                step,
                intendedFrame,
                scheduledFrame,
                errorFrames,
                1000.0 * errorFrames / SampleRate,
                accuracy,
                scheduledFrame != intendedFrame,
                true
            );
        }
    }

    public readonly struct QuantizedHit
    {
        public readonly long InputFrame;
        public readonly long Step;
        public readonly long IntendedFrame;
        public readonly long ScheduledFrame;
        public readonly long ErrorFrames;
        public readonly double ErrorMs;
        public readonly double Accuracy;
        public readonly bool Delayed;
        public readonly bool Within;

        public QuantizedHit(
            long inputFrame,
            long step,
            long intendedFrame,
            long scheduledFrame,
            long errorFrames,
            double errorMs,
            double accuracy,
            bool delayed,
            bool within
        )
        {
            InputFrame = inputFrame;
            Step = step;
            IntendedFrame = intendedFrame;
            ScheduledFrame = scheduledFrame;
            ErrorFrames = errorFrames;
            ErrorMs = errorMs;
            Accuracy = accuracy;
            Delayed = delayed;
            Within = within;
        }

        public static QuantizedHit Immediate(long inputFrame, long scheduledFrame) =>
            new QuantizedHit(
                inputFrame,
                0,
                scheduledFrame,
                scheduledFrame,
                0,
                0.0,
                1.0,
                false,
                true
            );
    }
}
