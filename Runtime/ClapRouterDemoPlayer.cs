using System;
using System.Collections.Generic;
using System.IO;
using Unity.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Zori.ClapRouter
{
    using Random = UnityEngine.Random;

    public enum DemoSongMode
    {
        MetronomeBeat = 0,
        SeededEnsemble = 1,
        RhythmLevel = 2,
    }

    [RequireComponent(typeof(MusicRouterHost))]
    public sealed class ClapRouterDemoPlayer : MonoBehaviour
    {
        [SerializeField]
        private MusicRouterHost host;

        [SerializeField]
        private DemoSongMode songMode = DemoSongMode.MetronomeBeat;

        [SerializeField]
        private int metronomeBpm = 120;

        [SerializeField]
        private int quantizeSubdivision = 4;

        [SerializeField]
        private int rhythmBars = 4;

        [SerializeField]
        private double hitPreTolerance = 0.35;

        [SerializeField]
        private double hitPostTolerance = 0.25;

        [SerializeField]
        private string sixSinesClapPath = "";

        [SerializeField]
        private string svfClapPath = "";

        [SerializeField]
        private string vitalClapPath = "";

        [SerializeField]
        private string vitalPresetPath = "";

        [SerializeField]
        private int seed = 20240617;

        [SerializeField]
        private bool loop = true;

        [SerializeField]
        private int leadNoteMs = 120;

        [SerializeField]
        private InputActionReference keypressAction;

        private const int SvfIndex = 13;
        private const int GainIndex = 4;

        private static readonly short[] LeadScale = { 79, 81, 84, 86, 88, 91, 93, 96 };

        private MusicRouterSession _session;
        private LiveSongScheduler _scheduler;
        private readonly List<ClapRouterNodeRef> _inspectedNodes = new List<ClapRouterNodeRef>();
        private readonly List<SongOnset> _songOnsets = new List<SongOnset>();
        private readonly List<RhythmSequence> _sheets = new List<RhythmSequence>();
        private int _sheetIndex;
        private int _leadTrack = -1;
        private bool _ready;
        private bool _failed;

        public event Action<long, short, int> LeadNoteFired;

        public event Action<QuantizedHit> PlayerHitJudged;

        public MusicRouterSession Session => _ready ? _session : null;

        public RhythmSequence RhythmSequence => _ready ? _scheduler.Sequence : null;

        public IReadOnlyList<ClapRouterNodeRef> InspectedNodes => _inspectedNodes;

        public IReadOnlyList<SongOnset> SongOnsets => _songOnsets;

        public long SongBaseFrame => _ready ? _scheduler.BaseFrame : 0;

        public void SetParam(int track, int destSlot, uint paramId, double value)
        {
            if (_ready)
            {
                _scheduler.PushParam((long)_session.NowFrame, track, destSlot, paramId, value);
            }
        }

        private void Awake()
        {
            if (host == null)
            {
                host = GetComponent<MusicRouterHost>();
            }
            ResolveContentPaths();
        }

        private void ResolveContentPaths()
        {
            sixSinesClapPath = ResolveClap(sixSinesClapPath, "six-sines");
            svfClapPath = ResolveClap(svfClapPath, "clap-plugins");
            vitalClapPath = ResolveClap(vitalClapPath, "vital");
        }

        private static string ResolveClap(string authored, string id)
        {
            return ClapRouterContent.TryGetClap(id, out string packed) ? packed : authored;
        }

        private void OnEnable()
        {
            keypressAction?.action?.Enable();
        }

        private void OnDisable()
        {
            keypressAction?.action?.Disable();

            _ready = false;
            _scheduler = null;
            _session = null;
            _sheets.Clear();
        }

        private void Update()
        {
            if (_failed)
            {
                return;
            }

            if (!_ready)
            {
                MusicRouterSession session = host != null ? host.Session : null;
                if (session == null || !session.IsOpen)
                {
                    return;
                }
                TryBeginPlayback(session);
                return;
            }

            _scheduler.PumpSong((long)_session.NowFrame);

            InputAction action = keypressAction != null ? keypressAction.action : null;
            if (action != null && action.WasPressedThisDynamicUpdate())
            {
                FireLeadNote();
            }
        }

        private void FireLeadNote()
        {
            short key = LeadScale[Random.Range(0, LeadScale.Length)];
            LeadNoteLog log = _scheduler.TriggerLead((long)_session.NowFrame, key);
            if (log.Hit.Within)
            {
                LeadNoteFired?.Invoke(log.Stamp, log.Key, _leadTrack);
            }
            PlayerHitJudged?.Invoke(log.Hit);
            QuantizedHit hit = log.Hit;
            Log.Info(
                $"[ClapRouterDemoPlayer] keypress -> {(hit.Within ? "HIT" : "MISS")} key={log.Key} id={log.NoteId} step={hit.Step} err={hit.ErrorMs:F1}ms ({(hit.ErrorFrames < 0 ? "early" : "late")}) acc={hit.Accuracy:P0}{(hit.Delayed ? " (quantized to next onset)" : " (on grid)")} now={log.NowFrame} lead={log.LeadFrames}f ({log.LeadMs:F1}ms). Audible latency adds host device output latency (see [clap-ipc][audio] OPEN latency)."
            );
        }

        private void TryBeginPlayback(MusicRouterSession session)
        {
            switch (songMode)
            {
                case DemoSongMode.MetronomeBeat:
                    BeginMetronomePlayback(session);
                    break;
                case DemoSongMode.RhythmLevel:
                    BeginRhythmLevelPlayback(session);
                    break;
                default:
                    BeginEnsemblePlayback(session);
                    break;
            }
        }

        private void BeginRhythmLevelPlayback(MusicRouterSession session)
        {
            if (!ClapExists(sixSinesClapPath, nameof(sixSinesClapPath), "SixSines.clap"))
            {
                _failed = true;
                return;
            }

            int guide = session.CreateTrack();
            int lead = session.CreateTrack();
            if (guide < 0 || lead < 0)
            {
                Log.Error("[ClapRouterDemoPlayer] host rejected track creation");
                _failed = true;
                return;
            }
            if (!Require("loadGuide", session.LoadInstrument(guide, sixSinesClapPath, 0)))
            {
                _failed = true;
                return;
            }

            string leadClap = HasClap(vitalClapPath) ? vitalClapPath : sixSinesClapPath;
            if (!Require("loadLead", session.LoadInstrument(lead, leadClap, 0)))
            {
                _failed = true;
                return;
            }
            bool distinctLead = leadClap != sixSinesClapPath;
            session.SetTrackGain(guide, 0.8f);
            session.SetTrackGain(lead, 1.0f);

            _sheetIndex = 0;
            Composition composition = BuildRhythmSheet(guide, session, _sheetIndex);

            _songOnsets.Clear();
            _leadTrack = lead;

            long leadFrames = (long)leadNoteMs * session.SampleRate / 1000;
            _scheduler = new LiveSongScheduler(
                session,
                composition,
                lead,
                session.LookaheadFrames,
                leadFrames,
                loop,
                session.OutputLatencyFrames
            );
            _sheets.Clear();
            _scheduler.SegmentStarted += OnSheetStarted;
            _scheduler.SetNextSegmentProvider(() =>
                BuildRhythmSheet(guide, session, ++_sheetIndex)
            );
            _session = session;
            _scheduler.Begin((long)session.NowFrame);

            _inspectedNodes.Clear();
            _inspectedNodes.Add(new ClapRouterNodeRef(guide, (int)MrDest.Instrument, "Hint"));
            _inspectedNodes.Add(new ClapRouterNodeRef(lead, (int)MrDest.Instrument, "Play"));

            _ready = true;

            Log.Info(
                $"[ClapRouterDemoPlayer] rhythm level: 2D-curve pulse over {rhythmBars} bars starting on a 1/4 rest, {metronomeBpm} BPM, a fresh sheet generated each time one completes; hint on track {guide} (SixSines), play on separate track {lead} ({(distinctLead ? "Vital" : "SixSines — set vitalClapPath for a distinct instrument")}); keypress snaps to the nearest anticipated onset, tolerance pre {hitPreTolerance:F2}x / post {hitPostTolerance:F2}x of the note interval, latency-compensated by {session.OutputLatencyFrames}f. sr={session.SampleRate} lookahead={session.LookaheadFrames}."
            );
        }

        private static bool HasClap(string path) =>
            !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        private Composition BuildRhythmSheet(int guide, MusicRouterSession session, int index)
        {
            RhythmPattern pattern = new RhythmCurveGenerator(rhythmBars).Generate(SheetSeed(index));
            RhythmLevelComposer composer = new RhythmLevelComposer(
                guide,
                pattern,
                (int)session.SampleRate,
                bpm: metronomeBpm,
                subdivision: quantizeSubdivision,
                preTolerance: hitPreTolerance,
                postTolerance: hitPostTolerance
            );
            return composer.Compose();
        }

        private ulong SheetSeed(int index) =>
            (ulong)(uint)seed * 0x9E3779B97F4A7C15UL
            + (ulong)(uint)index * 0xD1B54A32D192ED03UL
            + 1UL;

        private void OnSheetStarted(RhythmSequence sheet)
        {
            _sheets.Add(sheet);
        }

        public RhythmSequence CurrentSheet(long audibleFrame)
        {
            while (_sheets.Count > 1 && _sheets[0].Origin + _sheets[0].Span <= audibleFrame)
            {
                _sheets.RemoveAt(0);
            }
            for (int i = _sheets.Count - 1; i >= 0; i--)
            {
                if (_sheets[i].Origin <= audibleFrame)
                {
                    return _sheets[i];
                }
            }
            return _sheets.Count > 0 ? _sheets[0] : null;
        }

        private void BeginMetronomePlayback(MusicRouterSession session)
        {
            if (!ClapExists(sixSinesClapPath, nameof(sixSinesClapPath), "SixSines.clap"))
            {
                _failed = true;
                return;
            }

            int beat = session.CreateTrack();
            if (beat < 0)
            {
                Log.Error("[ClapRouterDemoPlayer] host rejected track creation");
                _failed = true;
                return;
            }
            if (!Require("loadBeat", session.LoadInstrument(beat, sixSinesClapPath, 0)))
            {
                _failed = true;
                return;
            }
            session.SetTrackGain(beat, 1.0f);

            MetronomeComposer composer = new MetronomeComposer(
                beat,
                (int)session.SampleRate,
                bpm: metronomeBpm,
                subdivision: quantizeSubdivision
            );
            Composition composition = composer.Compose();

            _songOnsets.Clear();
            foreach (MrEvent ev in composition.Events)
            {
                if (ev.Kind == MrEventKind.NoteOn)
                {
                    _songOnsets.Add(new SongOnset(ev.SampleTime, ev.Note.Key, ev.TrackId));
                }
            }
            _leadTrack = beat;

            long leadFrames = (long)leadNoteMs * session.SampleRate / 1000;
            _scheduler = new LiveSongScheduler(
                session,
                composition,
                beat,
                session.LookaheadFrames,
                leadFrames,
                loop
            );
            _session = session;
            _scheduler.Begin((long)session.NowFrame);

            _inspectedNodes.Clear();
            _inspectedNodes.Add(new ClapRouterNodeRef(beat, (int)MrDest.Instrument, "Beat"));

            _ready = true;

            Log.Info(
                $"[ClapRouterDemoPlayer] metronome diagnostic: one voice (track={beat}), {metronomeBpm} BPM quarter notes ({composer.StepFrames} frames/beat), keypress notes quantize to 1/{4 * quantizeSubdivision} grid ({composer.Grid.FramesPerStep:F0} frames/step), sr={session.SampleRate} lookahead={session.LookaheadFrames} loop={loop}. Circle closure must land exactly on every tick."
            );
        }

        private void BeginEnsemblePlayback(MusicRouterSession session)
        {
            if (
                !ClapExists(sixSinesClapPath, nameof(sixSinesClapPath), "SixSines.clap")
                || !ClapExists(svfClapPath, nameof(svfClapPath), "ClapPlugins.clap")
            )
            {
                _failed = true;
                return;
            }

            int bass = session.CreateTrack();
            int keys = session.CreateTrack();
            int perc = session.CreateTrack();
            int lead = session.CreateTrack();
            if (bass < 0 || keys < 0 || perc < 0 || lead < 0)
            {
                Log.Error("[ClapRouterDemoPlayer] host rejected track creation");
                _failed = true;
                return;
            }

            if (
                !Require("loadBass", session.LoadInstrument(bass, sixSinesClapPath, 0))
                || !Require("loadKeys", session.LoadInstrument(keys, sixSinesClapPath, 0))
                || !Require("loadPerc", session.LoadInstrument(perc, sixSinesClapPath, 0))
                || !Require("loadLead", session.LoadInstrument(lead, sixSinesClapPath, 0))
            )
            {
                _failed = true;
                return;
            }

            if (
                !Require("svfBass", session.InsertEffect(bass, 0, svfClapPath, SvfIndex))
                || !Require("gainBass", session.InsertEffect(bass, 1, svfClapPath, GainIndex))
                || !Require("svfKeys", session.InsertEffect(keys, 0, svfClapPath, SvfIndex))
                || !Require("svfPerc", session.InsertEffect(perc, 0, svfClapPath, SvfIndex))
            )
            {
                _failed = true;
                return;
            }

            session.SetTrackGain(bass, 1.0f);
            session.SetTrackGain(keys, 0.95f);
            session.SetTrackGain(perc, 0.85f);
            session.SetTrackGain(lead, 1.0f);

            int vital = TryLoadVital(session);

            SeededComposer composer = new SeededComposer(
                seed,
                bass,
                keys,
                perc,
                effectSlot: 0,
                sampleRate: (int)session.SampleRate,
                bpm: metronomeBpm,
                subdivision: quantizeSubdivision
            );
            Composition composition = composer.Compose();

            _songOnsets.Clear();
            foreach (MrEvent ev in composition.Events)
            {
                if (ev.Kind == MrEventKind.NoteOn)
                {
                    _songOnsets.Add(new SongOnset(ev.SampleTime, ev.Note.Key, ev.TrackId));
                }
            }
            _leadTrack = lead;

            long leadFrames = (long)leadNoteMs * session.SampleRate / 1000;
            _scheduler = new LiveSongScheduler(
                session,
                composition,
                lead,
                session.LookaheadFrames,
                leadFrames,
                loop
            );
            _session = session;
            _scheduler.Begin((long)session.NowFrame);

            _inspectedNodes.Clear();
            _inspectedNodes.Add(new ClapRouterNodeRef(bass, (int)MrDest.Instrument, "Bass"));
            _inspectedNodes.Add(new ClapRouterNodeRef(keys, (int)MrDest.Instrument, "Keys"));
            _inspectedNodes.Add(new ClapRouterNodeRef(perc, (int)MrDest.Instrument, "Perc"));
            _inspectedNodes.Add(new ClapRouterNodeRef(lead, (int)MrDest.Instrument, "Lead"));
            if (vital >= 0)
            {
                _inspectedNodes.Add(new ClapRouterNodeRef(vital, (int)MrDest.Instrument, "Vital"));
                _scheduler.PushSustainedNote((long)session.NowFrame, vital, 48, 0.6);
            }

            _ready = true;

            bool haveAction = keypressAction != null && keypressAction.action != null;
            Log.Info(
                $"[ClapRouterDemoPlayer] streaming {composition.Events.Length} song events (bass={bass} keys={keys} perc={perc} lead={lead}) sr={session.SampleRate} lookahead={session.LookaheadFrames} (audio scheduled at now+lookahead) loop={loop}. {(haveAction ? "Press the mapped key to blip the lead instrument." : "keypressAction is NOT assigned — assign an InputActionReference to hear the keypress lead.")}"
            );
        }

        private int TryLoadVital(MusicRouterSession session)
        {
            if (
                string.IsNullOrEmpty(vitalClapPath)
                || !(File.Exists(vitalClapPath) || Directory.Exists(vitalClapPath))
            )
            {
                return -1;
            }

            int vital = session.CreateTrack();
            if (vital < 0 || !Require("loadVital", session.LoadInstrument(vital, vitalClapPath, 0)))
            {
                return -1;
            }

            session.SetTrackGain(vital, 0.7f);
            if (!string.IsNullOrEmpty(vitalPresetPath) && File.Exists(vitalPresetPath))
            {
                Require(
                    "vitalPreset",
                    session.LoadState(vital, (int)MrDest.Instrument, vitalPresetPath)
                );
            }
            return vital;
        }

        private static bool ClapExists(string path, string field, string fixtureName)
        {
            if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
            {
                return true;
            }
            Log.Error(
                $"[ClapRouterDemoPlayer] {field} is not set to an existing {fixtureName}. Point it at the built fixture (repo tests/fixtures/{fixtureName}). Current value: '{path}'."
            );
            return false;
        }

        private static bool Require(string what, MrStatus status)
        {
            if (status == MrStatus.Ok)
            {
                return true;
            }
            Log.Error($"[ClapRouterDemoPlayer] graph op '{what}' returned {status.ToString()}");
            return false;
        }
    }
}
