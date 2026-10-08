using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    public enum LoaderKind { Wheel, Skid }

    /// <summary>Every tunable of a loader, grouped for the Inspector. CraneTestBootstrap holds one per machine and applies it.</summary>
    [Serializable]
    public sealed class LoaderTuning
    {
        [Header("Drive")]
        [Tooltip("Top speed, m/s.")] public float maxSpeed = 5f;
        [Tooltip("Reverse top speed as a fraction of forward.")] [Range(0.3f, 1f)] public float reverseScale = 0.8f;
        [Tooltip("Speed gained per second under power, m/s².")] public float accel = 4f;
        [Tooltip("Speed lost per second when braking or coasting to a stop, m/s².")] public float brake = 12f;

        [Header("Steering")]
        [Tooltip("Wheel loader: largest bend at the central pivot, degrees.")] public float maxArticulation = 38f;
        [Tooltip("Wheel loader: how fast the chassis bends, deg/s.")] public float articulationSpeed = 45f;
        [Tooltip("Skid steer: share of track speed used for steering. 0.5 turns in place at half track speed.")] [Range(0.1f, 1f)] public float turnMix = 0.55f;

        [Header("Lift arms")]
        public float liftSpeed = 16f;
        public float liftAccel = 50f;
        [Tooltip("Largest arm angle from the lowered rest pose, degrees. Set from the model handoff.")] public float liftMaxAngle = 64f;

        [Header("Bucket tilt")]
        public float tiltSpeed = 40f;
        public float tiltAccel = 140f;
        [Tooltip("Bucket angle relative to the arms: most curled back (negative), degrees. From the model handoff.")] public float bucketLocalMin = -45f;
        [Tooltip("Bucket angle relative to the arms: most tipped forward, degrees. From the model handoff.")] public float bucketLocalMax = 95f;
        [Tooltip("Most the bucket floor may tilt back from level, degrees (negative). Keeps a curled-back load inside the bucket.")] public float curlLimit = -50f;
        [Tooltip("How much of the arm lift the linkage cancels, so the bucket keeps its angle to the ground while the arms rise (1 = fully self-levelling).")]
        [Range(0f, 1f)] public float selfLevel = 1f;

        [Header("Bucket load")]
        [Tooltip("Largest load the bucket takes, kg.")] public float capacityKg = 4500f;
        [Tooltip("Heaviest single piece the bucket will take, kg. Heavier chunks stay outside.")] public float maxPieceMassKg = 2000f;
        [Tooltip("Largest bounds extent of a piece the bucket will take, m. Long, thin pieces up to the bucket's width are also taken, lying across it.")]
        public float maxPieceSize = 1.4f;
        [Tooltip("Opening tipped below horizontal by more than this, degrees, pours the load out. Collection stops 5 degrees short of it.")] public float dumpAngle = 20f;
        [Tooltip("How far ahead of the cutting edge a moving bucket can take material, m.")] public float scoopReach = 0.6f;
        [Tooltip("Height of the scoop zone above the bucket floor, m.")] public float scoopHeight = 0.7f;
        [Tooltip("Forward speed of the bucket needed to scoop material in front of the edge, m/s.")] public float scoopMinSpeed = 0.2f;
        [Tooltip("Seconds for a scooped piece to settle into the bucket.")] public float loadInTime = 0.35f;
        [Tooltip("Seconds between pieces leaving a tipped bucket.")] public float releaseInterval = 0.12f;
        [Tooltip("Speed along the bucket floor given to each piece as it leaves a tipped bucket, m/s.")] public float releaseShove = 1.8f;

        [Header("Gamepad")]
        [Tooltip("Response curve on stick input: 1 is linear, higher is gentler near the centre.")] [Range(1f, 2.5f)] public float stickExponent = 1.4f;
        [Tooltip("Wheel loader on a gamepad: while steering with the stick leaning forward/back, drive is held at least this share of the steer input so the turn does not stall.")] [Range(0f, 1f)] public float turnDriveFloor = 0.7f;
        public bool invertDrive, invertSteer, invertLift, invertTilt;

        public static LoaderTuning WheelLoader() => new LoaderTuning();

        public static LoaderTuning SkidSteer() => new LoaderTuning
        {
            maxSpeed = 3.6f, accel = 6f, brake = 14f, turnMix = 0.55f,
            liftSpeed = 22f, liftAccel = 70f, liftMaxAngle = 66f,
            tiltSpeed = 55f, tiltAccel = 180f, bucketLocalMin = -55f, bucketLocalMax = 140f, selfLevel = 1f,
            capacityKg = 900f, maxPieceMassKg = 400f, maxPieceSize = 0.9f, dumpAngle = 32f,
            scoopReach = 0.45f, scoopHeight = 0.5f, scoopMinSpeed = 0.15f,
        };
    }

    /// <summary>
    /// A playable bucket loader for CraneTest: the articulated wheel loader or the wheeled skid steer, built from their
    /// FBX (see Tools/blender/WheelLoader and SkidSteer HANDOFF.md for the node contract). One class drives both
    /// because they share the same arm / bucket / load behaviour; only the steering differs.
    ///
    ///   Wheel loader  A kinematic root (the rear frame) moves by bicycle kinematics for an articulated vehicle; the
    ///                 front frame bends about the central pivot, so steering is visible in the chassis.
    ///   Skid steer    Left and right wheel sides run at separate speeds; the root's yaw rate is their difference, so
    ///                 it turns tightly and spins in place.
    ///
    /// Like the excavators, the root is a kinematic body moved in FixedUpdate and tested against the world with
    /// <see cref="RigCollision"/> (solid structure blocks it, light debris is pushed). Arms and bucket move in
    /// LateUpdate with the same check, and cannot push the bucket into the ground. The load lives in
    /// <see cref="BucketLoad"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class LoaderRig : MonoBehaviour, IOperableRig, ILevelRig, IMachineSound
    {
        public LoaderKind kind;
        public LoaderTuning tuning = new LoaderTuning();
        public DestructionWorld world;
        [System.NonSerialized] public CleanupLedger ledger;

        // ------------------------------------------------------------------ sound

        /// <summary>The skid steer has the small diesel; neither loader runs on tracks.</summary>
        public MachineSoundProfile SoundProfile => new MachineSoundProfile
        {
            lightEngine = kind != LoaderKind.Wheel, workLoop = "hydraulic_move_loop",
        };
        public float DriveActivity => Mathf.Max(Mathf.Abs(leftSpeed), Mathf.Abs(rightSpeed)) / tuning.maxSpeed;
        public float WorkActivity => Mathf.Max(Mathf.Abs(lift.vel) / tuning.liftSpeed, Mathf.Abs(tiltVel) / tuning.tiltSpeed);

        // ------------------------------------------------------------------ state (read by the HUD and tests)

        public float LiftAngle => lift.angle;
        /// <summary>Bucket angle to the ground, degrees: + tipped forward, − curled back.</summary>
        public float TiltAngle => tilt;
        /// <summary>Bucket angle relative to the arms.</summary>
        public float BucketLocalAngle => bucket.angle;
        /// <summary>Wheel loader: current bend at the pivot, + right.</summary>
        public float Articulation => steer.angle;
        /// <summary>Signed forward speed of the machine, m/s.</summary>
        public float Speed => (leftSpeed + rightSpeed) * 0.5f;
        public float YawRate { get; private set; }
        public float WheelSpinDegrees(int i) => wheels[i].angle;
        public int WheelCount => wheels.Count;
        public int Blocked { get; private set; }
        public bool Parked { get; private set; }
        public BucketLoad Load { get; private set; }
        public Transform BucketNode => bucket.node;
        public Transform Edge { get; private set; }
        public Transform FrontFrame => steer.node;
        public Transform Chassis { get; private set; }
        public readonly RigCollision Collision = new RigCollision();
        public readonly RigTerrain Terrain = new RigTerrain();

        [Tooltip("Tallest obstacle the wheels roll over, m. 0 = from the wheel size (0.55 × wheel radius).")]
        public float stepHeight;

        public string RigName => kind == LoaderKind.Wheel ? "Wheel loader" : "Skid-steer loader";
        public string AttachmentName => "Bucket";
        public Vector3 DoorPosition => door != null ? GroundPoint(door.position) : transform.position - transform.right * 2.2f;
        public Vector3 SeatPosition => seat != null ? seat.position : transform.position + Vector3.up * 2.4f;
        public Quaternion SeatRotation
        {
            get
            {
                Transform t = steer.node != null ? steer.node : transform;
                return Quaternion.LookRotation(Vector3.ProjectOnPlane(t.rotation * seatForwardLocal, Vector3.up), Vector3.up);
            }
        }

        /// <summary>Ground point between the machine and its bucket: frames the machine and the working area.</summary>
        public Vector3 CameraFocus
        {
            get
            {
                Vector3 p = Vector3.Lerp(transform.position, Edge.position, 0.55f);
                return new Vector3(p.x, transform.position.y, p.z);
            }
        }

        // ------------------------------------------------------------------ internals

        sealed class Hinge
        {
            public Transform node;
            public Quaternion rest;
            public Vector3 axis;
            public float sign = 1f;
            public float angle, vel;
            public void Apply() => node.localRotation = Quaternion.AngleAxis(angle * sign, axis) * rest;
        }

        readonly Hinge lift = new Hinge(), bucket = new Hinge(), steer = new Hinge();
        readonly List<Hinge> wheels = new List<Hinge>();
        readonly List<bool> wheelLeft = new List<bool>();
        float wheelRadius = 0.5f;
        float tilt, tiltVel;
        float leftSpeed, rightSpeed, steerRate;
        float rearToPivot = 1.7f, pivotToFront = 1.7f, trackWidth = 1.4f;
        float inDrive, inSteer, inLift, inTilt;
        Transform seat, door, cameraFocusAnchor;
        Transform[] exits = new Transform[0];
        Vector3 seatForwardLocal = Vector3.forward;
        Rigidbody body;
        Vector3 startPos;
        Quaternion startRot;
        readonly List<Collider> ownColliders = new List<Collider>(), bucketColliders = new List<Collider>(),
            armColliders = new List<Collider>(), frontColliders = new List<Collider>();
        BoxCollider[] bucketBoxes = new BoxCollider[0];
        /// <summary>Wall thickness of the collider shell around the bucket cavity, m.</summary>
        const float ShellThickness = 0.15f;
        readonly List<Collider> shellColliders = new List<Collider>();

        void OnDestroy()
        {
            foreach (var c in shellColliders) RigCollision.debrisOnly.Remove(c);
        }

        struct Ram { public Transform node, other; public Quaternion rest; public Vector3 dirLocal; }
        readonly List<Ram> rams = new List<Ram>();

        // ------------------------------------------------------------------ build

        string P => kind == LoaderKind.Wheel ? "WL_" : "SS_";

        public void Build(GameObject fbxInstance, LoaderKind loaderKind)
        {
            kind = loaderKind;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var model = fbxInstance.transform;
            model.SetParent(transform, false);
            var armNode = Find(model, P + "LiftArm");
            var bucketNode = Find(model, P + "Bucket");
            Edge = Find(model, "Anchor_BucketEdge");
            var vol = Find(model, "Vol_Cavity");
            seat = FindOptional(model, "Anchor_Seat");
            door = FindOptional(model, "Anchor_Door");
            cameraFocusAnchor = FindOptional(model, "Anchor_CameraFocus");
            var ex = new List<Transform>();
            foreach (var n in new[] { "Anchor_Exit_L", "Anchor_Exit_R" })
            {
                var t = FindOptional(model, n);
                if (t != null) ex.Add(t);
            }
            exits = ex.ToArray();
            Chassis = kind == LoaderKind.Wheel ? Find(model, "WL_RearFrame") : Find(model, "SS_Chassis");
            Transform front = kind == LoaderKind.Wheel ? Find(model, "WL_FrontFrame") : null;

            // Face the wrapper's +Z whatever frame the FBX arrives in: the bucket is ahead of the machine.
            AlignModel(transform, model);
            seatForwardLocal = Quaternion.Inverse((front != null ? front : transform).rotation) * transform.forward;

            Vector3 lateral = transform.right;
            Setup(lift, armNode, lateral);
            Setup(bucket, bucketNode, lateral);
            if (front != null) Setup(steer, front, Vector3.up);
            foreach (var w in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" })
            {
                var node = Find(model, P + w);
                var h = new Hinge();
                Setup(h, node, lateral); // + angle rolls the wheel forward (rotation about +right lifts the top toward +Z)
                wheels.Add(h);
                wheelLeft.Add(transform.InverseTransformPoint(node.position).x < 0f);
            }
            MeasureWheels();

            AddRam(model, P + "LiftRam_L", P + "LiftRamRod_L");
            AddRam(model, P + "LiftRam_R", P + "LiftRamRod_R");
            AddRam(model, P + "TiltRam", P + "TiltRamRod");

            // Signs from the geometry, so they hold whatever axis conventions the import uses.
            lift.sign = Probe(lift, () => bucket.node.position.y);              // + raises the bucket
            bucket.sign = Probe(bucket, () => -Edge.position.y);                // + tips forward (edge goes down)
            if (front != null) steer.sign = ProbeSteer();                       // + turns the front to the right

            AddColliders(model, front);
            Load = new BucketLoad(bucketNode, transform, vol)
            {
                CapacityKg = tuning.capacityKg,
                MaxPieceMassKg = tuning.maxPieceMassKg,
                MaxPieceSize = tuning.maxPieceSize,
            };
            ApplyTuningToLoad();
            AddBucketShell();
            var gear = ownColliders.FindAll(c => !bucketColliders.Contains(c) && !armColliders.Contains(c));
            Terrain.Setup(transform, model, gear, Collision, stepHeight > 0f ? stepHeight : 0.55f * wheelRadius);
            vol.GetComponent<Renderer>().enabled = false;

            startPos = transform.position;
            startRot = transform.rotation;
            SetPose(0f, 0f, 0f);
        }

        void ApplyTuningToLoad()
        {
            if (Load == null) return;
            Load.CapacityKg = tuning.capacityKg;
            Load.MaxPieceMassKg = tuning.maxPieceMassKg;
            Load.MaxPieceSize = tuning.maxPieceSize;
            Load.DumpAngle = tuning.dumpAngle;
            Load.ScoopReach = tuning.scoopReach;
            Load.ScoopHeight = tuning.scoopHeight;
            Load.ScoopMinSpeed = tuning.scoopMinSpeed;
            Load.LoadInTime = tuning.loadInTime;
            Load.ReleaseInterval = tuning.releaseInterval;
            Load.ReleaseShove = tuning.releaseShove;
        }

        /// <summary>Jump to a pose (arm angle from rest, bucket angle to the ground, bend at the pivot) with no motion.</summary>
        public void SetPose(float liftDeg, float tiltDeg, float articulationDeg)
        {
            lift.angle = Mathf.Clamp(liftDeg, 0f, tuning.liftMaxAngle);
            tilt = tiltDeg;
            ClampTilt();
            if (steer.node != null) steer.angle = Mathf.Clamp(articulationDeg, -tuning.maxArticulation, tuning.maxArticulation);
            lift.vel = tiltVel = steerRate = 0f;
            ApplyPose();
        }

        /// <summary>Bucket angle to the arms that gives this ground angle at the current arm lift.</summary>
        float LocalFor(float groundTilt) => groundTilt + tuning.selfLevel * lift.angle;

        void ClampTilt()
        {
            float lo = Mathf.Max(tuning.curlLimit, tuning.bucketLocalMin - tuning.selfLevel * lift.angle);
            float hi = tuning.bucketLocalMax - tuning.selfLevel * lift.angle;
            tilt = Mathf.Clamp(tilt, lo, hi);
        }

        [Header("Level prefab")]
        [Tooltip("The loader FBX instance under this object (WheelLoader or SkidSteer, matching Kind). Built when the level starts.")]
        public GameObject authoredModel;

        public bool IsBuilt => body != null;

        public void BuildInLevel(DestructionWorld levelWorld, CleanupLedger levelLedger, Collider ground)
        {
            if (IsBuilt || authoredModel == null) return;
            world = levelWorld;
            ledger = levelLedger;
            if (ground != null) Collision.ignore.Add(ground);
            Build(authoredModel, kind);
        }

        /// <summary>Turn a loader FBX instance under <paramref name="root"/> to face +Z (the bucket ahead).</summary>
        public static void AlignModel(Transform root, Transform model)
        {
            var edge = RigModel.Find(model, "Anchor_BucketEdge");
            if (edge != null) RigModel.FaceForward(root, model, edge.position, root.position);
        }

        static Transform FindOptional(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Transform Find(Transform root, string name) =>
            FindOptional(root, name) ?? throw new InvalidOperationException($"Loader FBX is missing '{name}'.");

        static void Setup(Hinge h, Transform node, Vector3 worldAxis)
        {
            h.node = node;
            h.rest = node.localRotation;
            h.axis = node.parent.InverseTransformDirection(worldAxis).normalized;
            h.angle = h.vel = 0f;
        }

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

        float ProbeSteer()
        {
            Vector3 f0 = Vector3.ProjectOnPlane(Edge.position - steer.node.position, Vector3.up);
            steer.sign = 1f;
            steer.angle = 5f;
            steer.Apply();
            Vector3 f1 = Vector3.ProjectOnPlane(Edge.position - steer.node.position, Vector3.up);
            steer.angle = 0f;
            steer.Apply();
            return Vector3.SignedAngle(f0, f1, Vector3.up) > 0f ? 1f : -1f; // Unity: + about up is clockwise from above
        }

        void MeasureWheels()
        {
            float sumRear = 0f, sumFront = 0f, sumX = 0f;
            int rear = 0, front = 0;
            foreach (var w in wheels)
            {
                Vector3 l = transform.InverseTransformPoint(w.node.position);
                if (l.z < 0f) { sumRear += -l.z; rear++; } else { sumFront += l.z; front++; }
                sumX += Mathf.Abs(l.x);
                var r = w.node.GetComponent<Renderer>();
                if (r != null) wheelRadius = r.bounds.extents.y;
            }
            if (rear > 0) rearToPivot = sumRear / rear;
            if (front > 0) pivotToFront = sumFront / front;
            if (wheels.Count > 0) trackWidth = Mathf.Max(0.5f, 2f * sumX / wheels.Count);
        }

        void AddRam(Transform model, string barrel, string rod)
        {
            var a = FindOptional(model, barrel);
            var b = FindOptional(model, rod);
            if (a == null || b == null) return;
            rams.Add(new Ram { node = a, other = b, rest = a.localRotation, dirLocal = a.InverseTransformDirection(b.position - a.position) });
            rams.Add(new Ram { node = b, other = a, rest = b.localRotation, dirLocal = b.InverseTransformDirection(a.position - b.position) });
        }

        /// <summary>BoxColliders from the model's Col_* helper meshes (hidden), grouped by the part they ride on. Simple
        /// compound boxes only: the bucket is floor, back, two sides and a thin edge plate, so its opening stays clear.</summary>
        void AddColliders(Transform model, Transform front)
        {
            var bucketBoxList = new List<BoxCollider>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.StartsWith("Col_", StringComparison.Ordinal) || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                var box = mf.gameObject.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
                var r = mf.GetComponent<Renderer>();
                if (r != null) r.enabled = false;
                ownColliders.Add(box);
                Collision.own.Add(box);
                if (mf.transform.IsChildOf(bucket.node))
                {
                    bucketColliders.Add(box);
                    bucketBoxList.Add(box);
                }
                else if (mf.transform.IsChildOf(lift.node)) armColliders.Add(box);
                if (front != null && mf.transform.IsChildOf(front)) frontColliders.Add(box);
            }
            bucketBoxes = bucketBoxList.ToArray();
            if (ownColliders.Count == 0) Debug.LogError($"[DestructionLab] {RigName} FBX has no Col_* collider helpers.");
        }

        /// <summary>
        /// Closed walls around the bucket cavity for loose debris. The model's Col_* plates are a few centimetres thick,
        /// leave the curved heel between floor and back open, and move with the transform rather than through the
        /// solver: a chunk sliding back in a lifting bucket slips out under the back plate, and a quick move can step a
        /// plate through a chunk. These boxes wrap the cavity (Vol_Cavity) on the outside in plates
        /// <see cref="ShellThickness"/> thick that overlap at the corners. The opening and the cutting edge stay as
        /// modelled. The shell only holds debris: machine collision tests (this one's and every other machine's) still
        /// use the modelled plates.
        /// </summary>
        void AddBucketShell()
        {
            var shell = new GameObject("Col_BucketShell").transform;
            shell.SetParent(bucket.node, false);
            shell.SetPositionAndRotation(Load.CavityPosition, Load.CavityRotation);
            float k = 1f / Mathf.Max(1e-6f, Mathf.Abs(shell.lossyScale.x)); // metres to the shell's local units
            Vector3 h = Load.Half;
            float t = ShellThickness;
            void Plate(Vector3 centre, Vector3 size)
            {
                var box = shell.gameObject.AddComponent<BoxCollider>();
                box.center = centre * k;
                box.size = size * k;
                Collision.own.Add(box);
                RigCollision.debrisOnly.Add(box);
                shellColliders.Add(box);
            }
            Plate(new Vector3(0f, -h.y - t * 0.5f, -t * 0.5f), new Vector3(2f * h.x + 2f * t, t, 2f * h.z + t));         // floor, under the heel too
            Plate(new Vector3(0f, -t * 0.5f, -h.z - t * 0.5f), new Vector3(2f * h.x + 2f * t, 2f * h.y + t, t));          // back, down to the floor
            Plate(new Vector3(-h.x - t * 0.5f, -t * 0.5f, -t * 0.5f), new Vector3(t, 2f * h.y + t, 2f * h.z + t));         // sides
            Plate(new Vector3(h.x + t * 0.5f, -t * 0.5f, -t * 0.5f), new Vector3(t, 2f * h.y + t, 2f * h.z + t));
        }

        // ------------------------------------------------------------------ controls

        /// <summary>Latch this frame's requests, each −1..1: drive (+ forward), steer (+ right), lift (+ raise), tilt (+ tip forward).</summary>
        public void Command(float drive, float steerIn, float liftIn, float tiltIn)
        {
            inDrive = drive;
            inSteer = steerIn;
            inLift = liftIn;
            inTilt = tiltIn;
        }

        public InputActionMap ControlMap(CraneTestInput input) => input.loader;

        public void OnEnter()
        {
            Parked = false;
            StopPowered();
        }

        /// <summary>Parking hold: the machine stops dead and stays put; arms, bucket and load keep their pose.</summary>
        public void OnExit()
        {
            StopPowered();
            Parked = true;
        }

        void StopPowered()
        {
            Command(0f, 0f, 0f, 0f);
            leftSpeed = rightSpeed = 0f;
            lift.vel = tiltVel = steerRate = 0f;
            YawRate = 0f;
        }

        float Shape(float v, bool pad, bool invert)
        {
            if (!pad) return Mathf.Clamp(v, -1f, 1f);
            v = Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), tuning.stickExponent);
            return Mathf.Clamp(invert ? -v : v, -1f, 1f);
        }

        public void Operate(CraneTestInput input)
        {
            bool pad = input.UsingGamepad;
            float drive = Shape(input.LoaderDrive.ReadValue<float>(), pad, tuning.invertDrive);
            float steerIn = Shape(input.LoaderSteer.ReadValue<float>(), pad, tuning.invertSteer);
            // Articulated steering only turns the machine while it rolls, and both axes share one stick, so a sideways
            // push would otherwise cut the drive and stall the turn. Keep rolling the way the stick leans.
            if (pad && kind == LoaderKind.Wheel && drive != 0f)
                drive = Mathf.Sign(drive) * Mathf.Max(Mathf.Abs(drive), Mathf.Abs(steerIn) * tuning.turnDriveFloor);
            Command(drive,
                    steerIn,
                    Shape(input.LoaderLift.ReadValue<float>(), pad, tuning.invertLift),
                    Shape(input.LoaderTilt.ReadValue<float>(), pad, tuning.invertTilt));
        }

        public void ExitCandidates(List<Vector3> into)
        {
            into.Add(DoorPosition);
            foreach (var e in exits) into.Add(GroundPoint(e.position));
            var t = transform;
            float side = kind == LoaderKind.Wheel ? 2.6f : 1.9f;
            into.Add(t.position - t.right * side);
            into.Add(t.position + t.right * side);
            into.Add(t.position - t.forward * (side + 1.5f));
        }

        Vector3 GroundPoint(Vector3 p) => new Vector3(p.x, transform.position.y, p.z);

        public void ControlHints(CraneTestInput input, List<ControlHint> into)
        {
            bool pad = input.UsingGamepad;
            into.Add(ControlHint.Row(input.Keys(input.LoaderDrive), "Drive forward / reverse"));
            into.Add(ControlHint.Row(input.Keys(input.LoaderSteer, pad, negativeFirst: true),
                kind == LoaderKind.Wheel ? "Steer (bend) left / right" : "Turn left / right (spins in place)"));
            into.Add(ControlHint.Row(input.Keys(input.LoaderLift), "Raise / lower arms"));
            into.Add(ControlHint.Row(input.Keys(input.LoaderTilt, pad, negativeFirst: true), "Curl back / tip forward"));
        }

        public string Telemetry
        {
            get
            {
                int pct = Mathf.RoundToInt(Load.Fill * 100f);
                string state = Load.Spilling ? "  POURING" : Load.Fill >= 0.98f ? "  FULL" : "";
                return $"Arms {lift.angle:0}°   Bucket {Load.PitchDown:+0;-0;0}° (pours past +{tuning.dumpAngle:0}°)\nLoad {Load.MassKg:N0} / {Load.CapacityKg:N0} kg  ({pct}%){state}";
            }
        }

        // ------------------------------------------------------------------ simulation

        static float Approach(float v, float target, float accel, float brake, float dt)
        {
            bool braking = Mathf.Abs(target) < Mathf.Abs(v) || v * target < 0f;
            return Mathf.MoveTowards(v, target, (braking ? brake : accel) * dt);
        }

        void FixedUpdate()
        {
            if (body == null) return;
            float dt = Time.fixedDeltaTime;
            if (Parked) { leftSpeed = rightSpeed = 0f; YawRate = 0f; }
            else
            {
                Vector3 pos = body.position;
                Quaternion rot = body.rotation;
                if (kind == LoaderKind.Wheel) StepArticulated(dt, ref pos, ref rot);
                else StepSkid(dt, ref pos, ref rot);
                if (pos != body.position || rot != body.rotation)
                {
                    if (DriveBlocked(pos, rot))
                    {
                        leftSpeed = rightSpeed = 0f;
                        YawRate = 0f;
                        Blocked++;
                    }
                    else
                    {
                        body.MoveRotation(rot);
                        body.MovePosition(pos);
                    }
                }
            }
            Terrain.Step(dt);
            ApplyTuningToLoad(); // Inspector edits apply live
            Load.Step(dt, world, ledger, Collision);
        }

        void StepArticulated(float dt, ref Vector3 pos, ref Quaternion rot)
        {
            float target = inDrive * tuning.maxSpeed * (inDrive < 0f ? tuning.reverseScale : 1f);
            float v = Approach(leftSpeed, target, tuning.accel, tuning.brake, dt);
            leftSpeed = rightSpeed = v;
            float g = steer.angle * Mathf.Deg2Rad;
            float gd = steerRate * Mathf.Deg2Rad;
            // Kinematics of an articulated vehicle: the rear axle moves along the rear frame, the front axle along the
            // front frame. v is the rear-axle speed, g the bend (+ right), a and b the axle distances to the pivot.
            float yawRate = (v * Mathf.Sin(g) - pivotToFront * gd) / (rearToPivot * Mathf.Cos(g) + pivotToFront);
            YawRate = yawRate * Mathf.Rad2Deg;
            if (Mathf.Abs(v) < 1e-4f && Mathf.Abs(YawRate) < 1e-3f) return;
            Vector3 rear = pos - rot * Vector3.forward * rearToPivot;
            Quaternion rot2 = Quaternion.AngleAxis(YawRate * dt, Vector3.up) * rot;
            rear += (Quaternion.Slerp(rot, rot2, 0.5f) * Vector3.forward) * v * dt;
            rot = rot2;
            pos = rear + rot * Vector3.forward * rearToPivot;
        }

        void StepSkid(float dt, ref Vector3 pos, ref Quaternion rot)
        {
            // Arcade mix: steering adds to one side and subtracts from the other, then scales down to stay in range.
            float scale = inDrive < 0f ? tuning.reverseScale : 1f;
            float l = inDrive * scale + inSteer * tuning.turnMix, r = inDrive * scale - inSteer * tuning.turnMix;
            float m = Mathf.Max(1f, Mathf.Max(Mathf.Abs(l), Mathf.Abs(r)));
            leftSpeed = Approach(leftSpeed, l / m * tuning.maxSpeed, tuning.accel, tuning.brake, dt);
            rightSpeed = Approach(rightSpeed, r / m * tuning.maxSpeed, tuning.accel, tuning.brake, dt);
            float v = (leftSpeed + rightSpeed) * 0.5f;
            YawRate = (leftSpeed - rightSpeed) / trackWidth * Mathf.Rad2Deg; // + clockwise from above (turning right)
            if (Mathf.Abs(v) < 1e-4f && Mathf.Abs(YawRate) < 1e-3f) return;
            Quaternion rot2 = Quaternion.AngleAxis(YawRate * dt, Vector3.up) * rot;
            pos += (Quaternion.Slerp(rot, rot2, 0.5f) * Vector3.forward) * v * dt;
            rot = rot2;
        }

        bool DriveBlocked(Vector3 pos, Quaternion rot) => Collision.MoveBlocked(transform, body, ownColliders, pos, rot);

        void LateUpdate()
        {
            if (body == null) return;
            float dt = Time.deltaTime;
            if (!Parked || inLift != 0f || inTilt != 0f) StepArms(dt);
            if (steer.node != null) StepSteer(dt);
            SpinWheels(dt);
            ApplyPose();
        }

        void StepSteer(float dt)
        {
            float target = Parked ? steer.angle : inSteer * tuning.maxArticulation;
            float err = target - steer.angle;
            float want = Mathf.Clamp(err * 6f, -tuning.articulationSpeed, tuning.articulationSpeed);
            if (Mathf.Abs(err) > 0.05f) want = Mathf.Sign(err) * Mathf.Max(Mathf.Abs(want), 3f); // never crawl asymptotically
            steerRate = Mathf.MoveTowards(steerRate, want, tuning.articulationSpeed * 8f * dt);
            float before = steer.angle;
            float next = Mathf.Clamp(before + steerRate * dt, -tuning.maxArticulation, tuning.maxArticulation);
            if (next == before) { steerRate = 0f; return; }
            steer.angle = before;
            ApplyPose();
            Physics.SyncTransforms();
            float pen0 = Collision.Penetration(frontColliders);
            steer.angle = next;
            ApplyPose();
            Physics.SyncTransforms();
            if (Collision.Penetration(frontColliders) > pen0 + Collision.tolerance)
            {
                steer.angle = before;
                steerRate = 0f;
                ApplyPose();
                Physics.SyncTransforms();
                Blocked++;
            }
        }

        /// <summary>Move arms and bucket one frame, undoing the step if it would push them further into something
        /// solid or drive the bucket below the ground.</summary>
        void StepArms(float dt)
        {
            lift.vel = Approach(lift.vel, inLift * tuning.liftSpeed, tuning.liftAccel, tuning.liftAccel * 2f, dt);
            tiltVel = Approach(tiltVel, inTilt * tuning.tiltSpeed, tuning.tiltAccel, tuning.tiltAccel * 2f, dt);
            float nl = Mathf.Clamp(lift.angle + lift.vel * dt, 0f, tuning.liftMaxAngle);
            if (nl == 0f || nl == tuning.liftMaxAngle) lift.vel = 0f;
            // Each axis is tried on its own, so a blocked tilt (edge against the ground) does not also stop the lift.
            if (nl != lift.angle && !TryArms(nl, tilt)) lift.vel = 0f;
            float nt = tilt + tiltVel * dt;
            if (nt != tilt && !TryArms(lift.angle, nt)) tiltVel = 0f;
        }

        /// <summary>Pose the arms at these angles unless that pushes them further into something solid or the bucket
        /// below the ground; then restore the previous pose.</summary>
        bool TryArms(float newLift, float newTilt)
        {
            float liftBefore = lift.angle, tiltBefore = tilt;
            ApplyPose();
            Physics.SyncTransforms();
            float pen0 = Collision.Penetration(bucketColliders) + Collision.Penetration(armColliders);
            float low0 = LowestPoint();
            lift.angle = newLift;
            tilt = newTilt;
            ClampTilt();
            ApplyPose();
            Physics.SyncTransforms();
            float floor = transform.position.y - 0.01f;
            float low1 = LowestPoint();
            bool intoGround = low1 < floor && low1 < low0 - 1e-5f;
            if (!intoGround && Collision.Penetration(bucketColliders) + Collision.Penetration(armColliders) <= pen0 + Collision.tolerance) return true;
            lift.angle = liftBefore;
            tilt = tiltBefore;
            ApplyPose();
            Physics.SyncTransforms();
            Blocked++;
            return false;
        }

        float LowestPoint()
        {
            float low = float.MaxValue;
            foreach (var b in bucketBoxes)
            {
                var t = b.transform;
                Vector3 e = b.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    low = Mathf.Min(low, t.TransformPoint(c).y);
                }
            }
            return low;
        }

        void SpinWheels(float dt)
        {
            for (int i = 0; i < wheels.Count; i++)
            {
                float v = wheelLeft[i] ? leftSpeed : rightSpeed;
                wheels[i].angle = Mathf.Repeat(wheels[i].angle + v * dt / wheelRadius * Mathf.Rad2Deg, 360f);
            }
        }

        void ApplyPose()
        {
            lift.Apply();
            bucket.angle = LocalFor(tilt);
            bucket.Apply();
            if (steer.node != null) steer.Apply();
            foreach (var w in wheels) w.Apply();
            foreach (var r in rams)
            {
                r.node.localRotation = r.rest;
                Vector3 want = r.other.position - r.node.position;
                if (want.sqrMagnitude > 1e-6f)
                    r.node.rotation = Quaternion.FromToRotation(r.node.TransformDirection(r.dirLocal), want) * r.node.rotation;
            }
        }

        // ------------------------------------------------------------------ reset

        /// <summary>Back to the start pose, keeping the parking hold if nobody is driving.</summary>
        public void Respawn()
        {
            bool parked = Parked;
            ResetPose();
            Parked = parked;
        }

        public void ResetPose()
        {
            Load.ReleaseAll(ledger, Collision);
            StopPowered();
            Parked = false;
            body.position = startPos;
            body.rotation = startRot;
            transform.SetPositionAndRotation(startPos, startRot);
            foreach (var w in wheels) w.angle = 0f;
            Terrain.Reset();
            SetPose(0f, 0f, 0f);
            Blocked = 0;
            Physics.SyncTransforms();
        }

        /// <summary>Apply Inspector edits to the load limits (they are read live elsewhere).</summary>
        void OnValidate() => ApplyTuningToLoad();
    }
}
