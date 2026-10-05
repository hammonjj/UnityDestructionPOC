using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Shared structural representation for v1: nodes are pieces (region id == piece id), edges are
    /// <see cref="Connection"/>s, plus a virtual ground node. Pure C#; no scene objects.
    /// </summary>
    public sealed class StructureGraph
    {
        public readonly List<PieceDef> pieces = new List<PieceDef>();
        public readonly List<float> mass = new List<float>();
        public readonly List<Connection> connections = new List<Connection>();
        public readonly List<List<int>> adjacency = new List<List<int>>();

        public int PieceCount => pieces.Count;

        public static StructureGraph Build(IReadOnlyList<PieceDef> defs, DestructionSettings settings, float groundY = 0f)
        {
            var g = new StructureGraph();
            foreach (var d in defs) g.AddPieceInternal(d, settings);
            g.DetectConnections(settings, groundY);
            return g;
        }

        /// <summary>Adds a loose piece (no connections) after the graph was built, e.g. a dropped block.</summary>
        public int AddLoosePiece(PieceDef def, DestructionSettings settings)
        {
            def.loose = true;
            return AddPieceInternal(def, settings);
        }

        int AddPieceInternal(PieceDef d, DestructionSettings settings)
        {
            if (d.rotation == default(Quaternion)) d.rotation = Quaternion.identity;
            pieces.Add(d);
            mass.Add(Mathf.Max(1f, settings.Material(d.material).density * d.Volume));
            adjacency.Add(new List<int>());
            return pieces.Count - 1;
        }

        void DetectConnections(DestructionSettings settings, float groundY)
        {
            var s = settings.structure;
            int n = pieces.Count;
            for (int i = 0; i < n; i++)
            {
                if (pieces[i].loose) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (pieces[j].loose) continue;
                    if (TryFace(pieces[i], pieces[j], s, out var c)) AddConnection(c, i, j, settings);
                }

                // Ground connection when the bottom face rests on the ground plane.
                var p = pieces[i];
                float bottom = p.center.y - p.size.y * 0.5f;
                if (Mathf.Abs(bottom - groundY) <= s.contactTolerance)
                {
                    var c = new Connection
                    {
                        centerOffsetA = new Vector3(0f, -p.size.y * 0.5f, 0f),
                        normal = Vector3.down,
                    };
                    bool xLonger = p.size.x >= p.size.z;
                    c.tangent = xLonger ? Vector3.right : Vector3.forward;
                    c.depthDir = xLonger ? Vector3.forward : Vector3.right;
                    c.width = Mathf.Max(p.size.x, p.size.z);
                    c.depth = Mathf.Min(p.size.x, p.size.z);
                    AddConnection(c, i, Connection.Ground, settings);
                }
            }
        }

        static bool TryFace(PieceDef A, PieceDef B, StructureSettings s, out Connection c)
        {
            c = null;
            Vector3 minA = A.center - A.size * 0.5f, maxA = A.center + A.size * 0.5f;
            Vector3 minB = B.center - B.size * 0.5f, maxB = B.center + B.size * 0.5f;
            for (int k = 0; k < 3; k++)
            {
                bool plus = Mathf.Abs(minB[k] - maxA[k]) <= s.contactTolerance;
                bool minus = Mathf.Abs(minA[k] - maxB[k]) <= s.contactTolerance;
                if (!plus && !minus) continue;

                int u = (k + 1) % 3, v = (k + 2) % 3;
                float lu = Mathf.Max(minA[u], minB[u]), hu = Mathf.Min(maxA[u], maxB[u]);
                float lv = Mathf.Max(minA[v], minB[v]), hv = Mathf.Min(maxA[v], maxB[v]);
                float ou = hu - lu, ov = hv - lv;
                if (ou < s.minOverlap || ov < s.minOverlap) return false;

                Vector3 center = Vector3.zero;
                center[k] = plus ? 0.5f * (maxA[k] + minB[k]) : 0.5f * (minA[k] + maxB[k]);
                center[u] = 0.5f * (lu + hu);
                center[v] = 0.5f * (lv + hv);

                Vector3 normal = Vector3.zero;
                normal[k] = plus ? 1f : -1f;
                Vector3 au = Vector3.zero; au[u] = 1f;
                Vector3 av = Vector3.zero; av[v] = 1f;

                c = new Connection
                {
                    centerOffsetA = center - A.center,
                    normal = normal,
                    tangent = ou >= ov ? au : av,
                    depthDir = ou >= ov ? av : au,
                    width = Mathf.Max(ou, ov),
                    depth = Mathf.Min(ou, ov),
                };
                return true;
            }
            return false;
        }

        void AddConnection(Connection c, int a, int b, DestructionSettings settings)
        {
            c.id = connections.Count;
            c.a = a;
            c.b = b;
            c.area = c.width * c.depth;
            ApplyAuthoredCapacity(c, settings);
            connections.Add(c);
            adjacency[a].Add(c.id);
            if (b >= 0) adjacency[b].Add(c.id);
        }

        /// <summary>Interface strength uses the weaker of the two materials (ground counts as the piece's own).</summary>
        public void ApplyAuthoredCapacity(Connection c, DestructionSettings settings)
        {
            var ma = settings.Material(pieces[c.a].material);
            var mb = c.b >= 0 ? settings.Material(pieces[c.b].material) : ma;
            float mul = settings.structure.strengthMultiplier * (c.b < 0 ? settings.structure.groundCapacityFactor : 1f);
            float sectionModulus = c.width * c.depth * c.depth / 6f;

            c.authoredTension = Mathf.Min(ma.tensileStrength, mb.tensileStrength) * c.area * mul;
            c.authoredCompression = Mathf.Min(ma.compressiveStrength, mb.compressiveStrength) * c.area * mul;
            c.authoredShear = Mathf.Min(ma.shearStrength, mb.shearStrength) * c.area * mul;
            c.authoredBending = Mathf.Min(ma.tensileStrength, mb.tensileStrength) * sectionModulus * mul;
            c.capTension = c.authoredTension;
            c.capCompression = c.authoredCompression;
            c.capShear = c.authoredShear;
            c.capBending = c.authoredBending;

            c.impactEnergyCapacity = Mathf.Min(ma.impactToughness, mb.impactToughness) * c.area
                                     * (c.b < 0 ? settings.structure.groundCapacityFactor : 1f);
            ApplyResidualCapacity(c, settings);
        }

        /// <summary>Residual (rebar) capacity scales with interface width. Safe to re-run at any time.</summary>
        public void ApplyResidualCapacity(Connection c, DestructionSettings settings)
        {
            var ma = settings.Material(pieces[c.a].material);
            var mb = c.b >= 0 ? settings.Material(pieces[c.b].material) : ma;
            float rmul = settings.residual.strengthMultiplier;
            c.residualForceCapacity = Mathf.Min(ma.residualForcePerMeter, mb.residualForcePerMeter) * c.width * rmul;
            c.residualYieldMoment = Mathf.Min(ma.residualYieldMomentPerMeter, mb.residualYieldMomentPerMeter) * c.width * rmul;
        }

        /// <summary>
        /// Calibration: capacities are raised to at least the intact load times the safety factor, so authored
        /// structures are stable in their original configuration while still reacting to changed loading.
        /// Call after a solve on the intact structure.
        /// </summary>
        public void Calibrate(float safetyFactor, float groundFactor = 1f)
        {
            foreach (var c in connections)
            {
                c.intactNormal = c.loadNormal;
                c.intactShear = c.loadShear;
                c.intactBending = c.loadBending;
                float sf = safetyFactor * (c.IsGround ? groundFactor : 1f);
                if (c.loadNormal > 0f) c.capTension = Mathf.Max(c.authoredTension, c.loadNormal * sf);
                else c.capCompression = Mathf.Max(c.authoredCompression, -c.loadNormal * sf);
                c.capShear = Mathf.Max(c.authoredShear, c.loadShear * sf);
                c.capBending = Mathf.Max(c.authoredBending, c.loadBending * sf);
            }
        }

        public Connection Find(int a, int b)
        {
            foreach (var id in adjacency[a])
            {
                var c = connections[id];
                if (c.Other(a) == b) return c;
            }
            return null;
        }

        public Connection GroundConnection(int piece)
        {
            foreach (var id in adjacency[piece])
                if (connections[id].IsGround) return connections[id];
            return null;
        }

        /// <summary>World-space interface centre given piece A's current pose.</summary>
        public static Vector3 WorldCenter(Connection c, Vector3 posA, Quaternion rotA) => posA + rotA * c.centerOffsetA;
    }
}
