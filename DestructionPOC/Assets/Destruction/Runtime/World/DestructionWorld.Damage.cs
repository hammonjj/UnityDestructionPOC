using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>Damage sources: tool actions (queued from Update) and filtered contacts (queued from physics).</summary>
    public sealed partial class DestructionWorld
    {
        enum ActionKind { Damage, Explosion, Sever, DropBlock, Demolish }

        struct ToolAction
        {
            public ActionKind kind;
            public int piece;
            public Vector3 point;
            public float radius, amount, impulse, mass, size;
            public int[] connectionIds;
        }

        struct PendingImpulse { public Vector3 point; public float radius, impulse; }

        struct PendingPush { public int piece; public Vector3 deltaV; }
        readonly List<PendingPush> pendingPushes = new List<PendingPush>();

        /// <summary>Gives the piece's rigid body a velocity change after this step's commit (e.g. a knocked-out prop).</summary>
        public void Push(int piece, Vector3 velocityChange) =>
            pendingPushes.Add(new PendingPush { piece = piece, deltaV = velocityChange });

        struct ContactRecord
        {
            public int a, b;           // piece indices, −1 for non-piece colliders
            public Rigidbody bodyA, bodyB;
            public Vector3 impulse;    // N·s
            public float speed;        // pre-impact relative normal speed, m/s
            public Vector3 point;
        }

        struct ContactLoadKey : System.IEquatable<ContactLoadKey>
        {
            public int piece;
            public Rigidbody body;
            public bool Equals(ContactLoadKey o) => piece == o.piece && ReferenceEquals(body, o.body);
            public override bool Equals(object o) => o is ContactLoadKey k && Equals(k);
            public override int GetHashCode() =>
                piece * 486187739 ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(body);
        }

        struct ContactLoad
        {
            public float force;
            public Vector3 point;
            public int step;
            public Rigidbody body;
        }

        readonly List<ToolAction> actions = new List<ToolAction>();
        readonly List<PendingImpulse> pendingImpulses = new List<PendingImpulse>();
        readonly List<ContactRecord> contacts = new List<ContactRecord>();
        readonly Dictionary<ContactLoadKey, ContactLoad> contactLoads = new Dictionary<ContactLoadKey, ContactLoad>();
        readonly Dictionary<long, float> pairCooldown = new Dictionary<long, float>();

        /// <summary>Last explosion applied, for tests and diagnostics: how many bodies received an impulse.</summary>
        public int LastExplosionBodyCount { get; private set; }
        public int ExplosionCount { get; private set; }

        // ------------------------------------------------------------------ public tool API (queued)

        public void Damage(int piece, float amount) =>
            actions.Add(new ToolAction { kind = ActionKind.Damage, piece = piece, amount = amount });

        public void Explode(Vector3 point, float radius, float damage, float impulse) =>
            actions.Add(new ToolAction { kind = ActionKind.Explosion, point = point, radius = radius, amount = damage, impulse = impulse });

        /// <summary>Destroys the given connections outright (scenario triggers). Reason: direct damage.</summary>
        public void Sever(IEnumerable<int> connectionIds) =>
            actions.Add(new ToolAction { kind = ActionKind.Sever, connectionIds = new List<int>(connectionIds).ToArray() });

        public void DropBlock(Vector3 point, float mass, float size) =>
            actions.Add(new ToolAction { kind = ActionKind.DropBlock, point = point, mass = mass, size = size });

        /// <summary>Removes a piece entirely (e.g. a demolished support): its connections are destroyed and it
        /// stops colliding, so it can no longer support anything by contact.</summary>
        public void Demolish(int piece) =>
            actions.Add(new ToolAction { kind = ActionKind.Demolish, piece = piece });

        public int PendingActions => actions.Count;

        void ApplyDemolish(int i)
        {
            if (i < 0 || i >= Graph.PieceCount || pieces[i].removed) return;
            foreach (int cid in Graph.adjacency[i])
            {
                var c = Graph.connections[cid];
                if (c.state == ConnectionState.Structural)
                {
                    c.violentPending = true;
                    c.lastDamageSource = DamageSource.Direct;
                    c.damage = 1f;
                }
                else if (c.state == ConnectionState.Residual)
                {
                    c.residualDamage = 1f;
                    c.residualSource = DamageSource.Direct;
                }
            }
            RemovePieceFromWorld(i);
            WakeAround(StaticCluster);
        }

        void ProcessActions()
        {
            if (actions.Count == 0) return;
            foreach (var a in actions)
            {
                switch (a.kind)
                {
                    case ActionKind.Damage: ApplyDirect(a.piece, a.amount); break;
                    case ActionKind.Explosion: ApplyExplosion(a); break;
                    case ActionKind.Sever:
                        foreach (int id in a.connectionIds)
                        {
                            if (id < 0 || id >= Graph.connections.Count) continue;
                            var c = Graph.connections[id];
                            if (c.state == ConnectionState.Structural)
                            {
                                c.violentPending = true;
                                c.lastDamageSource = DamageSource.Direct;
                                c.damage = 1f;
                            }
                            else if (c.state == ConnectionState.Residual)
                            {
                                c.residualDamage = 1f;
                                c.residualSource = DamageSource.Direct;
                            }
                        }
                        break;
                    case ActionKind.DropBlock: SpawnBlock(a); break;
                    case ActionKind.Demolish: ApplyDemolish(a.piece); break;
                }
            }
            actions.Clear();
        }

        void ApplyDirect(int piece, float amount)
        {
            if (piece < 0 || piece >= Graph.PieceCount) return;
            foreach (int cid in Graph.adjacency[piece])
            {
                var c = Graph.connections[cid];
                if (c.state == ConnectionState.Structural)
                {
                    c.damage = Mathf.Clamp01(c.damage + amount);
                    c.lastDamageSource = DamageSource.Direct;
                }
                else if (c.state == ConnectionState.Residual)
                {
                    c.residualDamage = Mathf.Clamp01(c.residualDamage + amount);
                    c.residualSource = DamageSource.Direct;
                }
            }
            var p = pieces[piece];
            p.shatterDamage += amount;
            if (p.shatterDamage >= Settings.fragments.directShatterDamage - 1e-4f) RequestShatter(piece, FailureReason.DirectDamage);
            WakePiece(piece);
        }

        void ApplyExplosion(ToolAction a)
        {
            float inner = a.radius * Settings.tools.explosionInnerFraction;
            foreach (var c in Graph.connections)
            {
                if (a.amount <= 0f) break; // impulse-only push
                if (c.state == ConnectionState.Severed) continue;
                if (pieces[c.a].removed) continue;
                float d = Vector3.Distance(ConnectionWorldCenter(c), a.point);
                if (d >= a.radius) continue;
                float amount = a.amount * (1f - d / a.radius);
                if (c.state == ConnectionState.Structural)
                {
                    c.damage = Mathf.Clamp01(c.damage + amount);
                    c.lastDamageSource = DamageSource.Explosion;
                    if (d < inner) c.violentPending = true;
                }
                else
                {
                    c.residualDamage = d < inner ? 1f : Mathf.Clamp01(c.residualDamage + amount);
                    c.residualSource = DamageSource.Explosion;
                }
            }
            if (a.amount > 0f) RequestExplosionShatter(a.point, inner);
            pendingImpulses.Add(new PendingImpulse { point = a.point, radius = a.radius, impulse = a.impulse });
            ExplosionCount++;
        }

        /// <summary>Applied after the commit so bodies created by the same blast receive it exactly once.</summary>
        void ApplyPendingImpulses()
        {
            foreach (var push in pendingPushes)
            {
                if (push.piece < 0 || push.piece >= pieces.Count || pieces[push.piece].removed) continue;
                var body = pieces[push.piece].cluster != null ? pieces[push.piece].cluster.body : null;
                if (body != null) body.AddForce(push.deltaV, ForceMode.VelocityChange);
            }
            pendingPushes.Clear();
            if (pendingImpulses.Count == 0) return;
            // Fragments and clusters created this step must be visible to the overlap query.
            Physics.SyncTransforms();
            var seen = new HashSet<Rigidbody>();
            foreach (var p in pendingImpulses)
            {
                seen.Clear();
                foreach (var col in Physics.OverlapSphere(p.point, p.radius))
                {
                    var rb = col.attachedRigidbody;
                    if (rb == null || rb.isKinematic || !seen.Add(rb)) continue;
                    // Linear falloff from the centre, slight upward bias, and a cap on the velocity change so
                    // light fragments get a believable kick instead of a fixed impulse launching them.
                    Vector3 com = rb.worldCenterOfMass;
                    Vector3 dir = com - p.point;
                    float dist = dir.magnitude;
                    dir = (dist > 1e-3f ? dir / dist : Vector3.up) + Vector3.up * 0.3f;
                    dir.Normalize();
                    float falloff = Mathf.Clamp01(1f - dist / Mathf.Max(0.01f, p.radius));
                    float mag = Mathf.Min(p.impulse * falloff, rb.mass * Settings.tools.explosionMaxDeltaV);
                    if (mag > 0f) rb.AddForceAtPosition(dir * mag, Vector3.Lerp(com, col.ClosestPoint(p.point), 0.5f), ForceMode.Impulse);
                }
                LastExplosionBodyCount = seen.Count;
            }
            pendingImpulses.Clear();
        }

        void SpawnBlock(ToolAction a)
        {
            var spec = Settings.Material(DestructionSettings.Concrete);
            float side = a.size > 0.05f ? a.size : Mathf.Pow(a.mass / Mathf.Max(1f, spec.density), 1f / 3f);
            var def = PieceDef.Box($"Dropped block {Graph.PieceCount}", a.point, Vector3.one * side, PieceKind.Block);
            int i = Graph.AddLoosePiece(def, Settings);
            var holder = StaticCluster; // temporary parent; moved into its own cluster below
            CreatePieceObject(i, holder);
            holder.pieces.Remove(i);
            CreateDynamicCluster(new List<int> { i }, null);
        }

        void WakePiece(int piece)
        {
            var k = pieces[piece].cluster;
            if (k != null && k.body != null) k.body.WakeUp();
        }

        // ------------------------------------------------------------------ contacts

        void OnContactEvent(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            if (Graph == null) return;
            for (int h = 0; h < headers.Length; h++)
            {
                var header = headers[h];
                var bodyA = header.body as Rigidbody;
                var bodyB = header.otherBody as Rigidbody;
                Vector3 vA = header.bodyLinearVelocity, wA = header.bodyAngularVelocity;
                Vector3 vB = header.otherBodyLinearVelocity, wB = header.otherBodyAngularVelocity;
                for (int p = 0; p < header.pairCount; p++)
                {
                    var pair = header.GetContactPair(p);
                    if (pair.isCollisionExit || pair.contactCount == 0) continue;
                    var ca = pair.collider;
                    var cb = pair.otherCollider;
                    int ia = ca != null && colliderToPiece.TryGetValue(ca, out int xa) ? xa : -1;
                    int ib = cb != null && colliderToPiece.TryGetValue(cb, out int xb) ? xb : -1;
                    if (ia < 0 && ib < 0) continue;

                    var cp = pair.GetContactPoint(0);
                    Vector3 pt = cp.position;
                    Vector3 n = cp.normal;
                    Vector3 pa = bodyA != null ? vA + Vector3.Cross(wA, pt - bodyA.worldCenterOfMass) : Vector3.zero;
                    Vector3 pb = bodyB != null ? vB + Vector3.Cross(wB, pt - bodyB.worldCenterOfMass) : Vector3.zero;
                    contacts.Add(new ContactRecord
                    {
                        a = ia, b = ib, bodyA = bodyA, bodyB = bodyB,
                        impulse = pair.impulseSum,
                        speed = Mathf.Abs(Vector3.Dot(pa - pb, n)),
                        point = pt,
                    });
                }
            }
        }

        void ProcessContacts(float dt)
        {
            var imp = Settings.impact;
            stats.contactsThisStep = contacts.Count;
            stats.impactsThisStep = 0;

            foreach (var r in contacts)
            {
                float J = r.impulse.magnitude;

                // Resting support loads on static pieces from dynamic bodies.
                if (Settings.structure.contactLoads)
                {
                    TrackSupport(r.a, r.bodyB, r, dt);
                    TrackSupport(r.b, r.bodyA, r, dt);
                }

                if (!imp.enabled) continue;
                float energy = ImpactMath.Energy(J, r.speed, imp.minRelativeSpeed);
                if (energy <= 0f) continue;

                long key = PairKey(r.a, r.b);
                if (pairCooldown.TryGetValue(key, out float until) && until > SimTime) continue;
                pairCooldown[key] = SimTime + imp.pairCooldown;
                stats.impactsThisStep++;
                // Each body of the pair absorbs half of the dissipated energy.
                ApplyImpact(r.a, 0.5f * energy);
                ApplyImpact(r.b, 0.5f * energy);
            }
            contacts.Clear();

            // Forget support loads whose body moved away; keep those from sleeping bodies (no reports while asleep).
            var drop = new List<ContactLoadKey>();
            foreach (var kv in contactLoads)
            {
                var v = kv.Value;
                if (v.step == StepIndex) continue;
                if (v.body != null && v.body.gameObject.activeInHierarchy && v.body.IsSleeping()) continue;
                drop.Add(kv.Key);
            }
            foreach (var k in drop) contactLoads.Remove(k);
        }

        void TrackSupport(int staticPiece, Rigidbody other, ContactRecord r, float dt)
        {
            if (staticPiece < 0 || other == null) return;
            if (!IsStaticSide(staticPiece)) return;
            var key = new ContactLoadKey { piece = staticPiece, body = other };
            float f = Mathf.Abs(r.impulse.y) / Mathf.Max(1e-4f, dt);
            if (contactLoads.TryGetValue(key, out var prev) && prev.step != StepIndex)
                f = Mathf.Lerp(prev.force, f, 0.25f);
            else if (prev.step == StepIndex)
                f += prev.force;
            contactLoads[key] = new ContactLoad { force = f, point = r.point, step = StepIndex, body = other };
        }

        void ApplyImpact(int piece, float energy)
        {
            if (piece < 0 || pieces[piece].removed) return;
            // Hard enough hits shatter the piece itself, connected or loose.
            if (energy / Mathf.Max(1f, Graph.mass[piece]) >= Settings.fragments.impactShatterEnergyPerKg)
                RequestShatter(piece, FailureReason.Impact);
            var adj = Graph.adjacency[piece];
            float capacity = 0f;
            foreach (int cid in adj)
            {
                var c = Graph.connections[cid];
                if (c.state != ConnectionState.Severed) capacity += c.impactEnergyCapacity;
            }
            if (capacity <= 0f) return;

            var imp = Settings.impact;
            float dmg = ImpactMath.PieceDamage(energy, capacity, imp.damageScale);
            if (dmg <= 1e-4f) return;
            bool violent = dmg > imp.violentEnergyFactor;
            foreach (int cid in adj)
            {
                var c = Graph.connections[cid];
                if (c.state == ConnectionState.Severed) continue;
                if (SimTime - c.lastImpactTime < imp.connectionCooldown) continue;
                c.lastImpactTime = SimTime;
                if (c.state == ConnectionState.Structural)
                {
                    c.damage = Mathf.Clamp01(c.damage + dmg);
                    c.lastDamageSource = DamageSource.Impact;
                    if (violent) c.violentPending = true;
                }
                else
                {
                    c.residualDamage = Mathf.Clamp01(c.residualDamage + dmg);
                    c.residualSource = DamageSource.Impact;
                }
            }
        }

        static long PairKey(int a, int b)
        {
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            return ((long)(lo + 2) << 32) | (uint)(hi + 2);
        }
    }
}
