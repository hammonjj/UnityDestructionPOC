using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// The material a loader bucket carries. Hybrid model: debris stays ordinary destruction pieces (real rigid
    /// bodies, real mass, real shapes) but while it is in the bucket it is frozen as a kinematic body packed into the
    /// cavity, like a real rubble pile that does not slosh. That keeps loads stable at any speed and keeps the
    /// bucket free of solver fights, while chunks stay individually visible and keep their identity for accounting.
    ///
    ///   Scoop    A piece is taken when its centre lies inside the cavity, or in the scoop zone just ahead of the
    ///            cutting edge while the bucket moves forward into it, and the bucket opening is not tipped past the
    ///            dump angle. Nothing behind or beside the bucket, or on the far side of a wall, qualifies (a line of
    ///            sight from the lip must be clear). The piece glides into a free slot of a height-map packing of the
    ///            cavity; when no slot is free (full) it stays outside as ordinary rubble.
    ///   Carry    Frozen pieces follow the bucket exactly.
    ///   Dump     Tipping the opening past the dump angle (or rolling the bucket on its side) releases pieces one at a
    ///            time, lip first, as dynamic bodies that inherit the bucket's motion, so material pours out.
    ///
    /// Limits per machine: bucket mass capacity, largest piece mass and size (oversized chunks are never taken; long,
    /// thin pieces up to the bucket's width are taken lying across it), and the cavity volume itself, through the packing.
    /// Approximations: the pull-in is a short guided glide rather than contact physics, and fragments are not
    /// aggregated: each stays a separate body, so a very large number of tiny fragments is limited by the cavity.
    /// </summary>
    public sealed class BucketLoad
    {
        sealed class Item
        {
            public Rigidbody rb;
            public Collider[] colliders;
            public float mass;
            public Vector3 start, slot, half;     // cavity-frame centre of the piece's bounds (start, slot) and slot half extents
            public Quaternion startRot, slotRot;  // cavity-frame rotation
            public Vector3 boundsCentre;          // bounds centre in the piece's own frame
            public float t;
            public Vector3 lastWorld;
            public Vector3 velocity;
        }

        readonly Transform bucket, wrapper;
        readonly Quaternion frameRel;
        readonly Vector3 centreLocal;
        readonly List<Item> items = new List<Item>();
        readonly Dictionary<Rigidbody, float> cooldown = new Dictionary<Rigidbody, float>();
        readonly List<(Rigidbody rb, float depen, float until)> softened = new List<(Rigidbody, float, float)>();
        readonly List<(Rigidbody rb, float until)> pouring = new List<(Rigidbody, float)>();
        readonly float[,] heights;
        const float Cell = 0.08f;
        static readonly Collider[] hits = new Collider[128];

        /// <summary>Cavity half extents in its own frame (x right, y up, z forward toward the cutting edge).</summary>
        public Vector3 Half { get; }
        public float CapacityKg, MaxPieceMassKg, MaxPieceSize, DumpAngle = 32f;
        /// <summary>Speed along the bucket floor given to a piece as it leaves, m/s (rubble is not free-flowing).</summary>
        public float ReleaseShove = 1.8f;
        /// <summary>Acceleration along the opening applied for 1.2 s to pieces leaving the bucket, m/s².</summary>
        public float PourAcceleration = 5f;
        public float ScoopReach = 0.6f, ScoopHeight = 0.7f, ScoopMinSpeed = 0.2f, LoadInTime = 0.35f, ReleaseInterval = 0.12f;

        public float MassKg { get; private set; }
        public float VolumeM3 { get; private set; }
        public int Count => items.Count;
        public float Fill => CapacityKg <= 0f ? 0f : Mathf.Clamp01(MassKg / CapacityKg);
        public bool Spilling { get; private set; }
        public int Captured { get; private set; }
        public int Released { get; private set; }
        /// <summary>Degrees the opening is tipped below horizontal (negative = curled back).</summary>
        public float PitchDown { get; private set; }

        public Vector3 CavityPosition => bucket.TransformPoint(centreLocal);
        public Quaternion CavityRotation => bucket.rotation * frameRel;

        float releaseTimer;
        DestructionWorld world;
        Vector3 prevPos;
        bool hasPrev;

        /// <summary>Reads the cavity volume from the bucket's Vol_Cavity mesh. The machine must be at its rest pose,
        /// facing the wrapper's forward.</summary>
        public BucketLoad(Transform bucketNode, Transform wrapperRoot, Transform volume)
        {
            bucket = bucketNode;
            wrapper = wrapperRoot;
            Quaternion wr = wrapper.rotation;
            frameRel = Quaternion.Inverse(bucket.rotation) * wr;
            Vector3 origin = bucket.position;
            var mb = volume.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = Quaternion.Inverse(wr) * (volume.TransformPoint(c) - origin);
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            Half = (hi - lo) * 0.5f;
            centreLocal = bucket.InverseTransformPoint(origin + wr * ((hi + lo) * 0.5f));
            heights = new float[Mathf.Max(1, Mathf.FloorToInt(Half.x * 2f / Cell)), Mathf.Max(1, Mathf.FloorToInt(Half.z * 2f / Cell))];
        }

        /// <summary>Usable interior volume, m³.</summary>
        public float CavityVolume => 8f * Half.x * Half.y * Half.z;

        public bool Holds(Rigidbody rb)
        {
            foreach (var it in items) if (it.rb == rb) return true;
            return false;
        }

        public IEnumerable<Rigidbody> HeldBodies()
        {
            foreach (var it in items) if (it.rb != null) yield return it.rb;
        }

        // ------------------------------------------------------------------ per fixed step

        public void Step(float dt, DestructionWorld world, CleanupLedger ledger, RigCollision collision)
        {
            this.world = world;
            Vector3 pos = CavityPosition;
            Quaternion rot = CavityRotation;
            Vector3 fwd = rot * Vector3.forward;
            float fwdSpeed = hasPrev ? Vector3.Dot(pos - prevPos, fwd) / Mathf.Max(1e-4f, dt) : 0f;
            prevPos = pos;
            hasPrev = true;
            PitchDown = Mathf.Asin(Mathf.Clamp(-fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            // Side roll only: curling back never pours (the back wall holds the load).
            float upDot = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(Vector3.Dot(rot * Vector3.right, Vector3.up), 2f)));

            RestoreSoftened();
            Pour(fwd);
            PurgeLost(ledger, collision);
            Follow(dt, pos, rot);

            // Tipped past the dump angle, or lying on its side: pour material out, one chunk per interval, lip first.
            Spilling = items.Count > 0 && (PitchDown > DumpAngle || upDot < 0.45f || MassKg > CapacityKg * 1.001f);
            if (Spilling)
            {
                releaseTimer -= dt;
                if (releaseTimer <= 0f)
                {
                    ReleaseNearestLip(ledger, collision);
                    releaseTimer = ReleaseInterval;
                }
                return;
            }
            releaseTimer = 0f;

            if (world == null || world.Graph == null || PitchDown > DumpAngle - 5f || upDot < 0.6f) return;
            Scan(world, ledger, collision, pos, rot, fwdSpeed >= ScoopMinSpeed);
        }

        /// <summary>Rubble does not flow like grain: for a moment after leaving the pile each piece is nudged along the
        /// bucket floor (the machine shakes its load out), so chunks do not stay wedged on a floor tipped only part way.</summary>
        void Pour(Vector3 forward)
        {
            for (int i = pouring.Count - 1; i >= 0; i--)
            {
                var (rb, until) = pouring[i];
                if (rb == null || Time.time >= until) { pouring.RemoveAt(i); continue; }
                if (!rb.isKinematic) rb.AddForce(forward * PourAcceleration, ForceMode.Acceleration);
            }
        }

        void Scan(DestructionWorld world, CleanupLedger ledger, RigCollision collision, Vector3 pos, Quaternion rot, bool moving)
        {
            // One box covering the cavity plus the scoop zone ahead of the edge.
            float yLo = -Half.y - 0.1f, yHi = Mathf.Max(Half.y, -Half.y + ScoopHeight);
            float zLo = -Half.z, zHi = Half.z + ScoopReach;
            Vector3 c = new Vector3(0f, (yLo + yHi) * 0.5f, (zLo + zHi) * 0.5f);
            Vector3 h = new Vector3(Half.x, (yHi - yLo) * 0.5f, (zHi - zLo) * 0.5f);
            int n = Physics.OverlapBoxNonAlloc(pos + rot * c, h, hits, rot, ~0, QueryTriggerInteraction.Ignore);
            Quaternion inv = Quaternion.Inverse(rot);
            for (int k = 0; k < n; k++)
            {
                if (!world.TryGetPiece(hits[k], out int i)) continue;
                var piece = world.pieces[i];
                if (piece == null || piece.removed || piece.cluster == null || piece.cluster.isStatic) continue;
                var rb = piece.cluster.body;
                if (rb == null || rb.isKinematic || Holds(rb)) continue;
                if (cooldown.TryGetValue(rb, out float until) && until > Time.time) continue;
                if (rb.mass > MaxPieceMassKg || MassKg + rb.mass > CapacityKg) continue;

                Vector3 l = inv * (rb.worldCenterOfMass - pos);
                bool inCavity = Mathf.Abs(l.x) <= Half.x - 0.05f && Mathf.Abs(l.y) <= Half.y && Mathf.Abs(l.z) <= Half.z;
                bool inZone = !inCavity && moving && Mathf.Abs(l.x) <= Half.x && l.z > Half.z && l.z <= Half.z + ScoopReach &&
                              l.y >= -Half.y - 0.05f && l.y <= -Half.y + ScoopHeight;
                if (!inCavity && !inZone) continue;

                var lb = LocalBounds(rb);
                // Long, thin pieces (posts, curbs, beams) are taken lying across the bucket, up to its width.
                Vector3 s = lb.size;
                float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z)), shortest = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
                float middle = s.x + s.y + s.z - longest - shortest;
                if (middle > MaxPieceSize || longest > Mathf.Max(MaxPieceSize, 2f * Half.x)) continue;
                if (!LineClear(pos + rot * (inZone ? new Vector3(0f, -Half.y + 0.15f, Half.z) : Vector3.zero), rb, world, collision)) continue;
                if (!TryPack(rb, lb, rot, pos, out var item)) continue;
                Capture(item, ledger, collision);
            }
        }

        /// <summary>Nothing solid between the lip (or cavity centre) and the piece: no scooping through walls.</summary>
        static bool LineClear(Vector3 from, Rigidbody target, DestructionWorld world, RigCollision collision)
        {
            Vector3 to = target.worldCenterOfMass;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1e-3f) return true;
            int n = Physics.RaycastNonAlloc(from, d / len, rayHits, len, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = rayHits[i].collider;
                if (collision.own.Contains(col) || collision.ignore.Contains(col)) continue;
                if (col.attachedRigidbody == target) continue;
                if (world.TryGetPiece(col, out _)) continue; // other loose debris does not shield
                return false;
            }
            return true;
        }

        static readonly RaycastHit[] rayHits = new RaycastHit[16];

        bool TryPack(Rigidbody rb, Bounds lb, Quaternion rot, Vector3 pos, out Item item)
        {
            item = null;
            // Lay the piece flat across the bucket. Yaw jitter keeps the load from looking like stacked crates; a long
            // piece that only fits square to the edge gets none. Extents are the rotated bounds' AABB.
            Quaternion lay = LayFlat(lb.extents);
            float jitter = ((rb.name.GetHashCode() & 0xFF) / 255f - 0.5f) * 40f;
            int nx = heights.GetLength(0), nz = heights.GetLength(1);
            Quaternion q = lay;
            Vector3 half = Vector3.zero;
            int sx = 0, sz = 0;
            bool fits = false;
            for (int pass = 0; pass < 2 && !fits; pass++)
            {
                q = Quaternion.Euler(0f, pass == 0 ? jitter : 0f, 0f) * lay;
                half = RotatedHalf(q, lb.extents) + Vector3.one * 0.01f;
                sx = Mathf.CeilToInt(half.x * 2f / Cell);
                sz = Mathf.CeilToInt(half.z * 2f / Cell);
                fits = sx <= nx && sz <= nz;
            }
            if (!fits) return false;
            float bestH = float.MaxValue;
            int bx = 0, bz = 0;
            for (int ix = 0; ix <= nx - sx; ix++)
            for (int iz = 0; iz <= nz - sz; iz++)
            {
                float h = 0f;
                for (int x = ix; x < ix + sx && h < bestH; x++)
                for (int z = iz; z < iz + sz; z++)
                    if (heights[x, z] > h) h = heights[x, z];
                float score = h + iz * 0.0004f; // pack the back of the bucket first
                if (score < bestH)
                {
                    bestH = score;
                    bx = ix;
                    bz = iz;
                }
            }
            float top = 0f;
            for (int x = bx; x < bx + sx; x++)
            for (int z = bz; z < bz + sz; z++)
                if (heights[x, z] > top) top = heights[x, z];
            if (top + half.y * 2f > Half.y * 2f) return false; // no room left

            Vector3 slot = new Vector3(-Half.x + (bx + sx * 0.5f) * Cell, -Half.y + top + half.y, -Half.z + (bz + sz * 0.5f) * Cell);
            Quaternion inv = Quaternion.Inverse(rot);
            item = new Item
            {
                rb = rb, mass = rb.mass, half = half, slot = slot, slotRot = q,
                boundsCentre = lb.center,
                start = inv * (rb.position + rb.rotation * lb.center - pos),
                startRot = inv * rb.rotation,
                colliders = rb.GetComponentsInChildren<Collider>(),
                lastWorld = rb.position,
            };
            return true;
        }

        /// <summary>Rotation from a piece's own frame to the cavity frame that puts its longest side across the bucket
        /// (x), its thinnest side up (y) and the remaining side front to back (z).</summary>
        static Quaternion LayFlat(Vector3 e)
        {
            int lo = e.x <= e.y && e.x <= e.z ? 0 : e.y <= e.z ? 1 : 2;
            int hi = e.x > e.y && e.x > e.z ? 0 : e.y > e.z ? 1 : 2;
            if (hi == lo) hi = (lo + 1) % 3;
            int mid = 3 - lo - hi;
            return Quaternion.Inverse(Quaternion.LookRotation(Axis(mid), Axis(lo)));
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>Half extents of the axis-aligned box around a box of half extents <paramref name="e"/> turned by q.</summary>
        static Vector3 RotatedHalf(Quaternion q, Vector3 e)
        {
            Vector3 a = q * new Vector3(e.x, 0f, 0f), b = q * new Vector3(0f, e.y, 0f), c = q * new Vector3(0f, 0f, e.z);
            return new Vector3(Mathf.Abs(a.x) + Mathf.Abs(b.x) + Mathf.Abs(c.x),
                               Mathf.Abs(a.y) + Mathf.Abs(b.y) + Mathf.Abs(c.y),
                               Mathf.Abs(a.z) + Mathf.Abs(b.z) + Mathf.Abs(c.z));
        }

        void Stamp(Vector3 slot, Vector3 half)
        {
            int nx = heights.GetLength(0), nz = heights.GetLength(1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((slot.x - half.x + Half.x) / Cell + 0.01f), 0, nx - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((slot.x + half.x + Half.x) / Cell - 0.01f), 1, nx);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((slot.z - half.z + Half.z) / Cell + 0.01f), 0, nz - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((slot.z + half.z + Half.z) / Cell - 0.01f), 1, nz);
            float top = slot.y + half.y + Half.y;
            for (int x = x0; x < x1; x++)
            for (int z = z0; z < z1; z++)
                heights[x, z] = Mathf.Max(heights[x, z], top);
        }

        void RebuildHeights()
        {
            System.Array.Clear(heights, 0, heights.Length);
            foreach (var it in items) Stamp(it.slot, it.half);
        }

        void Capture(Item it, CleanupLedger ledger, RigCollision collision)
        {
            var rb = it.rb;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            foreach (var c in it.colliders) if (c != null) collision.ignore.Add(c);
            ledger?.SetHeld(rb, true);
            world?.SetImpactImmune(rb, true);
            Stamp(it.slot, it.half);
            items.Add(it);
            MassKg += it.mass;
            VolumeM3 += it.half.x * it.half.y * it.half.z * 8f;
            Captured++;
            Sfx.PlayAt("bucket_scoop_dirt", CavityPosition, 0.6f, 1f, 1.5f, 5f, 90f);
        }

        void Follow(float dt, Vector3 pos, Quaternion rot)
        {
            foreach (var it in items)
            {
                if (it.rb == null) continue;
                it.t = Mathf.Min(1f, it.t + dt / Mathf.Max(0.05f, LoadInTime));
                float s = it.t * it.t * (3f - 2f * it.t);
                Vector3 c = Vector3.Lerp(it.start, it.slot, s);
                Quaternion q = Quaternion.Slerp(it.startRot, it.slotRot, s);
                Vector3 world = pos + rot * c - rot * q * it.boundsCentre;
                it.velocity = (world - it.lastWorld) / Mathf.Max(1e-4f, dt);
                it.lastWorld = world;
                it.rb.MovePosition(world);
                it.rb.MoveRotation(rot * q);
            }
        }

        /// <summary>Drop references to bodies the world destroyed (shattered or reset) so mass is not counted twice.</summary>
        void PurgeLost(CleanupLedger ledger, RigCollision collision)
        {
            bool changed = false;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].rb != null && items[i].rb.gameObject.activeInHierarchy) continue;
                if (items[i].colliders != null)
                    foreach (var c in items[i].colliders) if (c != null) collision.ignore.Remove(c);
                items.RemoveAt(i);
                changed = true;
            }
            if (changed) Recount();
        }

        void Recount()
        {
            MassKg = 0f;
            VolumeM3 = 0f;
            foreach (var it in items)
            {
                MassKg += it.mass;
                VolumeM3 += it.half.x * it.half.y * it.half.z * 8f;
            }
            RebuildHeights();
        }

        void ReleaseNearestLip(CleanupLedger ledger, RigCollision collision)
        {
            Item best = null;
            foreach (var it in items)
                if (it.rb != null && (best == null || it.slot.z + it.slot.y * 0.25f > best.slot.z + best.slot.y * 0.25f)) best = it;
            if (best == null) return;
            Release(best, ledger, collision);
            Sfx.PlayAt("bucket_dump_earth", CavityPosition, 0.7f, 1f, 2.5f, 5f, 100f);
        }

        void Release(Item it, CleanupLedger ledger, RigCollision collision)
        {
            items.Remove(it);
            var rb = it.rb;
            if (rb != null)
            {
                foreach (var c in it.colliders) if (c != null) collision.ignore.Remove(c);
                ledger?.SetHeld(rb, false);
                world?.SetImpactImmune(rb, false);
                rb.isKinematic = false;
                // A shove along the opening, so chunks leave the lip instead of resting on a tipped floor.
                rb.linearVelocity = Vector3.ClampMagnitude(it.velocity, 6f) + CavityRotation * Vector3.forward * ReleaseShove;
                rb.angularVelocity = Vector3.zero;
                // Pieces were packed with no overlap, but give the solver a gentle push-out anyway.
                softened.Add((rb, rb.maxDepenetrationVelocity, Time.time + 1.5f));
                pouring.Add((rb, Time.time + 1.2f));
                rb.maxDepenetrationVelocity = 1f;
                cooldown[rb] = Time.time + 1.2f;
                rb.WakeUp();
                Released++;
            }
            Recount();
        }

        void RestoreSoftened()
        {
            for (int i = softened.Count - 1; i >= 0; i--)
            {
                var (rb, depen, until) = softened[i];
                if (rb == null) { softened.RemoveAt(i); continue; }
                if (Time.time < until) continue;
                rb.maxDepenetrationVelocity = depen;
                softened.RemoveAt(i);
            }
            if (cooldown.Count > 64)
            {
                var stale = new List<Rigidbody>();
                foreach (var kv in cooldown) if (kv.Key == null || kv.Value < Time.time) stale.Add(kv.Key);
                foreach (var k in stale) cooldown.Remove(k);
            }
        }

        /// <summary>Let go of everything (scene reset). Bodies become dynamic where they are.</summary>
        public void ReleaseAll(CleanupLedger ledger, RigCollision collision)
        {
            foreach (var it in new List<Item>(items)) Release(it, ledger, collision);
            items.Clear();
            cooldown.Clear();
            softened.Clear();
            MassKg = VolumeM3 = 0f;
            RebuildHeights();
            Spilling = false;
            hasPrev = false;
        }

        // ------------------------------------------------------------------ geometry helpers

        /// <summary>Bounds of a piece body in its own frame (collider shapes, not renderers).</summary>
        static Bounds LocalBounds(Rigidbody rb)
        {
            var cols = rb.GetComponentsInChildren<Collider>();
            bool any = false;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            var t = rb.transform;
            foreach (var col in cols)
            {
                Bounds lb;
                if (col is BoxCollider bc) lb = new Bounds(bc.center, bc.size);
                else if (col is MeshCollider mc && mc.sharedMesh != null) lb = mc.sharedMesh.bounds;
                else
                {
                    var wb = col.bounds;
                    lb = new Bounds(col.transform.InverseTransformPoint(wb.center), col.transform.InverseTransformVector(wb.size));
                }
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = t.InverseTransformPoint(col.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
