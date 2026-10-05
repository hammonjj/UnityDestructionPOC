using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// Basic first-person controller that can climb into the crane cab and operate it.
    ///
    /// On foot:  WASD / left stick move, mouse / right stick look, Shift / left-stick click run, Space / A jump,
    ///           E / X enter the cab when standing beside the steps.
    /// In cab:   A/D or left stick X slew, W/S or left stick Y boom up/down, F/R or RT/LT ball down/up,
    ///           arrows or D-pad drive, mouse / right stick look, E / B leave.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class CranePlayer : MonoBehaviour
    {
        public CraneRig crane;
        public Camera cam;
        public DestructionWorld world;
        public float walkSpeed = 4.2f;
        public float runSpeed = 7.5f;
        public float jumpSpeed = 5.5f;
        public float gravity = -20f;
        public float lookSensitivity = 0.1f;     // deg per mouse count
        public float stickLookSpeed = 160f;      // deg/s at full deflection
        public float interactRange = 3.2f;
        public System.Action onReset;

        public bool InCab { get; private set; }

        CharacterController cc;
        float yaw, pitch, vy;
        bool cursorLocked;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
        }

        void Start()
        {
            yaw = transform.eulerAngles.y;
            SetCursor(true);
        }

        void SetCursor(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        bool NearDoor
        {
            get
            {
                if (crane == null) return false;
                Vector3 d = crane.DoorPosition - transform.position;
                d.y = 0f;
                return d.magnitude < interactRange;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursor(!cursorLocked);
            if (!cursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame) SetCursor(true);
            if ((kb != null && kb.backspaceKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame))
                onReset?.Invoke();

            Look(mouse, pad);

            bool interact = (kb != null && kb.eKey.wasPressedThisFrame) ||
                            (pad != null && (pad.buttonWest.wasPressedThisFrame || (InCab && pad.buttonEast.wasPressedThisFrame)));
            if (interact)
            {
                if (InCab) Leave();
                else if (NearDoor) Enter();
            }

            if (InCab) Operate(kb, pad);
            else Walk(kb, pad);
        }

        void Look(Mouse mouse, Gamepad pad)
        {
            float dx = 0f, dy = 0f;
            if (cursorLocked && mouse != null)
            {
                Vector2 m = mouse.delta.ReadValue();
                dx += m.x * lookSensitivity;
                dy += m.y * lookSensitivity;
            }
            if (pad != null)
            {
                Vector2 r = pad.rightStick.ReadValue();
                dx += r.x * stickLookSpeed * Time.deltaTime;
                dy += r.y * stickLookSpeed * Time.deltaTime;
            }
            yaw += dx;
            pitch = Mathf.Clamp(pitch - dy, -80f, 80f);
        }

        void Walk(Keyboard kb, Gamepad pad)
        {
            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
            }
            if (pad != null) move += pad.leftStick.ReadValue();
            move = Vector2.ClampMagnitude(move, 1f);
            bool run = (kb != null && kb.leftShiftKey.isPressed) || (pad != null && pad.leftStickButton.isPressed);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 v = (transform.right * move.x + transform.forward * move.y) * (run ? runSpeed : walkSpeed);

            if (cc.isGrounded && vy < 0f) vy = -2f;
            bool jump = (kb != null && kb.spaceKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (jump && cc.isGrounded) vy = jumpSpeed;
            vy += gravity * Time.deltaTime;
            v.y = vy;
            cc.Move(v * Time.deltaTime);

            cam.transform.SetPositionAndRotation(transform.position + Vector3.up * 1.62f, Quaternion.Euler(pitch, yaw, 0f));
        }

        void Operate(Keyboard kb, Gamepad pad)
        {
            float dt = Time.deltaTime;
            float slew = 0f, luff = 0f, winch = 0f, fwd = 0f, turn = 0f;
            if (kb != null)
            {
                if (kb.dKey.isPressed) slew += 1f;
                if (kb.aKey.isPressed) slew -= 1f;
                if (kb.wKey.isPressed) luff += 1f;
                if (kb.sKey.isPressed) luff -= 1f;
                if (kb.fKey.isPressed) winch += 1f;      // F: pay out (ball down)
                if (kb.rKey.isPressed) winch -= 1f;      // R: reel in (ball up)
                if (kb.upArrowKey.isPressed) fwd += 1f;
                if (kb.downArrowKey.isPressed) fwd -= 1f;
                if (kb.rightArrowKey.isPressed) turn += 1f;
                if (kb.leftArrowKey.isPressed) turn -= 1f;
            }
            if (pad != null)
            {
                Vector2 l = pad.leftStick.ReadValue();
                slew += l.x;
                luff += l.y;
                winch += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                Vector2 d = pad.dpad.ReadValue();
                fwd += d.y;
                turn += d.x;
            }
            crane.Slew(Mathf.Clamp(slew, -1f, 1f), dt);
            crane.Luff(Mathf.Clamp(luff, -1f, 1f), dt);
            crane.Winch(Mathf.Clamp(winch, -1f, 1f), dt);
            crane.Drive(Mathf.Clamp(fwd, -1f, 1f), Mathf.Clamp(turn, -1f, 1f));
        }

        void LateUpdate()
        {
            if (!InCab) return;
            // Head look inside the cab, relative to the carriage, so the view turns with a slew.
            float relYaw = Mathf.Clamp(Mathf.DeltaAngle(0f, yaw - crane.SeatRotation.eulerAngles.y), -110f, 110f);
            yaw = crane.SeatRotation.eulerAngles.y + relYaw;
            cam.transform.SetPositionAndRotation(crane.SeatPosition, Quaternion.Euler(pitch, yaw, 0f));
            transform.position = crane.SeatPosition - Vector3.up * 1.62f;
        }

        void Enter()
        {
            InCab = true;
            crane.SetOperatorInside(true);
            cc.enabled = false;
            vy = 0f;
            yaw = crane.SeatRotation.eulerAngles.y;
            pitch = 0f;
        }

        void Leave()
        {
            crane.Drive(0f, 0f);
            crane.SetOperatorInside(false);
            InCab = false;
            Vector3 exit = crane.DoorPosition;
            exit.y = 0.1f;
            transform.position = exit;
            cc.enabled = true;
        }

        // ------------------------------------------------------------------ HUD

        GUIStyle style, shadow;

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                style.normal.textColor = Color.white;
                shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            }
            string text;
            if (InCab)
            {
                text = "<b>CRANE</b>\n" +
                       "Slew  A / D    stick L-R\n" +
                       "Boom  W / S    stick U-D\n" +
                       "Ball  R up / F down    LT up / RT down\n" +
                       "Drive arrows    D-pad\n" +
                       "Look  mouse    right stick\n" +
                       "Leave E    B / X";
            }
            else
            {
                text = "Move WASD / left stick    Look mouse / right stick    Run Shift    Jump Space / A\n" +
                       (NearDoor ? "<b>Press E / X to climb into the crane</b>" : "Walk to the crane cab steps (left side of the machine)");
            }
            text += "\nReset Backspace / Start    Mouse unlock Esc";
            if (world != null)
                text += $"\nPieces {world.stats.pieces}   moving bodies {world.stats.dynamicBodies}   fragments {world.LiveFragments}   broken joints logged {world.log.Total}";
            if (InCab) text += $"\nBoom {crane.BoomAngle:0}°   Cable {crane.CableLength:0.0} m";

            var r = new Rect(16f, 12f, 900f, 260f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
            GUI.Label(new Rect(Screen.width * 0.5f - 4f, Screen.height * 0.5f - 4f, 8f, 8f), "·", style);
        }
    }
}
