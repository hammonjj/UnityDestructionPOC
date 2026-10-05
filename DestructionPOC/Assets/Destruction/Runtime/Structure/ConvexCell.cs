using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// A convex polyhedron as a list of faces. Each face is a polygon whose vertices run counter-clockwise
    /// seen from outside, so Newell's normal points out of the solid. Pure geometry: no Unity objects.
    /// </summary>
    public sealed class ConvexCell
    {
        public readonly List<List<Vector3>> faces = new List<List<Vector3>>();
        public float volume;
        public Vector3 centroid;

        public int VertexCount
        {
            get
            {
                int n = 0;
                foreach (var f in faces) n += f.Count;
                return n;
            }
        }

        public ConvexCell Clone()
        {
            var c = new ConvexCell { volume = volume, centroid = centroid };
            foreach (var f in faces) c.faces.Add(new List<Vector3>(f));
            return c;
        }

        /// <summary>An axis-aligned box centred on the origin.</summary>
        public static ConvexCell Box(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            var c = new ConvexCell();
            void Face(Vector3 a, Vector3 b, Vector3 d, Vector3 e) => c.faces.Add(new List<Vector3> { a, b, d, e });
            // Counter-clockwise seen from outside.
            Face(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z));    // +z
            Face(new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z)); // −z
            Face(new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z));    // +x
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z)); // −x
            Face(new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z));    // +y
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z)); // −y
            c.Recompute();
            return c;
        }

        /// <summary>
        /// Cuts the solid with the half-space dot(normal, p) ≤ distance, keeping that side, and caps the
        /// opening with a new face. Returns false when nothing of the solid survives.
        /// </summary>
        public bool Clip(Vector3 normal, float distance, float eps = 1e-5f)
        {
            var kept = new List<List<Vector3>>(faces.Count + 1);
            var cuts = new List<Vector3>();
            bool anyCut = false;

            foreach (var poly in faces)
            {
                int n = poly.Count;
                var s = new float[n];
                int inside = 0, outside = 0;
                for (int i = 0; i < n; i++)
                {
                    s[i] = Vector3.Dot(normal, poly[i]) - distance;
                    if (s[i] < -eps) inside++;
                    else if (s[i] > eps) outside++;
                }

                if (outside == 0) { kept.Add(poly); continue; }   // fully on the kept side (or on the plane)
                if (inside == 0) { anyCut = true; continue; }     // fully removed

                anyCut = true;
                var clipped = new List<Vector3>(n + 2);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    float si = s[i], sj = s[j];
                    if (si <= eps) clipped.Add(poly[i]);
                    bool crosses = (si < -eps && sj > eps) || (si > eps && sj < -eps);
                    if (!crosses) continue;
                    float t = si / (si - sj);
                    Vector3 p = poly[i] + (poly[j] - poly[i]) * t;
                    clipped.Add(p);
                    cuts.Add(p);
                }
                if (clipped.Count >= 3) kept.Add(clipped);
                // Vertices that sit exactly on the plane also bound the cap.
                for (int i = 0; i < n; i++)
                    if (Mathf.Abs(s[i]) <= eps) cuts.Add(poly[i]);
            }

            if (!anyCut) return true;              // plane missed the solid entirely
            if (kept.Count == 0) { faces.Clear(); volume = 0f; return false; }

            var cap = BuildCap(cuts, normal, eps);
            if (cap != null) kept.Add(cap);

            faces.Clear();
            faces.AddRange(kept);
            Recompute();
            return volume > 0f;
        }

        /// <summary>Orders the cut points around the plane normal into a convex cap polygon.</summary>
        static List<Vector3> BuildCap(List<Vector3> cuts, Vector3 normal, float eps)
        {
            var unique = new List<Vector3>(cuts.Count);
            float merge = Mathf.Max(eps, 1e-4f);
            foreach (var p in cuts)
            {
                bool dup = false;
                foreach (var q in unique)
                    if ((p - q).sqrMagnitude < merge * merge) { dup = true; break; }
                if (!dup) unique.Add(p);
            }
            if (unique.Count < 3) return null;

            Vector3 c = Vector3.zero;
            foreach (var p in unique) c += p;
            c /= unique.Count;

            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            unique.Sort((a, b) =>
            {
                float aa = Mathf.Atan2(Vector3.Dot(a - c, v), Vector3.Dot(a - c, u));
                float bb = Mathf.Atan2(Vector3.Dot(b - c, v), Vector3.Dot(b - c, u));
                return aa.CompareTo(bb);
            });
            return unique;
        }

        /// <summary>Volume and centroid from a tetrahedron fan over every face triangle.</summary>
        public void Recompute()
        {
            float v6 = 0f;
            Vector3 acc = Vector3.zero;
            foreach (var poly in faces)
            {
                for (int i = 1; i + 1 < poly.Count; i++)
                {
                    Vector3 a = poly[0], b = poly[i], d = poly[i + 1];
                    float t = Vector3.Dot(a, Vector3.Cross(b, d));
                    v6 += t;
                    acc += t * (a + b + d);
                }
            }
            volume = v6 / 6f;
            centroid = Mathf.Abs(v6) < 1e-9f ? Vector3.zero : acc / (4f * v6);
        }

        /// <summary>Smallest dimension of the cell's axis-aligned bounds; used to reject slivers.</summary>
        public float MinExtent()
        {
            if (faces.Count == 0) return 0f;
            Vector3 lo = faces[0][0], hi = faces[0][0];
            foreach (var poly in faces)
            foreach (var p in poly)
            {
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            Vector3 e = hi - lo;
            return Mathf.Min(e.x, Mathf.Min(e.y, e.z));
        }
    }

    /// <summary>
    /// Voronoi fracture of a box: scatter sites, then clip the box against each pair's bisector plane. The
    /// resulting convex cells tile the box exactly, so volume and mass are conserved, while shapes and sizes
    /// vary. Deterministic for a given seed.
    /// </summary>
    public static class VoronoiFracture
    {
        public static List<ConvexCell> Cells(Vector3 size, int count, int seed, float minExtent = 0.08f)
        {
            var sites = Sites(size, count, seed);
            var cells = new List<ConvexCell>(sites.Count);
            foreach (var si in sites)
            {
                var cell = ConvexCell.Box(size);
                bool alive = true;
                foreach (var sj in sites)
                {
                    if (sj == si) continue;
                    Vector3 n = sj - si;
                    float d = (sj.sqrMagnitude - si.sqrMagnitude) * 0.5f;
                    if (n.sqrMagnitude < 1e-10f) continue;
                    if (!cell.Clip(n, d)) { alive = false; break; }
                }
                if (!alive || cell.faces.Count < 4 || cell.volume <= 1e-7f) continue;
                if (cell.MinExtent() < minExtent) continue;
                cells.Add(cell);
            }
            return cells;
        }

        /// <summary>
        /// Jittered grid sites. The grid holds more slots than sites, so the chosen subset leaves gaps and the
        /// cells end up at different sizes; the jitter keeps them from looking like a grid.
        /// </summary>
        public static List<Vector3> Sites(Vector3 size, int count, int seed, float jitter = 0.8f)
        {
            var rng = new System.Random(seed);
            int slots = Mathf.Max(count, Mathf.CeilToInt(count * 1.7f));
            Vector3 n = GridDims(size, slots);
            int nx = (int)n.x, ny = (int)n.y, nz = (int)n.z;

            var order = new List<int>(nx * ny * nz);
            for (int i = 0; i < nx * ny * nz; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--) // deterministic Fisher-Yates
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var cellSize = new Vector3(size.x / nx, size.y / ny, size.z / nz);
            var sites = new List<Vector3>(count);
            for (int k = 0; k < order.Count && sites.Count < count; k++)
            {
                int idx = order[k];
                int ix = idx % nx, iy = (idx / nx) % ny, iz = idx / (nx * ny);
                Vector3 p = new Vector3(
                    -size.x * 0.5f + (ix + 0.5f) * cellSize.x,
                    -size.y * 0.5f + (iy + 0.5f) * cellSize.y,
                    -size.z * 0.5f + (iz + 0.5f) * cellSize.z);
                p += new Vector3(
                    (float)(rng.NextDouble() - 0.5) * cellSize.x * jitter,
                    (float)(rng.NextDouble() - 0.5) * cellSize.y * jitter,
                    (float)(rng.NextDouble() - 0.5) * cellSize.z * jitter);
                sites.Add(p);
            }
            return sites;
        }

        /// <summary>Grid divisions roughly proportional to the box's dimensions, with at least `slots` cells.</summary>
        static Vector3 GridDims(Vector3 size, int slots)
        {
            float v = Mathf.Max(1e-6f, size.x * size.y * size.z);
            float s = Mathf.Pow(v / slots, 1f / 3f);
            int nx = Mathf.Max(1, Mathf.RoundToInt(size.x / s));
            int ny = Mathf.Max(1, Mathf.RoundToInt(size.y / s));
            int nz = Mathf.Max(1, Mathf.RoundToInt(size.z / s));
            while (nx * ny * nz < slots)
            {
                // Grow the axis that is currently coarsest relative to the box.
                float rx = size.x / nx, ry = size.y / ny, rz = size.z / nz;
                if (rx >= ry && rx >= rz) nx++;
                else if (ry >= rz) ny++;
                else nz++;
            }
            return new Vector3(nx, ny, nz);
        }
    }
}
