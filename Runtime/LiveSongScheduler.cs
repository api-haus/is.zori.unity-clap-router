using System;
using System.Collections.Generic;

namespace Zori.ClapRouter
{
    public struct LeadNoteLog
    {
        public long NowFrame;
        public uint Stamp;
        public long LeadFrames;
        public double LeadMs;
        public short Key;
        public int NoteId;
    }

    public sealed class LiveSongScheduler
    {
        private struct PendingOff
        {
            public long Stamp;
            public int NoteId;
            public short Key;
        }

        private readonly MusicRouterSession _session;
        private readonly MrEvent[] _songEvents;
        private readonly long _totalFrames;
        private readonly int _sampleRate;
        private readonly int _leadTrack;
        private readonly uint _lookahead;
        private readonly long _leadDurationFrames;
        private readonly bool _loop;
        private readonly Queue<PendingOff> _pendingOffs = new Queue<PendingOff>(16);

        private long _baseFrame;
        private int _cursor;
        private long _lastStamp;
        private int _leadVoice;

        public LiveSongScheduler(MusicRouterSession session, Composition composition, int leadTrack,
            uint lookaheadFrames, long leadDurationFrames, bool loop)
        {
            _session = session;
            _songEvents = composition.Events;
            _totalFrames = composition.TotalFrames;
            _sampleRate = composition.SampleRate;
            _leadTrack = leadTrack;
            _lookahead = lookaheadFrames;
            _leadDurationFrames = leadDurationFrames;
            _loop = loop;
        }

        public long BaseFrame => _baseFrame;

        public void Begin(long now)
        {
            _baseFrame = now + _lookahead;
            _cursor = 0;
            _lastStamp = 0;
            _leadVoice = 1;
            _pendingOffs.Clear();
        }

        public void PumpSong(long now)
        {
            long horizon = now + _lookahead;

            while (true)
            {
                long nextSong = _cursor < _songEvents.Length
                    ? _baseFrame + _songEvents[_cursor].SampleTime
                    : long.MaxValue;
                long nextOff = _pendingOffs.Count > 0 ? _pendingOffs.Peek().Stamp : long.MaxValue;
                if (nextSong == long.MaxValue && nextOff == long.MaxValue)
                {
                    break;
                }

                bool songFirst = nextSong <= nextOff;
                long pick = songFirst ? nextSong : nextOff;
                if (pick > horizon)
                {
                    break;
                }

                if (songFirst)
                {
                    MrEvent ev = _songEvents[_cursor];
                    long stamp = Math.Max(_baseFrame + ev.SampleTime, _lastStamp);
                    ev.SampleTime = (uint)stamp;
                    if (_session.PushEvent(in ev) != MrPushResult.Ok)
                    {
                        break;
                    }
                    _lastStamp = stamp;
                    _cursor++;
                }
                else
                {
                    PendingOff off = _pendingOffs.Peek();
                    long stamp = Math.Max(off.Stamp, _lastStamp);
                    MrEvent ev = MrEvent.NoteOff((ushort)_leadTrack, (uint)stamp, off.NoteId, 0, 0, off.Key);
                    if (_session.PushEvent(in ev) != MrPushResult.Ok)
                    {
                        break;
                    }
                    _lastStamp = stamp;
                    _pendingOffs.Dequeue();
                }
            }

            if (_cursor >= _songEvents.Length && now >= _baseFrame + _totalFrames && _loop)
            {
                _baseFrame += _totalFrames;
                _cursor = 0;
            }
        }

        public void PushParam(long now, int track, int destSlot, uint paramId, double value)
        {
            long stamp = Math.Max(now + _lookahead, _lastStamp);
            MrEvent ev = MrEvent.ParamValue((ushort)track, (short)destSlot, (uint)stamp, paramId, value);
            _session.PushEvent(in ev);
            _lastStamp = stamp;
        }

        public void PushSustainedNote(long now, int track, short key, double velocity)
        {
            long stamp = Math.Max(now + _lookahead, _lastStamp);
            int id = _leadVoice++;
            MrEvent ev = MrEvent.NoteOn((ushort)track, (uint)stamp, id, 0, 0, key, velocity);
            _session.PushEvent(in ev);
            _lastStamp = stamp;
        }

        public LeadNoteLog TriggerLead(long now, short key)
        {
            long onStamp = Math.Max(now + _lookahead, _lastStamp);
            int id = _leadVoice++;
            MrEvent on = MrEvent.NoteOn((ushort)_leadTrack, (uint)onStamp, id, 0, 0, key, 0.95);
            _session.PushEvent(in on);
            _lastStamp = onStamp;
            _pendingOffs.Enqueue(new PendingOff
            {
                Stamp = onStamp + _leadDurationFrames,
                NoteId = id,
                Key = key
            });

            long leadFrames = onStamp - now;
            return new LeadNoteLog
            {
                NowFrame = now,
                Stamp = (uint)onStamp,
                LeadFrames = leadFrames,
                LeadMs = 1000.0 * leadFrames / _sampleRate,
                Key = key,
                NoteId = id
            };
        }
    }
}
