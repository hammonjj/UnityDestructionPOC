using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Quasi-static, single-pass load propagation with lever arms. Not a constraint solver.
    ///
    /// 1. BFS levels over Structural edges from anchors (pieces with a Structural ground connection, plus
    ///    pseudo-anchors supplied by the caller, e.g. pieces hanging from a residual hinge on static structure).
    /// 2. Pieces are processed from the farthest level inward. Each carries its own weight plus everything
    ///    handed to it, and the centre of mass of that supported load. It splits the load across its
    ///    connections to the next-lower level, weighted by interface area × alignment. Handed-down load
    ///    enters the next piece at the interface centre (force transfer, no moment transfer), so an
    ///    asymmetric roof does not appear as a giant moment on a ground-floor column joint.
    /// 3. Per connection: normal (tension/compression), shear and bending. Bending is the supported weight
    ///    times the horizontal distance from the supported centre of mass to the convex hull of the support
    ///    interfaces, so a slab on several supports carries no bending while a cantilever carries the full arm.
    /// 4. q = max(normal, shear, bending ratios). Unanchored (free) components carry no internal load.
    ///
    /// Exact for a single piece on its supports; underestimates moments in multi-piece cantilever chains
    /// (moment is not transmitted through joints) and ignores same-level lateral load sharing. Calibration
    /// absorbs the remaining artefacts in the intact configuration.
    /// </summary>
    public sealed class LoadModel
    {
        public const float Gravity = 9.81f;

        int[] level = new int[0];
        float[] supMass = new float[0];
        Vector3[] supMoment = new Vector3[0];
        int[] order = new int[0];
        readonly Queue<int> queue = new Queue<int>();
        readonly List<int> downstream = new List<int>();
        readonly List<float> shares = new List<float>();
        readonly List<Vector2> hullPoints = new List<Vector2>();
        readonly List<Vector2> hull = new List<Vector2>();

        public int MaxLevel { get; private set; }
        public int ReachedPieces { get; private set; }
        public int Level(int piece) => piece < level.Length ? level[piece] : -1;

        public void Solve(StructureGraph g, IReadOnlyList<Vector3> pos, IReadOnlyList<Quaternion> rot,
            IReadOnlyList<bool> pseudoAnchor, IReadOnlyList<float> extraMass, IReadOnlyList<Vector3> extraMoment,
            StructureSettings s)
        {
            int n = g.PieceCount;
            Ensure(n);

            foreach (var c in g.connections)
            {
                c.loadNormal = c.loadShear = c.loadBending = 0f;
                c.qNormal = c.qShear = c.qBending = c.q = 0f;
            }

            // 1. BFS.
            queue.Clear();
            for (int i = 0; i < n; i++)
            {
                level[i] = -1;
                if (g.pieces[i].loose) continue;
                var gc = g.GroundConnection(i);
                bool grounded = gc != null && gc.state == ConnectionState.Structural;
                bool pseudo = pseudoAnchor != null && i < pseudoAnchor.Count && pseudoAnchor[i];
                if (grounded || pseudo)
                {
                    level[i] = 0;
                    queue.Enqueue(i);
                }
            }

            int maxLevel = 0;
            int reached = 0;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                reached++;
                foreach (var cid in g.adjacency[i])
                {
                    var c = g.connections[cid];
                    if (c.state != ConnectionState.Structural || c.IsGround) continue;
                    int j = c.Other(i);
                    if (level[j] >= 0) continue;
                    level[j] = level[i] + 1;
                    if (level[j] > maxLevel) maxLevel = level[j];
                    queue.Enqueue(j);
                }
            }
            MaxLevel = maxLevel;
            ReachedPieces = reached;

            // 2. Order by level descending (counting sort).
            var counts = new int[maxLevel + 2];
            for (int i = 0; i < n; i++) if (level[i] >= 0) counts[level[i]]++;
            var start = new int[maxLevel + 2];
            int acc = 0;
            for (int l = maxLevel; l >= 0; l--) { start[l] = acc; acc += counts[l]; }
            int m = acc;
            for (int i = 0; i < n; i++)
            {
                if (level[i] < 0) continue;
                order[start[level[i]]++] = i;
                float em = extraMass != null && i < extraMass.Count ? extraMass[i] : 0f;
                Vector3 emo = extraMoment != null && i < extraMoment.Count ? extraMoment[i] : Vector3.zero;
                supMass[i] = g.mass[i] + em;
                supMoment[i] = g.mass[i] * pos[i] + emo;
            }

            // 3. Propagate.
            for (int idx = 0; idx < m; idx++)
            {
                int i = order[idx];
                downstream.Clear();
                foreach (var cid in g.adjacency[i])
                {
                    var c = g.connections[cid];
                    if (c.state != ConnectionState.Structural) continue;
                    if (c.IsGround)
                    {
                        if (level[i] == 0) downstream.Add(cid);
                        continue;
                    }
                    int j = c.Other(i);
                    if (level[j] >= 0 && level[j] < level[i]) downstream.Add(cid);
                }
                if (downstream.Count == 0) continue; // pseudo-anchor: its joint carries the load.

                float W = supMass[i] * Gravity;
                Vector3 com = supMoment[i] / Mathf.Max(1e-3f, supMass[i]);

                shares.Clear();
                float sum = 0f;
                hullPoints.Clear();
                foreach (var cid in downstream)
                {
                    var c = g.connections[cid];
                    Vector3 nij = NormalFrom(c, i, rot);
                    float vert = Vector3.Dot(nij, Vector3.up);
                    float align = vert < -0.5f ? s.bearingWeight : vert > 0.5f ? s.hangingWeight : s.lateralWeight;
                    float w = Mathf.Max(1e-6f, c.area * c.StrengthFactor * align);
                    shares.Add(w);
                    sum += w;

                    Vector3 cw = StructureGraph.WorldCenter(c, pos[c.a], rot[c.a]);
                    Vector3 t = rot[c.a] * c.tangent * (0.5f * c.width);
                    Vector3 d = rot[c.a] * c.depthDir * (0.5f * c.depth);
                    hullPoints.Add(XZ(cw + t + d));
                    hullPoints.Add(XZ(cw + t - d));
                    hullPoints.Add(XZ(cw - t + d));
                    hullPoints.Add(XZ(cw - t - d));
                }

                float eccentricity = Hull2D.DistanceOutside(hullPoints, XZ(com), hull);
                float moment = W * eccentricity;

                for (int k = 0; k < downstream.Count; k++)
                {
                    var c = g.connections[downstream[k]];
                    float sh = shares[k] / sum;
                    float F = sh * W;
                    Vector3 nij = NormalFrom(c, i, rot);
                    float vert = Vector3.Dot(nij, Vector3.up);
                    float av = Mathf.Abs(vert);
                    float normalMag = F * av;
                    // Support above (vert > 0) means the piece hangs: tension. Support below: compression.
                    c.loadNormal += vert > 0f ? normalMag : -normalMag;
                    c.loadShear += F * Mathf.Sqrt(Mathf.Max(0f, 1f - av * av));
                    c.loadBending += sh * moment;

                    if (!c.IsGround)
                    {
                        // The load enters the next piece at the interface: eccentricity is resolved locally
                        // by this piece's own supports instead of being carried down the whole structure.
                        int j = c.Other(i);
                        float handed = sh * supMass[i];
                        supMass[j] += handed;
                        supMoment[j] += handed * StructureGraph.WorldCenter(c, pos[c.a], rot[c.a]);
                    }
                }
            }

            // 4. Ratios.
            foreach (var c in g.connections)
            {
                if (c.state != ConnectionState.Structural) continue;
                EvaluateRatio(c);
            }
        }

        public static void EvaluateRatio(Connection c)
        {
            float f = c.StrengthFactor;
            c.qNormal = c.loadNormal >= 0f ? Ratio(c.loadNormal, c.capTension * f) : Ratio(-c.loadNormal, c.capCompression * f);
            c.qShear = Ratio(c.loadShear, c.capShear * f);
            c.qBending = Ratio(c.loadBending, c.capBending * f);
            c.q = Mathf.Max(c.qNormal, Mathf.Max(c.qShear, c.qBending));
        }

        static float Ratio(float load, float cap)
        {
            if (load <= 0f) return 0f;
            return cap <= 1e-6f ? 1e6f : load / cap;
        }

        static Vector3 NormalFrom(Connection c, int from, IReadOnlyList<Quaternion> rot)
        {
            Vector3 n = rot[c.a] * c.normal;
            return from == c.a ? n : -n;
        }

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        void Ensure(int n)
        {
            if (level.Length >= n) return;
            int cap = Mathf.Max(n, level.Length * 2);
            level = new int[cap];
            supMass = new float[cap];
            supMoment = new Vector3[cap];
            order = new int[cap];
        }
    }

    public static class Hull2D
    {
        /// <summary>Distance from p to the convex hull of points; 0 when inside. Handles degenerate hulls.</summary>
        public static float DistanceOutside(List<Vector2> points, Vector2 p, List<Vector2> scratch)
        {
            Build(points, scratch);
            if (scratch.Count == 0) return 0f;
            if (scratch.Count == 1) return Vector2.Distance(scratch[0], p);
            if (scratch.Count == 2) return SegmentDistance(p, scratch[0], scratch[1]);

            bool inside = true;
            float best = float.MaxValue;
            for (int i = 0; i < scratch.Count; i++)
            {
                Vector2 a = scratch[i], b = scratch[(i + 1) % scratch.Count];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (cross < -1e-6f) inside = false;
                best = Mathf.Min(best, SegmentDistance(p, a, b));
            }
            return inside ? 0f : best;
        }

        /// <summary>Andrew's monotone chain, counter-clockwise, collinear points removed.</summary>
        public static void Build(List<Vector2> pts, List<Vector2> hull)
        {
            hull.Clear();
            if (pts.Count == 0) return;
            pts.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            var unique = new List<Vector2>(pts.Count);
            foreach (var q in pts)
                if (unique.Count == 0 || (q - unique[unique.Count - 1]).sqrMagnitude > 1e-10f) unique.Add(q);
            if (unique.Count < 3) { hull.AddRange(unique); return; }

            var h = new Vector2[unique.Count * 2];
            int k = 0;
            for (int i = 0; i < unique.Count; i++)
            {
                while (k >= 2 && Cross(h[k - 2], h[k - 1], unique[i]) <= 1e-9f) k--;
                h[k++] = unique[i];
            }
            for (int i = unique.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(h[k - 2], h[k - 1], unique[i]) <= 1e-9f) k--;
                h[k++] = unique[i];
            }
            for (int i = 0; i < k - 1; i++) hull.Add(h[i]);
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + t * ab);
        }
    }

    /// <summary>Accumulated damage law D += dt·k·max(0, q−1)^p. Not a timer: it stops when q drops below 1.</summary>
    public static class DamageLaw
    {
        public static float Accumulate(float damage, float q, float dt, float rate, float exponent)
        {
            if (q <= 1f) return damage;
            return Mathf.Clamp01(damage + dt * rate * Mathf.Pow(q - 1f, exponent));
        }
    }
}
