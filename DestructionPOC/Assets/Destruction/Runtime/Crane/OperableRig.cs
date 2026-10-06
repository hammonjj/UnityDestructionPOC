using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>One line of the controls panel: the keys (already formatted for the active device) and what they do.</summary>
    public struct ControlHint
    {
        public string keys;
        public string label;
        /// <summary>Currently live alternate action (e.g. the gamepad modifier is held).</summary>
        public bool highlight;
        /// <summary>Currently suppressed (e.g. the primary right-stick actions while the modifier is held).</summary>
        public bool dim;
        /// <summary>Explanatory line without keys.</summary>
        public bool note;

        public static ControlHint Row(string keys, string label, bool highlight = false, bool dim = false) =>
            new ControlHint { keys = keys, label = label, highlight = highlight, dim = dim };

        public static ControlHint Note(string text, bool highlight = false) =>
            new ControlHint { label = text, note = true, highlight = highlight };
    }

    /// <summary>
    /// A CraneTest machine the player can walk up to, climb into and operate (the wrecking crane and the excavators).
    /// <see cref="CranePlayer"/> owns the enter/exit lifecycle and routes input only to the occupied rig.
    /// </summary>
    public interface IOperableRig
    {
        string RigName { get; }
        /// <summary>Tool fitted to the machine, empty when not applicable.</summary>
        string AttachmentName { get; }
        /// <summary>Ground point beside the cab steps where the player can climb in.</summary>
        Vector3 DoorPosition { get; }
        /// <summary>Operator's eye point (first-person view, and where the hidden player body rides).</summary>
        Vector3 SeatPosition { get; }
        Quaternion SeatRotation { get; }
        /// <summary>Overhead-camera focus while operating: the centre of the working area.</summary>
        Vector3 CameraFocus { get; }
        /// <summary>The action map with this rig's controls.</summary>
        InputActionMap ControlMap(CraneTestInput input);
        void OnEnter();
        /// <summary>Stop powered motion and hold the current pose.</summary>
        void OnExit();
        /// <summary>Read this frame's input. Only called on the occupied rig.</summary>
        void Operate(CraneTestInput input);
        /// <summary>Places to put the player on exit, best first. The player takes the first unblocked one.</summary>
        void ExitCandidates(List<Vector3> into);
        void ControlHints(CraneTestInput input, List<ControlHint> into);
        /// <summary>Short live readout for the panel (angles, grip, ...).</summary>
        string Telemetry { get; }
    }
}
