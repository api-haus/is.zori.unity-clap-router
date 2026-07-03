using System;
using System.Collections.Generic;

namespace Zori.ClapRouter
{
    public enum NoteValue
    {
        Quarter = 0,
        Sixteenth = 1,
        QuarterTriplet = 2,
        SixteenthTriplet = 3,
        DottedQuarter = 4,
        DottedSixteenth = 5,
        Eighth = 6,
        Half = 7,
    }

    public static class NoteValues
    {
        public const int TicksPerBeat = 96;

        public static int Ticks(NoteValue value) =>
            value switch
            {
                NoteValue.Half => 192,
                NoteValue.Quarter => 96,
                NoteValue.Eighth => 48,
                NoteValue.Sixteenth => 24,
                NoteValue.QuarterTriplet => 64,
                NoteValue.SixteenthTriplet => 16,
                NoteValue.DottedQuarter => 144,
                NoteValue.DottedSixteenth => 36,
                _ => 96,
            };

        public static string Label(NoteValue value) =>
            value switch
            {
                NoteValue.Half => "1/2",
                NoteValue.Quarter => "1/4",
                NoteValue.Eighth => "1/8",
                NoteValue.Sixteenth => "1/16",
                NoteValue.QuarterTriplet => "1/4T",
                NoteValue.SixteenthTriplet => "1/16T",
                NoteValue.DottedQuarter => "1/4.",
                NoteValue.DottedSixteenth => "1/16.",
                _ => "?",
            };

        public static bool IsTriplet(NoteValue value) =>
            value == NoteValue.QuarterTriplet || value == NoteValue.SixteenthTriplet;

        public static bool IsDotted(NoteValue value) =>
            value == NoteValue.DottedQuarter || value == NoteValue.DottedSixteenth;
    }

    public sealed class RhythmCell
    {
        public readonly string Name;
        public readonly NoteValue[] Notes;
        public readonly int Ticks;

        public RhythmCell(string name, params NoteValue[] notes)
        {
            Name = name;
            Notes = notes;
            int ticks = 0;
            foreach (NoteValue note in notes)
            {
                ticks += NoteValues.Ticks(note);
            }
            Ticks = ticks;
        }

        public int Beats => Ticks / NoteValues.TicksPerBeat;
    }

    public sealed class RhythmPattern
    {
        public readonly NoteValue[] Notes;
        public readonly int[] OnsetTicks;
        public readonly int TotalTicks;

        private RhythmPattern(NoteValue[] notes, int[] onsetTicks, int totalTicks)
        {
            Notes = notes;
            OnsetTicks = onsetTicks;
            TotalTicks = totalTicks;
        }

        public int Count => Notes.Length;

        public int TotalBeats => TotalTicks / NoteValues.TicksPerBeat;

        public static RhythmPattern FromNotes(IReadOnlyList<NoteValue> notes)
        {
            NoteValue[] values = new NoteValue[notes.Count];
            int[] onsets = new int[notes.Count];
            int cursor = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                values[i] = notes[i];
                onsets[i] = cursor;
                cursor += NoteValues.Ticks(notes[i]);
            }
            return new RhythmPattern(values, onsets, cursor);
        }

        public static RhythmPattern FromOnsets(
            IReadOnlyList<int> onsetTicks,
            IReadOnlyList<NoteValue> noteValues,
            int totalTicks
        )
        {
            int[] onsets = new int[onsetTicks.Count];
            NoteValue[] values = new NoteValue[onsetTicks.Count];
            for (int i = 0; i < onsetTicks.Count; i++)
            {
                onsets[i] = onsetTicks[i];
                values[i] = noteValues[i];
            }
            return new RhythmPattern(values, onsets, totalTicks);
        }
    }

    public sealed class RhythmPatternGenerator
    {
        public static readonly RhythmCell[] DefaultPalette =
        {
            new RhythmCell("quarter", NoteValue.Quarter),
            new RhythmCell(
                "sixteenths",
                NoteValue.Sixteenth,
                NoteValue.Sixteenth,
                NoteValue.Sixteenth,
                NoteValue.Sixteenth
            ),
            new RhythmCell(
                "sextuplet",
                NoteValue.SixteenthTriplet,
                NoteValue.SixteenthTriplet,
                NoteValue.SixteenthTriplet,
                NoteValue.SixteenthTriplet,
                NoteValue.SixteenthTriplet,
                NoteValue.SixteenthTriplet
            ),
            new RhythmCell(
                "quarter-triplets",
                NoteValue.QuarterTriplet,
                NoteValue.QuarterTriplet,
                NoteValue.QuarterTriplet
            ),
            new RhythmCell(
                "dotted-sixteenths",
                NoteValue.DottedSixteenth,
                NoteValue.DottedSixteenth,
                NoteValue.Sixteenth
            ),
            new RhythmCell(
                "dotted-quarters",
                NoteValue.DottedQuarter,
                NoteValue.DottedQuarter,
                NoteValue.Quarter
            ),
        };

        private readonly RhythmCell[] _palette;

        public RhythmPatternGenerator(RhythmCell[] palette = null)
        {
            _palette = palette ?? DefaultPalette;
        }

        public RhythmPattern Generate(int bars, ulong seed, int beatsPerBar = 4)
        {
            DeterministicRandom rng = new DeterministicRandom(seed);
            List<NoteValue> notes = new List<NoteValue>(bars * beatsPerBar * 6);
            int beatsLeft = bars * beatsPerBar;

            while (beatsLeft > 0)
            {
                RhythmCell cell = PickCell(ref rng, beatsLeft);
                notes.AddRange(cell.Notes);
                beatsLeft -= cell.Beats;
            }

            return RhythmPattern.FromNotes(notes);
        }

        private RhythmCell PickCell(ref DeterministicRandom rng, int beatsLeft)
        {
            int candidates = 0;
            foreach (RhythmCell cell in _palette)
            {
                if (cell.Beats <= beatsLeft)
                {
                    candidates++;
                }
            }

            int pick = rng.NextInt(0, candidates);
            foreach (RhythmCell cell in _palette)
            {
                if (cell.Beats > beatsLeft)
                {
                    continue;
                }
                if (pick == 0)
                {
                    return cell;
                }
                pick--;
            }

            return _palette[0];
        }
    }
}
