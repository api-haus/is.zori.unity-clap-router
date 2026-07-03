using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zori.ClapRouter
{
    public struct ClapRouterNodeRef
    {
        public int Track;
        public int Slot;
        public string Label;

        public ClapRouterNodeRef(int track, int slot, string label)
        {
            Track = track;
            Slot = slot;
            Label = label;
        }
    }

    public sealed class ClapRouterParamInspector : MonoBehaviour
    {
        [SerializeField] private ClapRouterDemoPlayer player;
        [SerializeField] private float panelWidth = 340f;
        [SerializeField] private float panelHeight = 440f;

        private sealed class Panel
        {
            public ClapRouterNodeRef Node;
            public MrParamInfo[] Infos = Array.Empty<MrParamInfo>();
            public string[] Names = Array.Empty<string>();
            public string[] ValueTexts = Array.Empty<string>();
            public bool Expanded = true;
            public Vector2 Scroll;
            public Rect Window;
            public bool Resizing;
            public bool NeedsInfoRescan;
            public bool NeedsValueRescan;
        }

        private readonly List<Panel> _panels = new List<Panel>();
        private bool _built;

        private void Awake()
        {
            if (player == null)
            {
                player = GetComponent<ClapRouterDemoPlayer>();
            }
        }

        private void Update()
        {
            MusicRouterSession session = player != null ? player.Session : null;
            if (session == null)
            {
                _built = false;
                _panels.Clear();
                return;
            }

            if (!_built)
            {
                Build(session);
            }
            PollNotifications(session);
        }

        private void Build(MusicRouterSession session)
        {
            _panels.Clear();
            float x = 12f;
            foreach (ClapRouterNodeRef node in player.InspectedNodes)
            {
                Panel p = new Panel { Node = node, Window = new Rect(x, 12f, panelWidth, panelHeight) };
                QueryAll(session, p);
                _panels.Add(p);
                x += panelWidth + 12f;
            }
            _built = true;
        }

        private void QueryAll(MusicRouterSession session, Panel p)
        {
            int count = session.ParamCount(p.Node.Track, p.Node.Slot);
            if (count < 0)
            {
                count = 0;
            }
            p.Infos = new MrParamInfo[count];
            p.Names = new string[count];
            p.ValueTexts = new string[count];
            for (int i = 0; i < count; i++)
            {
                session.ParamInfo(p.Node.Track, p.Node.Slot, i, out p.Infos[i]);
                p.Names[i] = session.ParamName(p.Node.Track, p.Node.Slot, p.Infos[i].Id);
                p.ValueTexts[i] = session.ParamValueText(p.Node.Track, p.Node.Slot, p.Infos[i].Id, p.Infos[i].CurrentValue);
            }
            p.NeedsInfoRescan = false;
            p.NeedsValueRescan = false;
        }

        private void RefreshValues(MusicRouterSession session, Panel p)
        {
            for (int i = 0; i < p.Infos.Length; i++)
            {
                double value = session.ParamValue(p.Node.Track, p.Node.Slot, p.Infos[i].Id);
                if (!double.IsNaN(value))
                {
                    p.Infos[i].CurrentValue = value;
                }
                p.ValueTexts[i] = session.ParamValueText(p.Node.Track, p.Node.Slot, p.Infos[i].Id, p.Infos[i].CurrentValue);
            }
            p.NeedsValueRescan = false;
        }

        private void PollNotifications(MusicRouterSession session)
        {
            while (session.PollNotification(out MrNotification n))
            {
                if (n.Kind != MrNotifyKind.ParamRescan)
                {
                    continue;
                }
                foreach (Panel p in _panels)
                {
                    if (p.Node.Track != (int)n.TrackId || p.Node.Slot != n.DestSlot)
                    {
                        continue;
                    }
                    if ((n.RescanFlags & (MrRescanFlags.Info | MrRescanFlags.All)) != 0)
                    {
                        p.NeedsInfoRescan = true;
                    }
                    else if ((n.RescanFlags & (MrRescanFlags.Values | MrRescanFlags.Text)) != 0)
                    {
                        p.NeedsValueRescan = true;
                    }
                }
            }

            foreach (Panel p in _panels)
            {
                if (p.NeedsInfoRescan)
                {
                    QueryAll(session, p);
                }
                else if (p.NeedsValueRescan)
                {
                    RefreshValues(session, p);
                }
            }
        }

        private void OnGUI()
        {
            MusicRouterSession session = player != null ? player.Session : null;
            if (session == null || !_built)
            {
                return;
            }
            for (int i = 0; i < _panels.Count; i++)
            {
                Panel p = _panels[i];
                p.Window = GUILayout.Window(4200 + i, p.Window, _ => DrawPanel(session, p), p.Node.Label);
            }
        }

        private void DrawPanel(MusicRouterSession session, Panel p)
        {
            GUILayout.BeginHorizontal();
            p.Expanded = GUILayout.Toggle(p.Expanded, p.Expanded ? "▼" : "▶", GUILayout.Width(24));
            GUILayout.Label($"{p.Infos.Length} params", GUILayout.Width(90));
            if (GUILayout.Button("Show GUI"))
            {
                session.ShowGui(p.Node.Track, p.Node.Slot);
            }
            if (GUILayout.Button("Hide GUI"))
            {
                session.HideGui(p.Node.Track, p.Node.Slot);
            }
            GUILayout.EndHorizontal();

            if (p.Expanded)
            {
                p.Scroll = GUILayout.BeginScrollView(p.Scroll);
                for (int i = 0; i < p.Infos.Length; i++)
                {
                    DrawParam(session, p, i);
                }
                GUILayout.EndScrollView();
            }

            HandleResize(p);
            GUI.DragWindow(new Rect(0f, 0f, p.Window.width, 18f));
        }

        private void DrawParam(MusicRouterSession session, Panel p, int index)
        {
            MrParamInfo info = p.Infos[index];
            if (info.Has(MrParamFlags.IsHidden))
            {
                return;
            }

            GUILayout.BeginHorizontal();
            string label = string.IsNullOrEmpty(p.Names[index]) ? $"#{info.Id}" : p.Names[index];
            GUILayout.Label(label, GUILayout.Width(130));

            if (info.Has(MrParamFlags.IsReadonly))
            {
                GUILayout.Label(p.ValueTexts[index]);
            }
            else if (info.IsEnumList)
            {
                int cur = (int)Math.Round(info.CurrentValue);
                if (GUILayout.Button("◄", GUILayout.Width(26)) && cur > (int)info.MinValue)
                {
                    Set(session, p, index, cur - 1);
                }
                GUILayout.Label(p.ValueTexts[index], GUILayout.Width(120));
                if (GUILayout.Button("►", GUILayout.Width(26)) && cur < (int)info.MaxValue)
                {
                    Set(session, p, index, cur + 1);
                }
            }
            else if (info.IsBoolean)
            {
                bool on = info.CurrentValue >= 0.5;
                bool next = GUILayout.Toggle(on, on ? "on" : "off");
                if (next != on)
                {
                    Set(session, p, index, next ? 1.0 : 0.0);
                }
            }
            else if (info.IsIntegerStepped)
            {
                int cur = (int)Math.Round(info.CurrentValue);
                int next = Mathf.RoundToInt(GUILayout.HorizontalSlider(cur, (float)info.MinValue, (float)info.MaxValue,
                    GUILayout.Width(110)));
                GUILayout.Label(next.ToString(), GUILayout.Width(48));
                if (next != cur)
                {
                    Set(session, p, index, next);
                }
            }
            else
            {
                float cur = (float)info.CurrentValue;
                float next = GUILayout.HorizontalSlider(cur, (float)info.MinValue, (float)info.MaxValue,
                    GUILayout.Width(110));
                GUILayout.Label(p.ValueTexts[index], GUILayout.Width(70));
                if (!Mathf.Approximately(next, cur))
                {
                    Set(session, p, index, next);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void Set(MusicRouterSession session, Panel p, int index, double value)
        {
            player.SetParam(p.Node.Track, p.Node.Slot, p.Infos[index].Id, value);
            p.Infos[index].CurrentValue = value;
            p.ValueTexts[index] = session.ParamValueText(p.Node.Track, p.Node.Slot, p.Infos[index].Id, value);
        }

        private static void HandleResize(Panel p)
        {
            Rect grip = new Rect(p.Window.width - 18f, p.Window.height - 18f, 18f, 18f);
            GUI.Box(grip, "◢");
            Event e = Event.current;
            if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
            {
                p.Resizing = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                p.Resizing = false;
            }
            else if (p.Resizing && e.type == EventType.MouseDrag)
            {
                p.Window.width = Mathf.Max(220f, p.Window.width + e.delta.x);
                p.Window.height = Mathf.Max(140f, p.Window.height + e.delta.y);
                e.Use();
            }
        }
    }
}
