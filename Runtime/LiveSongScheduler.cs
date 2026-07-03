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
        public QuantizedHit Hit;
    }

    public sealed class LiveSongScheduler
    {
        private struct PendingOff
        {
            public long Stamp;
            public int NoteId;
            public short Key;
        }

        private struct LeadOn
        {
            public long Stamp;
            public int NoteId;
            public short Key;
            public double Velocity;
        }

        private readonly MusicRouterSession _session;
        private MrEvent[] _songEvents;
        private long _totalFrames;
        private readonly int _sampleRate;
        private readonly int _leadTrack;
        private readonly uint _lookahead;
        private readonly long _leadDurationFrames;
        private readonly bool _loop;
        private readonly BeatGrid _baseGrid;
        private readonly bool _quantized;
        private readonly RhythmSequence _baseSequence;
        private readonly long _judgeLatency;
        private readonly Queue<PendingOff> _pendingOffs = new Queue<PendingOff>(16);
        private readonly Queue<LeadOn> _pendingLeadOns = new Queue<LeadOn>(16);

        private long _baseFrame;
        private int _cursor;
        private long _lastStamp;
        private int _leadVoice;
        private BeatGrid _grid;
        private RhythmSequence _sequence;
        private Func<Composition> _nextSegment;

        public event Action<RhythmSequence> SegmentStarted;

        public LiveSongScheduler(
            MusicRouterSession session,
            Composition composition,
            int leadTrack,
            uint lookaheadFrames,
            long leadDurationFrames,
            bool loop,
            long judgeLatencyFrames = 0
        )
        {
            _session = session;
            _songEvents = composition.Events;
            _totalFrames = composition.TotalFrames;
            _sampleRate = composition.SampleRate;
            _leadTrack = leadTrack;
            _lookahead = lookaheadFrames;
            _leadDurationFrames = leadDurationFrames;
            _loop = loop;
            _baseGrid = composition.Grid;
            _quantized = composition.Grid.IsValid;
            _baseSequence = composition.Sequence;
            _judgeLatency = judgeLatencyFrames;
        }

        public long BaseFrame => _baseFrame;

        public void Begin(long now)
        {
            _baseFrame = now + _lookahead;
            _cursor = 0;
            _lastStamp = 0;
            _leadVoice = 1;
            _pendingOffs.Clear();
            _pendingLeadOns.Clear();
            _grid = _baseGrid.WithOrigin(_baseFrame);
            _sequence = _baseSequence?.WithOrigin(_baseFrame);
            if (_sequence != null)
            {
                SegmentStarted?.Invoke(_sequence);
            }
        }

        public BeatGrid Grid => _grid;

        public RhythmSequence Sequence => _sequence;

        public void SetQuantizeSequence(RhythmSequence sequence)
        {
            _sequence = sequence?.WithOrigin(_baseFrame);
        }

        public void ClearQuantizeSequence()
        {
            _sequence = null;
        }

        public void SetNextSegmentProvider(Func<Composition> provider)
        {
            _nextSegment = provider;
        }

        public void PumpSong(long now)
        {
            long horizon = now + _lookahead;

            while (true)
            {
                long nextSong =
                    _cursor < _songEvents.Length
                        ? _baseFrame + _songEvents[_cursor].SampleTime
                        : long.MaxValue;
                long nextOff = _pendingOffs.Count > 0 ? _pendingOffs.Peek().Stamp : long.MaxValue;
                long nextLeadOn =
                    _pendingLeadOns.Count > 0 ? _pendingLeadOns.Peek().Stamp : long.MaxValue;

                long pick = Math.Min(nextSong, Math.Min(nextOff, nextLeadOn));
                if (pick == long.MaxValue || pick > horizon)
                {
                    break;
                }

                MrEvent ev;
                if (nextOff <= nextSong && nextOff <= nextLeadOn)
                {
                    PendingOff off = _pendingOffs.Peek();
                    long stamp = Math.Max(off.Stamp, _lastStamp);
                    ev = MrEvent.NoteOff(
                        (ushort)_leadTrack,
                        (uint)stamp,
                        off.NoteId,
                        0,
                        0,
                        off.Key
                    );
                    if (_session.PushEvent(in ev) != MrPushResult.Ok)
                    {
                        break;
                    }
                    _lastStamp = stamp;
                    _pendingOffs.Dequeue();
                }
                else if (nextLeadOn <= nextSong)
                {
                    LeadOn on = _pendingLeadOns.Peek();
                    long stamp = Math.Max(on.Stamp, _lastStamp);
                    ev = MrEvent.NoteOn(
                        (ushort)_leadTrack,
                        (uint)stamp,
                        on.NoteId,
                        0,
                        0,
                        on.Key,
                        on.Velocity
                    );
                    if (_session.PushEvent(in ev) != MrPushResult.Ok)
                    {
                        break;
                    }
                    _lastStamp = stamp;
                    _pendingLeadOns.Dequeue();
                }
                else
                {
                    ev = _songEvents[_cursor];
                    long stamp = Math.Max(_baseFrame + ev.SampleTime, _lastStamp);
                    ev.SampleTime = (uint)stamp;
                    if (_session.PushEvent(in ev) != MrPushResult.Ok)
                    {
                        break;
                    }
                    _lastStamp = stamp;
                    _cursor++;
                }
            }

            if (_cursor >= _songEvents.Length && now >= _baseFrame + _totalFrames)
            {
                if (_nextSegment != null)
                {
                    AdvanceSegment(_nextSegment());
                }
                else if (_loop)
                {
                    _baseFrame += _totalFrames;
                    _cursor = 0;
                }
            }
        }

        private void AdvanceSegment(Composition next)
        {
            _baseFrame += _totalFrames;
            _songEvents = next.Events;
            _totalFrames = next.TotalFrames;
            _grid = next.Grid.WithOrigin(_baseFrame);
            _sequence = next.Sequence?.WithOrigin(_baseFrame);
            _cursor = 0;
            if (_sequence != null)
            {
                SegmentStarted?.Invoke(_sequence);
            }
        }

        public void PushParam(long now, int track, int destSlot, uint paramId, double value)
        {
            long stamp = Math.Max(now + _lookahead, _lastStamp);
            MrEvent ev = MrEvent.ParamValue(
                (ushort)track,
                (short)destSlot,
                (uint)stamp,
                paramId,
                value
            );
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
            long earliest = Math.Max(now + _lookahead, _lastStamp);
            QuantizedHit hit;
            if (_sequence != null)
            {
                hit = _sequence.Quantize(now - _judgeLatency, earliest);
            }
            else if (_quantized)
            {
                hit = _grid.Quantize(now, earliest);
            }
            else
            {
                hit = QuantizedHit.Immediate(now, earliest);
            }
            long onStamp = hit.ScheduledFrame;
            int id = -1;
            if (hit.Within)
            {
                id = _leadVoice++;
                _pendingLeadOns.Enqueue(
                    new LeadOn
                    {
                        Stamp = onStamp,
                        NoteId = id,
                        Key = key,
                        Velocity = 0.95,
                    }
                );
                _pendingOffs.Enqueue(
                    new PendingOff
                    {
                        Stamp = onStamp + _leadDurationFrames,
                        NoteId = id,
                        Key = key,
                    }
                );
            }

            long leadFrames = onStamp - now;
            return new LeadNoteLog
            {
                NowFrame = now,
                Stamp = (uint)onStamp,
                LeadFrames = leadFrames,
                LeadMs = 1000.0 * leadFrames / _sampleRate,
                Key = key,
                NoteId = id,
                Hit = hit,
            };
        }
    }
}
