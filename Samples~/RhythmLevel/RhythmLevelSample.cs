using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Zori.ClapRouter.Samples
{
    [RequireComponent(typeof(MusicRouterHost))]
    public sealed class RhythmLevelSample : MonoBehaviour, IRhythmSheetView
    {
        [Tooltip("The MusicRouterHost on this GameObject (auto-found if left empty).")]
        [SerializeField]
        private MusicRouterHost host;

        [Header("Devices (drag CLAP Device assets)")]
        [Tooltip("CLAP Device that plays the guide pattern you follow.")]
        [SerializeField]
        private ClapDeviceDefinition guideInstrument;

        [Tooltip("CLAP Device for your tapped notes. Falls back to the guide instrument if empty.")]
        [SerializeField]
        private ClapDeviceDefinition playInstrument;

        [Header("Input")]
        [Tooltip(
            "Action that taps a note (bind it to a key/button). Fires on WasPressedThisFrame."
        )]
        [SerializeField]
        private InputActionReference tapAction;

        [Header("Level")]
        [Tooltip("Tempo, in beats per minute.")]
        [SerializeField]
        private int bpm = 120;

        [Tooltip("Length of each generated sheet, in 4/4 bars.")]
        [SerializeField]
        private int bars = 4;

        [Tooltip(
            "Chance each slot is a rest instead of a note. 0 = dense; higher = more gaps / syncopation."
        )]
        [SerializeField]
        private double restChance = 0.4;

        [Tooltip(
            "Cap on consecutive 1/16 notes before the curve is forced slower (keeps it playable)."
        )]
        [SerializeField]
        private int maxSixteenthRun = 4;

        [Header("Judging (fractions of a 1/16 note)")]
        [Tooltip("How early a tap still counts, as a fraction of a 1/16 note.")]
        [SerializeField]
        private double preTolerance = 0.35;

        [Tooltip("How late a tap still counts, as a fraction of a 1/16 note.")]
        [SerializeField]
        private double postTolerance = 0.25;

        [Tooltip("Sustain length of a tapped note, in milliseconds.")]
        [SerializeField]
        private int leadNoteMs = 120;

        [Tooltip("Deterministic seed for the generated sheets.")]
        [SerializeField]
        private int seed = 12345;

        [Header("Metronome")]
        [Tooltip("Enable metronome click — a steady 1-2-3-4 with an accented (higher) downbeat.")]
        [SerializeField]
        private bool enableMetronome = true;

        private static readonly short[] Scale = { 72, 74, 76, 79, 81 };

        private MusicRouterSession _session;
        private LiveSongScheduler _scheduler;
        private Metronome _metronome;
        private readonly List<RhythmSequence> _sheets = new List<RhythmSequence>();
        private int _guideTrack = -1;
        private int _playTrack = -1;
        private int _sheetIndex;
        private bool _ready;
        private bool _failed;

        public event Action<QuantizedHit> PlayerHitJudged;

        public MusicRouterSession Session => _ready ? _session : null;

        private void Awake()
        {
            if (host == null)
            {
                host = GetComponent<MusicRouterHost>();
            }
        }

        private void OnEnable()
        {
            tapAction?.action?.Enable();
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
                if (session != null && session.IsOpen)
                {
                    Begin(session);
                }
                return;
            }

            _scheduler.PumpSong((long)_session.NowFrame);
            _metronome?.Pump((long)_session.NowFrame);

            InputAction action = tapAction != null ? tapAction.action : null;
            if (action != null && action.WasPressedThisFrame())
            {
                Tap();
            }
        }

        private void Begin(MusicRouterSession session)
        {
            ClapDeviceDefinition play = playInstrument != null ? playInstrument : guideInstrument;
            if (
                !Resolve(guideInstrument, "guide instrument", out string guidePath)
                || !Resolve(play, "play instrument", out string playPath)
            )
            {
                _failed = true;
                return;
            }

            _guideTrack = session.CreateTrack();
            _playTrack = session.CreateTrack();
            if (_guideTrack < 0 || _playTrack < 0)
            {
                Debug.LogError("[RhythmLevelSample] host rejected track creation");
                _failed = true;
                return;
            }

            session.LoadInstrument(_guideTrack, guidePath, guideInstrument.PluginIndex);
            session.LoadInstrument(_playTrack, playPath, play.PluginIndex);
            session.SetTrackGain(_guideTrack, 0.8f);
            session.SetTrackGain(_playTrack, 1.0f);

            _session = session;
            _sheetIndex = 0;
            Composition composition = BuildSheet(_sheetIndex);

            long leadFrames = (long)leadNoteMs * session.SampleRate / 1000;
            _scheduler = new LiveSongScheduler(
                session,
                composition,
                _playTrack,
                session.LookaheadFrames,
                leadFrames,
                true,
                session.OutputLatencyFrames
            );
            _sheets.Clear();
            _scheduler.SegmentStarted += _sheets.Add;
            _scheduler.SetNextSegmentProvider(() => BuildSheet(++_sheetIndex));
            long startFrame = (long)session.NowFrame;
            _scheduler.Begin(startFrame);

            if (enableMetronome)
            {
                _metronome = new Metronome(
                    session,
                    guidePath,
                    guideInstrument.PluginIndex,
                    bpm,
                    startFrame
                );
            }

            _ready = true;
            Debug.Log(
                $"[RhythmLevelSample] {bars} bars @ {bpm} BPM — guide on track {_guideTrack}, taps on track {_playTrack}. Press Space on the beat; hits inside tolerance sound on the grid, misses are silent."
            );
        }

        private Composition BuildSheet(int index)
        {
            RhythmPattern pattern = new RhythmCurveGenerator(
                bars,
                restChance: restChance,
                maxSixteenthRun: maxSixteenthRun
            ).Generate(SheetSeed(index));
            return new RhythmLevelComposer(
                _guideTrack,
                pattern,
                (int)_session.SampleRate,
                bpm: bpm,
                preTolerance: preTolerance,
                postTolerance: postTolerance
            ).Compose();
        }

        private ulong SheetSeed(int index) =>
            (ulong)(uint)seed * 0x9E3779B97F4A7C15UL
            + (ulong)(uint)index * 0xD1B54A32D192ED03UL
            + 1UL;

        private void Tap()
        {
            short key = Scale[UnityEngine.Random.Range(0, Scale.Length)];
            LeadNoteLog log = _scheduler.TriggerLead((long)_session.NowFrame, key);
            PlayerHitJudged?.Invoke(log.Hit);
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

        private static bool Resolve(ClapDeviceDefinition device, string what, out string path)
        {
            path = null;
            if (device == null)
            {
                Debug.LogError(
                    $"[RhythmLevelSample] no {what} assigned — drag a CLAP Device asset onto the field."
                );
                return false;
            }
            if (!device.TryResolvePath(out path))
            {
                Debug.LogWarning(
                    $"[RhythmLevelSample] {what} '{device.DisplayName}' has no .clap for {ClapDeviceDefinition.CurrentPlatform} yet. Open 'CLAP Router ▸ CLAP Device Downloader' and click Download (or Browse the device to your own build), then press Play again."
                );
                return false;
            }
            return true;
        }

        private void OnDisable()
        {
            tapAction?.action?.Disable();
            _metronome?.Dispose();
            _metronome = null;
            _ready = false;
            _scheduler = null;
            _session = null;
            _sheets.Clear();
        }
    }
}
