using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    public enum ExcavatorAttachment { Crusher, Shear, Breaker, Grapple }

    /// <summary>
    /// Turns the imported Excavator FBX (shared base + four attachments) into a drivable demolition machine with one
    /// attachment fitted. Tracks drive the kinematic root in FixedUpdate (interpolated); the upper body, boom, stick
    /// and wrist are hinged nodes posed every frame with momentum, like the crane. Attachments act on the destruction
    /// world through its queued tool API (Damage / Sever) and grab loose debris with a joint, with contact checks and
    /// fixed intervals so nothing is damaged every frame.
    ///
    /// Approximations: jaws stop at a fixed contact closure while something valid is in the bite (no per-shape
    /// contact); the shear frees the steel member segment in its throat by severing that segment's joints rather
    /// than cutting a mesh; the machine is kinematic, so it drives through static structure like the crane does.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ExcavatorRig : MonoBehaviour, IOperableRig
    {
        public ExcavatorAttachment attachment;
        public DestructionWorld world;
        [Tooltip("Material index the shear can cut (steel is added to the runtime settings by CraneTest).")]
        public int steelMaterial = -1;

        [Header("Tracks")]
        public float driveSpeed = 2.2f;     // m/s
        public float driveAccel = 2.5f;     // m/s²
        public float turnSpeed = 32f;       // deg/s
        public float turnAccel = 70f;       // deg/s²

        [Header("Articulation speed (deg/s)")]
        public float swingSpeed = 40f;
        public float boomSpeed = 22f;
        public float stickSpeed = 30f;
        public float curlSpeed = 60f;

        [Header("Articulation acceleration (deg/s²)")]
        public float swingAccel = 110f;
        public float boomAccel = 80f;
        public float stickAccel = 100f;
        public float curlAccel = 200f;

        [Header("Travel limits, degrees from the rest pose")]
        public float boomMin = -28f;
        public float boomMax = 28f;
        public float stickMin = -40f;
        public float stickMax = 50f;
        public float curlMin = -90f;
        public float curlMax = 90f;
        [Tooltip("Lowest height of the jaw/grip centre above the ground (the breaker tip may touch it).")]
        public float toolGroundClearance = 0.3f;

        [Tooltip("Boom angle at spawn and after reset, from the model's rest pose. Raised so tools start clear of their targets.")]
        public float startBoomAngle = 18f;

        [Header("Jaws")]
        [Tooltip("Jaw rotation from fully open to fully closed (deg). Set from the model handoff.")]
        public float jawCloseAngle = 30f;
        [Tooltip("Closure per second at full input (0 = open, 1 = closed). Triggers scale it.")]
        public float jawSpeed = 1.3f;

        [Header("Crusher")]
        [Tooltip("Closure at which the jaws meet concrete in the bite and stop.")]
        [Range(0.2f, 0.95f)] public float crushContactClosure = 0.7f;
        public float crushInterval = 0.45f;    // s of squeezing per bite
        public float crushDamage = 0.34f;      // direct damage per bite
        public Vector3 biteHalfExtents = new Vector3(0.45f, 0.55f, 0.6f);

        [Header("Shear")]
        [Range(0.2f, 0.95f)] public float cutContactClosure = 0.65f;
        public float cutTime = 0.7f;           // s of squeezing to part a steel member

        [Header("Breaker")]
        [Tooltip("Primary input above this runs the hammer (trigger threshold on gamepad).")]
        [Range(0.05f, 0.95f)] public float breakerThreshold = 0.35f;
        public float hammerRate = 5f;          // strikes per second
        public float breakerDamage = 0.12f;    // direct damage per strike on contact
        public float bitStroke = 0.25f;        // m
        public float strikeRadius = 0.3f;      // m around the bit tip

        [Header("Grapple")]
        public float maxGrabMass = 2500f;      // kg
        public float maxGrabSize = 2.4f;       // m, largest bounds extent
        [Range(0.2f, 0.95f)] public float gripClosure = 0.55f;
        [Range(0.05f, 0.9f)] public float releaseClosure = 0.4f;
        public Vector3 gripHalfExtents = new Vector3(0.7f, 0.6f, 0.7f);

        // ------------------------------------------------------------------ state (read by tests and the HUD)

        public float SwingAngle => swing.angle;
        public float BoomAngle => boom.angle;
        public float StickAngle => stick.angle;
        public float CurlAngle => curl.angle;
        /// <summary>0 = jaws open, 1 = closed.</summary>
        public float Closure { get; private set; }
        public float BitOffset { get; private set; }
        public bool Hammering { get; private set; }
        public Rigidbody HeldBody { get; private set; }
        public bool Holding => HeldBody != null;
        public int Bites { get; private set; }
        public int Cuts { get; private set; }
        public int Strikes { get; private set; }
        public int Hits { get; private set; }

        public Transform Chassis { get; private set; }
        public Transform Upper { get; private set; }
        public Transform Wrist { get; private set; }
        public Transform Attachment { get; private set; }
        /// <summary>Working point of the attachment (bite centre, cutting throat, bit tip or grip centre).</summary>
        public Transform WorkPoint { get; private set; }

        public static string AttachmentLabel(ExcavatorAttachment a)
        {
            switch (a)
            {
                case ExcavatorAttachment.Crusher: return "Concrete crusher";
                case ExcavatorAttachment.Shear: return "Steel shear";
                case ExcavatorAttachment.Breaker: return "Hydraulic breaker";
                default: return "Sorting grapple";
            }
        }

        public string RigName => "Excavator";
        public string AttachmentName => AttachmentLabel(attachment);
        public Vector3 DoorPosition => door != null ? door.position : Upper.TransformPoint(doorFallback);
        public Vector3 SeatPosition => seat != null ? seat.position : Upper.position + Vector3.up * 1.8f;
        public Quaternion SeatRotation => Quaternion.LookRotation(Vector3.ProjectOnPlane(Upper.rotation * upperForwardLocal, Vector3.up), Vector3.up);

        /// <summary>The slew axis on the ground: the machine's working area is a circle around it. Turning the upper
        /// body does not move it, so the camera never follows the swing or the tool.</summary>
        public Vector3 CameraFocus
        {
            get
            {
                Vector3 p = Upper.position + transform.forward * cameraLookAhead;
                return new Vector3(p.x, transform.position.y, p.z);
            }
        }

        [Tooltip("While operating, the camera centres this far ahead of the slew axis along the tracks (not the upper body), framing the usual work area.")]
        public float cameraLookAhead = 3f;

        // ------------------------------------------------------------------ internals

        sealed class Hinge
        {
            public Transform node;
            public Quaternion rest;
            public Vector3 axis;     // in the parent's space
            public float sign = 1f;  // + input direction → + rotation sign
            public float angle, vel;

            public void Apply() => node.localRotation = Quaternion.AngleAxis(angle * sign, axis) * rest;
        }

        readonly Hinge swing = new Hinge(), boom = new Hinge(), stick = new Hinge(), curl = new Hinge();
        readonly List<Hinge> jaws = new List<Hinge>();
        Transform bit;
        Vector3 bitRest, bitDirLocal;
        Transform seat, door;
        Vector3 doorFallback, upperForwardLocal, upperRightLocal;
        Rigidbody body;
        Rigidbody gripBody;
        readonly List<Collider> ownColliders = new List<Collider>();

        Vector3 startPos;
        Quaternion startRot;

        float inDrive, inTurn, inSwing, inBoom, inStick, inCurl, inClose, inOpen;
        float driveVel, turnVel;
        float squeeze, hammerPhase;
        bool toolLatched; // after entering, ignore the tool buttons until they are released
        ConfigurableJoint grip;
        bool gripping;
        readonly List<Collider> heldColliders = new List<Collider>();
        readonly List<(Collider a, Collider b, float until)> pendingRestore = new List<(Collider, Collider, float)>();
        static readonly Collider[] overlap = new Collider[64];

        // ------------------------------------------------------------------ build

        static string RootName(ExcavatorAttachment a) => "Att_" + a;

        /// <summary>Builds the rig from an instantiated Excavator FBX holding the base and every attachment.</summary>
        public void Build(GameObject fbxInstance, ExcavatorAttachment kind)
        {
            attachment = kind;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var model = fbxInstance.transform;
            model.SetParent(transform, false);
            Chassis = Find(model, "Exc_Chassis");
            Upper = Find(model, "Exc_Upper");
            var boomNode = Find(model, "Exc_Boom");
            var stickNode = Find(model, "Exc_Stick");
            Wrist = Find(model, "Exc_Wrist");
            seat = FindOptional(model, "Anchor_Seat");
            door = FindOptional(model, "Anchor_Door");

            // Face the wrapper's +Z whatever frame the FBX arrives in: the stick hangs ahead of the machine at rest.
            Vector3 fwd = Vector3.ProjectOnPlane(Wrist.position - Upper.position, Vector3.up);
            float yaw = Vector3.SignedAngle(fwd, transform.forward, Vector3.up);
            model.RotateAround(transform.position, Vector3.up, yaw);
            upperForwardLocal = Quaternion.Inverse(Upper.rotation) * transform.forward;
            upperRightLocal = Quaternion.Inverse(Upper.rotation) * transform.right;
            doorFallback = Upper.InverseTransformPoint(transform.position - transform.right * 2.2f);

            // Fit the chosen attachment to the wrist; drop the others. Both are authored with identity world rotation.
            foreach (ExcavatorAttachment a in Enum.GetValues(typeof(ExcavatorAttachment)))
            {
                var root = Find(model, RootName(a));
                if (a == kind) Attachment = root;
                else
                {
                    root.gameObject.SetActive(false);
                    Destroy(root.gameObject);
                }
            }
            Attachment.SetPositionAndRotation(Wrist.position, Wrist.rotation);
            Attachment.SetParent(Wrist, true);

            Vector3 lateral = transform.right;
            Setup(swing, Upper, Vector3.up);
            Setup(boom, boomNode, lateral);
            Setup(stick, stickNode, lateral);
            Setup(curl, Wrist, lateral);
            AddRam(model, "Exc_BoomRam_L", "Exc_BoomRamRod_L");
            AddRam(model, "Exc_BoomRam_R", "Exc_BoomRamRod_R");
            AddRam(model, "Exc_StickRam", "Exc_StickRamRod");

            string prefix = RootName(kind) + "_";
            switch (kind)
            {
                case ExcavatorAttachment.Crusher:
                    jaws.Add(SetupJaw(Find(model, prefix + "JawA")));
                    jaws.Add(SetupJaw(Find(model, prefix + "JawB")));
                    WorkPoint = Find(model, "Anchor_Crusher_Bite");
                    break;
                case ExcavatorAttachment.Shear:
                    jaws.Add(SetupJaw(Find(model, prefix + "Blade")));
                    WorkPoint = Find(model, "Anchor_Shear_Cut");
                    break;
                case ExcavatorAttachment.Breaker:
                    bit = Find(model, prefix + "Bit");
                    WorkPoint = Find(model, "Anchor_Breaker_Tip");
                    bitRest = bit.localPosition;
                    Vector3 slide = (WorkPoint.position - Attachment.position).normalized;
                    bitDirLocal = bit.parent.InverseTransformVector(slide); // per metre, scale-aware
                    break;
                case ExcavatorAttachment.Grapple:
                    jaws.Add(SetupJaw(Find(model, prefix + "ClawA")));
                    jaws.Add(SetupJaw(Find(model, prefix + "ClawB")));
                    WorkPoint = Find(model, "Anchor_Grapple_Grip");
                    break;
            }

            // Input signs from the geometry, so they hold whatever axis conventions the import uses.
            Func<float> wristHeight = () => Wrist.position.y;
            Func<float> wristReach = () => Reach(Wrist.position);
            Func<float> toolReach = () => -Reach(WorkPoint.position);
            boom.sign = Probe(boom, wristHeight);        // + raises
            stick.sign = Probe(stick, wristReach);       // + extends
            curl.sign = Probe(curl, toolReach);          // + curls in (tool toward the machine)
            swing.sign = ProbeSwing();                   // + turns right (clockwise from above)
            if (jaws.Count == 2)
            {
                Func<float> gap = () => -Vector3.Distance(Centre(jaws[0].node), Centre(jaws[1].node));
                foreach (var j in jaws) j.sign = Probe(j, gap);
            }
            else if (jaws.Count == 1)
            {
                Func<float> gap = () => -Vector3.Distance(Centre(jaws[0].node), WorkPoint.position);
                jaws[0].sign = Probe(jaws[0], gap);
            }

            AddColliders(model);

            var gripGo = new GameObject(name + " Grip Anchor");
            gripGo.transform.SetPositionAndRotation(WorkPoint.position, WorkPoint.rotation);
            gripBody = gripGo.AddComponent<Rigidbody>();
            gripBody.isKinematic = true;
            gripBody.useGravity = false;
            gripBody.interpolation = RigidbodyInterpolation.Interpolate;

            startPos = transform.position;
            startRot = transform.rotation;
            SetPose(0f, startBoomAngle, 0f, 0f);
        }

        /// <summary>Jump straight to a pose (degrees from the model's rest pose, clamped to the limits) with no motion.</summary>
        public void SetPose(float swingDeg, float boomDeg, float stickDeg, float curlDeg)
        {
            swing.angle = swingDeg;
            boom.angle = Mathf.Clamp(boomDeg, boomMin, boomMax);
            stick.angle = Mathf.Clamp(stickDeg, stickMin, stickMax);
            curl.angle = Mathf.Clamp(curlDeg, curlMin, curlMax);
            swing.vel = boom.vel = stick.vel = curl.vel = 0f;
            ApplyPose();
            if (gripBody != null)
            {
                gripBody.position = WorkPoint.position;
                gripBody.rotation = WorkPoint.rotation;
            }
        }

        static Transform FindOptional(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Transform Find(Transform root, string name) =>
            FindOptional(root, name) ?? throw new InvalidOperationException($"Excavator FBX is missing '{name}'.");

        static void Setup(Hinge h, Transform node, Vector3 worldAxis)
        {
            h.node = node;
            h.rest = node.localRotation;
            h.axis = node.parent.InverseTransformDirection(worldAxis).normalized;
            h.angle = h.vel = 0f;
        }

        Hinge SetupJaw(Transform node)
        {
            var h = new Hinge();
            Setup(h, node, transform.right);
            return h;
        }

        float Reach(Vector3 p) => Vector3.ProjectOnPlane(p - Upper.position, Vector3.up).magnitude;

        static Vector3 Centre(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            return r != null ? r.bounds.center : t.position;
        }

        /// <summary>Which rotation sign increases the metric.</summary>
        static float Probe(Hinge h, Func<float> metric)
        {
            h.angle = 0f;
            h.sign = 1f;
            h.Apply();
            float m0 = metric();
            h.angle = 2f;
            h.Apply();
            float m1 = metric();
            h.angle = 0f;
            h.Apply();
            return m1 > m0 ? 1f : -1f;
        }

        float ProbeSwing()
        {
            Vector3 f0 = Vector3.ProjectOnPlane(Wrist.position - Upper.position, Vector3.up);
            swing.sign = 1f;
            swing.angle = 5f;
            swing.Apply();
            Vector3 f1 = Vector3.ProjectOnPlane(Wrist.position - Upper.position, Vector3.up);
            swing.angle = 0f;
            swing.Apply();
            return Vector3.SignedAngle(f0, f1, Vector3.up) > 0f ? 1f : -1f; // Unity: + about up is clockwise from above
        }

        /// <summary>
        /// Simple, stable shapes for moving machinery: boxes from each part's mesh bounds, convex hulls for the boom,
        /// stick and the moving jaws (so the gap between open jaws stays open). The attachment housing's box stops
        /// above the bite so the throat stays clear. Rams are visual only. Must run at the rest pose (attachment
        /// hanging straight down).
        /// </summary>
        void AddColliders(Transform model)
        {
            var hulls = new HashSet<Transform>();
            foreach (var j in jaws) hulls.Add(j.node);
            hulls.Add(boom.node);
            hulls.Add(stick.node);
            Transform housing = attachment == ExcavatorAttachment.Breaker ? null : FindOptional(Attachment, RootName(attachment) + "_Body");
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
                if (mf.name.Contains("Ram")) continue;
                Collider col;
                if (hulls.Contains(mf.transform))
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                    col = mc;
                }
                else
                {
                    var b = mf.sharedMesh.bounds;
                    var box = mf.gameObject.AddComponent<BoxCollider>();
                    if (mf.transform == housing) b = ClipBelow(mf.transform, b, WorkPoint.position.y + 0.3f);
                    box.center = b.center;
                    box.size = b.size;
                    col = box;
                }
                ownColliders.Add(col);
                Collision.own.Add(col);
                if (mf.transform.IsChildOf(Upper)) upperColliders.Add(col);
                if (mf.transform.IsChildOf(boom.node)) boomColliders.Add(col);
                if (mf.transform.IsChildOf(stick.node)) stickColliders.Add(col);
                if (mf.transform.IsChildOf(Wrist)) wristColliders.Add(col);
                if (hulls.Contains(mf.transform) && mf.transform != boom.node && mf.transform != stick.node) jawColliders.Add(col);
            }
        }

        /// <summary>Local mesh bounds with everything below a world height cut away (node is axis-aligned at rest).</summary>
        static Bounds ClipBelow(Transform node, Bounds local, float worldY)
        {
            var world = new Bounds(node.TransformPoint(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                world.Encapsulate(node.TransformPoint(c));
            }
            if (world.min.y >= worldY || world.max.y <= worldY) return local;
            world.SetMinMax(new Vector3(world.min.x, worldY, world.min.z), world.max);
            Vector3 size = node.InverseTransformVector(world.size);
            return new Bounds(node.InverseTransformPoint(world.center), new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }

        // ------------------------------------------------------------------ collision with the world

        /// <summary>Collision test against the world (solid things block, light debris is pushed). Add the ground to
        /// <see cref="RigCollision.ignore"/>: the tracks sit on it and the tool has its own floor.</summary>
        public readonly RigCollision Collision = new RigCollision();

        readonly List<Collider> upperColliders = new List<Collider>(), boomColliders = new List<Collider>(),
            stickColliders = new List<Collider>(), wristColliders = new List<Collider>(), jawColliders = new List<Collider>();

        float Penetration(List<Collider> mine) => Collision.Penetration(mine);

        /// <summary>Count of refused moves, for tests and the HUD.</summary>
        public int Blocked { get; private set; }

        /// <summary>
        /// Advance one hinge, then undo the step if it would drive the moving parts further into something solid or
        /// push the tool below the ground. Moves that keep or reduce existing overlap are allowed, so the machine can
        /// always back out.
        /// </summary>
        void StepChecked(Hinge h, List<Collider> moving, float input, float speed, float accel, float min, float max, float dt)
        {
            float before = h.angle;
            Step(h, input, speed, accel, min, max, dt);
            if (h.angle == before) return;
            float after = h.angle;
            h.angle = before;
            ApplyPose();
            Physics.SyncTransforms();
            float pen0 = Penetration(moving);
            float y0 = WorkPoint.position.y;
            h.angle = after;
            ApplyPose();
            Physics.SyncTransforms();
            float floor = transform.position.y + (attachment == ExcavatorAttachment.Breaker ? -0.05f : toolGroundClearance);
            float y1 = WorkPoint.position.y;
            bool intoGround = y1 < floor && y1 < y0 - 1e-5f;
            if (intoGround || Penetration(moving) > pen0 + Collision.tolerance)
            {
                h.angle = before;
                h.vel = 0f;
                ApplyPose();
                Physics.SyncTransforms();
                Blocked++;
            }
        }

        /// <summary>Would moving the whole machine to this pose drive it further into something solid?</summary>
        bool DriveBlocked(Vector3 pos, Quaternion rot) => Collision.MoveBlocked(transform, body, ownColliders, pos, rot);

        // ------------------------------------------------------------------ controls

        /// <summary>
        /// Latch this frame's requests, each -1..1 (close/open 0..1): drive (+ forward), turn (+ right), swing (+ right),
        /// boom (+ up), stick (+ out), curl (+ in), close / open the jaws or run the breaker.
        /// </summary>
        public void Command(float drive, float turn, float swingIn, float boomIn, float stickIn, float curlIn, float close, float open)
        {
            inDrive = drive;
            inTurn = turn;
            inSwing = swingIn;
            inBoom = boomIn;
            inStick = stickIn;
            inCurl = curlIn;
            inClose = close;
            inOpen = open;
        }

        public InputActionMap ControlMap(CraneTestInput input) => input.excavator;

        public void OnEnter()
        {
            toolLatched = true;
            StopPowered();
        }

        public void OnExit() => StopPowered();

        /// <summary>Kill every powered motion now; the pose, jaw closure and any grip are kept.</summary>
        void StopPowered()
        {
            Command(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            driveVel = turnVel = 0f;
            swing.vel = boom.vel = stick.vel = curl.vel = 0f;
            squeeze = 0f;
            Hammering = false;
        }

        public void Operate(CraneTestInput input)
        {
            float close = input.Close.ReadValue<float>();
            float open = input.Open.ReadValue<float>();
            if (toolLatched)
            {
                if (close < 0.05f && open < 0.05f) toolLatched = false;
                close = open = 0f;
            }
            Command(input.Drive.ReadValue<float>(), input.Turn.ReadValue<float>(), input.Swing.ReadValue<float>(),
                input.Boom.ReadValue<float>(), input.Stick.ReadValue<float>(), input.Curl.ReadValue<float>(), close, open);
        }

        public void ExitCandidates(List<Vector3> into)
        {
            into.Add(DoorPosition);
            var t = transform;
            into.Add(t.position - t.right * 2.6f);
            into.Add(t.position + t.right * 2.6f);
            into.Add(t.position - t.forward * 3.6f);
            into.Add(t.position - t.right * 2.6f - t.forward * 2.5f);
            into.Add(t.position + t.right * 2.6f - t.forward * 2.5f);
        }

        public void ControlHints(CraneTestInput input, List<ControlHint> into)
        {
            bool pad = input.UsingGamepad;
            into.Add(ControlHint.Row(input.Keys(input.Drive), "Drive forward / back"));
            into.Add(ControlHint.Row(input.Keys(input.Turn, pad, negativeFirst: true), "Turn tracks left / right"));
            if (pad)
            {
                bool mod = input.Modifier.IsPressed();
                string lb = input.Keys(input.Modifier, true);
                into.Add(ControlHint.Row(input.Keys(input.Swing, true), "Swing body", dim: mod));
                into.Add(ControlHint.Row(input.Keys(input.Boom, true), "Boom up / down", dim: mod));
                into.Add(ControlHint.Row($"{lb} + {input.Keys(input.Stick, true)}", "Stick out / in", highlight: mod));
                into.Add(ControlHint.Row($"{lb} + {input.Keys(input.Curl, true)}", "Curl in / out", highlight: mod));
                into.Add(ControlHint.Note(mod ? $"{lb} held: right stick moves stick + curl only"
                                              : $"Hold {lb}: right stick moves stick + curl", mod));
            }
            else
            {
                into.Add(ControlHint.Row(input.Keys(input.Swing, false, negativeFirst: true), "Swing body left / right"));
                into.Add(ControlHint.Row(input.Keys(input.Boom, false), "Boom up / down"));
                into.Add(ControlHint.Row(input.Keys(input.Stick, false), "Stick out / in"));
                into.Add(ControlHint.Row(input.Keys(input.Curl, false), "Curl in / out"));
            }
            string close = input.Keys(input.Close), open = input.Keys(input.Open);
            switch (attachment)
            {
                case ExcavatorAttachment.Breaker:
                    into.Add(ControlHint.Row(close + " hold", "Run breaker", highlight: Hammering));
                    break;
                case ExcavatorAttachment.Grapple:
                    into.Add(ControlHint.Row(close + " hold", "Close claws (grab)"));
                    into.Add(ControlHint.Row(open + " hold", "Open claws (release)"));
                    break;
                case ExcavatorAttachment.Shear:
                    into.Add(ControlHint.Row(close + " hold", "Close blades (cut steel)"));
                    into.Add(ControlHint.Row(open + " hold", "Open blades"));
                    break;
                default:
                    into.Add(ControlHint.Row(close + " hold", "Close jaws (crush concrete)"));
                    into.Add(ControlHint.Row(open + " hold", "Open jaws"));
                    break;
            }
        }

        public string Telemetry
        {
            get
            {
                string tool;
                switch (attachment)
                {
                    case ExcavatorAttachment.Breaker: tool = Hammering ? $"hammering  hits {Hits}" : $"idle  hits {Hits}"; break;
                    case ExcavatorAttachment.Grapple:
                        tool = Holding ? $"holding {HeldBody.mass:0} kg" : $"claws {Closure * 100f:0}%";
                        break;
                    case ExcavatorAttachment.Shear: tool = $"blades {Closure * 100f:0}%  cuts {Cuts}"; break;
                    default: tool = $"jaws {Closure * 100f:0}%  bites {Bites}"; break;
                }
                return $"Boom {boom.angle:+0;-0}°  Stick {stick.angle:+0;-0}°  Curl {curl.angle:+0;-0}°\n{tool}";
            }
        }

        // ------------------------------------------------------------------ simulation

        static float Approach(float v, float target, float accel, float dt)
        {
            bool braking = Mathf.Abs(target) < Mathf.Abs(v) || v * target < 0f;
            return Mathf.MoveTowards(v, target, accel * (braking ? 2f : 1f) * dt);
        }

        static void Step(Hinge h, float input, float speed, float accel, float min, float max, float dt)
        {
            h.vel = Approach(h.vel, Mathf.Clamp(input, -1f, 1f) * speed, accel, dt);
            if (h.vel == 0f) return;
            float next = h.angle + h.vel * dt;
            if (next <= min || next >= max)
            {
                next = Mathf.Clamp(next, min, max);
                h.vel = 0f;
            }
            h.angle = next;
        }

        void FixedUpdate()
        {
            if (body == null) return;
            float dt = Time.fixedDeltaTime;
            driveVel = Approach(driveVel, Mathf.Clamp(inDrive, -1f, 1f) * driveSpeed, driveAccel, dt);
            turnVel = Approach(turnVel, Mathf.Clamp(inTurn, -1f, 1f) * turnSpeed, turnAccel, dt);
            if (driveVel != 0f || turnVel != 0f)
            {
                Quaternion rot = Quaternion.AngleAxis(turnVel * dt, Vector3.up) * body.rotation;
                Vector3 pos = body.position + rot * Vector3.forward * (driveVel * dt);
                if (DriveBlocked(pos, rot))
                {
                    // Tracks stall against walls, structure and other machines.
                    driveVel = turnVel = 0f;
                    Blocked++;
                }
                else
                {
                    body.MoveRotation(rot);
                    body.MovePosition(pos);
                }
            }
            gripBody.MovePosition(WorkPoint.position);
            gripBody.MoveRotation(WorkPoint.rotation);
        }

        void LateUpdate()
        {
            if (body == null) return;
            float dt = Time.deltaTime;
            // Each axis moves only if it does not drive the machine into something solid or the tool into the ground.
            StepChecked(swing, upperColliders, inSwing, swingSpeed, swingAccel, float.NegativeInfinity, float.PositiveInfinity, dt);
            StepChecked(boom, boomColliders, inBoom, boomSpeed, boomAccel, boomMin, boomMax, dt);
            StepChecked(stick, stickColliders, inStick, stickSpeed, stickAccel, stickMin, stickMax, dt);
            StepChecked(curl, wristColliders, inCurl, curlSpeed, curlAccel, curlMin, curlMax, dt);
            ApplyPose();
            Tool(dt);
            RestoreCollisions();
        }

        void ApplyPose()
        {
            swing.Apply();
            boom.Apply();
            stick.Apply();
            curl.Apply();
            foreach (var j in jaws)
            {
                j.angle = Closure * jawCloseAngle;
                j.Apply();
            }
            if (bit != null) bit.localPosition = bitRest + bitDirLocal * BitOffset;
            // Each ram half has its origin at its own pin and points at the other pin: re-aim both after posing.
            foreach (var r in rams)
            {
                r.node.localRotation = r.rest;
                Vector3 want = r.other.position - r.node.position;
                if (want.sqrMagnitude > 1e-6f)
                    r.node.rotation = Quaternion.FromToRotation(r.node.TransformDirection(r.dirLocal), want) * r.node.rotation;
            }
        }

        struct Ram { public Transform node, other; public Quaternion rest; public Vector3 dirLocal; }
        readonly List<Ram> rams = new List<Ram>();

        void AddRam(Transform model, string barrel, string rod)
        {
            var a = FindOptional(model, barrel);
            var b = FindOptional(model, rod);
            if (a == null || b == null) return;
            // Pin positions are the origins; capture them before either half moves.
            rams.Add(new Ram { node = a, other = b, rest = a.localRotation, dirLocal = a.InverseTransformDirection(b.position - a.position) });
            rams.Add(new Ram { node = b, other = a, rest = b.localRotation, dirLocal = b.InverseTransformDirection(a.position - b.position) });
        }

        // ------------------------------------------------------------------ attachments

        void Tool(float dt)
        {
            float close = Mathf.Clamp01(inClose), open = Mathf.Clamp01(inOpen);
            if (attachment == ExcavatorAttachment.Breaker)
            {
                Breaker(close >= breakerThreshold, dt);
                return;
            }
            float cmd = close - open;
            switch (attachment)
            {
                case ExcavatorAttachment.Crusher: Crusher(cmd, dt); break;
                case ExcavatorAttachment.Shear: Shear(cmd, dt); break;
                case ExcavatorAttachment.Grapple: Grapple(cmd, dt); break;
            }
        }

        /// <summary>
        /// Move the jaws toward the command. Closing stops when the jaws press on something solid, or at the contact
        /// closure while something valid is in the bite (thin members the hulls might miss). Returns true when closing
        /// is stalled against the load, i.e. the jaws are squeezing.
        /// </summary>
        bool MoveJaws(float cmd, float limit, float dt)
        {
            float next = Mathf.Clamp01(Closure + cmd * jawSpeed * dt);
            if (cmd <= 0f)
            {
                Closure = next;
                ApplyPose();
                return false;
            }
            bool stalled = false;
            if (Closure <= limit && next >= limit)
            {
                next = limit;
                stalled = Closure >= limit - 1e-4f;
            }
            if (next > Closure && !JawStepAllowed(next)) return true;
            Closure = next;
            ApplyPose();
            return stalled;
        }

        bool JawStepAllowed(float next)
        {
            Physics.SyncTransforms();
            float pen0 = Penetration(jawColliders);
            float prev = Closure;
            Closure = next;
            ApplyPose();
            Physics.SyncTransforms();
            bool ok = Penetration(jawColliders) <= pen0 + Collision.tolerance;
            if (!ok)
            {
                Closure = prev;
                ApplyPose();
                Physics.SyncTransforms();
            }
            return ok;
        }

        void Crusher(float cmd, float dt)
        {
            int target = cmd > 0f ? FindPiece(WorkPoint, biteHalfExtents, CrushablePiece) : -1;
            bool stalled = MoveJaws(cmd, target >= 0 ? crushContactClosure : 1f, dt);
            if (target >= 0 && cmd > 0.1f && stalled)
            {
                squeeze += dt * Mathf.Clamp01(cmd);
                if (squeeze >= crushInterval)
                {
                    squeeze = 0f;
                    world.Damage(target, crushDamage);
                    Bites++;
                }
            }
            else squeeze = 0f;
        }

        void Shear(float cmd, float dt)
        {
            int target = cmd > 0f ? FindPiece(WorkPoint, biteHalfExtents, CuttablePiece) : -1;
            bool stalled = MoveJaws(cmd, target >= 0 ? cutContactClosure : 1f, dt);
            if (target >= 0 && cmd > 0.1f && stalled)
            {
                squeeze += dt * Mathf.Clamp01(cmd);
                if (squeeze >= cutTime)
                {
                    squeeze = 0f;
                    world.Sever(world.Graph.adjacency[target]);
                    Cuts++;
                }
            }
            else squeeze = 0f;
        }

        void Breaker(bool run, float dt)
        {
            Hammering = run;
            if (!run)
            {
                hammerPhase = 0f;
                BitOffset = Mathf.MoveTowards(BitOffset, 0f, bitStroke * 8f * dt);
                ApplyPose();
                return;
            }
            float before = hammerPhase;
            hammerPhase += hammerRate * dt;
            // Quick strike, slower return: the bit is fully out at each whole cycle.
            float f = hammerPhase - Mathf.Floor(hammerPhase);
            BitOffset = bitStroke * (f < 0.8f ? f / 0.8f : 1f - (f - 0.8f) / 0.2f);
            ApplyPose();
            if (Mathf.Floor(hammerPhase - 0.8f) > Mathf.Floor(before - 0.8f))
            {
                Strikes++;
                int target = FindPieceSphere(WorkPoint.position, strikeRadius, BreakablePiece);
                if (target >= 0)
                {
                    world.Damage(target, breakerDamage);
                    Hits++;
                }
            }
        }

        void Grapple(float cmd, float dt)
        {
            if (gripping && (HeldBody == null || grip == null)) // the world removed or replaced the held body (shatter)
            {
                if (grip != null) Destroy(grip);
                DropGrip();
            }
            if (HeldBody != null)
            {
                // Holding: the claws rest on the load. Only opening does anything, and past the release point lets go.
                if (cmd < 0f)
                {
                    Closure = Mathf.Max(0f, Closure + cmd * jawSpeed * dt);
                    ApplyPose();
                    if (Closure <= releaseClosure) Release();
                }
                return;
            }
            float next = Mathf.Clamp01(Closure + cmd * jawSpeed * dt);
            if (cmd > 0f && Closure <= gripClosure && next >= gripClosure)
            {
                var candidate = FindLoad();
                if (candidate != null)
                {
                    Closure = gripClosure;
                    ApplyPose();
                    Grab(candidate);
                    return;
                }
            }
            if (next > Closure && !JawStepAllowed(next)) return; // claws stall on walls and structure
            Closure = next;
            ApplyPose();
        }

        bool CrushablePiece(int i) => world.Graph.pieces[i].material != steelMaterial;

        bool BreakablePiece(int i) => world.Graph.pieces[i].material != steelMaterial;

        /// <summary>Steel still attached to something: free segments can be pushed aside but need no cutting.</summary>
        bool CuttablePiece(int i)
        {
            if (world.Graph.pieces[i].material != steelMaterial) return false;
            foreach (int cid in world.Graph.adjacency[i])
                if (world.Graph.connections[cid].state != ConnectionState.Severed) return true;
            return false;
        }

        int FindPiece(Transform at, Vector3 halfExtents, Func<int, bool> valid)
        {
            int n = Physics.OverlapBoxNonAlloc(at.position, halfExtents, overlap, ToolFrame(), ~0, QueryTriggerInteraction.Ignore);
            return Closest(at.position, n, valid);
        }

        /// <summary>Attachment frame for contact boxes: x = hinge (lateral) axis, y = from the work point up to the pin,
        /// z = the direction the jaws open and close in.</summary>
        Quaternion ToolFrame()
        {
            Vector3 right = Upper.rotation * upperRightLocal;
            Vector3 up = Attachment.position - WorkPoint.position;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up = Vector3.ProjectOnPlane(up, right).normalized;
            return Quaternion.LookRotation(Vector3.Cross(right, up), up);
        }

        int FindPieceSphere(Vector3 p, float r, Func<int, bool> valid)
        {
            int n = Physics.OverlapSphereNonAlloc(p, r, overlap, ~0, QueryTriggerInteraction.Ignore);
            return Closest(p, n, valid);
        }

        int Closest(Vector3 p, int n, Func<int, bool> valid)
        {
            if (world == null || world.Graph == null) return -1;
            int best = -1;
            float bestD = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                if (!world.TryGetPiece(overlap[k], out int i)) continue;
                if (i < 0 || i >= world.Graph.PieceCount || world.pieces[i] == null || world.pieces[i].removed) continue;
                if (!valid(i)) continue;
                float d = (overlap[k].ClosestPoint(p) - p).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Loose debris in the claws: a dynamic, non-kinematic piece body within the mass and size limits.
        /// Static (intact) structure, the machine itself and anything that is not a destruction piece are ignored.</summary>
        Rigidbody FindLoad()
        {
            if (world == null || world.Graph == null) return null;
            int n = Physics.OverlapBoxNonAlloc(WorkPoint.position, gripHalfExtents, overlap, ToolFrame(), ~0, QueryTriggerInteraction.Ignore);
            Rigidbody best = null;
            float bestD = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                if (!world.TryGetPiece(overlap[k], out int i)) continue;
                var piece = world.pieces[i];
                if (piece == null || piece.removed || piece.cluster == null || piece.cluster.isStatic) continue;
                var rb = piece.cluster.body;
                if (rb == null || rb.isKinematic || rb.mass > maxGrabMass) continue;
                if (BoundsOf(rb).size.MaxComponent() > maxGrabSize) continue;
                float d = (rb.worldCenterOfMass - WorkPoint.position).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = rb;
                }
            }
            return best;
        }

        static Bounds BoundsOf(Rigidbody rb)
        {
            var cols = rb.GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return new Bounds(rb.position, Vector3.zero);
            var b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
            return b;
        }

        void Grab(Rigidbody rb)
        {
            gripBody.position = WorkPoint.position;
            gripBody.rotation = WorkPoint.rotation;
            HeldBody = rb;
            gripping = true;
            heldColliders.Clear();
            rb.GetComponentsInChildren(heldColliders);
            foreach (var b in heldColliders) Collision.ignore.Add(b); // a carried load never blocks its own machine
            // The load rides in the claws: never let it fight the machine's own colliders.
            foreach (var a in ownColliders)
                foreach (var b in heldColliders)
                    if (a != null && b != null) Physics.IgnoreCollision(a, b, true);
            grip = rb.gameObject.AddComponent<ConfigurableJoint>();
            grip.connectedBody = gripBody;
            grip.autoConfigureConnectedAnchor = true;
            grip.xMotion = grip.yMotion = grip.zMotion = ConfigurableJointMotion.Locked;
            grip.angularXMotion = grip.angularYMotion = grip.angularZMotion = ConfigurableJointMotion.Locked;
            grip.enablePreprocessing = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.WakeUp();
        }

        void Release()
        {
            if (grip != null) Destroy(grip);
            DropGrip();
        }

        void DropGrip()
        {
            // Re-enable contact with the machine a moment later, once the load has fallen clear of the claws.
            float until = Time.time + 0.6f;
            foreach (var a in ownColliders)
                foreach (var b in heldColliders)
                    if (a != null && b != null) pendingRestore.Add((a, b, until));
            foreach (var b in heldColliders) Collision.ignore.Remove(b);
            heldColliders.Clear();
            grip = null;
            HeldBody = null;
            gripping = false;
        }

        void RestoreCollisions()
        {
            for (int i = pendingRestore.Count - 1; i >= 0; i--)
            {
                var (a, b, until) = pendingRestore[i];
                if (a == null || b == null) { pendingRestore.RemoveAt(i); continue; }
                if (Time.time < until) continue;
                Physics.IgnoreCollision(a, b, false);
                pendingRestore.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ reset

        public void ResetPose()
        {
            if (grip != null) Destroy(grip);
            grip = null;
            HeldBody = null;
            gripping = false;
            foreach (var b in heldColliders) Collision.ignore.Remove(b);
            heldColliders.Clear();
            pendingRestore.Clear();
            StopPowered();
            toolLatched = false;
            body.position = startPos;
            body.rotation = startRot;
            transform.SetPositionAndRotation(startPos, startRot);
            Closure = 0f;
            BitOffset = 0f;
            hammerPhase = 0f;
            Bites = Cuts = Strikes = Hits = 0;
            SetPose(0f, startBoomAngle, 0f, 0f);
            Physics.SyncTransforms();
        }

        void OnDestroy()
        {
            if (gripBody != null) Destroy(gripBody.gameObject);
        }
    }

    static class VectorExt
    {
        public static float MaxComponent(this Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));
    }
}
