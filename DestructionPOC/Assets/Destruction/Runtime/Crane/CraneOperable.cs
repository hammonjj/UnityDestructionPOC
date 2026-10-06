using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// Plugs the wrecking crane into the shared enter/exit lifecycle. The crane's controls are unchanged; they are
    /// only read from the Crane action map now instead of raw device polling.
    /// </summary>
    [RequireComponent(typeof(CraneRig))]
    public sealed class CraneOperable : MonoBehaviour, IOperableRig
    {
        [Tooltip("In the cab, the camera focus sits this far ahead of the seat along the carriage, to frame the boom's reach.")]
        public float cabLookAhead = 7f;

        CraneRig crane;
        CraneRig Crane => crane != null ? crane : (crane = GetComponent<CraneRig>());

        public string RigName => "Wrecking crane";
        public string AttachmentName => "Wrecking ball";
        public Vector3 DoorPosition => Crane.DoorPosition;
        public Vector3 SeatPosition => Crane.SeatPosition;
        public Quaternion SeatRotation => Crane.SeatRotation;

        public Vector3 CameraFocus
        {
            get
            {
                Vector3 p = Crane.SeatPosition + Crane.SeatRotation * Vector3.forward * cabLookAhead;
                p.y = Crane.SeatPosition.y - 1.62f;
                return p;
            }
        }

        public InputActionMap ControlMap(CraneTestInput input) => input.crane;

        public void OnEnter() => Crane.SetOperatorInside(true);

        public void OnExit()
        {
            Crane.Drive(0f, 0f);
            Crane.SetOperatorInside(false);
        }

        public void Operate(CraneTestInput input)
        {
            Crane.Slew(input.CraneSlew.ReadValue<float>());
            Crane.Luff(input.CraneBoom.ReadValue<float>());
            Crane.Winch(input.CraneWinch.ReadValue<float>());
            Vector2 d = input.CraneDrive.ReadValue<Vector2>();
            Crane.Drive(Mathf.Clamp(d.y, -1f, 1f), Mathf.Clamp(d.x, -1f, 1f));
        }

        public void ExitCandidates(List<Vector3> into)
        {
            into.Add(Crane.DoorPosition);
            // Fallbacks around the carriage if the steps are blocked.
            var t = transform;
            into.Add(t.position - t.right * 3.2f);
            into.Add(t.position + t.right * 3.2f);
            into.Add(t.position - t.forward * 4.5f);
        }

        public void ControlHints(CraneTestInput input, List<ControlHint> into)
        {
            bool pad = input.UsingGamepad;
            into.Add(ControlHint.Row(input.Keys(input.CraneSlew, pad, negativeFirst: true), "Slew left / right"));
            into.Add(ControlHint.Row(input.Keys(input.CraneBoom), "Boom up / down"));
            into.Add(ControlHint.Row(input.Keys(input.CraneWinch, pad, negativeFirst: true), "Ball up / down"));
            into.Add(ControlHint.Row(input.Keys(input.CraneDrive), "Drive tracks"));
        }

        public string Telemetry => $"Boom {Crane.BoomAngle:0}°   Cable {Crane.CableLength:0.0} m";
    }
}
