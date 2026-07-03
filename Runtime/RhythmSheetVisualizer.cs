using System.Collections.Generic;
using UnityEngine;

namespace Zori.ClapRouter
{
    public sealed class RhythmSheetVisualizer : MonoBehaviour
    {
        [SerializeField]
        private ClapRouterDemoPlayer player;

        [SerializeField]
        private float marginX = 60f;

        [SerializeField]
        private float sheetHeight = 140f;

        [SerializeField]
        private float flashSeconds = 0.35f;

        [SerializeField]
        private bool compensatePresentLatency = true;

        [SerializeField]
        private float playheadLeadMs = 0f;

        private struct HitFlash
        {
            public long OnsetFrame;
            public float Accuracy;
            public bool Within;
            public double SpawnTime;
        }

        private readonly List<HitFlash> _flashes = new List<HitFlash>();
        private Texture2D _disc;
        private Texture2D _pixel;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<ClapRouterDemoPlayer>();
            }
            _disc = MakeDisc(64);
            _pixel = MakePixel();
        }

        private void OnEnable()
        {
            if (player != null)
            {
                player.PlayerHitJudged += OnHit;
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.PlayerHitJudged -= OnHit;
            }
            _flashes.Clear();
        }

        private void OnHit(QuantizedHit hit)
        {
            _flashes.Add(
                new HitFlash
                {
                    OnsetFrame = hit.IntendedFrame,
                    Accuracy = (float)hit.Accuracy,
                    Within = hit.Within,
                    SpawnTime = Time.realtimeSinceStartupAsDouble,
                }
            );
        }

        private void OnGUI()
        {
            MusicRouterSession session = player != null ? player.Session : null;
            if (session == null || _disc == null)
            {
                return;
            }

            double presentLead = compensatePresentLatency ? Time.smoothDeltaTime : 0.0;
            long leadFrames = (long)((presentLead + playheadLeadMs / 1000.0) * session.SampleRate);
            long audibleFrame =
                (long)session.NowFrameInterpolated - session.OutputLatencyFrames + leadFrames;

            RhythmSequence sequence = player.CurrentSheet(audibleFrame);
            if (sequence == null || sequence.Span <= 0)
            {
                return;
            }

            float left = marginX;
            float width = Screen.width - marginX * 2f;
            float centerY = Screen.height * 0.5f;
            float top = centerY - sheetHeight * 0.5f;
            long span = sequence.Span;

            DrawRect(left, top, width, sheetHeight, new Color(0.06f, 0.07f, 0.10f, 0.85f));
            DrawGridlines(sequence, left, top, width, span);

            for (int i = 0; i < sequence.Count; i++)
            {
                DrawToleranceBand(sequence, i, left, width, centerY, span);
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                float x = left + width * (sequence.LocalOnset(i) / (float)span);
                DrawNote(x, centerY, sequence.ValueAt(i));
            }

            DrawFlashes(sequence, left, width, centerY, span);

            long local = audibleFrame - sequence.Origin;
            local = local < 0 ? 0 : (local > span ? span : local);
            float playX = left + width * (local / (float)span);
            DrawRect(
                playX - 1.5f,
                top - 8f,
                3f,
                sheetHeight + 16f,
                new Color(1f, 0.9f, 0.25f, 0.95f)
            );
        }

        private void DrawGridlines(
            RhythmSequence sequence,
            float left,
            float top,
            float width,
            long span
        )
        {
            double sixteenth = sequence.FramesPerBeat / 4.0;
            int steps = sequence.Beats * 4;
            for (int s = 0; s <= steps; s++)
            {
                float x = left + width * (float)(s * sixteenth / span);
                bool bar = s % 16 == 0;
                bool beat = s % 4 == 0;
                Color c =
                    bar ? new Color(1f, 1f, 1f, 0.38f)
                    : beat ? new Color(1f, 1f, 1f, 0.18f)
                    : new Color(1f, 1f, 1f, 0.07f);
                DrawRect(x - 0.5f, top, bar ? 2f : (beat ? 1.5f : 1f), sheetHeight, c);
            }
        }

        private void DrawToleranceBand(
            RhythmSequence sequence,
            int i,
            float left,
            float width,
            float centerY,
            long span
        )
        {
            long onset = sequence.LocalOnset(i);
            long lo = System.Math.Max(0, onset - sequence.PreWindowFrames(i));
            long hi = System.Math.Min(span, onset + sequence.PostWindowFrames(i));
            float x0 = left + width * (lo / (float)span);
            float x1 = left + width * (hi / (float)span);
            DrawRect(x0, centerY - 30f, x1 - x0, 60f, new Color(0.3f, 1f, 0.45f, 0.10f));
        }

        private void DrawNote(float x, float centerY, NoteValue value)
        {
            float radius = RadiusFor(value);
            Color color = SpeedColor(value);
            DrawRect(
                x - 0.5f,
                centerY - 26f,
                1.5f,
                26f,
                new Color(color.r, color.g, color.b, 0.5f)
            );
            DrawDisc(x, centerY, radius, color);
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(x - 18f, centerY + radius + 2f, 36f, 18f), NoteValues.Label(value));
            GUI.color = Color.white;
        }

        private static float RadiusFor(NoteValue value) =>
            value switch
            {
                NoteValue.Half => 15f,
                NoteValue.Quarter => 13f,
                NoteValue.Eighth => 10f,
                NoteValue.Sixteenth => 8f,
                _ => 11f,
            };

        private static Color SpeedColor(NoteValue value) =>
            value switch
            {
                NoteValue.Half => new Color(0.4f, 0.7f, 1f),
                NoteValue.Quarter => new Color(0.55f, 0.9f, 1f),
                NoteValue.Eighth => new Color(1f, 0.85f, 0.4f),
                NoteValue.Sixteenth => new Color(1f, 0.55f, 0.3f),
                _ => new Color(0.85f, 0.85f, 0.95f),
            };

        private void DrawFlashes(
            RhythmSequence sequence,
            float left,
            float width,
            float centerY,
            long span
        )
        {
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                float age = (float)(now - _flashes[i].SpawnTime);
                if (age > flashSeconds)
                {
                    _flashes.RemoveAt(i);
                    continue;
                }

                long rel = _flashes[i].OnsetFrame - sequence.Origin;
                if (rel < 0 || rel >= span)
                {
                    continue;
                }
                float x = left + width * (rel / (float)span);
                float t = age / flashSeconds;
                float alpha = 1f - t;
                float radius = Mathf.Lerp(10f, 34f, t);
                Color judge = _flashes[i].Within
                    ? Color.Lerp(
                        new Color(0.4f, 1f, 0.5f, 0.5f),
                        new Color(0.2f, 1f, 0.4f, 1f),
                        _flashes[i].Accuracy
                    )
                    : new Color(1f, 0.3f, 0.25f, 1f);
                DrawDisc(x, centerY, radius, new Color(judge.r, judge.g, judge.b, alpha * 0.7f));
            }
        }

        private void DrawDisc(float cx, float cy, float radius, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(
                new Rect(cx - radius, cy - radius, radius * 2f, radius * 2f),
                _disc,
                ScaleMode.StretchToFill,
                true
            );
            GUI.color = previous;
        }

        private void DrawRect(float x, float y, float w, float h, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, h), _pixel, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private static Texture2D MakePixel()
        {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return tex;
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
