using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// The destruction director: the only writer of structural topology and physics objects.
    ///
    /// Fixed-step order (runs before the physics step, script execution order −100):
    ///   1. residual joints read the previous solve (EMA force, plastic sag, residual damage)
    ///   2. contacts from the previous step become impact damage and resting support loads
    ///   3. queued tool actions (clicks, explosions, triggers, dropped blocks)
    ///   4. structural load model on the current poses, damage law
    ///   5. commit: at most N transitions, re-cluster, sync residual joints, wake neighbours
    ///   6. explosion impulses, once per resulting rigid body
    /// Nothing mutates colliders or joints inside physics callbacks; they only enqueue.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed partial class DestructionWorld : MonoBehaviour
    {
        public DestructionSettings settingsAsset;

        /// <summary>Runtime copy; slider changes never write back to the asset.</summary>
        public DestructionSettings Settings { get; private set; }
        public StructureGraph Graph { get; private set; }
        public Scenario CurrentScenario { get; private set; }
        public readonly List<Piece> pieces = new List<Piece>();
        public readonly List<RigidCluster> clusters = new List<RigidCluster>();
        public RigidCluster StaticCluster { get; private set; }
        public readonly EventLog log = new EventLog(2048);
        [NonSerialized] public WorldStats stats;
        public float SimTime { get; private set; }
        public int StepIndex { get; private set; }
        public LoadModel Loads { get; } = new LoadModel();

        public event Action<BreakEvent> OnBreak;
        public event Action OnRebuilt;
        /// <summary>A collision involving a piece hard enough to count as an impact (after the per-pair cooldown).</summary>
        public event Action<ImpactEvent> OnImpact;

        Transform root;
        int nextClusterId;
        bool paused;
        readonly Dictionary<Collider, int> colliderToPiece = new Dictionary<Collider, int>();
        readonly Dictionary<int, PhysicsMaterial> physicsMaterials = new Dictionary<int, PhysicsMaterial>();
        readonly Stopwatch stopwatch = new Stopwatch();

        // Per-step scratch.
        readonly List<Vector3> poses = new List<Vector3>();
        readonly List<Quaternion> rotations = new List<Quaternion>();
        readonly List<bool> pseudoAnchors = new List<bool>();
        readonly List<float> extraMass = new List<float>();
        readonly List<Vector3> extraMoment = new List<Vector3>();
        readonly List<Connection> candidates = new List<Connection>();

        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                Physics.simulationMode = paused ? SimulationMode.Script : SimulationMode.FixedUpdate;
            }
        }

        void Awake()
        {
            Settings = settingsAsset != null ? Instantiate(settingsAsset) : DestructionSettings.CreateDefault();
            Settings.name = "DestructionSettings (runtime)";
            EnsureRenderMaterials();
            root = new GameObject("Destruction Root").transform;
            root.SetParent(transform, false);
            if (GetComponent<DestructionAudio>() == null) gameObject.AddComponent<DestructionAudio>();
        }

        void OnEnable() { Physics.ContactEvent += OnContactEvent; }
        void OnDisable() { Physics.ContactEvent -= OnContactEvent; }

        void OnDestroy()
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        void FixedUpdate()
        {
            if (paused || Graph == null) return;
            PreStep(Time.fixedDeltaTime);
        }

        /// <summary>Advance exactly one fixed step while paused (also used by PlayMode tests).</summary>
        public void Step()
        {
            if (!paused) Paused = true;
            float dt = Time.fixedDeltaTime;
            PreStep(dt);
            Physics.Simulate(dt);
        }

        // ------------------------------------------------------------------ build / reset

        public int BuildCount { get; private set; }

        public void Build(Scenario scenario)
        {
            Clear();
            BuildCount++;
            CurrentScenario = scenario;
            scenario.configure?.Invoke(Settings);
            var defs = scenario.build();
            Graph = StructureGraph.Build(defs, Settings);

            StaticCluster = NewClusterObject("Static structure", true);
            for (int i = 0; i < Graph.PieceCount; i++) CreatePieceObject(i, StaticCluster);

            // Intact solve with the design live load on slabs, calibration, then a second solve without it
            // so q reflects calibrated capacities under self-weight only.
            GatherPoses();
            ResizeScratch();
            float live = Settings.structure.designLiveLoad;
            for (int i = 0; i < Graph.PieceCount; i++)
            {
                var d = Graph.pieces[i];
                if (d.kind != PieceKind.Slab || d.loose) continue;
                float m = live * d.size.x * d.size.z / LoadModel.Gravity;
                extraMass[i] = m;
                extraMoment[i] = m * d.center;
            }
            Loads.Solve(Graph, poses, rotations, null, extraMass, extraMoment, Settings.structure);
            Graph.Calibrate(Settings.structure.safetyFactor, Settings.structure.groundCapacityFactor);
            Loads.Solve(Graph, poses, rotations, null, null, null, Settings.structure);

            Recluster(null);
            for (int i = 0; i < Graph.PieceCount; i++)
            {
                var d = Graph.pieces[i];
                var body = pieces[i].cluster.body;
                if (d.initialVelocity != Vector3.zero && body != null) body.linearVelocity = d.initialVelocity;
            }
            scenario.postBuild?.Invoke(this);
            UpdateStats();
            OnRebuilt?.Invoke();
        }

        public void Clear()
        {
            ClearJoints();
            actions.Clear();
            shatterRequests.Clear();
            shatterOrder.Clear();
            pendingImpulses.Clear();
            pendingPushes.Clear();
            contacts.Clear();
            contactLoads.Clear();
            pairCooldown.Clear();
            impactImmune.Clear();
            colliderToPiece.Clear();
            log.Clear();
            pieces.Clear();
            clusters.Clear();
            StaticCluster = null;
            Graph = null;
            SimTime = 0f;
            StepIndex = 0;
            stats = default;
            removedHolder = null;
            ClearFragmentMeshes();
            ClearPieceMaterials();
            nextClusterId = 0;
            if (root != null)
            {
                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    var c = root.GetChild(i).gameObject;
                    c.SetActive(false);
                    Destroy(c);
                }
            }
        }

        // ------------------------------------------------------------------ step

        void PreStep(float dt)
        {
            SimTime += dt;
            StepIndex++;
            stopwatch.Restart();
            stats.failuresThisStep = 0;

            UpdateResidualJoints(dt);
            ProcessContacts(dt);
            ProcessActions();
            KillPlane();
            ExecuteShatters();

            GatherPoses();
            ResizeScratch();
            BuildSupportLoads();
            Loads.Solve(Graph, poses, rotations, pseudoAnchors, extraMass, extraMoment, Settings.structure);

            var st = Settings.structure;
            foreach (var c in Graph.connections)
            {
                if (c.state != ConnectionState.Structural || c.q <= 1f) continue;
                float before = c.damage;
                c.damage = DamageLaw.Accumulate(c.damage, c.q, dt, st.damageRate, st.damageExponent);
                if (c.damage > before) c.lastDamageSource = DamageSource.Overload;
                LoadModel.EvaluateRatio(c);
            }

            Commit();
            ApplyPendingImpulses();

            stopwatch.Stop();
            stats.structuralMs = (float)stopwatch.Elapsed.TotalMilliseconds;
            stats.maxStructuralMs = Mathf.Max(stats.maxStructuralMs, stats.structuralMs);
            UpdateStats();
        }

        void GatherPoses()
        {
            int n = Graph.PieceCount;
            poses.Clear();
            rotations.Clear();
            for (int i = 0; i < n; i++)
            {
                var p = pieces[i];
                if (p == null || p.removed)
                {
                    // Removed pieces have only severed connections; their pose no longer matters.
                    poses.Add(Graph.pieces[i].center);
                    rotations.Add(Quaternion.identity);
                    continue;
                }
                var t = p.transform;
                poses.Add(t.position);
                rotations.Add(t.rotation);
            }
        }

        void ResizeScratch()
        {
            int n = Graph.PieceCount;
            pseudoAnchors.Clear();
            extraMass.Clear();
            extraMoment.Clear();
            for (int i = 0; i < n; i++)
            {
                pseudoAnchors.Add(false);
                extraMass.Add(0f);
                extraMoment.Add(Vector3.zero);
            }
        }

        void Commit()
        {
            candidates.Clear();
            float instant = Settings.structure.instantFailRatio;
            foreach (var c in Graph.connections)
            {
                if (c.state == ConnectionState.Structural)
                {
                    if (c.violentPending || c.damage >= 1f || c.q >= instant) candidates.Add(c);
                }
                else if (c.state == ConnectionState.Residual)
                {
                    if (c.residualDamage >= 1f || c.catastrophicSteps >= 3) candidates.Add(c);
                }
            }
            if (candidates.Count == 0) { stats.pendingFailures = 0; return; }

            candidates.Sort((x, y) => Severity(y).CompareTo(Severity(x)));
            int cap = Mathf.Max(1, Settings.impact.maxFailuresPerStep);
            int committed = 0;
            for (int k = 0; k < candidates.Count && committed < cap; k++)
            {
                Transition(candidates[k]);
                committed++;
            }
            stats.failuresThisStep = committed;
            stats.pendingFailures = candidates.Count - committed;

            Recluster(candidates.GetRange(0, committed));
        }

        float Severity(Connection c)
        {
            if (c.violentPending) return 1000f;
            if (c.state == ConnectionState.Residual) return 10f + c.residualDamage;
            return Mathf.Max(c.q, c.damage);
        }

        void Transition(Connection c)
        {
            var from = c.state;
            float qAt = Mathf.Min(99f, from == ConnectionState.Structural ? c.q : c.qResidual);
            float dAt = from == ConnectionState.Structural ? c.damage : c.residualDamage;
            FailureReason reason;
            ConnectionState to;

            if (from == ConnectionState.Structural)
            {
                bool overload = !c.violentPending && c.damage < 1f && c.q >= Settings.structure.instantFailRatio;
                reason = overload ? FailureReason.StructuralOverload : ReasonFrom(c.lastDamageSource);
                bool destructive = c.violentPending
                    || c.lastDamageSource == DamageSource.Direct
                    || c.lastDamageSource == DamageSource.Explosion;
                to = (overload || !destructive) && Settings.residual.enabled ? ConnectionState.Residual : ConnectionState.Severed;
                c.failTime = SimTime;
                c.reason = reason;
                c.violentPending = false;
                if (to == ConnectionState.Residual)
                {
                    c.residualDamage = 0f;
                    c.residualSource = DamageSource.None;
                    c.sagDegrees = Settings.residual.initialSagDegrees;
                    c.emaForce = Vector3.zero;
                    c.emaAxisTorque = c.emaOffAxisTorque = 0f;
                    c.catastrophicSteps = 0;
                }
            }
            else
            {
                reason = c.residualSource == DamageSource.Direct ? FailureReason.DirectDamage
                    : c.residualSource == DamageSource.Explosion ? FailureReason.Explosion
                    : c.residualSource == DamageSource.Impact ? FailureReason.Impact
                    : FailureReason.ResidualJointFailure;
                to = ConnectionState.Severed;
                c.residualFailTime = SimTime;
                c.reason = reason;
            }

            c.state = to;
            if (to == ConnectionState.Severed)
            {
                DestroyJointFor(c);
                ShrinkIfDebris(c.a);
                if (!c.IsGround) ShrinkIfDebris(c.b);
            }
            Record(c, from, to, reason, qAt, dAt);
        }

        /// <summary>A piece with no live connections left is debris: shrink it slightly so it cannot wedge.</summary>
        void ShrinkIfDebris(int i)
        {
            var p = pieces[i];
            if (p.shrunk) return;
            foreach (int cid in Graph.adjacency[i])
                if (Graph.connections[cid].state != ConnectionState.Severed) return;
            p.shrunk = true;
            p.transform.localScale *= Settings.structure.debrisScale;
        }

        static FailureReason ReasonFrom(DamageSource s)
        {
            switch (s)
            {
                case DamageSource.Direct: return FailureReason.DirectDamage;
                case DamageSource.Explosion: return FailureReason.Explosion;
                case DamageSource.Impact: return FailureReason.Impact;
                default: return FailureReason.StructuralOverload;
            }
        }

        void Record(Connection c, ConnectionState from, ConnectionState to, FailureReason reason, float q, float d)
        {
            var e = new BreakEvent
            {
                time = SimTime, connection = c.id,
                pieceA = Graph.pieces[c.a].name,
                pieceB = c.IsGround ? "ground" : Graph.pieces[c.b].name,
                from = from, to = to, reason = reason, q = q, damage = d,
                point = c.a < pieces.Count && pieces[c.a] != null ? pieces[c.a].transform.position : Vector3.zero,
                material = Graph.pieces[c.a].material, mass = Graph.mass[c.a],
            };
            log.Add(e);
            OnBreak?.Invoke(e);
        }

        void RaiseBreak(BreakEvent e) => OnBreak?.Invoke(e);

        // ------------------------------------------------------------------ clusters

        /// <summary>
        /// Rebuilds rigid clusters from Structural connectivity. Static pieces that lost their path to the
        /// ground become dynamic; dynamic clusters that lost internal connections split with inherited motion.
        /// </summary>
        void Recluster(List<Connection> changed)
        {
            var comp = ClusterMath.Components(Graph, out var anchored);
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < Graph.PieceCount; i++)
            {
                if (pieces[i].removed) continue;
                if (!groups.TryGetValue(comp[i], out var list)) groups[comp[i]] = list = new List<int>();
                list.Add(i);
            }

            var touched = new List<RigidCluster>();
            bool any = false;

            // Static pieces whose component is no longer anchored.
            var detach = new Dictionary<int, List<int>>();
            foreach (int i in new List<int>(StaticCluster.pieces))
            {
                if (anchored[comp[i]]) continue;
                if (!detach.TryGetValue(comp[i], out var list)) detach[comp[i]] = list = new List<int>();
                list.Add(i);
            }
            foreach (var kv in detach)
            {
                foreach (int i in kv.Value) StaticCluster.pieces.Remove(i);
                touched.Add(CreateDynamicCluster(kv.Value, null));
                any = true;
            }

            // Dynamic clusters that split.
            foreach (var k in new List<RigidCluster>(clusters))
            {
                if (k == null || k.isStatic || !k.gameObject.activeSelf) continue;
                int c0 = -1;
                bool split = false;
                foreach (int i in k.pieces)
                {
                    if (c0 < 0) c0 = comp[i];
                    else if (comp[i] != c0) { split = true; break; }
                }
                if (!split) continue;

                var sub = new Dictionary<int, List<int>>();
                foreach (int i in k.pieces)
                {
                    if (!sub.TryGetValue(comp[i], out var list)) sub[comp[i]] = list = new List<int>();
                    list.Add(i);
                }
                SyncTransform(k);
                foreach (var kv in sub) touched.Add(CreateDynamicCluster(kv.Value, k));
                DestroyCluster(k);
                any = true;
            }

            if (any || changed == null || changed.Count > 0)
            {
                SyncJoints();
                foreach (var k in touched) WakeAround(k);
            }
        }

        RigidCluster NewClusterObject(string label, bool isStatic)
        {
            var go = new GameObject(label);
            go.transform.SetParent(root, false);
            var k = go.AddComponent<RigidCluster>();
            k.id = nextClusterId++;
            k.isStatic = isStatic;
            k.createdTime = SimTime;
            k.debugColor = isStatic ? new Color(0.55f, 0.55f, 0.58f) : Color.HSVToRGB((k.id * 0.618034f) % 1f, 0.65f, 0.95f);
            clusters.Add(k);
            return k;
        }

        RigidCluster CreateDynamicCluster(List<int> members, RigidCluster parent)
        {
            float m = 0f;
            Vector3 com = Vector3.zero;
            foreach (int i in members)
            {
                m += Graph.mass[i];
                com += Graph.mass[i] * pieces[i].transform.position;
            }
            com /= Mathf.Max(1e-3f, m);

            var k = NewClusterObject($"Cluster {nextClusterId}", false);
            k.transform.SetPositionAndRotation(com, Quaternion.identity);
            foreach (int i in members)
            {
                pieces[i].transform.SetParent(k.transform, true);
                pieces[i].cluster = k;
                k.pieces.Add(i);
            }

            var rb = k.gameObject.AddComponent<Rigidbody>();
            var ps = Settings.physics;
            rb.mass = m;
            rb.centerOfMass = Vector3.zero;
            rb.solverIterations = ps.solverIterations;
            rb.solverVelocityIterations = ps.solverVelocityIterations;
            rb.maxDepenetrationVelocity = ps.maxDepenetrationVelocity;
            rb.maxAngularVelocity = ps.maxAngularVelocity;
            rb.interpolation = ps.interpolate ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            k.body = rb;

            if (parent != null && parent.body != null)
            {
                var pb = parent.body;
                rb.linearVelocity = ClusterMath.InheritedVelocity(pb.linearVelocity, pb.angularVelocity, pb.worldCenterOfMass, com);
                rb.angularVelocity = pb.angularVelocity;
            }
            return k;
        }

        void DestroyCluster(RigidCluster k)
        {
            DestroyJointsOnCluster(k);
            clusters.Remove(k);
            k.gameObject.SetActive(false);
            Destroy(k.gameObject);
        }

        static void SyncTransform(RigidCluster k)
        {
            if (k.body == null) return;
            k.transform.SetPositionAndRotation(k.body.position, k.body.rotation);
        }

        void WakeAround(RigidCluster k)
        {
            if (k == null || k.pieces.Count == 0) return;
            var b = new Bounds(pieces[k.pieces[0]].transform.position, Vector3.zero);
            foreach (int i in k.pieces) b.Encapsulate(pieces[i].shape.bounds);
            b.Expand(0.4f);
            var hits = Physics.OverlapBox(b.center, b.extents, Quaternion.identity);
            foreach (var h in hits)
            {
                var rb = h.attachedRigidbody;
                if (rb != null && !rb.isKinematic) rb.WakeUp();
            }
        }

        void KillPlane()
        {
            float y = Settings.physics.killPlaneY;
            foreach (var k in new List<RigidCluster>(clusters))
            {
                if (k.isStatic || k.body == null || k.body.position.y > y) continue;
                foreach (int i in k.pieces)
                {
                    foreach (var cid in Graph.adjacency[i])
                    {
                        var c = Graph.connections[cid];
                        if (c.state != ConnectionState.Severed) { c.state = ConnectionState.Severed; DestroyJointFor(c); }
                    }
                    ParkRemovedPiece(i);
                }
                k.pieces.Clear();
                DestroyCluster(k);
            }
        }

        // ------------------------------------------------------------------ pieces

        void CreatePieceObject(int i, RigidCluster owner)
        {
            var d = Graph.pieces[i];
            GameObject go;
            Collider shape;
            MeshRenderer mr;

            if (d.mesh != null)
            {
                // Irregular fragment: its own convex mesh, already centred on its centre of mass.
                go = new GameObject(d.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                go.transform.SetParent(owner.transform, false);
                go.transform.SetPositionAndRotation(d.center, d.rotation);
                go.GetComponent<MeshFilter>().sharedMesh = d.mesh;
                var mc = go.GetComponent<MeshCollider>();
                // The mesh is already a clean convex hull, so skip the cleaning and welding passes.
                mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                                    MeshColliderCookingOptions.UseFastMidphase;
                mc.convex = true;
                mc.sharedMesh = d.mesh;
                shape = mc;
                mr = go.GetComponent<MeshRenderer>();
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = d.name;
                go.transform.SetParent(owner.transform, false);
                go.transform.SetPositionAndRotation(d.center, d.rotation);
                go.transform.localScale = d.size;

                var box = go.GetComponent<BoxCollider>();
                float inset = Settings.structure.colliderInset;
                box.size = new Vector3(
                    Mathf.Max(0.05f, 1f - 2f * inset / Mathf.Max(0.01f, d.size.x)),
                    Mathf.Max(0.05f, 1f - 2f * inset / Mathf.Max(0.01f, d.size.y)),
                    Mathf.Max(0.05f, 1f - 2f * inset / Mathf.Max(0.01f, d.size.z)));
                shape = box;
                mr = go.GetComponent<MeshRenderer>();
            }

            shape.providesContacts = true;
            shape.sharedMaterial = PhysicsMaterialFor(d.material);
            mr.sharedMaterial = MaterialAssetFor(d.material);

            var p = go.AddComponent<Piece>();
            p.index = i;
            p.cluster = owner;
            p.shape = shape;
            p.meshRenderer = mr;
            pieces.Add(p);
            owner.pieces.Add(i);
            colliderToPiece[shape] = i;
            SetPieceColor(i, Settings.Material(d.material).color);
        }

        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// A MaterialPropertyBlock makes a renderer ineligible for the SRP batcher, so draw calls would scale
        /// one for one with pieces. Pieces showing their plain material colour therefore carry no block at all
        /// and batch with every other piece of that material; only genuinely tinted pieces pay for one. The
        /// colour is also cached, so a piece that did not change costs nothing.
        /// </summary>
        public void SetPieceColor(int i, Color c)
        {
            var piece = pieces[i];
            if (piece == null || piece.removed) return;
            if (piece.hasColor && piece.appliedColor == c) return;

            var mr = piece.meshRenderer;
            bool plain = c == Settings.Material(Graph.pieces[i].material).color;
            if (plain)
            {
                if (piece.tinted)
                {
                    mr.SetPropertyBlock(null);
                    piece.tinted = false;
                }
            }
            else
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();
                mpb.Clear();
                mpb.SetColor(BaseColorId, c);
                mpb.SetColor(ColorId, c);
                mr.SetPropertyBlock(mpb);
                piece.tinted = true;
            }
            piece.appliedColor = c;
            piece.hasColor = true;
            stats.tintUpdates++;
        }

        /// <summary>Pieces currently carrying a property block, so they cannot batch. For diagnostics.</summary>
        public int TintedPieces
        {
            get
            {
                int n = 0;
                foreach (var p in pieces) if (p != null && !p.removed && p.tinted) n++;
                return n;
            }
        }

        PhysicsMaterial PhysicsMaterialFor(int material)
        {
            if (physicsMaterials.TryGetValue(material, out var pm)) return pm;
            var spec = Settings.Material(material);
            pm = new PhysicsMaterial(spec.name)
            {
                dynamicFriction = spec.friction,
                staticFriction = spec.friction,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                frictionCombine = PhysicsMaterialCombine.Average,
            };
            physicsMaterials[material] = pm;
            return pm;
        }

        readonly List<Material> pieceMaterials = new List<Material>();

        /// <summary>One shared material per material spec, so untinted pieces of a kind batch together.</summary>
        Material MaterialAssetFor(int index)
        {
            while (pieceMaterials.Count <= index) pieceMaterials.Add(null);
            if (pieceMaterials[index] != null) return pieceMaterials[index];
            var spec = Settings.Material(index);
            var m = new Material(Settings.pieceMaterial)
            {
                name = $"Piece {spec.name} (runtime)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            m.SetColor(BaseColorId, spec.color);
            m.SetColor(ColorId, spec.color);
            pieceMaterials[index] = m;
            return m;
        }

        void ClearPieceMaterials()
        {
            foreach (var m in pieceMaterials)
                if (m != null) DestroyImmediate(m);
            pieceMaterials.Clear();
        }

        void EnsureRenderMaterials()
        {
            if (Settings.pieceMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Settings.pieceMaterial = new Material(sh) { name = "Piece (runtime)" };
            }
            if (Settings.overlayMaterial == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                var m = new Material(sh) { name = "Overlay (runtime)", hideFlags = HideFlags.HideAndDontSave };
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                m.SetInt("_ZWrite", 0);
                m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                Settings.overlayMaterial = m;
            }
        }

        public bool TryGetPiece(Collider col, out int index) => colliderToPiece.TryGetValue(col, out index);

        /// <summary>Re-applies the residual strength multiplier to every connection (live tuning).</summary>
        public void RefreshResidualCapacities()
        {
            if (Graph == null) return;
            foreach (var c in Graph.connections) Graph.ApplyResidualCapacity(c, Settings);
        }

        public Vector3 ConnectionWorldCenter(Connection c)
        {
            var p = pieces[c.a];
            if (p == null || p.removed) return Graph.pieces[c.a].center + c.centerOffsetA;
            var t = p.transform;
            return t.position + t.rotation * c.centerOffsetA;
        }

        void UpdateStats()
        {
            if (Graph == null) return;
            int dyn = 0, sleep = 0;
            foreach (var k in clusters)
            {
                if (k == null || k.isStatic || k.body == null || !k.gameObject.activeSelf) continue;
                if (k.body.IsSleeping()) sleep++; else dyn++;
            }
            int s = 0, r = 0, x = 0;
            foreach (var c in Graph.connections)
            {
                if (c.state == ConnectionState.Structural) s++;
                else if (c.state == ConnectionState.Residual) r++;
                else x++;
            }
            stats.pieces = Graph.PieceCount;
            stats.staticPieces = StaticCluster != null ? StaticCluster.pieces.Count : 0;
            stats.dynamicBodies = dyn;
            stats.sleepingBodies = sleep;
            stats.activeJoints = joints.Count;
            stats.structural = s;
            stats.residual = r;
            stats.severed = x;
            stats.maxDynamicBodies = Mathf.Max(stats.maxDynamicBodies, dyn + sleep);
            stats.maxActiveJoints = Mathf.Max(stats.maxActiveJoints, joints.Count);
            int frags = 0;
            // Fragments credited to a collection container (no-shatter) are finished with and leave the budget.
            foreach (var p in pieces) if (p != null && p.isFragment && !p.removed && !Graph.pieces[p.index].noShatter) frags++;
            stats.liveFragments = frags;
        }
    }
}
