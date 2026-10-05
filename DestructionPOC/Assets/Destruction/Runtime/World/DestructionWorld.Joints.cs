using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    public enum JointTorqueReference { Anchor, BodyCenterOfMass }

    /// <summary>Residual attachments: ConfigurableJoints at Residual connections between different clusters.</summary>
    public sealed partial class DestructionWorld
    {
        /// <summary>Point Unity reports Joint.currentTorque about. Verified by the residual PlayMode tests.</summary>
        public static JointTorqueReference TorqueReference = JointTorqueReference.Anchor;

        public sealed class JointRecord
        {
            public Connection connection;
            public ConfigurableJoint joint;
            public int owner;    // piece carrying the joint (dynamic side)
            public int support;  // other piece, or Connection.Ground
            public RigidCluster ownerCluster;
            public RigidCluster supportCluster; // null for ground / static structure
            public float twistAtCreation;
            public int createdStep;
            public Vector3 lastHingeWorld;
            public float lastAxisTorqueRaw, lastForceRaw;
        }

        readonly Dictionary<int, JointRecord> joints = new Dictionary<int, JointRecord>();
        public IReadOnlyDictionary<int, JointRecord> Joints => joints;

        /// <summary>Test hook: destroys and recreates every residual joint at the current pose.</summary>
        public void DebugRecreateAllJoints()
        {
            foreach (var id in new List<int>(joints.Keys)) DestroyJointFor(Graph.connections[id]);
            SyncJoints();
        }

        void ClearJoints()
        {
            foreach (var r in joints.Values)
                if (r.joint != null) DestroyImmediate(r.joint);
            joints.Clear();
        }

        void DestroyJointFor(Connection c)
        {
            if (!joints.TryGetValue(c.id, out var r)) return;
            if (r.joint != null) DestroyImmediate(r.joint);
            joints.Remove(c.id);
            c.jointActive = false;
            if (r.ownerCluster != null && r.ownerCluster.body != null) r.ownerCluster.body.WakeUp();
        }

        void DestroyJointsOnCluster(RigidCluster k)
        {
            var remove = new List<int>();
            foreach (var kv in joints)
                if (kv.Value.ownerCluster == k || kv.Value.supportCluster == k) remove.Add(kv.Key);
            foreach (int id in remove) DestroyJointFor(Graph.connections[id]);
        }

        RigidCluster SupportClusterOf(int piece)
        {
            if (piece < 0) return null;
            var k = pieces[piece].cluster;
            return k != null && k.isStatic ? null : k;
        }

        bool IsStaticSide(int piece) => piece < 0 || pieces[piece].cluster == null || pieces[piece].cluster.isStatic;

        /// <summary>Creates, keeps or removes joints so that every Residual connection between different
        /// clusters has exactly one joint, subject to the per-cluster budget (parallel hinges only).</summary>
        void SyncJoints()
        {
            // Drop stale records.
            var stale = new List<int>();
            foreach (var kv in joints)
            {
                var r = kv.Value;
                var c = r.connection;
                bool ok = r.joint != null
                          && c.state == ConnectionState.Residual
                          && r.ownerCluster != null && r.ownerCluster.gameObject.activeSelf
                          && pieces[r.owner].cluster == r.ownerCluster
                          && SupportClusterOf(r.support) == r.supportCluster
                          && (r.supportCluster == null || r.supportCluster.gameObject.activeSelf);
                if (!ok) stale.Add(kv.Key);
            }
            foreach (int id in stale) DestroyJointFor(Graph.connections[id]);

            // Budget from surviving joints.
            var count = new Dictionary<RigidCluster, int>();
            var axis = new Dictionary<RigidCluster, Vector3>();
            foreach (var r in joints.Values)
            {
                Vector3 ax = WorldAxis(r.connection);
                Count(count, axis, r.ownerCluster, ax);
                if (r.supportCluster != null) Count(count, axis, r.supportCluster, ax);
            }

            var wanted = new List<Connection>();
            foreach (var c in Graph.connections)
            {
                if (c.state != ConnectionState.Residual || joints.ContainsKey(c.id)) continue;
                if (pieces[c.a].removed || (!c.IsGround && pieces[c.b].removed)) continue;
                bool aStatic = IsStaticSide(c.a), bStatic = IsStaticSide(c.b);
                if (aStatic && bStatic) continue; // dormant: no relative motion possible
                if (!c.IsGround && pieces[c.a].cluster == pieces[c.b].cluster) continue; // dormant: same rigid body
                wanted.Add(c);
            }
            wanted.Sort((x, y) => (y.residualForceCapacity * y.ResidualFactor).CompareTo(x.residualForceCapacity * x.ResidualFactor));

            int max = Settings.residual.maxJointsPerCluster;
            foreach (var c in wanted)
            {
                bool aStatic = IsStaticSide(c.a), bStatic = IsStaticSide(c.b);
                int owner, support;
                if (bStatic) { owner = c.a; support = c.b; }
                else if (aStatic) { owner = c.b; support = c.a; }
                else
                {
                    bool aLighter = pieces[c.a].cluster.body.mass <= pieces[c.b].cluster.body.mass;
                    owner = aLighter ? c.a : c.b;
                    support = aLighter ? c.b : c.a;
                }
                var ok = pieces[owner].cluster;
                var sk = SupportClusterOf(support);
                Vector3 ax = WorldAxis(c);
                if (!Allowed(count, axis, ok, ax, max) || (sk != null && !Allowed(count, axis, sk, ax, max)))
                {
                    var from = c.state;
                    c.state = ConnectionState.Severed;
                    c.reason = FailureReason.ResidualBudget;
                    c.residualFailTime = SimTime;
                    Record(c, from, ConnectionState.Severed, FailureReason.ResidualBudget, c.qResidual, c.residualDamage);
                    continue;
                }
                CreateJoint(c, owner, support);
                Count(count, axis, ok, ax);
                if (sk != null) Count(count, axis, sk, ax);
            }
        }

        static void Count(Dictionary<RigidCluster, int> count, Dictionary<RigidCluster, Vector3> axis, RigidCluster k, Vector3 ax)
        {
            count.TryGetValue(k, out int n);
            count[k] = n + 1;
            if (!axis.ContainsKey(k)) axis[k] = ax;
        }

        static bool Allowed(Dictionary<RigidCluster, int> count, Dictionary<RigidCluster, Vector3> axis, RigidCluster k, Vector3 ax, int max)
        {
            count.TryGetValue(k, out int n);
            if (n >= max) return false;
            if (axis.TryGetValue(k, out var a) && Mathf.Abs(Vector3.Dot(a, ax)) < 0.9f) return false;
            return true;
        }

        Vector3 WorldAxis(Connection c) => pieces[c.a].transform.rotation * c.tangent;

        Quaternion PieceRotation(int piece) => piece < 0 ? Quaternion.identity : pieces[piece].transform.rotation;

        void CreateJoint(Connection c, int owner, int support)
        {
            var ownerCluster = pieces[owner].cluster;
            var ownerRb = ownerCluster.body;
            var supportCluster = SupportClusterOf(support);
            var supportRb = supportCluster != null ? supportCluster.body : null;
            SyncTransform(ownerCluster);
            if (supportCluster != null) SyncTransform(supportCluster);

            var ta = pieces[c.a].transform;
            Vector3 center = ta.position + ta.rotation * c.centerOffsetA;
            if (!c.hingeChosen)
            {
                Vector3 hinge = HingeMath.HingePoint(center, ta.rotation * c.depthDir, c.depth, ownerRb.worldCenterOfMass);
                c.hingeOffsetA = Quaternion.Inverse(ta.rotation) * (hinge - ta.position);
                c.hingeChosen = true;
                if (c.sagDegrees <= 0f) c.sagDegrees = Settings.residual.initialSagDegrees;
            }
            Vector3 hingeWorld = ta.position + ta.rotation * c.hingeOffsetA;
            Vector3 axisWorld = ta.rotation * c.tangent;
            Vector3 secondaryWorld = ta.rotation * c.normal;

            var ot = ownerCluster.transform;
            var j = ownerCluster.gameObject.AddComponent<ConfigurableJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.anchor = ot.InverseTransformPoint(hingeWorld);
            j.axis = ot.InverseTransformDirection(axisWorld);
            j.secondaryAxis = ot.InverseTransformDirection(secondaryWorld);
            j.connectedBody = supportRb;
            j.connectedAnchor = supportRb != null ? supportRb.transform.InverseTransformPoint(hingeWorld) : hingeWorld;

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Limited;
            j.angularYMotion = ConfigurableJointMotion.Limited;
            j.angularZMotion = ConfigurableJointMotion.Limited;

            var rs = Settings.residual;
            float twist = HingeMath.TwistDegrees(Quaternion.Inverse(PieceRotation(support)) * pieces[owner].transform.rotation, c.tangent);
            var rec = new JointRecord
            {
                connection = c, joint = j, owner = owner, support = support,
                ownerCluster = ownerCluster, supportCluster = supportCluster,
                twistAtCreation = twist, createdStep = StepIndex, lastHingeWorld = hingeWorld,
            };
            ApplyLimits(rec);
            j.angularYLimit = new SoftJointLimit { limit = rs.swingLimitDegrees };
            j.angularZLimit = new SoftJointLimit { limit = rs.swingLimitDegrees };

            float lighter = supportRb != null ? Mathf.Min(ownerRb.mass, supportRb.mass) : ownerRb.mass;
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive
            {
                positionSpring = 0f,
                positionDamper = rs.damperPerTonne * lighter / 1000f,
                maximumForce = float.MaxValue,
            };
            j.enableCollision = false;
            j.enablePreprocessing = false;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.05f;
            j.projectionAngle = 5f;
            j.breakForce = float.PositiveInfinity;   // severing is decided by our own filtered law
            j.breakTorque = float.PositiveInfinity;

            if (supportRb != null)
            {
                float ratio = Mathf.Max(ownerRb.mass, supportRb.mass) / Mathf.Max(1f, Mathf.Min(ownerRb.mass, supportRb.mass));
                if (ratio > rs.maxMassRatio)
                {
                    float scale = ratio / rs.maxMassRatio;
                    if (ownerRb.mass < supportRb.mass) j.massScale = scale; else j.connectedMassScale = scale;
                }
                supportRb.solverIterations = Settings.physics.jointedSolverIterations;
                supportRb.solverVelocityIterations = Settings.physics.jointedSolverVelocityIterations;
                supportRb.WakeUp();
            }
            ownerRb.solverIterations = Settings.physics.jointedSolverIterations;
            ownerRb.solverVelocityIterations = Settings.physics.jointedSolverVelocityIterations;
            ownerRb.WakeUp();

            joints[c.id] = rec;
            c.jointActive = true;
        }

        void ApplyLimits(JointRecord r)
        {
            HingeMath.Limits(r.connection.sagDegrees, r.twistAtCreation, HingeMath.UnityTwistSign, out float low, out float high);
            r.joint.lowAngularXLimit = new SoftJointLimit { limit = low };
            r.joint.highAngularXLimit = new SoftJointLimit { limit = high };
        }

        /// <summary>Reads the previous solve: EMA force/torque, plastic sag, residual damage law.</summary>
        void UpdateResidualJoints(float dt)
        {
            if (joints.Count == 0) return;
            var rs = Settings.residual;
            foreach (var r in joints.Values)
            {
                var c = r.connection;
                var j = r.joint;
                if (j == null || r.ownerCluster == null || r.ownerCluster.body == null) continue;
                var rb = r.ownerCluster.body;
                var ta = pieces[c.a].transform;
                Vector3 hinge = ta.position + ta.rotation * c.hingeOffsetA;
                r.lastHingeWorld = hinge;
                if (rb.IsSleeping()) continue;
                if (StepIndex - r.createdStep <= rs.warmupSteps) continue;

                Vector3 F = j.currentForce;
                Vector3 T = j.currentTorque;
                if (TorqueReference == JointTorqueReference.BodyCenterOfMass)
                    T += Vector3.Cross(rb.worldCenterOfMass - hinge, F);
                Vector3 axisW = r.ownerCluster.transform.TransformDirection(j.axis).normalized;
                float tAxis = Mathf.Abs(Vector3.Dot(T, axisW));
                float tOff = (T - axisW * Vector3.Dot(T, axisW)).magnitude;
                r.lastForceRaw = F.magnitude;
                r.lastAxisTorqueRaw = tAxis;

                float a = rs.forceSmoothing;
                c.emaForce = Vector3.Lerp(c.emaForce, F, a);
                c.emaAxisTorque = Mathf.Lerp(c.emaAxisTorque, tAxis, a);
                c.emaOffAxisTorque = Mathf.Lerp(c.emaOffAxisTorque, tOff, a);

                float f = c.ResidualFactor;
                float fCap = Mathf.Max(1f, c.residualForceCapacity * f);
                float yield = Mathf.Max(1f, c.residualYieldMoment * f);
                float offCap = yield * rs.offAxisMomentFactor;
                bool atMax = c.sagDegrees >= rs.maxSagDegrees - 0.01f;

                float q = Mathf.Max(c.emaForce.magnitude / fCap, c.emaOffAxisTorque / offCap);
                if (atMax) q = Mathf.Max(q, c.emaAxisTorque / (2f * yield));
                c.qResidual = q;

                float qRaw = Mathf.Max(F.magnitude / fCap, tOff / offCap);
                c.catastrophicSteps = qRaw >= rs.catastrophicRatio ? c.catastrophicSteps + 1 : 0;

                float sag = HingeMath.Sag(c.sagDegrees, c.emaAxisTorque, yield, dt, rs.sagRateDegreesPerSecond, rs.maxSagDegrees);
                if (sag - c.sagDegrees > 0.05f || (atMax && sag != c.sagDegrees))
                {
                    c.sagDegrees = sag;
                    ApplyLimits(r);
                }

                float before = c.residualDamage;
                c.residualDamage = DamageLaw.Accumulate(c.residualDamage, q, dt, rs.damageRate, rs.damageExponent);
                if (c.residualDamage > before) c.residualSource = DamageSource.Residual;
            }
        }

        /// <summary>
        /// Loads the structural model cannot see on its own: resting debris on static pieces (from contacts)
        /// and residual joints hanging from static pieces. Also marks pseudo-anchors.
        /// </summary>
        void BuildSupportLoads()
        {
            foreach (var r in joints.Values)
            {
                var c = r.connection;
                if (r.supportCluster != null) continue; // support is dynamic: PhysX transmits it directly
                pseudoAnchors[r.owner] = true;
                if (r.support >= 0)
                {
                    float m = Mathf.Abs(c.emaForce.y) / LoadModel.Gravity;
                    extraMass[r.support] += m;
                    extraMoment[r.support] += m * r.lastHingeWorld;
                }
            }

            if (!Settings.structure.contactLoads) return;
            foreach (var kv in contactLoads)
            {
                int piece = kv.Key.piece;
                if (piece < 0 || piece >= extraMass.Count) continue;
                float m = kv.Value.force / LoadModel.Gravity;
                extraMass[piece] += m;
                extraMoment[piece] += m * kv.Value.point;
            }
        }
    }
}
