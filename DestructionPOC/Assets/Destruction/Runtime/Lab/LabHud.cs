using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DestructionLab
{
    /// <summary>uGUI HUD built in code: scenario selector, tools, playback, tuning sliders, help, stats,
    /// selected-connection inspector and failure log.</summary>
    public sealed class LabHud : MonoBehaviour
    {
        LabController lab;
        DestructionWorld world;
        Font font;
        Text scenarioText, statsText, inspectText, logText, helpText, playbackLabel, speedLabel, triggerLabel, diagLabel, tintLabel;
        readonly List<(Button button, int index)> scenarioButtons = new List<(Button, int)>();
        readonly List<(Button button, LabTool tool)> toolButtons = new List<(Button, LabTool)>();
        GameObject helpPanel;
        float nextText;
        float fps = 60f;
        float physicsMaxMs;
        int lastBuildCount = -1;
        ProfilerRecorder physicsRecorder;

        static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.1f, 0.82f);
        static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.28f, 1f);
        static readonly Color ButtonActive = new Color(0.15f, 0.55f, 0.75f, 1f);
        static readonly Color TextColor = new Color(0.92f, 0.93f, 0.95f, 1f);

        /// <summary>Hide the scenario list and the camera help card (top-left, bottom-left). Set by scenes that put the
        /// player's HUD and the rig controls card there (ConvenienceStore). Call before <see cref="Init"/>.</summary>
        public bool compactLayout;

        public void Init(LabController l, DestructionWorld w)
        {
            lab = l;
            world = w;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate", 15);

            var canvasGo = new GameObject("Lab HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            // Left: scenarios + instructions.
            var left = Panel(root, new Vector2(0f, 1f), new Vector2(12f, -12f), 400f);
            Label(left, "DESTRUCTION LAB", 20, FontStyle.Bold);
            Label(left, "Scenario (N / B)", 13, FontStyle.Italic);
            for (int i = 0; i < l.Scenarios.Count; i++)
            {
                int idx = i;
                var b = MakeButton(left, l.Scenarios[i].title, () => lab.LoadScenario(idx), out _);
                scenarioButtons.Add((b, i));
            }
            scenarioText = Label(left, "", 14, FontStyle.Normal);
            var row = Row(left);
            MakeButton(row, "Trigger (T)", () => lab.Trigger(), out triggerLabel);
            MakeButton(row, "Reset (R)", () => lab.RequestReset(), out _);
            left.gameObject.SetActive(!compactLayout);

            // Right: tools, playback, tuning.
            var right = Panel(root, new Vector2(1f, 1f), new Vector2(-12f, -12f), 330f);
            Label(right, "Tool", 15, FontStyle.Bold);
            var tools = Row(right);
            AddTool(tools, "1 Damage", LabTool.Damage);
            AddTool(tools, "2 Explode", LabTool.Explosion);
            var tools2 = Row(right);
            AddTool(tools2, "3 Drop block", LabTool.DropBlock);
            AddTool(tools2, "4 Inspect", LabTool.Inspect);

            Label(right, "Playback", 15, FontStyle.Bold);
            var pb = Row(right);
            MakeButton(pb, "Pause", () => lab.TogglePause(), out playbackLabel);
            MakeButton(pb, "Step (.)", () => lab.StepOnce(), out _);
            MakeButton(pb, "1×", () => lab.CycleSpeed(), out speedLabel);
            var dg = Row(right);
            MakeButton(dg, "Diagnostics (F1)", () => lab.DiagnosticsVisible = !lab.DiagnosticsVisible, out diagLabel);
            MakeButton(dg, "Tint (F2)", () => lab.Tint = (TintMode)(((int)lab.Tint + 1) % 4), out tintLabel);

            Label(right, "Tuning", 15, FontStyle.Bold);
            var s = w.Settings;
            Slider(right, "Click damage", 0.05f, 1f, s.tools.clickDamage, v => s.tools.clickDamage = v, "0.00");
            Slider(right, "Explosion radius (m)", 0.5f, 6f, s.tools.explosionRadius, v => s.tools.explosionRadius = v, "0.0");
            Slider(right, "Explosion impulse (kN·s)", 0f, 60f, s.tools.explosionImpulse / 1000f, v => s.tools.explosionImpulse = v * 1000f, "0");
            Slider(right, "Drop block mass (t)", 0.5f, 10f, s.tools.dropBlockMass / 1000f, v => s.tools.dropBlockMass = v * 1000f, "0.0");
            Slider(right, "Residual strength ×", 0.1f, 3f, s.residual.strengthMultiplier, v => s.residual.strengthMultiplier = v, "0.00", true);
            Slider(right, "Bond strength × (reset)", 0.1f, 3f, s.structure.strengthMultiplier, v => s.structure.strengthMultiplier = v, "0.00");
            Slider(right, "Impact speed gate (m/s)", 0.5f, 8f, s.impact.minRelativeSpeed, v => s.impact.minRelativeSpeed = v, "0.0");
            var tg = Row(right);
            MakeToggleButton(tg, "Residual joints", () => s.residual.enabled, v => s.residual.enabled = v);
            MakeToggleButton(tg, "Impact damage", () => s.impact.enabled, v => s.impact.enabled = v);
            Slider(right, "Shatter impact (J/kg)", 5f, 100f, s.fragments.impactShatterEnergyPerKg, v => s.fragments.impactShatterEnergyPerKg = v, "0");
            Slider(right, "Fragment size (m)", 0.3f, 2f, s.fragments.targetSize, v => s.fragments.targetSize = v, "0.00");
            var tg2 = Row(right);
            MakeToggleButton(tg2, "Fragmentation", () => s.fragments.enabled, v => s.fragments.enabled = v);

            // Bottom-left: help.
            helpPanel = Panel(root, new Vector2(0f, 0f), new Vector2(12f, 12f), 400f).gameObject;
            helpText = Label(helpPanel.transform, HelpString(), 13, FontStyle.Normal);

            // Bottom-right: stats, inspector, log.
            var br = Panel(root, new Vector2(1f, 0f), new Vector2(-12f, 12f), 560f);
            statsText = Label(br, "", 13, FontStyle.Normal);
            inspectText = Label(br, "", 13, FontStyle.Normal);
            logText = Label(br, "", 12, FontStyle.Normal);
        }

        void OnDestroy()
        {
            if (physicsRecorder.Valid) physicsRecorder.Dispose();
        }

        static string HelpString() =>
            "<b>Controls</b>\n" +
            "Left click: use tool  ·  Right drag: orbit  ·  Middle / Shift+right drag: pan\n" +
            "Wheel: zoom  ·  WASD QE: move  ·  F: re-frame\n" +
            "1–4: tools  ·  T: scenario trigger  ·  R: reset  ·  N / B: next / previous scenario\n" +
            "Space: pause  ·  . : step one fixed step  ·  [ ]: slow motion 1× / 0.25× / 0.1×\n" +
            "F1: diagnostics  ·  F2: tint (material / load / cluster / sleep)  ·  H: hide help\n" +
            "<b>Lines</b>  green→yellow→red: structural load ratio q  ·  magenta: residual hinge\n" +
            "purple: dormant residual  ·  grey: severed  ·  white square: ground anchor\n" +
            "<b>Crosses</b>  orange: active body  ·  blue: sleeping  ·  yellow: hinge point";

        void Update()
        {
            if (lab == null) return;
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime), 0.05f);
            if (Time.unscaledTime < nextText) return;
            nextText = Time.unscaledTime + 0.2f;
            if (world.BuildCount != lastBuildCount) { lastBuildCount = world.BuildCount; physicsMaxMs = 0f; }

            foreach (var (b, i) in scenarioButtons) Colorize(b, i == lab.ScenarioIndex);
            foreach (var (b, t) in toolButtons) Colorize(b, t == lab.Tool);
            playbackLabel.text = world.Paused ? "Resume" : "Pause";
            speedLabel.text = $"{lab.TimeScale:0.##}×";
            diagLabel.text = lab.DiagnosticsVisible ? "Diagnostics: on" : "Diagnostics: off";
            tintLabel.text = $"Tint: {lab.Tint}";
            helpPanel.SetActive(lab.HelpVisible && !compactLayout);

            var s = lab.Scenarios[lab.ScenarioIndex];
            triggerLabel.text = s.trigger != null ? $"T: {s.triggerLabel}" : "(no trigger)";
            scenarioText.text = $"<b>{s.title}</b>\n<color=#9fd8ff>Do:</color> {s.instruction}\n<color=#b8ffb0>Expect:</color> {s.expected}" +
                                (lab.LastActionText.Length > 0 ? $"\n<color=#ffd27f>Last action:</color> {lab.LastActionText}" : "");

            statsText.text = Stats();
            inspectText.text = Inspector();
            logText.text = Log();
        }

        string Stats()
        {
            var st = world.stats;
            string phys = "n/a";
            if (physicsRecorder.Valid && physicsRecorder.Count > 0)
            {
                // Frame-based samples are 0 on frames without a fixed step: report the recent maximum.
                long max = 0;
                for (int i = 0; i < physicsRecorder.Count; i++) max = System.Math.Max(max, physicsRecorder.GetSample(i).Value);
                physicsMaxMs = Mathf.Max(physicsMaxMs, max / 1e6f);
                phys = $"{max / 1e6:0.00} ms (max {physicsMaxMs:0.00})";
            }
            float since = world.SimTime;
            // Batch counts only exist in the editor; they are the number that explains a graphics ring
            // buffer warning, so they are worth showing while tuning a large model.
            string draw = "";
#if UNITY_EDITOR
            draw = $"   draw calls {UnityEditor.UnityStats.drawCalls} ({UnityEditor.UnityStats.srpBatcherDrawCalls} batched)" +
                   $"   tinted {world.TintedPieces}";
#endif
            return $"<b>Stats</b>  FPS {fps:0}   physics {phys}   structural {st.structuralMs:0.00} ms (max {st.maxStructuralMs:0.00}){draw}\n" +
                   $"sim time {since:0.0} s   pieces {st.pieces} (static {st.staticPieces})   bodies active {st.dynamicBodies} / sleeping {st.sleepingBodies}   joints {st.activeJoints}\n" +
                   $"connections structural {st.structural} · residual {st.residual} · severed {st.severed}   failures {world.log.Total} (pending {st.pendingFailures})   contacts {st.contactsThisStep} impacts {st.impactsThisStep}\n" +
                   $"shattered pieces {st.shatteredPieces} → live fragments {st.liveFragments} / {world.Settings.fragments.maxLiveFragments}" +
                   (st.pendingShatters > 0 ? $"   queued {st.pendingShatters}" : "") +
                   (st.shatterSkippedForBudget > 0 ? $"   (over budget, detached whole: {st.shatterSkippedForBudget})" : "") +
                   (world.Paused ? "   <color=#ffd27f>PAUSED</color>" : "");
        }

        string Inspector()
        {
            int id = lab.SelectedConnection;
            if (id < 0 || world.Graph == null || id >= world.Graph.connections.Count)
                return "<b>Inspect</b>  click a piece with Damage or Inspect to select its nearest connection.";
            var c = world.Graph.connections[id];
            var g = world.Graph;
            var sb = new StringBuilder();
            string b = c.IsGround ? "ground" : g.pieces[c.b].name;
            sb.Append($"<b>Connection #{c.id}</b>  {g.pieces[c.a].name} – {b}   state <b>{c.state}</b>   width {c.width:0.00} m × {c.depth:0.00} m\n");
            if (c.state == ConnectionState.Structural)
            {
                sb.Append($"load  N {c.loadNormal / 1000f:0.0} kN ({(c.loadNormal >= 0 ? "tension" : "compression")})  S {c.loadShear / 1000f:0.0} kN  M {c.loadBending / 1000f:0.0} kN·m\n");
                sb.Append($"cap   T {c.capTension / 1000f:0.0} / C {c.capCompression / 1000f:0.0} kN  S {c.capShear / 1000f:0.0} kN  M {c.capBending / 1000f:0.0} kN·m   (× (1−D))\n");
                sb.Append($"q (load ÷ capacity, dimensionless)  <b>{c.q:0.00}</b>  [normal {c.qNormal:0.00}, shear {c.qShear:0.00}, bending {c.qBending:0.00}]   damage D {c.damage:0.00}");
            }
            else if (c.state == ConnectionState.Residual)
            {
                sb.Append($"residual hinge {(c.jointActive ? "active joint" : "dormant (no relative motion)")}   sag {c.sagDegrees:0.0}°   failed at {c.failTime:0.00}s ({FailureReasonText.Label(c.reason)})\n");
                sb.Append($"force {c.emaForce.magnitude / 1000f:0.0} / {c.residualForceCapacity * c.ResidualFactor / 1000f:0.0} kN   hinge moment {c.emaAxisTorque / 1000f:0.0} kN·m (yield {c.residualYieldMoment * c.ResidualFactor / 1000f:0.0})   off-axis {c.emaOffAxisTorque / 1000f:0.0} kN·m\n");
                sb.Append($"residual q <b>{c.qResidual:0.00}</b>   residual damage {c.residualDamage:0.00}");
            }
            else
            {
                sb.Append($"severed at {(c.residualFailTime >= 0 ? c.residualFailTime : c.failTime):0.00}s — {FailureReasonText.Label(c.reason)}");
            }
            return sb.ToString();
        }

        string Log()
        {
            var sb = new StringBuilder("<b>Recent breaks</b>\n");
            int n = world.log.Count;
            int from = Mathf.Max(0, n - 8);
            if (n == 0) sb.Append("(none)");
            for (int i = n - 1; i >= from; i--) sb.Append(world.log[i].ToString()).Append('\n');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ widgets

        Transform Panel(Transform parent, Vector2 corner, Vector2 offset, float width)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(width, 100f);
            go.GetComponent<Image>().color = PanelColor;
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(10, 10, 8, 8);
            v.spacing = 4f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            var f = go.GetComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go.transform;
        }

        Transform Row(Transform parent)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childControlHeight = true;
            h.childControlWidth = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            return go.transform;
        }

        Text Label(Transform parent, string text, int size, FontStyle style)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = TextColor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        Button MakeButton(Transform parent, string label, UnityAction onClick, out Text text)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = ButtonColor;
            go.GetComponent<LayoutElement>().minHeight = 26f;
            var b = go.GetComponent<Button>();
            b.onClick.AddListener(onClick);
            text = Label(go.transform, label, 14, FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            var rt = (RectTransform)text.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 2f);
            rt.offsetMax = new Vector2(-4f, -2f);
            return b;
        }

        void MakeToggleButton(Transform parent, string label, System.Func<bool> get, System.Action<bool> set)
        {
            Text t = null;
            Button b = null;
            b = MakeButton(parent, label, () =>
            {
                set(!get());
                t.text = $"{label}: {(get() ? "on" : "off")}";
                Colorize(b, get());
            }, out t);
            t.text = $"{label}: {(get() ? "on" : "off")}";
            Colorize(b, get());
        }

        void AddTool(Transform parent, string label, LabTool tool)
        {
            var b = MakeButton(parent, label, () => lab.Tool = tool, out _);
            toolButtons.Add((b, tool));
        }

        void Slider(Transform parent, string label, float min, float max, float value, System.Action<float> set, string fmt, bool appliesToJoints = false)
        {
            var text = Label(parent, $"{label}: {value.ToString(fmt)}", 13, FontStyle.Normal);
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 18f;
            var s = go.GetComponent<UnityEngine.UI.Slider>();
            s.minValue = min;
            s.maxValue = max;
            s.value = value;
            s.onValueChanged.AddListener(v =>
            {
                set(v);
                text.text = $"{label}: {v.ToString(fmt)}";
                if (appliesToJoints) world.RefreshResidualCapacities();
            });
        }

        static void Colorize(Button b, bool on)
        {
            if (b == null) return;
            b.GetComponent<Image>().color = on ? ButtonActive : ButtonColor;
        }
    }
}
