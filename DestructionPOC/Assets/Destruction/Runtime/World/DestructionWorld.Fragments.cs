using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Fragmentation layer under structural pieces. Violent failures (accumulated direct damage, explosion
    /// core, hard impacts) shatter a piece into box fragments that become ordinary loose rubble. Overload
    /// failures never shatter, so slabs still hinge, sag and hang.
    /// </summary>
    public sealed partial class DestructionWorld
    {
        readonly Dictionary<int, FailureReason> shatterRequests = new Dictionary<int, FailureReason>();
        readonly List<int> shatterOrder = new List<int>();

        // Shape of each live convex fragment in its own frame (centred on its centre of mass, before the debris
        // shrink), so it can shatter again into cells that tile it exactly.
        readonly Dictionary<int, ConvexCell> fragmentCells = new Dictionary<int, ConvexCell>();

        public int LiveFragments => stats.liveFragments;

        bool CanShatter(int piece)
        {
            var f = Settings.fragments;
            if (!f.enabled || piece < 0 || piece >= pieces.Count) return false;
            var p = pieces[piece];
            if (p.removed) return false;
            if (Graph.pieces[piece].noShatter) return false;
            if (!p.isFragment) return true;
            if (!f.fragmentsCanShatter) return false;
            Vector3 s = ShatterSize(piece, out _);
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z)) >= f.minShatterSize;
        }

        /// <summary>
        /// Current dimensions of a piece, including any debris shrink. A convex fragment also returns its shape at
        /// that size; box pieces and box fragments return null and are described by their scale alone.
        /// </summary>
        Vector3 ShatterSize(int piece, out ConvexCell solid)
        {
            var t = pieces[piece].transform;
            solid = null;
            if (!fragmentCells.TryGetValue(piece, out var cell)) return t.localScale; // cluster parents are unit scale
            solid = cell.Transformed(Vector3.zero, t.localScale);
            return solid.GetBounds().size;
        }

        void RequestShatter(int piece, FailureReason reason)
        {
            // Rubble landing on rubble must not grind itself down; only tools break fragments further.
            if (reason == FailureReason.Impact && piece >= 0 && piece < pieces.Count && pieces[piece].isFragment) return;
            if (!CanShatter(piece) || shatterRequests.ContainsKey(piece)) return;
            shatterRequests[piece] = reason;
            shatterOrder.Add(piece);
        }

        /// <summary>Pieces whose collider lies within the explosion core shatter.</summary>
        void RequestExplosionShatter(Vector3 point, float coreRadius)
        {
            if (!Settings.fragments.enabled || !Settings.fragments.shatterOnExplosionCore || coreRadius <= 0f) return;
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p.removed || !p.shape.enabled || !p.gameObject.activeInHierarchy) continue;
                Vector3 closest = p.shape.ClosestPoint(point);
                if ((closest - point).sqrMagnitude <= coreRadius * coreRadius) RequestShatter(i, FailureReason.Explosion);
            }
        }

        /// <summary>
        /// Runs after damage sources and before the load model: severs the piece's connections, removes it,
        /// and spawns fragments with the piece's pose and point velocity. Explosion impulses are applied later
        /// in the same step, so fragments receive them exactly once.
        /// </summary>
        void ExecuteShatters()
        {
            stats.shatterMs = stats.shapeMs = stats.spawnMs = 0f;
            if (shatterOrder.Count == 0) return;
            var f = Settings.fragments;
            bool topologyChanged = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var part = new System.Diagnostics.Stopwatch();

            // Building fragment shapes is the most expensive thing in the step, so only a few pieces shatter
            // per step. The rest keep their place in the queue and shatter next step; none are dropped.
            int budget = Mathf.Max(1, f.maxShattersPerStep);
            int done = 0;
            var processed = new List<int>();

            foreach (int i in shatterOrder)
            {
                if (done >= budget) break;
                var reason = shatterRequests[i];
                processed.Add(i);
                var p = pieces[i];
                if (p.removed) continue;
                done++;

                var t = p.transform;
                Vector3 size = ShatterSize(i, out var solid);
                int wanted = Fragmenter.TargetCount(size, f.targetSize, f.minPerPiece, f.maxPerPiece);
                // A shattering fragment frees its own slot.
                int room = f.maxLiveFragments - stats.liveFragments + (p.isFragment ? 1 : 0);
                int count = Mathf.Min(wanted, room);

                // Sever everything attached to the piece, logging each connection with the shatter reason.
                foreach (int cid in Graph.adjacency[i])
                {
                    var c = Graph.connections[cid];
                    if (c.state == ConnectionState.Severed) continue;
                    var from = c.state;
                    float q = Mathf.Min(99f, from == ConnectionState.Structural ? c.q : c.qResidual);
                    float d = from == ConnectionState.Structural ? c.damage : c.residualDamage;
                    c.state = ConnectionState.Severed;
                    c.reason = reason;
                    c.violentPending = false;
                    if (from == ConnectionState.Structural) c.failTime = SimTime; else c.residualFailTime = SimTime;
                    DestroyJointFor(c);
                    Record(c, from, ConnectionState.Severed, reason, q, d);
                    topologyChanged = true;
                }

                if (count < 2)
                {
                    // Over budget: the piece detaches whole instead of shattering.
                    stats.shatterSkippedForBudget++;
                    ShrinkIfDebris(i);
                    continue;
                }

                // Parent motion for velocity inheritance.
                var parent = p.cluster;
                Rigidbody parentBody = parent != null && !parent.isStatic ? parent.body : null;
                Vector3 pos = t.position;
                Quaternion rot = t.rotation;
                int material = Graph.pieces[i].material;
                string baseName = Graph.pieces[i].name;

                part.Restart();
                var cells = new List<ConvexCell>(count);
                var defs = BuildFragments(size, solid, count, Fragmenter.SeedFor(f.seed, i), pos, rot, material, baseName,
                    Graph.pieces[i].Volume, cells);
                part.Stop();
                stats.shapeMs += (float)part.Elapsed.TotalMilliseconds;
                if (defs.Count < 2)
                {
                    // A fragment too awkward to cut cleanly stays whole rather than losing volume.
                    ShrinkIfDebris(i);
                    continue;
                }

                RemovePieceFromWorld(i);
                if (p.isFragment) stats.liveFragments--;

                part.Restart();
                for (int k = 0; k < defs.Count; k++)
                {
                    var def = defs[k];
                    Vector3 center = def.center;
                    int fi = Graph.AddLoosePiece(def, Settings);
                    CreatePieceObject(fi, StaticCluster);
                    StaticCluster.pieces.Remove(fi);
                    if (cells[k] != null) fragmentCells[fi] = cells[k];
                    var fp = pieces[fi];
                    fp.isFragment = true;
                    fp.shrunk = true;
                    // Same crumbled-edge shrink as detached debris, so fragments cannot jam in the opening
                    // they came from. Graph mass keeps the full tiled volume (mass is conserved).
                    fp.transform.localScale *= Settings.structure.debrisScale;
                    var k2 = CreateDynamicCluster(new List<int> { fi }, null);
                    if (parentBody != null)
                    {
                        k2.body.linearVelocity = parentBody.GetPointVelocity(center);
                        k2.body.angularVelocity = parentBody.angularVelocity;
                    }
                    stats.liveFragments++;
                }
                part.Stop();
                stats.spawnMs += (float)part.Elapsed.TotalMilliseconds;

                stats.shatteredPieces++;
                var e = new BreakEvent
                {
                    time = SimTime, connection = -1, pieceA = baseName, pieceB = "",
                    from = ConnectionState.Structural, to = ConnectionState.Severed,
                    reason = reason, fragments = defs.Count,
                };
                log.Add(e);
                RaiseBreak(e);
                topologyChanged = true;
            }

            foreach (int i in processed)
            {
                shatterRequests.Remove(i);
                shatterOrder.Remove(i);
            }
            stats.pendingShatters = shatterOrder.Count;
            stats.shatterMs = (float)clock.Elapsed.TotalMilliseconds;
            if (stats.shatterMs > stats.maxShatterMs)
            {
                stats.maxShatterMs = stats.shatterMs;
                stats.peakShapeMs = stats.shapeMs;
                stats.peakSpawnMs = stats.spawnMs;
            }
            if (topologyChanged) Recluster(null);
        }

        readonly List<Mesh> fragmentMeshes = new List<Mesh>();

        /// <summary>
        /// Fragment shapes for one shattered piece, already in world space. Convex Voronoi cells by default
        /// (irregular, exact tiling); axis-aligned box splits as a fallback when cells degenerate. A convex
        /// fragment (`solid` set) is re-cut along its own shape and never falls back to boxes, which would not fit
        /// it; it returns fewer than two fragments instead. `cells` receives each fragment's shape (null for box
        /// fragments). Fragment volumes are scaled so their mass adds up to the piece's (`pieceVolume`).
        /// </summary>
        List<PieceDef> BuildFragments(Vector3 size, ConvexCell solid, int count, int seed, Vector3 pos, Quaternion rot,
            int material, string baseName, float pieceVolume, List<ConvexCell> cells)
        {
            var defs = new List<PieceDef>(count);
            var f = Settings.fragments;

            if (f.shape == FragmentShape.ConvexCells || solid != null)
            {
                float wanted = solid != null ? solid.volume : size.x * size.y * size.z;
                // Awkward fragment shapes can lose sites or slivers at one count yet cut cleanly at a lower one.
                for (int n = count; n >= (solid != null ? 2 : count); n--)
                {
                    var made = solid != null
                        ? VoronoiFracture.Cells(solid, n, seed, f.minSize * 0.5f)
                        : VoronoiFracture.Cells(size, n, seed, f.minSize * 0.5f);
                    float got = 0f;
                    foreach (var c in made) got += c.volume;
                    // Degenerate site layouts can lose volume; fall back rather than lose mass.
                    if (made.Count < 2 || got < wanted * 0.9f) continue;

                    float volumeScale = pieceVolume / got;
                    for (int k = 0; k < made.Count; k++)
                    {
                        var cell = made[k];
                        var mesh = FragmentMesh.Build(cell, $"{baseName} frag {k}");
                        fragmentMeshes.Add(mesh);
                        var def = PieceDef.Box($"{baseName} frag {k}", pos + rot * cell.centroid, Vector3.one, PieceKind.Rubble, material);
                        def.rotation = rot;
                        def.mesh = mesh;
                        def.meshVolume = cell.volume * volumeScale;
                        defs.Add(def);
                        cells.Add(cell.Transformed(-cell.centroid, Vector3.one));
                    }
                    return defs;
                }
                if (solid != null) return defs;
            }

            var boxes = Fragmenter.Split(size, count, f.minSize, seed);
            for (int k = 0; k < boxes.Count; k++)
            {
                var b = boxes[k];
                var def = PieceDef.Box($"{baseName} frag {k}", pos + rot * b.center, b.size, PieceKind.Rubble, material);
                def.rotation = rot;
                defs.Add(def);
                cells.Add(null);
            }
            return defs;
        }

        void ClearFragmentMeshes()
        {
            foreach (var m in fragmentMeshes)
                if (m != null) DestroyImmediate(m);
            fragmentMeshes.Clear();
            fragmentCells.Clear();
        }

        /// <summary>Takes a piece out of the simulation (shattered or demolished).</summary>
        void RemovePieceFromWorld(int i)
        {
            var p = pieces[i];
            var k = p.cluster;
            ParkRemovedPiece(i);
            if (k != null)
            {
                k.pieces.Remove(i);
                if (!k.isStatic)
                {
                    if (k.pieces.Count == 0) DestroyCluster(k);
                    else RefreshClusterMass(k);
                }
            }
        }

        Transform removedHolder;

        /// <summary>
        /// Marks a piece removed and moves its (inactive) object out of its cluster, so destroying the cluster
        /// later cannot destroy the piece the world still indexes.
        /// </summary>
        void ParkRemovedPiece(int i)
        {
            var p = pieces[i];
            p.removed = true;
            colliderToPiece.Remove(p.shape);
            if (removedHolder == null)
            {
                removedHolder = new GameObject("Removed pieces").transform;
                removedHolder.SetParent(root, false);
            }
            p.gameObject.SetActive(false);
            p.transform.SetParent(removedHolder, true);
        }

        /// <summary>Mass and centre of mass follow the remaining pieces of a cluster.</summary>
        void RefreshClusterMass(RigidCluster k)
        {
            if (k.body == null || k.pieces.Count == 0) return;
            float m = 0f;
            Vector3 com = Vector3.zero;
            foreach (int i in k.pieces)
            {
                m += Graph.mass[i];
                com += Graph.mass[i] * pieces[i].transform.position;
            }
            k.body.mass = Mathf.Max(1f, m);
            k.body.centerOfMass = k.transform.InverseTransformPoint(com / Mathf.Max(1e-3f, m));
        }
    }
}
