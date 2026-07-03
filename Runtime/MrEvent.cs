using System.Runtime.InteropServices;

namespace Zori.ClapRouter
{
    public static class MrAbi
    {
        public const int Version = 4;
    }

    public enum MrEventKind : ushort
    {
        NoteOn = 0,
        NoteOff = 1,
        NoteChoke = 2,
        NoteExpr = 3,
        Param = 4,
        ParamMod = 5,
        Midi1 = 6,
        Midi2 = 7
    }

    public enum MrExpr
    {
        Volume = 0,
        Pan = 1,
        Tuning = 2,
        Vibrato = 3,
        Expression = 4,
        Brightness = 5,
        Pressure = 6
    }

    [System.Flags]
    public enum MrEventFlags : ushort
    {
        None = 0,
        IsLive = 1
    }

    public enum MrStatus
    {
        Ok = 0,
        ErrConnect = 1,
        ErrHandshake = 2,
        ErrVersion = 3,
        ErrLoad = 4,
        ErrNoNode = 5,
        ErrNoTrack = 6,
        ErrInvalid = 7,
        ErrIo = 8
    }

    public enum MrPushResult
    {
        Ok = 0,
        RingFull = 1,
        NoSession = 2
    }

    public static class MrDest
    {
        public const short Instrument = -1;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct MrNote
    {
        [FieldOffset(0)] public int NoteId;
        [FieldOffset(4)] public short Port;
        [FieldOffset(6)] public short Channel;
        [FieldOffset(8)] public short Key;
        [FieldOffset(24)] public double Velocity;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct MrExprRec
    {
        [FieldOffset(0)] public MrExpr ExpressionId;
        [FieldOffset(4)] public int NoteId;
        [FieldOffset(8)] public short Port;
        [FieldOffset(10)] public short Channel;
        [FieldOffset(12)] public short Key;
        [FieldOffset(24)] public double Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct MrParam
    {
        [FieldOffset(0)] public uint ParamId;
        [FieldOffset(4)] public int NoteId;
        [FieldOffset(24)] public double Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct MrMidi2
    {
        [FieldOffset(0)] public uint Ump0;
        [FieldOffset(4)] public uint Ump1;
        [FieldOffset(8)] public uint Ump2;
        [FieldOffset(12)] public uint Ump3;
        [FieldOffset(16)] public ushort Port;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct MrEvent
    {
        [FieldOffset(0)] public uint SampleTime;
        [FieldOffset(4)] public MrEventKind Kind;
        [FieldOffset(6)] public MrEventFlags Flags;
        [FieldOffset(8)] public ushort TrackId;
        [FieldOffset(10)] public short DestSlot;
        [FieldOffset(16)] public MrNote Note;
        [FieldOffset(16)] public MrExprRec Expr;
        [FieldOffset(16)] public MrParam Param;
        [FieldOffset(16)] public MrMidi2 Midi;

        public static MrEvent NoteOn(ushort trackId, uint sampleTime, int noteId, short port, short channel, short key,
            double velocity, bool live = true)
        {
            return Note4(MrEventKind.NoteOn, trackId, sampleTime, noteId, port, channel, key, velocity, live);
        }

        public static MrEvent NoteOff(ushort trackId, uint sampleTime, int noteId, short port, short channel, short key,
            double velocity = 0.0, bool live = true)
        {
            return Note4(MrEventKind.NoteOff, trackId, sampleTime, noteId, port, channel, key, velocity, live);
        }

        public static MrEvent NoteChoke(ushort trackId, uint sampleTime, int noteId, short port, short channel,
            short key, bool live = true)
        {
            return Note4(MrEventKind.NoteChoke, trackId, sampleTime, noteId, port, channel, key, 0.0, live);
        }

        public static MrEvent NoteExpr(ushort trackId, uint sampleTime, MrExpr expression, int noteId, short port,
            short channel, short key, double value)
        {
            MrEvent ev = default;
            ev.SampleTime = sampleTime;
            ev.Kind = MrEventKind.NoteExpr;
            ev.TrackId = trackId;
            ev.DestSlot = MrDest.Instrument;
            ev.Expr.ExpressionId = expression;
            ev.Expr.NoteId = noteId;
            ev.Expr.Port = port;
            ev.Expr.Channel = channel;
            ev.Expr.Key = key;
            ev.Expr.Value = value;
            return ev;
        }

        public static MrEvent PitchBend(ushort trackId, uint sampleTime, int noteId, double semitones, short port = 0,
            short channel = 0, short key = -1)
        {
            return NoteExpr(trackId, sampleTime, MrExpr.Tuning, noteId, port, channel, key, semitones);
        }

        public static MrEvent ParamValue(ushort trackId, short destSlot, uint sampleTime, uint paramId, double value,
            int noteId = -1)
        {
            return ParamRecord(MrEventKind.Param, trackId, destSlot, sampleTime, paramId, value, noteId);
        }

        public static MrEvent ParamMod(ushort trackId, short destSlot, uint sampleTime, uint paramId, double value,
            int noteId = -1)
        {
            return ParamRecord(MrEventKind.ParamMod, trackId, destSlot, sampleTime, paramId, value, noteId);
        }

        public static MrEvent Midi2(ushort trackId, uint sampleTime, uint ump0, uint ump1, uint ump2, uint ump3,
            ushort port, bool live = false)
        {
            MrEvent ev = default;
            ev.SampleTime = sampleTime;
            ev.Kind = MrEventKind.Midi2;
            ev.Flags = live ? MrEventFlags.IsLive : MrEventFlags.None;
            ev.TrackId = trackId;
            ev.DestSlot = MrDest.Instrument;
            ev.Midi.Ump0 = ump0;
            ev.Midi.Ump1 = ump1;
            ev.Midi.Ump2 = ump2;
            ev.Midi.Ump3 = ump3;
            ev.Midi.Port = port;
            return ev;
        }

        public static MrEvent Midi1(ushort trackId, uint sampleTime, byte status, byte data1, byte data2,
            ushort port = 0, bool live = false)
        {
            uint ump0 = (uint)status | ((uint)data1 << 8) | ((uint)data2 << 16);
            MrEvent ev = Midi2(trackId, sampleTime, ump0, 0u, 0u, 0u, port, live);
            ev.Kind = MrEventKind.Midi1;
            return ev;
        }

        private static MrEvent ParamRecord(MrEventKind kind, ushort trackId, short destSlot, uint sampleTime,
            uint paramId, double value, int noteId)
        {
            MrEvent ev = default;
            ev.SampleTime = sampleTime;
            ev.Kind = kind;
            ev.TrackId = trackId;
            ev.DestSlot = destSlot;
            ev.Param.ParamId = paramId;
            ev.Param.NoteId = noteId;
            ev.Param.Value = value;
            return ev;
        }

        private static MrEvent Note4(MrEventKind kind, ushort trackId, uint sampleTime, int noteId, short port,
            short channel, short key, double velocity, bool live)
        {
            MrEvent ev = default;
            ev.SampleTime = sampleTime;
            ev.Kind = kind;
            ev.Flags = live ? MrEventFlags.IsLive : MrEventFlags.None;
            ev.TrackId = trackId;
            ev.DestSlot = MrDest.Instrument;
            ev.Note.NoteId = noteId;
            ev.Note.Port = port;
            ev.Note.Channel = channel;
            ev.Note.Key = key;
            ev.Note.Velocity = velocity;
            return ev;
        }
    }
}
