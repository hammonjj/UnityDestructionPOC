using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>Every tunable of the landfill dozer, grouped for the Inspector. CraneTestBootstrap holds one and applies it.</summary>
    [Serializable]
    public sealed class DozerTuning
    {
        [Header("Drive")]
        [Tooltip("Top speed, m/s.")] public float maxSpeed = 3.4f;
        [Tooltip("Reverse top speed as a fraction of forward.")] [Range(0.3f, 1f)] public float reverseScale = 0.9f;
        [Tooltip("Speed gained per second under power, m/s².")] public float accel = 4f;
        [Tooltip("Speed lost per second when braking or coasting to a stop, m/s².")] public float brake = 12f;
        [Tooltip("Share of track speed used for steering. 0.5 pivots in place at half track speed.")] [Range(0.1f, 1f)] public float turnMix = 0.75f;

        [Header("Blade")]
        [Tooltip("Blade lift speed, deg/s.")] public float liftSpeed = 14f;
        public float liftAccel = 45f;
        [Tooltip("Largest blade angle above the lowered rest pose, degrees. From the model handoff.")] public float liftMaxAngle = 38f;

        [Header("Pushing")]
        [Tooltip("Debris mass ahead of the blade at which the dozer stalls, kg. Roughly its drawbar pull; a single body heavier than this is solid.")]
        public float stallPushMass = 35000f;
        [Tooltip("Debris mass the blade pushes without slowing, kg.")] public float freePushMass = 1500f;
        [Tooltip("Top speed with a load just under the stall mass, m/s.")] public float crawlSpeed = 0.25f;

        [Header("Ramming")]
        [Tooltip("Direct damage to the structure piece the blade hits at top speed. Scales with the square of impact speed; 1 breaks a piece's joints.")]
        public float ramDamage = 0.12f;
        [Tooltip("Impacts slower than this do no damage, m/s.")] public float ramMinSpeed = 0.8f;
        [Tooltip("Shortest time between two damaging impacts, s.")] public float ramCooldown = 0.5f;

        [Header("Gamepad")]
        [Tooltip("Response curve on stick input: 1 is linear, higher is gentler near the centre.")] [Range(1f, 2.5f)] public float stickExponent = 1.4f;
        public bool invertDrive, invertSteer, invertLift;
    }

    /// <summary>
    /// A playable landfill dozer for CraneTest, built from LandfillDozer.fbx (see Tools/blender/LandfillDozer/HANDOFF.md
    /// for the node contract). A kinematic root moves like a crawler: left and right tracks run at separate speeds and
    /// the yaw rate is their difference, so it pivots in place. The blade (with its push arms and trash rack) lifts about
    /// the trunnion. Like the other machines the root is tested against the world with <see cref="RigCollision"/>:
    /// solid structure blocks it and loose debris is pushed ahead of the blade. The debris load slows the machine
    /// (<see cref="DozerTuning.stallPushMass"/>), and hitting structure with the blade at speed damages it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DozerRig : MonoBehaviour, IOperableRig, ILevelRig
    {
        public DozerTuning tuning = new DozerTuning();
        public DestructionWorld world;

        // ------------------------------------------------------------------ state (read by tests)

        public float BladeAngle => lift.angle;
        /// <summary>Cutting edge height above the machine's ground plane, m.</summary>
        public float EdgeHeight => Edge.position.y - transform.position.y;
        public float Speed => (leftSpeed + rightSpeed) * 0.5f;
        public float YawRate { get; private set; }
        public int Blocked { get; private set; }
        /// <summary>Loose debris mass the blade is pushing, kg.</summary>
        public float PushLoad { get; private set; }
        /// <summary>Damaging blade impacts on structure.</summary>
        public int RamHits { get; private set; }
        public bool Parked { get; private set; }
        public Transform Edge { get; private set; }
        public Transform Chassis { get; private set; }
        public Transform BladeNode => lift.node;
        public readonly RigCollision Collision = new RigCollision();

        public string RigName => "Landfill dozer";
        public string AttachmentName => "Blade";
        public Vector3 DoorPosition => door != null ? GroundPoint(door.position) : transform.position - transform.right * 2.8f;
        public Vector3 SeatPosition => seat != null ? seat.position : transform.position + Vector3.up * 3f;
        public Quaternion SeatRotation => Quaternion.LookRotation(transform.forward, Vector3.up);

        public Vector3 CameraFocus
        {
            get
            {
                Vector3 p = cameraFocusAnchor != null ? cameraFocusAnchor.position : Vector3.Lerp(transform.position, Edge.position, 0.6f);
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

        struct Ram { public Transform node, other; public Quaternion rest; public Vector3 dirLocal; }

        readonly Hinge lift = new Hinge();
        readonly List<Hinge> sprockets = new List<Hinge>();
        readonly List<bool> sprocketLeft = new List<bool>();
        readonly List<Ram> rams = new List<Ram>();
        readonly List<Collider> ownColliders = new List<Collider>(), bladeColliders = new List<Collider>();
        float sprocketRadius = 0.45f, trackWidth = 2.6f;
        float leftSpeed, rightSpeed;
        float inDrive, inSteer, inLift;
        float ramTimer;
        static readonly Collider[] contacts = new Collider[64];
        Transform seat, door, cameraFocusAnchor;
        Transform[] exits = new Transform[0];
        Rigidbody body;
        Vector3 startPos;
        Quaternion startRot;

        // ------------------------------------------------------------------ build

        public void Build(GameObject fbxInstance)
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var model = fbxInstance.transform;
            model.SetParent(transform, false);
            var bladeNode = Find(model, "DZ_Blade");
            Edge = Find(model, "Anchor_BladeEdge");
            Chassis = Find(model, "DZ_Chassis");
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

            // Face the wrapper's +Z whatever frame the FBX arrives in: the blade is ahead of the machine.
            AlignModel(transform, model);

            Vector3 lateral = transform.right;
            Setup(lift, bladeNode, lateral);
            foreach (var n in new[] { "DZ_Sprocket_L", "DZ_Sprocket_R" })
            {
                var node = FindOptional(model, n);
                if (node == null) continue;
                var h = new Hinge();
                Setup(h, node, lateral);
                sprockets.Add(h);
                sprocketLeft.Add(transform.InverseTransformPoint(node.position).x < 0f);
                var r = node.GetComponent<Renderer>();
                if (r != null) sprocketRadius = Mathf.Max(0.2f, r.bounds.extents.y);
            }
            if (sprockets.Count == 2) trackWidth = Mathf.Max(1f, Mathf.Abs(transform.InverseTransformPoint(sprockets[0].node.position).x - transform.InverseTransformPoint(sprockets[1].node.position).x));

            AddRam(model, "DZ_LiftRam_L", "DZ_LiftRamRod_L");
            AddRam(model, "DZ_LiftRam_R", "DZ_LiftRamRod_R");

            // Signs from the geometry, so they hold whatever axis conventions the import uses.
            lift.sign = Probe(lift, () => Edge.position.y);          // + raises the blade
            // Sprocket handoff: the track bottom runs backward when driving forward, so forward is negative rotation_euler.x.
            // The loader wheels roll with +; here the lateral-axis sense is the same but the visible spin is flipped.
            foreach (var h in sprockets) h.sign = -1f;

            AddColliders(model);
            startPos = transform.position;
            startRot = transform.rotation;
            SetPose(0f);
        }

        /// <summary>Jump to a blade angle with no motion.</summary>
        public void SetPose(float liftDeg)
        {
            lift.angle = Mathf.Clamp(liftDeg, 0f, tuning.liftMaxAngle);
            lift.vel = 0f;
            ApplyPose();
        }

        [Header("Level prefab")]
        [Tooltip("The dozer FBX instance under this object. Built when the level starts.")]
        public GameObject authoredModel;

        public bool IsBuilt => body != null;

        public void BuildInLevel(DestructionWorld levelWorld, CleanupLedger ledger, Collider ground)
        {
            if (IsBuilt || authoredModel == null) return;
            world = levelWorld;
            if (ground != null) Collision.ignore.Add(ground);
            Build(authoredModel);
        }

        /// <summary>Turn a dozer FBX instance under <paramref name="root"/> to face +Z (the blade ahead).</summary>
        public static void AlignModel(Transform root, Transform model)
        {
            var edge = RigModel.Find(model, "Anchor_BladeEdge");
            if (edge != null) RigModel.FaceForward(root, model, edge.position, root.position);
        }

        static Transform FindOptional(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Transform Find(Transform root, string name) =>
            FindOptional(root, name) ?? throw new InvalidOperationException($"Dozer FBX is missing '{name}'.");

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

        void AddRam(Transform model, string barrel, string rod)
        {
            var a = FindOptional(model, barrel);
            var b = FindOptional(model, rod);
            if (a == null || b == null) return;
            rams.Add(new Ram { node = a, other = b, rest = a.localRotation, dirLocal = a.InverseTransformDirection(b.position - a.position) });
            rams.Add(new Ram { node = b, other = a, rest = b.localRotation, dirLocal = b.InverseTransformDirection(a.position - b.position) });
        }

        /// <summary>BoxColliders from the model's Col_* helper meshes (hidden), grouped by the part they ride on.</summary>
        void AddColliders(Transform model)
        {
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
                if (mf.transform.IsChildOf(lift.node)) bladeColliders.Add(box);
            }
            if (ownColliders.Count == 0) Debug.LogError($"[DestructionLab] {RigName} FBX has no Col_* collider helpers.");
        }

        // ------------------------------------------------------------------ controls

        /// <summary>Latch this frame's requests, each −1..1: drive (+ forward), steer (+ right), lift (+ raise the blade).</summary>
        public void Command(float drive, float steerIn, float liftIn)
        {
            inDrive = drive;
            inSteer = steerIn;
            inLift = liftIn;
        }

        public InputActionMap ControlMap(CraneTestInput input) => input.loader;

        public void OnEnter()
        {
            Parked = false;
            StopPowered();
        }

        /// <summary>Parking hold: the machine stops dead and stays put; the blade keeps its pose.</summary>
        public void OnExit()
        {
            StopPowered();
            Parked = true;
        }

        void StopPowered()
        {
            Command(0f, 0f, 0f);
            leftSpeed = rightSpeed = 0f;
            lift.vel = 0f;
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
            Command(Shape(input.LoaderDrive.ReadValue<float>(), pad, tuning.invertDrive),
                    Shape(input.LoaderSteer.ReadValue<float>(), pad, tuning.invertSteer),
                    Shape(input.LoaderLift.ReadValue<float>(), pad, tuning.invertLift));
        }

        public void ExitCandidates(List<Vector3> into)
        {
            into.Add(DoorPosition);
            foreach (var e in exits) into.Add(GroundPoint(e.position));
            var t = transform;
            into.Add(t.position - t.right * 3.4f);
            into.Add(t.position + t.right * 3.4f);
            into.Add(t.position - t.forward * 7f);
        }

        Vector3 GroundPoint(Vector3 p) => new Vector3(p.x, transform.position.y, p.z);

        public void ControlHints(CraneTestInput input, List<ControlHint> into)
        {
            bool pad = input.UsingGamepad;
            into.Add(ControlHint.Row(input.Keys(input.LoaderDrive), "Drive forward / reverse"));
            into.Add(ControlHint.Row(input.Keys(input.LoaderSteer, pad, negativeFirst: true), "Turn left / right (pivots in place)"));
            into.Add(ControlHint.Row(input.Keys(input.LoaderLift), "Raise / lower blade"));
        }

        public string Telemetry => $"Blade {lift.angle:0}°  (edge {EdgeHeight:0.00} m)  push {PushLoad / 1000f:0.0} t";

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
            ramTimer = Mathf.Max(0f, ramTimer - dt);
            if (Parked) { leftSpeed = rightSpeed = 0f; YawRate = 0f; PushLoad = 0f; return; }

            // Arcade mix: steering adds to one track and subtracts from the other, then scales down to stay in range.
            Vector3 pos = body.position;
            Quaternion rot = body.rotation;
            float scale = inDrive < 0f ? tuning.reverseScale : 1f;
            float l = inDrive * scale + inSteer * tuning.turnMix, r = inDrive * scale - inSteer * tuning.turnMix;
            float m = Mathf.Max(1f, Mathf.Max(Mathf.Abs(l), Mathf.Abs(r)));
            float lt = l / m * tuning.maxSpeed, rt = r / m * tuning.maxSpeed;

            // The load on the blade caps forward speed: full speed up to freePushMass, a crawl near stallPushMass and a
            // stall past it. Reversing and pivoting are never capped, so a stalled machine can always back out.
            Collision.pushableMass = tuning.stallPushMass; // anything lighter is pushed; one body past the stall is solid
            PushLoad = Collision.PushLoad(bladeColliders, transform.forward);
            float fwd = (lt + rt) * 0.5f, top = TopSpeedUnderLoad(PushLoad);
            if (fwd > top)
            {
                lt *= top / fwd;
                rt *= top / fwd;
            }
            leftSpeed = Approach(leftSpeed, lt, tuning.accel, tuning.brake, dt);
            rightSpeed = Approach(rightSpeed, rt, tuning.accel, tuning.brake, dt);
            float v = (leftSpeed + rightSpeed) * 0.5f;
            YawRate = (leftSpeed - rightSpeed) / trackWidth * Mathf.Rad2Deg; // + clockwise from above (turning right)
            if (Mathf.Abs(v) < 1e-4f && Mathf.Abs(YawRate) < 1e-3f) return;
            Quaternion rot2 = Quaternion.AngleAxis(YawRate * dt, Vector3.up) * rot;
            pos += (Quaternion.Slerp(rot, rot2, 0.5f) * Vector3.forward) * v * dt;
            rot = rot2;
            if (Collision.MoveBlocked(transform, body, ownColliders, pos, rot))
            {
                if (v >= tuning.ramMinSpeed) RamImpact(v);
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

        /// <summary>Forward speed limit (m/s) with <paramref name="load"/> kg of debris on the blade. Square-root falloff, so
        /// a full blade already slows the machine well before the stall.</summary>
        public float TopSpeedUnderLoad(float load)
        {
            if (load <= tuning.freePushMass) return tuning.maxSpeed;
            if (load >= tuning.stallPushMass) return 0f;
            float t = Mathf.InverseLerp(tuning.freePushMass, tuning.stallPushMass, load);
            return Mathf.Lerp(tuning.maxSpeed, tuning.crawlSpeed, Mathf.Sqrt(t));
        }

        /// <summary>The blade hit something solid at <paramref name="speed"/> m/s: damage the structure piece nearest the
        /// cutting edge, scaled like impact energy, at most once per <see cref="DozerTuning.ramCooldown"/>.</summary>
        void RamImpact(float speed)
        {
            if (ramTimer > 0f || world == null || world.Graph == null) return;
            int best = -1;
            float bestD = float.MaxValue;
            Vector3 edge = Edge.position;
            foreach (var c in bladeColliders)
            {
                if (!(c is BoxCollider box)) continue;
                int n = RigCollision.OverlapBox(box, 0.15f, contacts);
                for (int k = 0; k < n; k++)
                {
                    var o = contacts[k];
                    if (!Collision.Blocks(o) || !world.TryGetPiece(o, out int i)) continue;
                    if (i < 0 || i >= world.Graph.PieceCount || world.pieces[i] == null || world.pieces[i].removed) continue;
                    if (world.Settings.Material(world.Graph.pieces[i].material).name == ExcavatorTestSite.SteelName) continue;
                    float d = (o.ClosestPoint(edge) - edge).sqrMagnitude;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
            }
            if (best < 0) return;
            float f = Mathf.Clamp01(speed / tuning.maxSpeed);
            world.Damage(best, tuning.ramDamage * f * f);
            ramTimer = tuning.ramCooldown;
            RamHits++;
        }

        void LateUpdate()
        {
            if (body == null) return;
            float dt = Time.deltaTime;
            if (!Parked || inLift != 0f) StepBlade(dt);
            SpinSprockets(dt);
            ApplyPose();
        }

        /// <summary>Move the blade one frame, undoing the step if it would push it further into something solid or the
        /// edge below the ground.</summary>
        void StepBlade(float dt)
        {
            lift.vel = Approach(lift.vel, inLift * tuning.liftSpeed, tuning.liftAccel, tuning.liftAccel * 2f, dt);
            float nl = Mathf.Clamp(lift.angle + lift.vel * dt, 0f, tuning.liftMaxAngle);
            if (nl == 0f || nl == tuning.liftMaxAngle) lift.vel = 0f;
            if (nl != lift.angle && !TryBlade(nl)) lift.vel = 0f;
        }

        bool TryBlade(float newLift)
        {
            float before = lift.angle;
            ApplyPose();
            Physics.SyncTransforms();
            float pen0 = Collision.Penetration(bladeColliders);
            lift.angle = newLift;
            ApplyPose();
            Physics.SyncTransforms();
            bool intoGround = Edge.position.y < transform.position.y - 0.01f;
            if (!intoGround && Collision.Penetration(bladeColliders) <= pen0 + Collision.tolerance) return true;
            lift.angle = before;
            ApplyPose();
            Physics.SyncTransforms();
            Blocked++;
            return false;
        }

        void SpinSprockets(float dt)
        {
            for (int i = 0; i < sprockets.Count; i++)
            {
                float v = sprocketLeft[i] ? leftSpeed : rightSpeed;
                sprockets[i].angle = Mathf.Repeat(sprockets[i].angle + v * dt / sprocketRadius * Mathf.Rad2Deg, 360f);
            }
        }

        void ApplyPose()
        {
            lift.Apply();
            foreach (var s in sprockets) s.Apply();
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
            StopPowered();
            Parked = false;
            body.position = startPos;
            body.rotation = startRot;
            transform.SetPositionAndRotation(startPos, startRot);
            foreach (var s in sprockets) s.angle = 0f;
            SetPose(0f);
            Blocked = 0;
            RamHits = 0;
            ramTimer = 0f;
            Physics.SyncTransforms();
        }
    }
}
