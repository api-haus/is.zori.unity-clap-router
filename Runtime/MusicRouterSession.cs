using System;
using System.Text;
using System.Threading;

namespace Zori.ClapRouter
{
    public sealed class MusicRouterAbiMismatchException : Exception
    {
        public MusicRouterAbiMismatchException(int native, int managed)
            : base($"clap_ipc_client ABI mismatch: native reports {native}, managed expects {managed}")
        {
        }
    }

    public sealed class MusicRouterConnectException : Exception
    {
        public MusicRouterConnectException(string socketPath, string detail)
            : base($"mr_session_create failed for control socket '{socketPath}': {detail}")
        {
        }
    }

    public sealed unsafe class MusicRouterSession : IDisposable
    {
        private IntPtr _session;

        public MusicRouterSession(string controlSocketPath, uint flags = 0u)
        {
            int native = NativeMethods.mr_abi_version();
            if (native != MrAbi.Version)
            {
                throw new MusicRouterAbiMismatchException(native, MrAbi.Version);
            }

            byte[] pathBytes = Encoding.UTF8.GetBytes(controlSocketPath ?? string.Empty);
            byte[] nulTerminated = new byte[pathBytes.Length + 1];
            Array.Copy(pathBytes, nulTerminated, pathBytes.Length);

            fixed (byte* path = nulTerminated)
            {
                MrSessionConfigNative cfg = new MrSessionConfigNative
                {
                    ControlSocketPath = (IntPtr)path,
                    Flags = flags
                };
                _session = NativeMethods.mr_session_create(in cfg);
            }

            if (_session == IntPtr.Zero)
            {
                throw new MusicRouterConnectException(controlSocketPath, "session handle was null");
            }
        }

        public bool IsOpen => _session != IntPtr.Zero;

        public uint SampleRate => NativeMethods.mr_sample_rate(_session);

        public uint LookaheadFrames => NativeMethods.mr_lookahead_frames(_session);

        public uint OutputLatencyFrames => NativeMethods.mr_output_latency_frames(_session);

        public ulong NowFrame => NativeMethods.mr_now_frame(_session);

        public ulong NowFrameInterpolated => NativeMethods.mr_now_frame_interpolated(_session);

        public int CreateTrack()
        {
            return NativeMethods.mr_create_track(_session);
        }

        public MrStatus DestroyTrack(int trackId)
        {
            return NativeMethods.mr_destroy_track(_session, trackId);
        }

        public MrStatus LoadInstrument(int trackId, string clapPath, uint pluginIndex = 0u)
        {
            return NativeMethods.mr_load_instrument(_session, trackId, clapPath, pluginIndex);
        }

        public MrStatus InsertEffect(int trackId, int slotIndex, string clapPath, uint pluginIndex = 0u)
        {
            return NativeMethods.mr_insert_effect(_session, trackId, slotIndex, clapPath, pluginIndex);
        }

        public MrStatus RemoveEffect(int trackId, int slotIndex)
        {
            return NativeMethods.mr_remove_effect(_session, trackId, slotIndex);
        }

        public MrStatus SetTrackGain(int trackId, float gain)
        {
            return NativeMethods.mr_set_track_gain(_session, trackId, gain);
        }

        public MrStatus LoadState(int trackId, int destSlot, string path)
        {
            return NativeMethods.mr_load_state(_session, trackId, destSlot, path);
        }

        public MrStatus RenderCapture(uint blocks, string wavPath)
        {
            return NativeMethods.mr_render_capture(_session, blocks, wavPath);
        }

        public MrPushResult PushEvent(in MrEvent ev)
        {
            return NativeMethods.mr_push_event(_session, in ev);
        }

        public MrPushResult PushBatch(ReadOnlySpan<MrEvent> events, out int pushed)
        {
            fixed (MrEvent* p = events)
            {
                int n = 0;
                MrPushResult result = NativeMethods.mr_push_events(_session, p, events.Length, &n);
                pushed = n;
                return result;
            }
        }

        public MrPushResult NoteOn(int trackId, uint sampleTime, int noteId, short port, short channel, short key,
            double velocity)
        {
            return NativeMethods.mr_push_note_on(_session, trackId, sampleTime, noteId, port, channel, key, velocity);
        }

        public MrPushResult NoteOff(int trackId, uint sampleTime, int noteId, short port, short channel, short key,
            double velocity = 0.0)
        {
            return NativeMethods.mr_push_note_off(_session, trackId, sampleTime, noteId, port, channel, key, velocity);
        }

        public MrPushResult PitchBend(int trackId, uint sampleTime, int noteId, double semitones)
        {
            return NativeMethods.mr_push_pitch_bend(_session, trackId, sampleTime, noteId, semitones);
        }

        public MrPushResult SetParam(int trackId, int destSlot, uint sampleTime, uint paramId, double value)
        {
            return NativeMethods.mr_push_param(_session, trackId, destSlot, sampleTime, paramId, value);
        }

        public int ParamCount(int trackId, int destSlot)
        {
            return NativeMethods.mr_param_count(_session, trackId, destSlot);
        }

        public MrStatus ParamInfo(int trackId, int destSlot, int index, out MrParamInfo info)
        {
            return NativeMethods.mr_param_info(_session, trackId, destSlot, index, out info);
        }

        public MrStatus ParamInfoById(int trackId, int destSlot, uint paramId, out MrParamInfo info)
        {
            return NativeMethods.mr_param_info_by_id(_session, trackId, destSlot, paramId, out info);
        }

        public string ParamName(int trackId, int destSlot, uint paramId)
        {
            return ReadString(256, buf => NativeMethods.mr_param_name(_session, trackId, destSlot, paramId, buf, buf.Length));
        }

        public string ParamModule(int trackId, int destSlot, uint paramId)
        {
            return ReadString(1024, buf => NativeMethods.mr_param_module(_session, trackId, destSlot, paramId, buf, buf.Length));
        }

        public string ParamValueText(int trackId, int destSlot, uint paramId, double value)
        {
            return ReadString(128, buf => NativeMethods.mr_param_value_text(_session, trackId, destSlot, paramId, value, buf, buf.Length));
        }

        public double ParamValue(int trackId, int destSlot, uint paramId)
        {
            return NativeMethods.mr_param_value(_session, trackId, destSlot, paramId);
        }

        public bool PollNotification(out MrNotification notification)
        {
            return NativeMethods.mr_poll_notification(_session, out notification) == 1;
        }

        public MrStatus ShowGui(int trackId, int destSlot)
        {
            return NativeMethods.mr_show_gui(_session, trackId, destSlot);
        }

        public MrStatus HideGui(int trackId, int destSlot)
        {
            return NativeMethods.mr_hide_gui(_session, trackId, destSlot);
        }

        private static string ReadString(int capacity, Func<byte[], int> call)
        {
            byte[] buf = new byte[capacity];
            int len = call(buf);
            if (len <= 0)
            {
                return string.Empty;
            }
            if (len > buf.Length)
            {
                buf = new byte[len];
                len = call(buf);
                if (len <= 0)
                {
                    return string.Empty;
                }
            }
            return Encoding.UTF8.GetString(buf, 0, Math.Min(len, buf.Length));
        }

        public string LastError()
        {
            if (_session == IntPtr.Zero)
            {
                return string.Empty;
            }

            byte[] buf = new byte[256];
            int len = NativeMethods.mr_last_error_message(_session, buf, buf.Length);
            if (len <= 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buf, 0, Math.Min(len, buf.Length));
        }

        public void Dispose()
        {
            ReleaseHandle();
            GC.SuppressFinalize(this);
        }

        ~MusicRouterSession()
        {
            ReleaseHandle();
        }

        private void ReleaseHandle()
        {
            IntPtr handle = Interlocked.Exchange(ref _session, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                NativeMethods.mr_session_destroy(handle);
            }
        }
    }
}
