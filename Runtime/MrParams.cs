using System;
using System.Runtime.InteropServices;

namespace Zori.ClapRouter
{
    [Flags]
    public enum MrParamFlags : uint
    {
        None = 0,
        IsStepped = 1u << 0,
        IsPeriodic = 1u << 1,
        IsHidden = 1u << 2,
        IsReadonly = 1u << 3,
        IsBypass = 1u << 4,
        IsAutomatable = 1u << 5,
        IsModulatable = 1u << 10,
        RequiresProcess = 1u << 15,
        IsEnum = 1u << 16
    }

    [Flags]
    public enum MrRescanFlags : uint
    {
        None = 0,
        Values = 1u << 0,
        Text = 1u << 1,
        Info = 1u << 2,
        All = 1u << 3
    }

    public enum MrNotifyKind : ushort
    {
        ParamRescan = 0,
        ParamValue = 1,
        GuiClosed = 2
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct MrParamInfo
    {
        [FieldOffset(0)] public uint Id;
        [FieldOffset(4)] public MrParamFlags Flags;
        [FieldOffset(8)] public double MinValue;
        [FieldOffset(16)] public double MaxValue;
        [FieldOffset(24)] public double DefaultValue;
        [FieldOffset(32)] public double CurrentValue;

        public bool Has(MrParamFlags flag) => (Flags & flag) != 0;

        public bool IsBoolean => Has(MrParamFlags.IsStepped) && !Has(MrParamFlags.IsEnum)
            && MinValue == 0.0 && MaxValue == 1.0;

        public bool IsEnumList => Has(MrParamFlags.IsEnum);

        public bool IsIntegerStepped => Has(MrParamFlags.IsStepped) && !Has(MrParamFlags.IsEnum) && !IsBoolean;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct MrNotification
    {
        [FieldOffset(0)] public MrNotifyKind Kind;
        [FieldOffset(4)] public uint TrackId;
        [FieldOffset(8)] public int DestSlot;
        [FieldOffset(12)] public uint ParamId;
        [FieldOffset(16)] public MrRescanFlags RescanFlags;
        [FieldOffset(24)] public double Value;
    }
}
