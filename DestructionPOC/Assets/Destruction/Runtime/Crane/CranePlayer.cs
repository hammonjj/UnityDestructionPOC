using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// CraneTest character: walks up to any rig (the wrecking crane or an excavator), climbs in, operates it and gets
    /// out again. Bindings live in <see cref="CraneTestInput"/>; only the current context's actions are enabled, so
    /// on-foot input never reaches a rig and only the occupied rig receives vehicle input.
    ///
    /// On foot:  WASD / left stick move, Shift / left-stick click run, Space / A jump, E / X enter the rig whose cab
    ///           steps you stand beside. (First-person view only: mouse / right stick look.)
    /// In a rig: the rig's own controls (see the controls panel), E / B / X leave.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class CranePlayer : MonoBehaviour
    {
        public CraneRig crane;
        [Tooltip("Every machine the player can enter. Filled by CraneTestBootstrap.")]
        public List<MonoBehaviour> rigs = new List<MonoBehaviour>();
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

        [Header("Overhead camera (CraneTest). Leave 'overhead' empty for the first-person view.")]
        [Tooltip("Fixed-angle camera. When set, movement is camera-relative, mouse look is off and the cursor stays free.")]
        public CraneOverheadCamera overhead;
        [Tooltip("Transform the overhead camera follows. This script moves it: the player on foot, the rig's working area while operating.")]
        public Transform cameraFocus;
        [Tooltip("Visible body, hidden while in a rig.")]
        public GameObject avatar;
        [Tooltip("How fast the character turns to face its movement direction (deg/s).")]
        public float faceTurnSpeed = 720f;

        /// <summary>The rig being operated, or null on foot.</summary>
        public IOperableRig Current { get; private set; }
        public bool InCab => Current != null;
        public CraneTestInput Input { get; private set; }
        /// <summary>True while the first-person view is chosen. Only meaningful when an overhead camera exists.</summary>
        public bool FirstPerson { get; private set; }
        bool Overhead => overhead != null && !FirstPerson;

        CharacterController cc;
        float yaw, pitch, vy, relYaw;
        bool cursorLocked;
        int transitionFrame = -1;
        readonly List<Vector3> exits = new List<Vector3>();

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            Input = new CraneTestInput();
        }

        void OnDestroy() => Input?.Dispose();

        void Start()
        {
            yaw = transform.eulerAngles.y;
            if (crane != null)
            {
                crane.BallHit += OnBallHit;
                if (crane.GetComponent<CraneOperable>() is var op && op != null && !rigs.Contains(op)) rigs.Add(op);
            }
            SetCursor(!Overhead);
            UpdateFocus();
            if (Overhead) overhead.Snap();
        }

        /// <summary>Re-frame instantly after a scene reset or respawn.</summary>
        public void SnapCamera()
        {
            if (!Overhead) return;
            UpdateFocus();
            overhead.Snap();
        }

        /// <summary>Swap between the overhead camera and the first-person view.</summary>
        public void ToggleView()
        {
            if (overhead == null) return;
            FirstPerson = !FirstPerson;
            overhead.enabled = !FirstPerson;
            if (FirstPerson)
            {
                cam.orthographic = false;
                cam.fieldOfView = 70f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 600f;
                // Face the way the body faces, so the view does not jump.
                yaw = transform.eulerAngles.y;
                pitch = 0f;
            }
            else overhead.Snap();
            if (avatar != null) avatar.SetActive(!FirstPerson && Current == null);
            SetCursor(FirstPerson);
        }

        void UpdateFocus()
        {
            if (cameraFocus == null) return;
            cameraFocus.position = Current != null ? Current.CameraFocus : transform.position;
        }

        void SetCursor(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>The rig whose cab steps are within reach, nearest first; null if none.</summary>
        public IOperableRig NearestRig
        {
            get
            {
                IOperableRig best = null;
                float bestD = interactRange;
                foreach (var mb in rigs)
                {
                    if (!(mb is IOperableRig r) || mb == null || !mb.isActiveAndEnabled) continue;
                    Vector3 d = r.DoorPosition - transform.position;
                    d.y = 0f;
                    if (d.magnitude < bestD)
                    {
                        bestD = d.magnitude;
                        best = r;
                    }
                }
                return best;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            Input.PollDevice();

            if (overhead != null && Input.ToggleView.WasPressedThisFrame()) ToggleView();

            if (!Overhead)
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursor(!cursorLocked);
                if (!cursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame) SetCursor(true);
            }
            if (Input.Reset.WasPressedThisFrame())
            {
                onReset?.Invoke();
                return;
            }

            if (!Overhead) Look();

            // Enter / exit, never in the same frame as the previous transition, and the frame that changes possession
            // runs neither walking nor rig controls.
            if (Time.frameCount != transitionFrame)
            {
                if (Current == null && Input.Interact.WasPressedThisFrame())
                {
                    var rig = NearestRig;
                    if (rig != null)
                    {
                        Enter(rig);
                        return;
                    }
                }
                else if (Current != null && Input.Exit.WasPressedThisFrame())
                {
                    Leave();
                    return;
                }
            }

            if (Current != null) Current.Operate(Input);
            else Walk();
        }

        void Look()
        {
            Vector2 m = Vector2.zero, r = Vector2.zero;
            if (Current == null)
            {
                m = Input.LookMouse.ReadValue<Vector2>();
                r = Input.LookStick.ReadValue<Vector2>();
            }
            else if (Input.crane.enabled)
            {
                m = Input.CraneLookMouse.ReadValue<Vector2>();
                r = Input.CraneLookStick.ReadValue<Vector2>();
            }
            float dx = 0f, dy = 0f;
            if (cursorLocked)
            {
                dx += m.x * lookSensitivity;
                dy += m.y * lookSensitivity;
            }
            dx += r.x * stickLookSpeed * Time.deltaTime;
            dy += r.y * stickLookSpeed * Time.deltaTime;
            // In a cab the view is relative to the seat, so it turns with the machine and only head-turns are input.
            if (Current != null) relYaw = Mathf.Clamp(relYaw + dx, -110f, 110f);
            else yaw += dx;
            pitch = Mathf.Clamp(pitch - dy, -80f, 80f);
        }

        void Walk()
        {
            Vector2 move = Vector2.ClampMagnitude(Input.Move.ReadValue<Vector2>(), 1f);
            bool run = Input.Run.IsPressed();

            Vector3 v;
            if (Overhead)
            {
                // Screen-relative: up = toward the top of the screen, right = toward the right. Facing follows motion
                // and never feeds back into movement or the camera.
                overhead.GroundAxes(out Vector3 up, out Vector3 right);
                Vector3 dir = right * move.x + up * move.y;
                v = dir * (run ? runSpeed : walkSpeed);
                if (dir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), faceTurnSpeed * Time.deltaTime);
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                v = (transform.right * move.x + transform.forward * move.y) * (run ? runSpeed : walkSpeed);
            }

            if (cc.isGrounded && vy < 0f) vy = -2f;
            if (Input.Jump.WasPressedThisFrame() && cc.isGrounded) vy = jumpSpeed;
            vy += gravity * Time.deltaTime;
            v.y = vy;
            cc.Move(v * Time.deltaTime);

            if (!Overhead) cam.transform.SetPositionAndRotation(transform.position + Vector3.up * 1.62f, Quaternion.Euler(pitch, yaw, 0f));
        }

        void LateUpdate()
        {
            if (Current != null)
            {
                transform.position = Current.SeatPosition - Vector3.up * 1.62f;
                if (!Overhead)
                {
                    yaw = Current.SeatRotation.eulerAngles.y + relYaw;
                    cam.transform.SetPositionAndRotation(Current.SeatPosition, Quaternion.Euler(pitch, yaw, 0f));
                }
            }
            UpdateFocus();
            ApplyShake();
        }

        // ------------------------------------------------------------------ impact feel

        float shake, hitStopUntil;

        void OnBallHit(float speed, Vector3 point)
        {
            // Felt more the closer you are; inside the crane cab you are right next to it.
            bool inCrane = Current is CraneOperable;
            float near = inCrane ? 1f : Mathf.Clamp01(1f - Vector3.Distance(transform.position, point) / 30f);
            // Camera shake is a first-person effect; the overhead view stays steady.
            if (!Overhead) shake = Mathf.Max(shake, Mathf.Clamp01(speed / 9f) * near);
            if (speed >= 5f && near > 0.3f && hitStopUntil <= Time.unscaledTime)
            {
                // A brief hit-stop sells the mass of the ball.
                Time.timeScale = 0.25f;
                hitStopUntil = Time.unscaledTime + 0.09f;
            }
        }

        void ApplyShake()
        {
            if (hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil)
            {
                Time.timeScale = 1f;
                hitStopUntil = 0f;
            }
            if (shake <= 0.001f) return;
            float s = shake * shake;
            var t = cam.transform;
            if (Overhead)
            {
                // Positional only: the overhead view keeps a fixed orientation.
                overhead.AddShake(t.right * (Random.value - 0.5f) * 0.5f * s + t.up * (Random.value - 0.5f) * 0.5f * s);
            }
            else
            {
                t.position += t.right * (Random.value - 0.5f) * 0.18f * s + t.up * (Random.value - 0.5f) * 0.18f * s;
                t.rotation *= Quaternion.Euler(0f, 0f, (Random.value - 0.5f) * 5f * s);
            }
            shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.6f);
        }

        void OnDisable()
        {
            if (hitStopUntil > 0f) Time.timeScale = 1f;
            if (crane != null) crane.BallHit -= OnBallHit;
        }

        // ------------------------------------------------------------------ enter / exit

        void Enter(IOperableRig rig)
        {
            Current = rig;
            transitionFrame = Time.frameCount;
            if (avatar != null) avatar.SetActive(false);
            cc.enabled = false;
            vy = 0f;
            relYaw = 0f;
            pitch = 0f;
            rig.OnEnter();
            CabGlass.SetOperatorInside(rig as Component, true);
            Input.SetContext(rig.ControlMap(Input));
            if (Overhead) overhead.Snap();
        }

        void Leave()
        {
            var rig = Current;
            rig.OnExit();
            CabGlass.SetOperatorInside(rig as Component, false);
            Current = null;
            transitionFrame = Time.frameCount;
            Input.SetContext(null);
            PlaceAt(SafeExit(rig));
            if (avatar != null) avatar.SetActive(!FirstPerson);
            if (Overhead) overhead.Snap();
        }

        /// <summary>Leave any rig and stand at the given point (scene reset).</summary>
        public void ForceExit(Vector3 position, float yawDegrees)
        {
            if (Current != null)
            {
                Current.OnExit();
                CabGlass.SetOperatorInside(Current as Component, false);
                Current = null;
                Input.SetContext(null);
                if (avatar != null) avatar.SetActive(!FirstPerson);
            }
            transitionFrame = Time.frameCount;
            vy = 0f;
            PlaceAt(position);
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            yaw = yawDegrees;
        }

        void PlaceAt(Vector3 p)
        {
            cc.enabled = false;
            transform.position = p;
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>First of the rig's exit points where the character's capsule fits, dropped to the ground.</summary>
        Vector3 SafeExit(IOperableRig rig)
        {
            exits.Clear();
            rig.ExitCandidates(exits);
            float r = cc.radius, h = cc.height;
            foreach (var e in exits)
            {
                Vector3 p = new Vector3(e.x, 0.1f, e.z);
                Vector3 a = p + Vector3.up * (r + 0.05f), b = p + Vector3.up * (h - r);
                if (!Physics.CheckCapsule(a, b, r, ~0, QueryTriggerInteraction.Ignore)) return p;
            }
            Vector3 fallback = exits.Count > 0 ? exits[0] : transform.position;
            fallback.y = 0.1f;
            return fallback;
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
            bool pad = Input.UsingGamepad;
            string text;
            if (Current != null)
            {
                // The rig's controls are on the controls panel (bottom left).
                text = $"<b>{Current.RigName.ToUpperInvariant()}</b>" + (Overhead ? "" : $"\nLook  {Input.Keys(Input.CraneLookMouse)}  {Input.Keys(Input.CraneLookStick)}");
            }
            else
            {
                string move = $"Move {Input.Keys(Input.Move, pad)}" + (Overhead ? " (screen-relative)" : "");
                string look = Overhead ? "" : $"    Look {Input.Keys(Input.LookMouse, false)} / {Input.Keys(Input.LookStick, true)}";
                text = $"{move}{look}    Run {Input.Keys(Input.Run, pad)}    Jump {Input.Keys(Input.Jump, pad)}\n";
                var near = NearestRig;
                string interact = Input.Keys(Input.Interact, pad);
                text += near != null
                    ? $"<b>Press {interact} to climb into the {Label(near)}</b>"
                    : "Walk to a machine's cab steps (left side) to climb in: the crane, an excavator in the yard to the south, or a loader in the yard to the east";
            }
            text += $"\nReset {Input.Keys(Input.Reset, pad)}" + (Overhead ? "" : "    Mouse unlock Esc");
            if (overhead != null) text += $"    View {Input.Keys(Input.ToggleView, pad)} ({(FirstPerson ? "first person" : "overhead")})";
            if (world != null)
                text += $"\nPieces {world.stats.pieces}   moving bodies {world.stats.dynamicBodies}   fragments {world.LiveFragments}   broken joints logged {world.log.Total}";

            var r = new Rect(16f, 12f, 900f, 260f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
            if (!Overhead) GUI.Label(new Rect(Screen.width * 0.5f - 4f, Screen.height * 0.5f - 4f, 8f, 8f), "·", style);
        }

        static string Label(IOperableRig r) =>
            r is CraneOperable ? "crane" : r is LoaderRig || r is DozerRig ? r.RigName.ToLowerInvariant() : $"{r.RigName.ToLowerInvariant()} ({r.AttachmentName.ToLowerInvariant()})";
    }
}
