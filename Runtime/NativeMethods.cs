using System;
using System.Runtime.InteropServices;

namespace Zori.ClapRouter
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MrSessionConfigNative
    {
        public IntPtr ControlSocketPath;
        public uint Flags;
    }

    internal static unsafe class NativeMethods
    {
        private const string Lib = "clap_ipc_client";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_abi_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mr_session_create(in MrSessionConfigNative cfg);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mr_session_destroy(IntPtr session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong mr_now_frame(IntPtr session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong mr_now_frame_interpolated(IntPtr session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint mr_lookahead_frames(IntPtr session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint mr_output_latency_frames(IntPtr session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint mr_sample_rate(IntPtr session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_last_error_message(IntPtr session, byte[] buf, int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_create_track(IntPtr session);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_destroy_track(IntPtr session, int trackId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_load_instrument(IntPtr session, int trackId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string clapPath, uint pluginIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_insert_effect(IntPtr session, int trackId, int slotIndex,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string clapPath, uint pluginIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_remove_effect(IntPtr session, int trackId, int slotIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_set_track_gain(IntPtr session, int trackId, float gain);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_load_state(IntPtr session, int trackId, int destSlot,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_render_capture(IntPtr session, uint blocks,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string wavPath);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrPushResult mr_push_event(IntPtr session, in MrEvent ev);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrPushResult mr_push_events(IntPtr session, MrEvent* evs, int count, int* outPushed);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrPushResult mr_push_note_on(IntPtr session, int trackId, uint sampleTime, int noteId,
            short port, short channel, short key, double velocity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrPushResult mr_push_note_off(IntPtr session, int trackId, uint sampleTime, int noteId,
            short port, short channel, short key, double velocity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrPushResult mr_push_pitch_bend(IntPtr session, int trackId, uint sampleTime, int noteId,
            double semitones);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrPushResult mr_push_param(IntPtr session, int trackId, int destSlot, uint sampleTime,
            uint paramId, double value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_param_count(IntPtr session, int trackId, int destSlot);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_param_info(IntPtr session, int trackId, int destSlot, int index,
            out MrParamInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_param_info_by_id(IntPtr session, int trackId, int destSlot, uint paramId,
            out MrParamInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_param_name(IntPtr session, int trackId, int destSlot, uint paramId, byte[] buf,
            int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_param_module(IntPtr session, int trackId, int destSlot, uint paramId, byte[] buf,
            int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_param_value_text(IntPtr session, int trackId, int destSlot, uint paramId,
            double value, byte[] buf, int cap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern double mr_param_value(IntPtr session, int trackId, int destSlot, uint paramId);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mr_poll_notification(IntPtr session, out MrNotification notification);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_show_gui(IntPtr session, int trackId, int destSlot);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern MrStatus mr_hide_gui(IntPtr session, int trackId, int destSlot);
    }
}
