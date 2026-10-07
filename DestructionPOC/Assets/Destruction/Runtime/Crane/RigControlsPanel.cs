using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Small controls card in the bottom-left corner while the player operates a rig: rig and tool name, the occupied
    /// rig's controls for the active device (read from the live bindings), the exit binding and a short readout.
    /// Hidden on foot, where the existing HUD takes over. Also draws the yard's world-space labels.
    /// </summary>
    [RequireComponent(typeof(CranePlayer))]
    public sealed class RigControlsPanel : MonoBehaviour
    {
        public struct WorldLabel
        {
            public Vector3 position;
            public string text;
            /// <summary>Hidden while operating (rig name tags); target labels always show.</summary>
            public bool onFootOnly;
            /// <summary>When set, <see cref="position"/> is an offset from this transform (a machine that drives away from its start).</summary>
            public Transform follow;
        }

        public readonly List<WorldLabel> labels = new List<WorldLabel>();

        [Range(10, 24)] public int fontSize = 13;
        public float margin = 14f;

        CranePlayer player;
        readonly List<ControlHint> hints = new List<ControlHint>();
        GUIStyle title, keyStyle, labelStyle, noteStyle, tagStyle;
        Texture2D bg;

        /// <summary>Lines last drawn, for tests: "keys|label" per row.</summary>
        public readonly List<string> lastRows = new List<string>();
        public string LastTitle { get; private set; }
        public bool Visible { get; private set; }

        void Awake() => player = GetComponent<CranePlayer>();

        /// <summary>Rebuild the rows for the occupied rig (also callable from tests without a GUI pass).</summary>
        public void Refresh()
        {
            hints.Clear();
            lastRows.Clear();
            var rig = player.Current;
            Visible = rig != null && player.Input != null;
            if (!Visible)
            {
                LastTitle = null;
                return;
            }
            var input = player.Input;
            LastTitle = string.IsNullOrEmpty(rig.AttachmentName) ? rig.RigName.ToUpperInvariant()
                                                                 : $"{rig.RigName.ToUpperInvariant()}  ·  {rig.AttachmentName}";
            rig.ControlHints(input, hints);
            hints.Add(ControlHint.Row(input.Keys(input.Exit), "Exit"));
            hints.Add(ControlHint.Row(input.Keys(input.RespawnVehicle), "Respawn vehicle"));
            hints.Add(ControlHint.Row(input.Keys(input.RespawnPlayer), "Respawn player"));
            foreach (var h in hints) lastRows.Add($"{h.keys}|{h.label}");
        }

        void OnGUI()
        {
            if (player == null) return;
            EnsureStyles();
            var view = player.GuiRect;
            GUI.BeginGroup(view);
            DrawLabels(view);
            Refresh();
            if (Visible) DrawPanel(view);
            GUI.EndGroup();
        }

        void DrawPanel(Rect view)
        {
            const float keyWidth = 118f, labelWidth = 178f, pad = 10f;
            float line = fontSize + 6f;
            string telemetry = player.Current.Telemetry;
            int telemetryLines = string.IsNullOrEmpty(telemetry) ? 0 : telemetry.Split('\n').Length;
            float h = pad * 2f + line * 2.1f + hints.Count * line + telemetryLines * line + (telemetryLines > 0 ? 4f : 0f);
            float w = keyWidth + labelWidth + pad * 2f;
            var r = new Rect(margin, view.height - h - margin, w, h);
            GUI.DrawTexture(r, bg);

            float y = r.y + pad;
            GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, line), LastTitle, title);
            y += line;
            string device = player.Input.UsingGamepad ? "Gamepad controls" : "Keyboard + mouse controls";
            GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, line), device, noteStyle);
            y += line * 1.1f;
            foreach (var hint in hints)
            {
                var color = hint.dim ? new Color(1f, 1f, 1f, 0.38f) : hint.highlight ? new Color(1f, 0.82f, 0.3f) : Color.white;
                var prev = GUI.color;
                GUI.color = color;
                if (hint.note) GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, line), hint.label, noteStyle);
                else
                {
                    GUI.Label(new Rect(r.x + pad, y, keyWidth, line), hint.keys, keyStyle);
                    GUI.Label(new Rect(r.x + pad + keyWidth, y, labelWidth, line), hint.label, labelStyle);
                }
                GUI.color = prev;
                y += line;
            }
            if (telemetryLines > 0)
                GUI.Label(new Rect(r.x + pad, y + 4f, w - pad * 2f, line * telemetryLines), telemetry, noteStyle);
        }

        void DrawLabels(Rect view)
        {
            var cam = player.cam;
            if (cam == null) return;
            foreach (var l in labels)
            {
                if (l.onFootOnly && player.InCab) continue;
                DrawTag(view, cam, l.follow != null ? l.follow.position + l.position : l.position, l.text);
            }
            // Labels authored in the level scene.
            foreach (var l in LevelLabel.All)
            {
                if (l.onFootOnly && player.InCab) continue;
                DrawTag(view, cam, l.Position, l.text);
            }
        }

        void DrawTag(Rect view, Camera cam, Vector3 world, string text)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            if (s.z <= 0f) return;
            var size = tagStyle.CalcSize(new GUIContent(text));
            // The camera reports whole-screen pixels; the group is offset to this player's viewport.
            var r = new Rect(s.x - view.x - size.x * 0.5f - 5f, Screen.height - s.y - view.y - size.y * 0.5f - 2f, size.x + 10f, size.y + 4f);
            GUI.DrawTexture(r, bg);
            GUI.Label(new Rect(r.x + 5f, r.y + 2f, size.x, size.y), text, tagStyle);
        }

        void EnsureStyles()
        {
            if (title != null && title.fontSize == fontSize + 1) return;
            bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            bg.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.62f));
            bg.Apply();
            title = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 1, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            title.normal.textColor = new Color(1f, 0.82f, 0.3f);
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            keyStyle.normal.textColor = Color.white;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, clipping = TextClipping.Clip };
            labelStyle.normal.textColor = new Color(0.86f, 0.88f, 0.9f);
            noteStyle = new GUIStyle(labelStyle) { fontSize = fontSize - 1, fontStyle = FontStyle.Italic, wordWrap = false };
            tagStyle = new GUIStyle(labelStyle) { fontSize = fontSize - 1, alignment = TextAnchor.UpperRight };
            tagStyle.normal.textColor = new Color(0.86f, 0.88f, 0.9f, 0.75f);
        }

        void OnDestroy()
        {
            if (bg != null) Destroy(bg);
        }
    }
}
