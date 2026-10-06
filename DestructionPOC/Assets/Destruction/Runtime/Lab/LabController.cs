using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    public enum LabTool { Damage, Explosion, DropBlock, Inspect }
    public enum TintMode { Material, Damage, Cluster, Sleep }

    /// <summary>Interaction state: scenario, tool, playback, selection. Reads input; never mutates physics
    /// directly (all world changes go through the DestructionWorld queue).</summary>
    public sealed class LabController : MonoBehaviour
    {
        public DestructionWorld world;
        public LabCamera labCamera;

        public List<Scenario> Scenarios { get; private set; }
        public int ScenarioIndex { get; private set; }
        public LabTool Tool { get; set; } = LabTool.Damage;
        public float TimeScale { get; private set; } = 1f;
        public bool DiagnosticsVisible { get; set; } = true;
        public TintMode Tint { get; set; } = TintMode.Damage;
        public bool HelpVisible { get; set; } = true;
        public int SelectedPiece { get; private set; } = -1;
        public int SelectedConnection { get; private set; } = -1;
        public float ScenarioStartTime { get; private set; }
        public bool TriggerUsed { get; private set; }
        public string LastActionText { get; private set; } = "";

        // Hooks for scenes that share the lab with a walking player and drivable rigs (ConvenienceStore). Defaults keep
        // the plain lab exactly as it was.
        /// <summary>R (and the HUD reset button) call this instead of <see cref="Reset"/> when set, so the scene can
        /// reset its machines and ledger too. The handler is expected to call <see cref="Reset"/> itself.</summary>
        public System.Action onResetRequested;
        /// <summary>Pause key. Space in the plain lab; a scene whose player jumps with Space moves it.</summary>
        public Key pauseKey = Key.Space;
        /// <summary>N / B / PageUp / PageDown switch scenario. Off when the scene's machines belong to one scenario.</summary>
        public bool scenarioSwitching = true;
        /// <summary>While this returns true the lab keyboard shortcuts are ignored (the player is in a cab, where the same
        /// letters drive the machine). Mouse tools are unaffected.</summary>
        public System.Func<bool> keysBlocked;

        public void RequestReset()
        {
            if (onResetRequested != null) onResetRequested();
            else Reset();
        }

        static readonly float[] Speeds = { 1f, 0.25f, 0.1f };
        int speedIndex;
        Vector2 pressPos;
        bool pressOverUi;

        public void Init(DestructionWorld w, LabCamera cam)
        {
            world = w;
            labCamera = cam;
            Scenarios = ScenarioLibrary.All();
            LoadScenario(0);
        }

        public void LoadScenario(int index)
        {
            ScenarioIndex = (index % Scenarios.Count + Scenarios.Count) % Scenarios.Count;
            Reset();
        }

        /// <summary>Full reset: simulation, diagnostics, selection, time scale, camera, tool.</summary>
        public void Reset()
        {
            var s = Scenarios[ScenarioIndex];
            world.Paused = false;
            world.Build(s);
            SetSpeed(0);
            SelectedPiece = -1;
            SelectedConnection = -1;
            TriggerUsed = false;
            LastActionText = "";
            ScenarioStartTime = Time.unscaledTime;
            if (labCamera != null) labCamera.Frame(s.cameraPivot, s.cameraDistance, s.cameraYaw, s.cameraPitch);
        }

        public void Trigger()
        {
            var s = Scenarios[ScenarioIndex];
            if (s.trigger == null) return;
            s.trigger(world);
            TriggerUsed = true;
            LastActionText = $"Trigger: {s.triggerLabel}";
        }

        public void TogglePause() => world.Paused = !world.Paused;

        public void StepOnce()
        {
            if (!world.Paused) world.Paused = true;
            world.Step();
        }

        public void CycleSpeed() => SetSpeed((speedIndex + 1) % Speeds.Length);

        public void SetSpeed(int i)
        {
            speedIndex = Mathf.Clamp(i, 0, Speeds.Length - 1);
            TimeScale = Speeds[speedIndex];
            Time.timeScale = TimeScale;
        }

        void Update()
        {
            if (world == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && (keysBlocked == null || !keysBlocked()))
            {
                if (kb.digit1Key.wasPressedThisFrame) Tool = LabTool.Damage;
                if (kb.digit2Key.wasPressedThisFrame) Tool = LabTool.Explosion;
                if (kb.digit3Key.wasPressedThisFrame) Tool = LabTool.DropBlock;
                if (kb.digit4Key.wasPressedThisFrame) Tool = LabTool.Inspect;
                if (kb[pauseKey].wasPressedThisFrame) TogglePause();
                if (kb.periodKey.wasPressedThisFrame) StepOnce();
                if (kb.leftBracketKey.wasPressedThisFrame) SetSpeed(Mathf.Min(speedIndex + 1, Speeds.Length - 1));
                if (kb.rightBracketKey.wasPressedThisFrame) SetSpeed(Mathf.Max(speedIndex - 1, 0));
                if (kb.rKey.wasPressedThisFrame) RequestReset();
                if (kb.tKey.wasPressedThisFrame) Trigger();
                if (kb.f1Key.wasPressedThisFrame) DiagnosticsVisible = !DiagnosticsVisible;
                if (kb.f2Key.wasPressedThisFrame) Tint = (TintMode)(((int)Tint + 1) % 4);
                if (kb.hKey.wasPressedThisFrame) HelpVisible = !HelpVisible;
                if (scenarioSwitching && (kb.pageDownKey.wasPressedThisFrame || kb.nKey.wasPressedThisFrame)) LoadScenario(ScenarioIndex + 1);
                if (scenarioSwitching && (kb.pageUpKey.wasPressedThisFrame || kb.bKey.wasPressedThisFrame)) LoadScenario(ScenarioIndex - 1);
                if (kb.escapeKey.wasPressedThisFrame) { SelectedPiece = -1; SelectedConnection = -1; }
            }

            if (mouse == null) return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (labCamera != null) labCamera.InputBlocked = overUi && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                pressPos = mouse.position.ReadValue();
                pressOverUi = overUi;
            }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                // A click must start and end outside UI and not be a drag.
                bool drag = (mouse.position.ReadValue() - pressPos).sqrMagnitude > 36f;
                if (!pressOverUi && !overUi && !drag) Click(mouse.position.ReadValue());
            }
        }

        void Click(Vector2 screen)
        {
            var cam = labCamera != null ? labCamera.GetComponent<Camera>() : Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out var hit, 500f)) return;
            bool isPiece = world.TryGetPiece(hit.collider, out int piece);
            var t = world.Settings.tools;

            switch (Tool)
            {
                case LabTool.Damage:
                    if (!isPiece) return;
                    world.Damage(piece, t.clickDamage);
                    SelectPiece(piece, hit.point);
                    LastActionText = $"Damage +{t.clickDamage:0.00} on {world.Graph.pieces[piece].name}";
                    break;
                case LabTool.Explosion:
                    world.Explode(hit.point, t.explosionRadius, t.explosionDamage, t.explosionImpulse);
                    LastActionText = $"Explosion r={t.explosionRadius:0.0} m at {hit.point:F1}";
                    break;
                case LabTool.DropBlock:
                    world.DropBlock(hit.point + Vector3.up * t.dropBlockHeight, t.dropBlockMass, 0f);
                    LastActionText = $"Dropped {t.dropBlockMass / 1000f:0.#} t block";
                    break;
                case LabTool.Inspect:
                    if (isPiece) SelectPiece(piece, hit.point);
                    else { SelectedPiece = -1; SelectedConnection = -1; }
                    break;
            }
        }

        void SelectPiece(int piece, Vector3 point)
        {
            SelectedPiece = piece;
            SelectedConnection = -1;
            float best = float.MaxValue;
            foreach (int cid in world.Graph.adjacency[piece])
            {
                var c = world.Graph.connections[cid];
                float d = Vector3.Distance(world.ConnectionWorldCenter(c), point);
                if (d < best) { best = d; SelectedConnection = cid; }
            }
        }

        public void SelectConnection(int id) => SelectedConnection = id;
    }
}
