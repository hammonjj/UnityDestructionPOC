using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Turns the imported WreckingCrane FBX into something drivable: slews the carriage, luffs the boom, winches
    /// the ball, drives the tracks. The ball is a free rigid body hung from the boom tip by a rope limit (a
    /// ConfigurableJoint), so it swings and hits the structure through the normal contact path.
    /// The FBX arrives with a x100 root scale and a rotated frame, so everything physical lives on clean
    /// scale-1 wrapper objects and the FBX nodes are used only for pose and visuals.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CraneRig : MonoBehaviour
    {
        [Header("Speeds")]
        public float slewSpeed = 40f;       // deg/s
        public float luffSpeed = 9f;        // deg/s
        public float winchSpeed = 3f;       // m/s of cable
        public float driveSpeed = 4f;       // m/s
        public float turnSpeed = 25f;       // deg/s

        [Header("Limits")]
        public float minBoomAngle = 28f;
        public float maxBoomAngle = 78f;
        public float minCable = 2.5f;
        public float maxCable = 13f;

        [Header("Ball")]
        [Tooltip("Game scale. A solid 1.5 m steel sphere is ~14 t; this is heavier so a driven or swung hit breaks the " +
                 "warehouse at the lab's damage scale (a 10 t ball at 4 m/s only damages it; 6 m/s breaks it).")]
        public float ballMass = 25000f;

        public Transform Carriage { get; private set; }
        public Transform Boom { get; private set; }
        public Rigidbody Ball { get; private set; }
        public float CableLength { get; private set; }
        public float BoomAngle { get; private set; }
        public float SlewAngle { get; private set; }

        /// <summary>Operator's eye point; turns with the carriage. The FBX nodes are x100 scaled, so seats are poses, not children.</summary>
        public Vector3 SeatPosition => Carriage.TransformPoint(seatLocal);
        public Quaternion SeatRotation => Quaternion.AngleAxis(SlewAngle, Vector3.up) * transform.rotation;
        /// <summary>Ground point beside the cab steps; turns with the carriage.</summary>
        public Vector3 DoorPosition => Carriage.TransformPoint(doorLocal);

        Vector3 seatLocal, doorLocal;
        Rigidbody body;
        Transform model;
        Transform tipAnchor, ballAttach;
        Rigidbody tipBody;
        ConfigurableJoint rope;
        float startBoomAngle, startCable, luffSign;
        Vector3 startPos;
        Quaternion startRot, startCarriageRot, startBoomRot;
        Vector3 ballStartPos;
        Quaternion ballStartRot;

        struct Link
        {
            public Transform cable, topFrame, bottomFrame;
            public Vector3 topLocal, bottomLocal;
            public float baseLength;
        }
        readonly List<Link> links = new List<Link>();
        Transform suspension;
        float suspensionBaseLength;

        /// <summary>Builds the rig from an instantiated crane FBX (child it under this object first).</summary>
        public void Build(GameObject fbxInstance)
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            model = fbxInstance.transform;
            model.SetParent(transform, false);
            // The FBX root carries the axis-conversion rotation and faces -Z; keep that and turn it so the crane
            // faces the wrapper's +Z.
            model.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.localRotation;

            Carriage = Find("Crane_UpperCarriage");
            Boom = Find("Crane_Boom");
            tipAnchor = Find("Anchor_BoomTipCable");
            ballAttach = Find("Anchor_BallAttach");
            var ballNode = Find("Crane_WreckingBall");
            suspension = Find("Crane_SuspensionCable");

            AddStructureColliders();

            // Cab seat and door, authored in wrapper space at the start pose (crane faces +Z, cab on the left, -X).
            seatLocal = Carriage.InverseTransformPoint(transform.TransformPoint(new Vector3(-0.9f, 2.95f, 0.7f)));
            doorLocal = Carriage.InverseTransformPoint(transform.TransformPoint(new Vector3(-2.4f, 0f, 0.9f)));

            // Boom luff sign: which way does +angle about the lateral axis lift the tip?
            Vector3 lateral = Carriage.right;
            Vector3 reach = tipAnchor.position - Boom.position;
            luffSign = Vector3.Cross(lateral, reach).y > 0f ? 1f : -1f;
            startBoomAngle = BoomAngle = Mathf.Atan2(reach.y, new Vector3(reach.x, 0f, reach.z).magnitude) * Mathf.Rad2Deg;

            foreach (var name in new[] { "Crane_BoomSupportCable_L", "Crane_BoomSupportCable_R" })
            {
                var c = Find(name);
                float len = CableLengthOf(c);
                Vector3 top = c.position;
                Vector3 bottom = top - c.forward * len;
                links.Add(new Link
                {
                    cable = c, topFrame = Carriage, bottomFrame = Boom,
                    topLocal = Carriage.InverseTransformPoint(top),
                    bottomLocal = Boom.InverseTransformPoint(bottom),
                    baseLength = len,
                });
            }
            suspensionBaseLength = CableLengthOf(suspension);

            BuildBall(ballNode);

            startPos = transform.position;
            startRot = transform.rotation;
            startCarriageRot = Carriage.localRotation;
            startBoomRot = Boom.localRotation;
            startCable = CableLength;
            LateUpdate();
        }

        /// <summary>
        /// The cab glass is an opaque box on the outside. From the seat it would block the view, so while the
        /// operator is inside it is swapped for a see-through copy.
        /// </summary>
        public void SetOperatorInside(bool inside)
        {
            var r = Carriage.GetComponent<MeshRenderer>();
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].name.StartsWith("CraneCabGlass")) continue;
                if (glassOpaque == null) glassOpaque = mats[i];
                if (glassClear == null)
                {
                    glassClear = new Material(glassOpaque) { name = "CraneCabGlass (see-through)" };
                    glassClear.SetFloat("_Surface", 1f);
                    glassClear.SetFloat("_Blend", 0f);
                    glassClear.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    glassClear.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    glassClear.SetFloat("_ZWrite", 0f);
                    glassClear.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    glassClear.SetOverrideTag("RenderType", "Transparent");
                    glassClear.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    var c = glassOpaque.GetColor("_BaseColor");
                    c.a = 0.12f;
                    glassClear.SetColor("_BaseColor", c);
                }
                mats[i] = inside ? glassClear : glassOpaque;
            }
            r.sharedMaterials = mats;
        }

        Material glassOpaque, glassClear;

        // ------------------------------------------------------------------ controls (call from Update)

        /// <summary>Rotate the upper carriage. +1 turns clockwise seen from above.</summary>
        public void Slew(float input, float dt)
        {
            float d = input * slewSpeed * dt;
            if (Mathf.Approximately(d, 0f)) return;
            Carriage.Rotate(transform.up, d, Space.World);
            SlewAngle += d;
        }

        /// <summary>Raise (+1) or lower (-1) the boom.</summary>
        public void Luff(float input, float dt)
        {
            float d = Mathf.Clamp(BoomAngle + input * luffSpeed * dt, minBoomAngle, maxBoomAngle) - BoomAngle;
            if (Mathf.Approximately(d, 0f)) return;
            Boom.RotateAround(Boom.position, Carriage.right, d * luffSign);
            BoomAngle += d;
        }

        /// <summary>Pay out cable (+1, ball goes down) or reel it in (-1).</summary>
        public void Winch(float input, float dt)
        {
            if (Mathf.Approximately(input, 0f)) return;
            CableLength = Mathf.Clamp(CableLength + input * winchSpeed * dt, minCable, maxCable);
            var limit = rope.linearLimit;
            limit.limit = CableLength;
            rope.linearLimit = limit;
            Ball.WakeUp();
        }

        float pendingForward, pendingTurn;

        /// <summary>Drive the tracks: forward/back and turn on the spot. Applied in FixedUpdate.</summary>
        public void Drive(float forward, float turn)
        {
            pendingForward = forward;
            pendingTurn = turn;
        }

        // ------------------------------------------------------------------ simulation

        void FixedUpdate()
        {
            if (body == null) return;
            float dt = Time.fixedDeltaTime;
            if (pendingForward != 0f || pendingTurn != 0f)
            {
                body.MoveRotation(Quaternion.AngleAxis(pendingTurn * turnSpeed * dt, Vector3.up) * body.rotation);
                body.MovePosition(body.position + body.rotation * Vector3.forward * (pendingForward * driveSpeed * dt));
                Ball.WakeUp();
            }
            tipBody.MovePosition(tipAnchor.position);
        }

        void LateUpdate()
        {
            if (Ball == null) return;
            Stretch(suspension, tipAnchor.position, ballAttach.position, suspensionBaseLength);
            foreach (var l in links)
                Stretch(l.cable, l.topFrame.TransformPoint(l.topLocal), l.bottomFrame.TransformPoint(l.bottomLocal), l.baseLength);
        }

        public void ResetPose()
        {
            body.position = startPos;
            body.rotation = startRot;
            transform.SetPositionAndRotation(startPos, startRot);
            Carriage.localRotation = startCarriageRot;
            Boom.localRotation = startBoomRot;
            BoomAngle = startBoomAngle;
            SlewAngle = 0f;
            CableLength = startCable;
            var limit = rope.linearLimit;
            limit.limit = CableLength;
            rope.linearLimit = limit;
            tipBody.position = tipAnchor.position;
            Ball.linearVelocity = Vector3.zero;
            Ball.angularVelocity = Vector3.zero;
            Ball.position = ballStartPos;
            Ball.rotation = ballStartRot;
            Ball.transform.SetPositionAndRotation(ballStartPos, ballStartRot);
            Physics.SyncTransforms();
            Ball.WakeUp();
        }

        // ------------------------------------------------------------------ setup helpers

        Transform Find(string name)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            throw new System.InvalidOperationException($"Crane FBX is missing '{name}'.");
        }

        static float CableLengthOf(Transform cable)
        {
            var mf = cable.GetComponent<MeshFilter>();
            return mf.sharedMesh.bounds.size.z * cable.lossyScale.z;
        }

        /// <summary>Cable meshes run along local -Z from their origin; place, aim and stretch one between two points.</summary>
        static void Stretch(Transform cable, Vector3 top, Vector3 bottom, float baseLength)
        {
            Vector3 d = bottom - top;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            cable.SetPositionAndRotation(top, Quaternion.LookRotation(-d / len, Mathf.Abs(d.y / len) > 0.99f ? Vector3.forward : Vector3.up));
            var s = cable.localScale;
            s.z = len / baseLength;
            cable.localScale = s;
        }

        void AddStructureColliders()
        {
            foreach (var name in new[] { "Crane_Base", "Crane_TrackL", "Crane_TrackR", "Crane_UpperCarriage" })
            {
                var t = Find(name);
                if (t.GetComponent<MeshCollider>() == null) t.gameObject.AddComponent<MeshCollider>();
            }
        }

        void BuildBall(Transform ballNode)
        {
            ballStartPos = ballNode.position;
            ballStartRot = Quaternion.identity;

            var go = new GameObject("WreckingBall");
            go.transform.SetPositionAndRotation(ballNode.position, Quaternion.identity);
            ballNode.SetParent(go.transform, true);

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.75f;
            col.providesContacts = true;
            col.sharedMaterial = new PhysicsMaterial("WreckingBall") { dynamicFriction = 0.5f, staticFriction = 0.5f, bounciness = 0.1f };

            Ball = go.AddComponent<Rigidbody>();
            Ball.mass = ballMass;
            Ball.linearDamping = 0.03f;
            Ball.angularDamping = 0.6f;
            Ball.interpolation = RigidbodyInterpolation.Interpolate;
            Ball.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Ball.solverIterations = 24;
            Ball.solverVelocityIterations = 8;
            Ball.maxAngularVelocity = 20f;

            var tipGo = new GameObject("BoomTipAnchor");
            tipGo.transform.position = tipAnchor.position;
            tipBody = tipGo.AddComponent<Rigidbody>();
            tipBody.isKinematic = true;
            tipBody.useGravity = false;

            CableLength = Vector3.Distance(tipAnchor.position, ballAttach.position);
            rope = go.AddComponent<ConfigurableJoint>();
            rope.autoConfigureConnectedAnchor = false;
            rope.connectedBody = tipBody;
            rope.anchor = go.transform.InverseTransformPoint(ballAttach.position);
            rope.connectedAnchor = Vector3.zero;
            rope.xMotion = rope.yMotion = rope.zMotion = ConfigurableJointMotion.Limited;
            rope.angularXMotion = rope.angularYMotion = rope.angularZMotion = ConfigurableJointMotion.Free;
            rope.linearLimit = new SoftJointLimit { limit = CableLength, contactDistance = 0.05f };
            rope.linearLimitSpring = default;
            rope.enablePreprocessing = false;

            // The ball never collides with the crane it hangs from.
            foreach (var c in GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(col, c);
        }
    }
}
