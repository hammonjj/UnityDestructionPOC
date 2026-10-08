using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

namespace DestructionLab
{
    /// <summary>
    /// Every CraneTest binding, built in code as Input System actions. One map per control context, and only the
    /// context in use is enabled, so on-foot input cannot reach a rig and one rig's input cannot reach another:
    ///   Global    reset (always on)
    ///   OnFoot    move, run, jump, interact (enter a rig)
    ///   Vehicle   exit (on while in any rig)
    ///   Crane     wrecking-crane controls
    ///   Excavator shared controls for all four excavators
    ///   Loader    wheel loader and skid-steer controls
    /// The controls panel reads the bindings back from these actions (including overrides), so remapping an action
    /// changes the on-screen hints too. Nothing outside CraneTest uses this.
    /// </summary>
    public sealed class CraneTestInput : IDisposable
    {
        public const string KeyboardMouse = "Keyboard&Mouse";
        public const string Gamepad = "Gamepad";

        // Analog dead zones (normalised). Sticks drop small drift; triggers drop resting noise.
        const string StickAxis = "axisDeadzone(min=0.18,max=0.95)";
        const string TriggerAxis = "axisDeadzone(min=0.06,max=0.98)";

        public readonly InputActionAsset asset;
        public readonly InputActionMap global, onFoot, vehicle, crane, excavator, loader;

        // Global
        public readonly InputAction Reset, ToggleView, RespawnVehicle, RespawnPlayer;
        // On foot
        public readonly InputAction Move, Run, Jump, Interact, LookMouse, LookStick;
        // Any rig
        public readonly InputAction Exit;
        // Crane
        public readonly InputAction CraneSlew, CraneBoom, CraneWinch, CraneDrive, CraneLookMouse, CraneLookStick;
        // Excavator
        public readonly InputAction Drive, Turn, Swing, Boom, Stick, Curl, Modifier, Close, Open;
        // Loaders: + drive is forward, + steer is right, + lift raises the arms, + tilt tips the bucket forward
        public readonly InputAction LoaderDrive, LoaderSteer, LoaderLift, LoaderTilt;

        /// <summary>True when the most recent input came from a gamepad. Drives which hints the HUD shows.</summary>
        public bool UsingGamepad { get; private set; }

        readonly InputDevice[] devices;

        /// <summary>All devices (single player, the CraneTest default) or only the given ones (split screen: each player
        /// owns a keyboard + mouse or one gamepad, so one player's input never reaches another).</summary>
        public CraneTestInput(InputDevice[] onlyDevices = null)
        {
            InputSystem.RegisterBindingComposite<WithoutModifierComposite>(WithoutModifierComposite.Name);

            asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "CraneTest Controls (runtime)";
            if (onlyDevices != null && onlyDevices.Length > 0)
            {
                devices = onlyDevices;
                asset.devices = new UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>(onlyDevices);
            }
            asset.AddControlScheme(KeyboardMouse).WithRequiredDevice<Keyboard>().WithOptionalDevice<Mouse>();
            asset.AddControlScheme(Gamepad).WithRequiredDevice<UnityEngine.InputSystem.Gamepad>();

            global = asset.AddActionMap("Global");
            // Gamepad Start opens the pause menu (PauseMenu), which has Restart level, so it is not bound here.
            Reset = global.AddAction("Reset", InputActionType.Button, "<Keyboard>/backspace", groups: KeyboardMouse);
            // Camera view: V, or the PlayStation touchpad click (the big button at the top). Other pads use R-stick click.
            ToggleView = Button(global, "ToggleView", "<Keyboard>/v", "<DualShockGamepad>/touchpadButton");
            ToggleView.AddBinding("<Gamepad>/rightStickPress", groups: Gamepad);
            // Stuck? X puts the machine you are in (or the nearest one) back at its start; Q puts you back at your spawn.
            RespawnVehicle = Button(global, "RespawnVehicle", "<Keyboard>/x", "<Gamepad>/buttonNorth");
            RespawnPlayer = Button(global, "RespawnPlayer", "<Keyboard>/q", "<Gamepad>/select");

            onFoot = asset.AddActionMap("OnFoot");
            Move = onFoot.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", KeyboardMouse).With("Down", "<Keyboard>/s", KeyboardMouse)
                .With("Left", "<Keyboard>/a", KeyboardMouse).With("Right", "<Keyboard>/d", KeyboardMouse);
            Move.AddBinding("<Gamepad>/leftStick", groups: Gamepad);
            Run = Button(onFoot, "Run", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Jump = Button(onFoot, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Interact = Button(onFoot, "Interact", "<Keyboard>/e", "<Gamepad>/buttonWest");
            // First- and third-person views only (the overhead camera has no look).
            LookMouse = onFoot.AddAction("LookMouse", InputActionType.Value, "<Mouse>/delta", groups: KeyboardMouse, expectedControlLayout: "Vector2");
            LookStick = onFoot.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", groups: Gamepad, expectedControlLayout: "Vector2");

            vehicle = asset.AddActionMap("Vehicle");
            Exit = Button(vehicle, "Exit", "<Keyboard>/e", "<Gamepad>/buttonEast");
            Exit.AddBinding("<Gamepad>/buttonWest", groups: Gamepad); // the enter button also leaves

            crane = asset.AddActionMap("Crane");
            // Gamepad: the left stick drives the tracks and the D-pad slews and luffs the boom.
            CraneSlew = Axis(crane, "Slew", "<Keyboard>/a", "<Keyboard>/d", null);
            CraneSlew.AddBinding("<Gamepad>/dpad/x", groups: Gamepad);
            CraneBoom = Axis(crane, "Boom", "<Keyboard>/s", "<Keyboard>/w", null);
            CraneBoom.AddBinding("<Gamepad>/dpad/y", groups: Gamepad);
            CraneWinch = Axis(crane, "Winch", "<Keyboard>/r", "<Keyboard>/f", null); // + pays out (ball down)
            CraneWinch.AddCompositeBinding("1DAxis", processors: TriggerAxis)
                .With("Negative", "<Gamepad>/leftTrigger", Gamepad).With("Positive", "<Gamepad>/rightTrigger", Gamepad);
            CraneDrive = crane.AddAction("Drive", InputActionType.Value, expectedControlLayout: "Vector2");
            CraneDrive.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", KeyboardMouse).With("Down", "<Keyboard>/downArrow", KeyboardMouse)
                .With("Left", "<Keyboard>/leftArrow", KeyboardMouse).With("Right", "<Keyboard>/rightArrow", KeyboardMouse);
            CraneDrive.AddBinding("<Gamepad>/leftStick", groups: Gamepad);
            CraneLookMouse = crane.AddAction("LookMouse", InputActionType.Value, "<Mouse>/delta", groups: KeyboardMouse, expectedControlLayout: "Vector2");
            CraneLookStick = crane.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", groups: Gamepad, expectedControlLayout: "Vector2");

            // Excavator. E is enter/exit, so the suggested Q/E swing moves to Z/C and the curl to Y/H (the third of
            // the R/F, T/G, Y/H columns: boom, stick, wrist from left to right).
            excavator = asset.AddActionMap("Excavator");
            Drive = Axis(excavator, "Drive", "<Keyboard>/s", "<Keyboard>/w", "<Gamepad>/leftStick/y");
            Turn = Axis(excavator, "Turn", "<Keyboard>/a", "<Keyboard>/d", "<Gamepad>/leftStick/x");
            Modifier = excavator.AddAction("Modifier", InputActionType.Button, "<Gamepad>/leftShoulder", groups: Gamepad);
            // The right stick drives swing/boom normally and stick/curl while LB is held, never both.
            Swing = Axis(excavator, "Swing", "<Keyboard>/z", "<Keyboard>/c", null);
            Swing.AddCompositeBinding(WithoutModifierComposite.Name, processors: StickAxis)
                .With("Modifier", "<Gamepad>/leftShoulder", Gamepad).With("Binding", "<Gamepad>/rightStick/x", Gamepad);
            Boom = Axis(excavator, "Boom", "<Keyboard>/f", "<Keyboard>/r", null);
            Boom.AddCompositeBinding(WithoutModifierComposite.Name, processors: StickAxis)
                .With("Modifier", "<Gamepad>/leftShoulder", Gamepad).With("Binding", "<Gamepad>/rightStick/y", Gamepad);
            Stick = Axis(excavator, "Stick", "<Keyboard>/g", "<Keyboard>/t", null); // + extends
            Stick.AddCompositeBinding("OneModifier", processors: StickAxis)
                .With("Modifier", "<Gamepad>/leftShoulder", Gamepad).With("Binding", "<Gamepad>/rightStick/y", Gamepad);
            Curl = Axis(excavator, "Curl", "<Keyboard>/h", "<Keyboard>/y", null); // + curls in
            // ISO pattern: right stick left curls in, so invert X.
            Curl.AddCompositeBinding("OneModifier", processors: StickAxis + ",invert")
                .With("Modifier", "<Gamepad>/leftShoulder", Gamepad).With("Binding", "<Gamepad>/rightStick/x", Gamepad);
            Close = excavator.AddAction("Close", InputActionType.Value, "<Mouse>/leftButton", groups: KeyboardMouse, expectedControlLayout: "Axis");
            Close.AddBinding("<Gamepad>/rightTrigger", processors: TriggerAxis, groups: Gamepad);
            Open = excavator.AddAction("Open", InputActionType.Value, "<Mouse>/rightButton", groups: KeyboardMouse, expectedControlLayout: "Axis");
            Open.AddBinding("<Gamepad>/leftTrigger", processors: TriggerAxis, groups: Gamepad);

            // Loaders. E is enter/exit, so the suggested Q/E bucket tilt becomes Z (curl back) / C (tip forward), the same
            // pair the excavators use for swing. The right stick keeps the suggested split: Y lifts, X tilts.
            loader = asset.AddActionMap("Loader");
            LoaderDrive = Axis(loader, "Drive", "<Keyboard>/s", "<Keyboard>/w", "<Gamepad>/leftStick/y");
            LoaderSteer = Axis(loader, "Steer", "<Keyboard>/a", "<Keyboard>/d", "<Gamepad>/leftStick/x");
            LoaderLift = Axis(loader, "Lift", "<Keyboard>/f", "<Keyboard>/r", "<Gamepad>/rightStick/y");
            LoaderTilt = Axis(loader, "Tilt", "<Keyboard>/z", "<Keyboard>/c", "<Gamepad>/rightStick/x");

            UsingGamepad = devices != null ? !Has<Keyboard>() && Has<UnityEngine.InputSystem.Gamepad>()
                                           : UnityEngine.InputSystem.Gamepad.current != null && Keyboard.current == null;
            InputSystem.onActionChange += OnActionChange;
            global.Enable();
            SetContext(null);
        }

        static InputAction Button(InputActionMap map, string name, string key, string pad)
        {
            var a = map.AddAction(name, InputActionType.Button, key, groups: KeyboardMouse);
            a.AddBinding(pad, groups: Gamepad);
            return a;
        }

        static InputAction Axis(InputActionMap map, string name, string negKey, string posKey, string padAxis)
        {
            var a = map.AddAction(name, InputActionType.Value, expectedControlLayout: "Axis");
            a.AddCompositeBinding("1DAxis")
                .With("Negative", negKey, KeyboardMouse).With("Positive", posKey, KeyboardMouse);
            if (padAxis != null) a.AddBinding(padAxis, processors: StickAxis, groups: Gamepad);
            return a;
        }

        /// <summary>Enable exactly one context: null = on foot, otherwise the given rig map plus Exit.</summary>
        public void SetContext(InputActionMap rigMap)
        {
            foreach (var m in new[] { onFoot, vehicle, crane, excavator, loader })
                if (m != rigMap && m.enabled) m.Disable();
            if (rigMap == null) onFoot.Enable();
            else
            {
                vehicle.Enable();
                rigMap.Enable();
            }
        }

        public InputActionMap ActiveRigMap => crane.enabled ? crane : excavator.enabled ? excavator : loader.enabled ? loader : null;

        void OnActionChange(object obj, InputActionChange change)
        {
            if (change != InputActionChange.ActionStarted && change != InputActionChange.ActionPerformed) return;
            if (!(obj is InputAction a) || a.actionMap?.asset != asset) return;
            var device = a.activeControl?.device;
            if (device is UnityEngine.InputSystem.Gamepad) UsingGamepad = true;
            else if (device is Keyboard || device is Mouse) UsingGamepad = false;
        }

        /// <summary>Also switch hints on raw device activity that no enabled action listens to (e.g. any key).</summary>
        public void PollDevice()
        {
            var kb = devices != null ? Find<Keyboard>() : Keyboard.current;
            var mouse = devices != null ? Find<Mouse>() : Mouse.current;
            var pad = devices != null ? Find<UnityEngine.InputSystem.Gamepad>() : UnityEngine.InputSystem.Gamepad.current;
            if ((kb != null && kb.anyKey.wasPressedThisFrame) ||
                (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)))
                UsingGamepad = false;
            else if (pad != null && PadActive(pad))
                UsingGamepad = true;
        }

        T Find<T>() where T : InputDevice
        {
            if (devices != null)
                foreach (var d in devices)
                    if (d is T t && t.added) return t;
            return null;
        }

        bool Has<T>() where T : InputDevice => Find<T>() != null;

        static bool PadActive(UnityEngine.InputSystem.Gamepad pad)
        {
            if (pad.leftStick.ReadValue().sqrMagnitude > 0.25f || pad.rightStick.ReadValue().sqrMagnitude > 0.25f) return true;
            if (pad.leftTrigger.ReadValue() > 0.3f || pad.rightTrigger.ReadValue() > 0.3f) return true;
            foreach (var c in pad.allControls)
                if (c is UnityEngine.InputSystem.Controls.ButtonControl b && !b.synthetic && b.wasPressedThisFrame) return true;
            return false;
        }

        public void Dispose()
        {
            InputSystem.onActionChange -= OnActionChange;
            if (asset == null) return;
            asset.Disable();
            UnityEngine.Object.Destroy(asset);
        }

        // ------------------------------------------------------------------ binding display

        /// <summary>
        /// Short label for an action's binding on the active device (or the given one), read from the action itself
        /// so overrides show up. 1D axes read "neg / pos", modifier composites show only their main control (the panel
        /// explains the modifier), vectors list their four keys.
        /// </summary>
        public string Keys(InputAction action, bool? gamepad = null, bool negativeFirst = false)
        {
            bool pad = gamepad ?? UsingGamepad;
            string group = pad ? Gamepad : KeyboardMouse;
            var parts = new List<string>();
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite)
                {
                    var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    int j = i + 1;
                    bool inGroup = false;
                    for (; j < bindings.Count && bindings[j].isPartOfComposite; j++)
                    {
                        if (!InGroup(bindings[j], group)) continue;
                        inGroup = true;
                        p[bindings[j].name] = Short(bindings[j].effectivePath);
                    }
                    if (inGroup)
                    {
                        if (p.TryGetValue("binding", out var main)) parts.Add(main);
                        else if (p.TryGetValue("negative", out var neg) && p.TryGetValue("positive", out var pos)) parts.Add(negativeFirst ? $"{neg} / {pos}" : $"{pos} / {neg}");
                        else if (p.TryGetValue("up", out var u)) parts.Add(string.Concat(u, Get(p, "left"), Get(p, "down"), Get(p, "right")));
                        else parts.Add(string.Join("+", p.Values));
                    }
                    i = j - 1;
                    continue;
                }
                if (b.isPartOfComposite || !InGroup(b, group)) continue;
                parts.Add(Short(b.effectivePath));
            }
            return parts.Count == 0 ? "—" : string.Join(" / ", parts);
        }

        static string Get(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : "";

        static bool InGroup(InputBinding b, string group)
        {
            if (string.IsNullOrEmpty(b.groups)) return true;
            foreach (var g in b.groups.Split(InputBinding.Separator))
                if (g == group) return true;
            return false;
        }

        static readonly Dictionary<string, string> PadNames = new Dictionary<string, string>
        {
            { "<Gamepad>/leftStick", "L-stick" }, { "<Gamepad>/rightStick", "R-stick" },
            { "<Gamepad>/leftStick/x", "L-stick ↔" }, { "<Gamepad>/leftStick/y", "L-stick ↕" },
            { "<Gamepad>/rightStick/x", "R-stick ↔" }, { "<Gamepad>/rightStick/y", "R-stick ↕" },
            { "<Gamepad>/leftTrigger", "LT" }, { "<Gamepad>/rightTrigger", "RT" },
            { "<Gamepad>/leftShoulder", "LB" }, { "<Gamepad>/rightShoulder", "RB" },
            { "<Gamepad>/buttonSouth", "A" }, { "<Gamepad>/buttonEast", "B" },
            { "<Gamepad>/buttonWest", "X" }, { "<Gamepad>/buttonNorth", "Y" },
            { "<Gamepad>/start", "Start" }, { "<Gamepad>/select", "Back" },
            { "<Gamepad>/dpad", "D-pad" }, { "<Gamepad>/dpad/x", "D-pad ↔" }, { "<Gamepad>/dpad/y", "D-pad ↕" },
{ "<Gamepad>/leftStickPress", "L-stick click" },
            { "<Gamepad>/rightStickPress", "R-stick click" }, { "<DualShockGamepad>/touchpadButton", "Touchpad" },
            { "<Mouse>/leftButton", "LMB" }, { "<Mouse>/rightButton", "RMB" }, { "<Mouse>/middleButton", "MMB" },
            { "<Mouse>/delta", "Mouse" },
            { "<Keyboard>/upArrow", "↑" }, { "<Keyboard>/downArrow", "↓" },
            { "<Keyboard>/leftArrow", "←" }, { "<Keyboard>/rightArrow", "→" },
            { "<Keyboard>/leftShift", "Shift" }, { "<Keyboard>/backspace", "Backspace" },
        };

        /// <summary>Compact human-readable name for one control path.</summary>
        public static string Short(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            if (PadNames.TryGetValue(path, out var n)) return n;
            string s = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(s) ? path : s;
        }
    }

    /// <summary>
    /// The reverse of the Input System's OneModifier composite: passes the bound axis through only while the modifier
    /// is NOT held. Paired with OneModifier on the same control, a held modifier switches the stick from one pair of
    /// functions to another without ever driving both.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public sealed class WithoutModifierComposite : InputBindingComposite<float>
    {
        public const string Name = "WithoutModifier";

        [InputControl(layout = "Button")] public int modifier;
        [InputControl(layout = "Axis")] public int binding;

        public override float ReadValue(ref InputBindingCompositeContext context)
        {
            if (context.ReadValueAsButton(modifier)) return 0f;
            return context.ReadValue<float>(binding);
        }

        public override float EvaluateMagnitude(ref InputBindingCompositeContext context) => Mathf.Abs(ReadValue(ref context));
    }
}
