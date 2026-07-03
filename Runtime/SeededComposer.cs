using System;
using System.Collections.Generic;

namespace Zori.ClapRouter
{
    public struct SoloWindow
    {
        public int Track;
        public long StartFrame;
        public long EndFrame;
        public long OnsetFrame;
        public double ExpectedCents;
        public short Key;
    }

    public sealed class Composition
    {
        public MrEvent[] Events;
        public int SampleRate;
        public int BlockSize;
        public int TotalBlocks;
        public long TotalFrames;
        public int BassTrack;
        public int KeysTrack;
        public int PercTrack;
        public int EffectSlot;
        public int ExpectedNoteOnsets;
        public long PreRollEndFrame;
        public SoloWindow SampleAccuracy;
        public SoloWindow PitchGlide;
        public SoloWindow EffectSignature;
    }

    public struct DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public ulong NextU64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public double NextDouble()
        {
            return (NextU64() >> 11) * (1.0 / 9007199254740992.0);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            ulong range = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextU64() % range);
        }
    }

    public sealed class SeededComposer
    {
        public const uint SixSinesMainLevelParam = 500u;
        public const uint SixSinesFineTuningParam = 536u;
        public const uint SvfFreqParam = 1024u;
        public const uint SvfModeParam = 1026u;

        private static readonly short[] BassScale = { 36, 39, 41, 43, 46, 48 };
        private static readonly short[] KeysRoots = { 60, 62, 65, 67 };
        private static readonly short[] TriadIntervals = { 0, 4, 7 };
        private static readonly short[] PercKeys = { 84, 86, 88 };

        private readonly int _seed;
        private readonly int _sampleRate;
        private readonly int _blockSize;
        private readonly int _totalBlocks;
        private readonly int _bassTrack;
        private readonly int _keysTrack;
        private readonly int _percTrack;
        private readonly int _effectSlot;

        public SeededComposer(int seed, int bassTrack, int keysTrack, int percTrack, int effectSlot = 0,
            int sampleRate = 48000, int blockSize = 512, int totalBlocks = 512)
        {
            _seed = seed;
            _bassTrack = bassTrack;
            _keysTrack = keysTrack;
            _percTrack = percTrack;
            _effectSlot = effectSlot;
            _sampleRate = sampleRate;
            _blockSize = blockSize;
            _totalBlocks = totalBlocks;
        }

        public Composition Compose()
        {
            DeterministicRandom rng = new DeterministicRandom((ulong)(uint)_seed * 0x100000001B3UL + 0xCBF29CE484222325UL);
            List<MrEvent> events = new List<MrEvent>(1024);
            int voice = 1;

            long block = _blockSize;
            long preRollEnd = 8 * block;
            long ensembleAEnd = 200 * block;
            long g4Start = 210 * block;
            long g4End = 250 * block;
            long g3Start = 260 * block;
            long g3End = 300 * block;
            long g5Start = 310 * block;
            long g5End = 360 * block;
            long ensembleBStart = 372 * block;
            long ensembleBEnd = 504 * block;
            long totalFrames = (long)_totalBlocks * block;

            EmitVoicing(events);

            int onsets = 0;
            onsets += EmitEnsemble(events, ref rng, ref voice, preRollEnd, ensembleAEnd);

            SoloWindow g4 = EmitPitchGlide(events, ref rng, ref voice, g4Start, g4End);
            onsets += 1;

            SoloWindow g3 = EmitSampleAccuracy(events, ref voice, g3Start, g3End);
            onsets += 1;

            SoloWindow g5 = EmitEffectSignature(events, ref voice, g5Start, g5End);
            onsets += 1;

            onsets += EmitEnsemble(events, ref rng, ref voice, ensembleBStart, ensembleBEnd);

            MrEvent[] sorted = StableSortByTime(events);

            return new Composition
            {
                Events = sorted,
                SampleRate = _sampleRate,
                BlockSize = _blockSize,
                TotalBlocks = _totalBlocks,
                TotalFrames = totalFrames,
                BassTrack = _bassTrack,
                KeysTrack = _keysTrack,
                PercTrack = _percTrack,
                EffectSlot = _effectSlot,
                ExpectedNoteOnsets = onsets,
                PreRollEndFrame = preRollEnd,
                SampleAccuracy = g3,
                PitchGlide = g4,
                EffectSignature = g5
            };
        }

        private void EmitVoicing(List<MrEvent> events)
        {
            events.Add(MrEvent.ParamValue((ushort)_bassTrack, MrDest.Instrument, 0u, SixSinesMainLevelParam, 1.0));
            events.Add(MrEvent.ParamValue((ushort)_keysTrack, MrDest.Instrument, 0u, SixSinesMainLevelParam, 0.85));
            events.Add(MrEvent.ParamValue((ushort)_percTrack, MrDest.Instrument, 0u, SixSinesMainLevelParam, 0.9));
            events.Add(MrEvent.ParamValue((ushort)_keysTrack, MrDest.Instrument, 0u, SixSinesFineTuningParam, 0.5));
            events.Add(MrEvent.ParamValue((ushort)_keysTrack, (short)_effectSlot, 0u, SvfModeParam, 0.0));
            events.Add(MrEvent.ParamValue((ushort)_keysTrack, (short)_effectSlot, 0u, SvfFreqParam, 1.0));
        }

        private int EmitEnsemble(List<MrEvent> events, ref DeterministicRandom rng, ref int voice, long start, long end)
        {
            int onsets = 0;
            long bassStep = _sampleRate / 2;
            long keysStep = _sampleRate;
            long percStep = _sampleRate / 4;

            for (long t = start; t + bassStep <= end; t += bassStep)
            {
                short key = BassScale[rng.NextInt(0, BassScale.Length)];
                int id = voice++;
                long dur = bassStep - _blockSize;
                events.Add(MrEvent.NoteOn((ushort)_bassTrack, (uint)t, id, 0, 0, key, 0.85));
                if (rng.NextDouble() < 0.5)
                {
                    double bend = (rng.NextDouble() - 0.5) * 1.0;
                    events.Add(MrEvent.PitchBend((ushort)_bassTrack, (uint)(t + dur / 2), id, bend));
                }
                events.Add(MrEvent.NoteOff((ushort)_bassTrack, (uint)(t + dur), id, 0, 0, key));
                onsets += 1;
            }

            for (long t = start; t + keysStep <= end; t += keysStep)
            {
                short root = KeysRoots[rng.NextInt(0, KeysRoots.Length)];
                long dur = keysStep - _blockSize * 2;
                int chordVoice = voice;
                foreach (short interval in TriadIntervals)
                {
                    short key = (short)(root + interval);
                    int id = voice++;
                    events.Add(MrEvent.NoteOn((ushort)_keysTrack, (uint)t, id, 0, 0, key, 0.75));
                    events.Add(MrEvent.NoteOff((ushort)_keysTrack, (uint)(t + dur), id, 0, 0, key));
                    onsets += 1;
                }
                events.Add(MrEvent.PitchBend((ushort)_keysTrack, (uint)(t + dur / 2), chordVoice, 0.25));
            }

            for (long t = start; t + percStep <= end; t += percStep)
            {
                short key = PercKeys[rng.NextInt(0, PercKeys.Length)];
                int id = voice++;
                long dur = _blockSize * 3;
                events.Add(MrEvent.NoteOn((ushort)_percTrack, (uint)t, id, 0, 0, key, 0.9));
                events.Add(MrEvent.NoteOff((ushort)_percTrack, (uint)(t + dur), id, 0, 0, key));
                onsets += 1;
            }

            return onsets;
        }

        private SoloWindow EmitPitchGlide(List<MrEvent> events, ref DeterministicRandom rng, ref int voice, long start,
            long end)
        {
            short key = 43;
            int id = voice++;
            long onset = start + _blockSize * 4;
            long release = end - _blockSize * 4;
            long span = release - onset;
            double targetCents = 200.0;

            events.Add(MrEvent.NoteOn((ushort)_bassTrack, (uint)onset, id, 0, 0, key, 0.9));

            int steps = 24;
            for (int s = 1; s <= steps; s++)
            {
                long t = onset + span * s / (steps + 1);
                double semitones = 2.0 * s / steps;
                events.Add(MrEvent.PitchBend((ushort)_bassTrack, (uint)t, id, semitones));
            }

            events.Add(MrEvent.NoteOff((ushort)_bassTrack, (uint)release, id, 0, 0, key));

            return new SoloWindow
            {
                Track = _bassTrack,
                StartFrame = start,
                EndFrame = end,
                OnsetFrame = onset,
                ExpectedCents = targetCents,
                Key = key
            };
        }

        private SoloWindow EmitSampleAccuracy(List<MrEvent> events, ref int voice, long start, long end)
        {
            short key = 84;
            int id = voice++;
            long onset = start + 173;
            long release = onset + _blockSize * 6;

            events.Add(MrEvent.NoteOn((ushort)_percTrack, (uint)onset, id, 0, 0, key, 0.95));
            events.Add(MrEvent.NoteOff((ushort)_percTrack, (uint)release, id, 0, 0, key));

            return new SoloWindow
            {
                Track = _percTrack,
                StartFrame = start,
                EndFrame = end,
                OnsetFrame = onset,
                ExpectedCents = 0.0,
                Key = key
            };
        }

        private SoloWindow EmitEffectSignature(List<MrEvent> events, ref int voice, long start, long end)
        {
            short key = 72;
            int id = voice++;
            long onset = start + _blockSize * 4;
            long release = end - _blockSize * 4;

            events.Add(MrEvent.ParamValue((ushort)_keysTrack, (short)_effectSlot, (uint)start, SvfFreqParam, 0.15));
            events.Add(MrEvent.NoteOn((ushort)_keysTrack, (uint)onset, id, 0, 0, key, 0.85));
            events.Add(MrEvent.NoteOff((ushort)_keysTrack, (uint)release, id, 0, 0, key));
            events.Add(MrEvent.ParamValue((ushort)_keysTrack, (short)_effectSlot, (uint)end, SvfFreqParam, 1.0));

            return new SoloWindow
            {
                Track = _keysTrack,
                StartFrame = start,
                EndFrame = end,
                OnsetFrame = onset,
                ExpectedCents = 0.0,
                Key = key
            };
        }

        private static MrEvent[] StableSortByTime(List<MrEvent> events)
        {
            MrEvent[] arr = events.ToArray();
            int[] order = new int[arr.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (a, b) =>
            {
                int c = arr[a].SampleTime.CompareTo(arr[b].SampleTime);
                return c != 0 ? c : a.CompareTo(b);
            });

            MrEvent[] sorted = new MrEvent[arr.Length];
            for (int i = 0; i < order.Length; i++)
            {
                sorted[i] = arr[order[i]];
            }

            return sorted;
        }
    }
}
