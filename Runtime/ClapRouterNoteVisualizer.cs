using System.Collections.Generic;
using UnityEngine;

namespace Zori.ClapRouter
{
    public struct SongOnset
    {
        public long RelFrame;
        public short Key;
        public int Track;

        public SongOnset(long relFrame, short key, int track)
        {
            RelFrame = relFrame;
            Key = key;
            Track = track;
        }
    }

    public sealed class ClapRouterNoteVisualizer : MonoBehaviour
    {
        [SerializeField] private ClapRouterDemoPlayer player;
        [SerializeField] private int windupFrames = 24000;
        [SerializeField] private float startRadius = 130f;
        [SerializeField] private float targetRadius = 18f;

        private struct Cue
        {
            public long AudioFrame;
            public int Lane;
            public float Lead;
            public bool Keypress;
        }

        private readonly List<Cue> _cues = new List<Cue>();
        private readonly HashSet<long> _spawned = new HashSet<long>();
        private readonly Dictionary<int, int> _lanes = new Dictionary<int, int>();
        private AvSyncTimeline _timeline;
        private Texture2D _disc;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<ClapRouterDemoPlayer>();
            }
            _disc = MakeDisc(64);
        }

        private void OnEnable()
        {
            if (player != null)
            {
                player.LeadNoteFired += OnLeadNote;
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.LeadNoteFired -= OnLeadNote;
            }
            _cues.Clear();
            _spawned.Clear();
            _lanes.Clear();
        }

        private void Update()
        {
            MusicRouterSession session = player != null ? player.Session : null;
            if (session == null)
            {
                _cues.Clear();
                _spawned.Clear();
                _lanes.Clear();
                return;
            }

            _timeline = new AvSyncTimeline(session.SampleRate,
                AvSyncTimeline.LatencySecondsFromFrames(session.OutputLatencyFrames, session.SampleRate));
            if (_lanes.Count == 0)
            {
                int lane = 0;
                foreach (ClapRouterNodeRef node in player.InspectedNodes)
                {
                    _lanes[node.Track] = lane++;
                }
            }

            AvClock clock = SampleClock(session);
            long now = clock.NowFrame;
            long baseFrame = player.SongBaseFrame;
            float windupSeconds = windupFrames / (float)session.SampleRate;

            foreach (SongOnset onset in player.SongOnsets)
            {
                long audioFrame = baseFrame + onset.RelFrame;
                if (audioFrame < now || _spawned.Contains(audioFrame))
                {
                    continue;
                }
                double audible = _timeline.AudibleTime(audioFrame, clock);
                if (clock.WallSeconds >= audible - windupSeconds)
                {
                    _spawned.Add(audioFrame);
                    _cues.Add(new Cue { AudioFrame = audioFrame, Lane = Lane(onset.Track), Lead = windupSeconds });
                }
            }

            for (int i = _cues.Count - 1; i >= 0; i--)
            {
                double audible = _timeline.AudibleTime(_cues[i].AudioFrame, clock);
                if (clock.WallSeconds > audible + 0.14)
                {
                    _cues.RemoveAt(i);
                }
            }
            if (_spawned.Count > 512)
            {
                _spawned.Clear();
            }
        }

        private void OnLeadNote(long audioFrame, short key, int track)
        {
            if (_spawned.Contains(audioFrame))
            {
                return;
            }
            _spawned.Add(audioFrame);
            _cues.Add(new Cue { AudioFrame = audioFrame, Lane = Lane(track), Lead = 0f, Keypress = true });
        }

        private void OnGUI()
        {
            MusicRouterSession session = player != null ? player.Session : null;
            if (session == null || _timeline == null || _disc == null)
            {
                return;
            }

            AvClock clock = SampleClock(session);
            int laneCount = Mathf.Max(1, _lanes.Count);
            float laneWidth = Screen.width / (float)laneCount;
            float centerY = Screen.height * 0.5f;

            foreach (Cue cue in _cues)
            {
                double audible = _timeline.AudibleTime(cue.AudioFrame, clock);
                double progress = _timeline.Progress(cue.AudioFrame, cue.Lead, clock);
                float cx = laneWidth * (cue.Lane + 0.5f);
                float radius = Mathf.Lerp(startRadius, targetRadius, (float)progress);
                bool contact = clock.WallSeconds >= audible && clock.WallSeconds < audible + 0.12;

                Color ring = cue.Keypress ? new Color(1f, 0.75f, 0.2f, 0.6f) : new Color(0.35f, 0.8f, 1f, 0.55f);
                DrawDisc(cx, centerY, radius, ring);
                DrawDisc(cx, centerY, targetRadius,
                    contact ? Color.white : new Color(1f, 1f, 1f, 0.55f));
            }
        }

        private void DrawDisc(float cx, float cy, float radius, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(cx - radius, cy - radius, radius * 2f, radius * 2f), _disc,
                ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private static AvClock SampleClock(MusicRouterSession session)
        {
            return new AvClock((long)session.NowFrameInterpolated, Time.realtimeSinceStartupAsDouble);
        }

        private int Lane(int track)
        {
            return _lanes.TryGetValue(track, out int lane) ? lane : 0;
        }

        private static Texture2D MakeDisc(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) * 0.5f;
            float r = c;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(r - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
