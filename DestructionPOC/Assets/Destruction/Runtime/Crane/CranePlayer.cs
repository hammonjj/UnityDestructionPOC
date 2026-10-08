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
        [Tooltip("Every machine the player can enter. Filled by CraneTestBootstrap, or by the PlayerManager from the level.")]
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
        [Tooltip("Camera shake from wrecking-ball hits (first-person view).")]
        public bool cameraShake = true;
        public System.Action onReset;

        [Header("Split screen")]
        [Tooltip("Devices this player owns. Empty = every device (single player). Set before the component is enabled.")]
        [System.NonSerialized] public InputDevice[] devices;
        public int playerIndex;
        [Tooltip("Where Respawn Player puts this player. Defaults to the start position.")]
        public Vector3 spawnPosition;
        public float spawnYaw;
        [Tooltip("A machine can be respawned from this far away when the player is on foot.")]
        public float respawnRange = 14f;
        [Tooltip("Off in split screen: the first-person view locks the one mouse.")]
        public bool allowViewToggle = true;

        /// <summary>Every active player, so two players cannot climb into the same machine.</summary>
        public static readonly List<CranePlayer> All = new List<CranePlayer>();

        /// <summary>This player's pixel rectangle in IMGUI coordinates (origin top-left). The whole screen without a camera.</summary>
        public Rect GuiRect
        {
            get
            {
                if (cam == null) return new Rect(0f, 0f, Screen.width, Screen.height);
                Rect r = cam.rect;
                return new Rect(r.x * Screen.width, (1f - r.y - r.height) * Screen.height, r.width * Screen.width, r.height * Screen.height);
            }
        }

        /// <summary>The machine a respawn would act on: the one occupied, else the nearest within <see cref="respawnRange"/>.</summary>
        public IOperableRig RespawnTarget
        {
            get
            {
                if (Current != null) return Current;
                IOperableRig best = null;
                float bestD = respawnRange;
                foreach (var mb in rigs)
                {
                    if (!(mb is IOperableRig r) || mb == null || !mb.isActiveAndEnabled || OccupiedByOther(r)) continue;
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

        bool OccupiedByOther(IOperableRig rig)
        {
            foreach (var p in All)
                if (p != this && p.Current == rig) return true;
            return false;
        }

        /// <summary>Put the machine you are in (or the nearest one) back at its start pose.</summary>
        public void RespawnVehicle()
        {
            var rig = RespawnTarget;
            if (rig == null) return;
            rig.Respawn();
            if (Current != null)
            {
                // The seat jumped; do not smooth the camera across the map.
                if (Overhead) overhead.Snap();
            }
        }

        /// <summary>Stand this player back at their spawn point, leaving any machine.</summary>
        public void RespawnPlayer()
        {
            ForceExit(spawnPosition, spawnYaw);
            SnapCamera();
        }

        [Header("Overhead camera (CraneTest). Leave 'overhead' empty for the first-person view.")]
        [Tooltip("Fixed-angle camera. When set, movement is camera-relative, mouse look is off and the cursor stays free.")]
        public CraneOverheadCamera overhead;
        [Tooltip("Transform the overhead camera follows. This script moves it: the player on foot, the rig's working area while operating.")]
        public Transform cameraFocus;
        [Tooltip("Visible body, hidden while in a rig.")]
        public GameObject avatar;
        [Tooltip("How fast the character turns to face its movement direction (deg/s).")]
        public float faceTurnSpeed = 720f;

        [Header("Third-person cameras")]
        [Tooltip("Camera distance behind the character in the near third-person view (m).")]
        public float thirdPersonNear = 4f;
        [Tooltip("Camera distance behind the character in the far third-person view (m).")]
        public float thirdPersonFar = 9f;
        [Tooltip("Both distances are multiplied by this while operating a machine, which is far bigger than the character.")]
        public float rigDistanceScale = 2.2f;
        public float thirdPersonFieldOfView = 60f;

        /// <summary>The camera views the view button cycles through, in order.</summary>
        public enum CameraView { Overhead, FirstPerson, ThirdPersonNear, ThirdPersonFar }

        /// <summary>The rig being operated, or null on foot.</summary>
        public IOperableRig Current { get; private set; }
        public bool InCab => Current != null;
        public CraneTestInput Input { get; private set; }
        /// <summary>The chosen view. Only meaningful when an overhead camera exists; without one the view is first person.</summary>
        public CameraView View { get; private set; }
        /// <summary>True while the first-person view is chosen. Only meaningful when an overhead camera exists.</summary>
        public bool FirstPerson => View == CameraView.FirstPerson;
        bool ThirdPerson => overhead != null && (View == CameraView.ThirdPersonNear || View == CameraView.ThirdPersonFar);
        bool Overhead => overhead != null && View == CameraView.Overhead;
        bool FirstPersonView => !Overhead && !ThirdPerson;

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
            Input = new CraneTestInput(devices);
            haptics = gameObject.AddComponent<Haptics>();
            spawnPosition = transform.position;
            spawnYaw = transform.eulerAngles.y;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
            Input?.Dispose();
        }

        void Start()
        {
            yaw = transform.eulerAngles.y;
            if (crane != null)
            {
                crane.BallHit += OnBallHit;
                if (crane.GetComponent<CraneOperable>() is var op && op != null && !rigs.Contains(op)) rigs.Add(op);
            }
            // Start in near third person; the overhead view is no longer reachable (View defaults to Overhead only as a placeholder).
            if (overhead != null) SetView(CameraView.ThirdPersonNear);
            SetCursor(!Overhead);
            UpdateFocus();
            if (Overhead) overhead.Snap();
        }

        /// <summary>Re-frame instantly after a scene reset or respawn.</summary>
        public void SnapCamera()
        {
            if (ThirdPerson) thirdPersonDistance = -1f;
            if (!Overhead) return;
            UpdateFocus();
            overhead.Snap();
        }

        /// <summary>Step to the next view: first person, near third person, far third person, then first person again.</summary>
        public void CycleView() => SetView(View == CameraView.ThirdPersonFar ? CameraView.FirstPerson : (CameraView)((int)View + 1));

        public void SetView(CameraView view)
        {
            if (overhead == null) return;
            bool wasOverhead = Overhead;
            View = view;
            overhead.enabled = Overhead;
            if (Overhead) overhead.Snap();
            else
            {
                cam.orthographic = false;
                cam.fieldOfView = ThirdPerson ? thirdPersonFieldOfView : 70f;
                cam.nearClipPlane = ThirdPerson ? 0.2f : 0.05f;
                cam.farClipPlane = 600f;
                if (wasOverhead)
                {
                    // Face the way the body faces, so the view does not jump.
                    yaw = transform.eulerAngles.y;
                    pitch = ThirdPerson ? ThirdPersonPitch : 0f;
                }
                thirdPersonDistance = -1f;
            }
            if (avatar != null) avatar.SetActive(!FirstPerson && Current == null);
            SetCursor(!Overhead);
        }

        // ------------------------------------------------------------------ third person

        const float ThirdPersonPitch = 15f;
        float thirdPersonDistance = -1f;   // current, pulled in by obstacles; < 0 = snap to the wanted distance
        readonly RaycastHit[] cameraHits = new RaycastHit[16];

        /// <summary>Orbit the camera behind the character, or behind the seat while operating a machine.</summary>
        void PlaceThirdPersonCamera()
        {
            float wanted = View == CameraView.ThirdPersonFar ? thirdPersonFar : thirdPersonNear;
            Vector3 pivot;
            if (Current != null)
            {
                wanted *= rigDistanceScale;
                pivot = Current.SeatPosition + Vector3.up * 1.2f;
            }
            else pivot = transform.position + Vector3.up * 1.6f;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rot * Vector3.back;
            // Pull in in front of walls and the ground, but not for the machine you are in, the character or loose debris.
            float d = wanted;
            const float radius = 0.25f;
            int n = Physics.SphereCastNonAlloc(pivot, radius, back, cameraHits, wanted, ~0, QueryTriggerInteraction.Ignore);
            var rigRoot = (Current as Component)?.transform;
            for (int i = 0; i < n; i++)
            {
                var h = cameraHits[i];
                var c = h.collider;
                if (c == cc || h.distance <= 0f) continue;
                if (rigRoot != null && c.transform.IsChildOf(rigRoot)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                d = Mathf.Min(d, Mathf.Max(0.3f, h.distance));
            }
            // Snap in at once so walls never block the view; ease back out.
            thirdPersonDistance = thirdPersonDistance < 0f || d < thirdPersonDistance ? d : Mathf.MoveTowards(thirdPersonDistance, d, 12f * Time.deltaTime);
            cam.transform.SetPositionAndRotation(pivot + back * thirdPersonDistance, rot);
        }

        void UpdateFocus()
        {
            if (cameraFocus == null) return;
            cameraFocus.position = Current != null ? Current.CameraFocus : transform.position;
        }

        void SetCursor(bool locked)
        {
            cursorLocked = locked;
            // A gamepad-only player (split screen) must not grab the mouse that belongs to the keyboard player.
            if (devices != null && devices.Length > 0 && !System.Array.Exists(devices, d => d is Mouse)) return;
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
                    if (!(mb is IOperableRig r) || mb == null || !mb.isActiveAndEnabled || OccupiedByOther(r)) continue;
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
            if (PauseMenu.IsPaused) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            Input.PollDevice();

            if (allowViewToggle && overhead != null && Input.ToggleView.WasPressedThisFrame()) CycleView();

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
            if (Input.RespawnPlayer.WasPressedThisFrame())
            {
                RespawnPlayer();
                return;
            }
            if (Input.RespawnVehicle.WasPressedThisFrame()) RespawnVehicle();

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
            // Third person stops short of looking up from under the ground.
            pitch = Mathf.Clamp(pitch - dy, ThirdPerson ? -30f : -80f, ThirdPerson ? 75f : 80f);
        }

        void Walk()
        {
            Vector2 move = Vector2.ClampMagnitude(Input.Move.ReadValue<Vector2>(), 1f);
            bool run = Input.Run.IsPressed();

            Vector3 v;
            if (Overhead || ThirdPerson)
            {
                // Screen-relative: up = toward the top of the screen, right = toward the right. Facing follows motion
                // and never feeds back into movement or the camera.
                Vector3 up, right;
                if (Overhead) overhead.GroundAxes(out up, out right);
                else
                {
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                    up = q * Vector3.forward;
                    right = q * Vector3.right;
                }
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
            float fall = vy;
            cc.Move(v * Time.deltaTime);
            Footsteps(new Vector3(v.x, 0f, v.z).magnitude, run, fall);

            if (FirstPersonView) cam.transform.SetPositionAndRotation(transform.position + Vector3.up * 1.62f, Quaternion.Euler(pitch, yaw, 0f));
        }

        float strideLeft;
        bool wasGrounded = true;

        /// <summary>A gravel step every stride while moving on the ground, and a heavier one on landing.</summary>
        void Footsteps(float speed, bool run, float fallSpeed)
        {
            bool grounded = cc.isGrounded;
            Vector3 feet = transform.position + Vector3.up * 0.1f;
            if (grounded && !wasGrounded && fallSpeed < -4f)
            {
                Sfx.PlayAt("footstep_gravel", feet, 0.7f, 0.85f, 0.1f, 1.5f, 30f);
                strideLeft = 0f;
            }
            else if (grounded && speed > 0.3f)
            {
                strideLeft -= speed * Time.deltaTime;
                if (strideLeft <= 0f)
                {
                    Sfx.PlayAt("footstep_gravel", feet, run ? 0.55f : 0.4f, 1f, 0.12f, 1.5f, 25f);
                    strideLeft = run ? 2f : 1.5f;
                }
            }
            else strideLeft = 0.4f; // the first step comes soon after starting to move
            wasGrounded = grounded;
        }

        void LateUpdate()
        {
            if (Current != null)
            {
                transform.position = Current.SeatPosition - Vector3.up * 1.62f;
                if (!Overhead)
                {
                    yaw = Current.SeatRotation.eulerAngles.y + relYaw;
                    if (FirstPersonView) cam.transform.SetPositionAndRotation(Current.SeatPosition, Quaternion.Euler(pitch, yaw, 0f));
                }
            }
            if (ThirdPerson && !PauseMenu.IsPaused) PlaceThirdPersonCamera();
            UpdateFocus();
            ApplyShake();
        }

        // ------------------------------------------------------------------ impact feel

        float shake, hitStopUntil;
        Haptics haptics;

        void OnBallHit(float speed, Vector3 point)
        {
            // Felt more the closer you are; inside the crane cab you are right next to it.
            bool inCrane = Current is CraneOperable;
            float near = inCrane ? 1f : Mathf.Clamp01(1f - Vector3.Distance(transform.position, point) / 30f);
            if (haptics != null) haptics.Hit(Mathf.Clamp01(speed / 9f) * near * 0.8f);
            // Camera shake is a first-person effect; the overhead view stays steady.
            if (!Overhead && cameraShake) shake = Mathf.Max(shake, Mathf.Clamp01(speed / 9f) * near);
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
            All.Remove(this);
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
            pitch = ThirdPerson ? ThirdPersonPitch : 0f;
            thirdPersonDistance = -1f;
            rig.OnEnter();
            CabGlass.SetOperatorInside(rig as Component, true);
            if (rig is Component c) MachineAudio.For(c).SetEngine(true);
            Input.SetContext(rig.ControlMap(Input));
            if (Overhead) overhead.Snap();
        }

        void Leave()
        {
            var rig = Current;
            rig.OnExit();
            CabGlass.SetOperatorInside(rig as Component, false);
            if (rig is Component c) MachineAudio.For(c).SetEngine(false);
            Current = null;
            transitionFrame = Time.frameCount;
            Input.SetContext(null);
            PlaceAt(SafeExit(rig));
            thirdPersonDistance = -1f;
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
                if (Current is Component c) MachineAudio.For(c).SetEngine(false);
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
            text += $"\nRespawn vehicle {Input.Keys(Input.RespawnVehicle, pad)}    Respawn player {Input.Keys(Input.RespawnPlayer, pad)}";
            text += pad ? "    Pause Start" : $"    Reset all {Input.Keys(Input.Reset, pad)}" + (Overhead ? "" : "    Mouse unlock Esc");
            if (overhead != null && allowViewToggle) text += $"    View {Input.Keys(Input.ToggleView, pad)} ({ViewName(View)})";

            var view = GuiRect;
            GUI.BeginGroup(view);
            var r = new Rect(16f, 12f, Mathf.Min(900f, view.width - 32f), 260f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
            if (FirstPersonView) GUI.Label(new Rect(view.width * 0.5f - 4f, view.height * 0.5f - 4f, 8f, 8f), "·", style);
            GUI.EndGroup();
        }

        static string ViewName(CameraView v) =>
            v == CameraView.FirstPerson ? "first person" : v == CameraView.ThirdPersonNear ? "third person" : v == CameraView.ThirdPersonFar ? "third person, far" : "overhead";

        static string Label(IOperableRig r) =>
            r is CraneOperable ? "crane" : r is LoaderRig || r is DozerRig ? r.RigName.ToLowerInvariant() : $"{r.RigName.ToLowerInvariant()} ({r.AttachmentName.ToLowerInvariant()})";
    }
}
