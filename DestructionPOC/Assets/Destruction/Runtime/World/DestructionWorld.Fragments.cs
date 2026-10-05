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

        public int LiveFragments => stats.liveFragments;

        bool CanShatter(int piece)
        {
            var f = Settings.fragments;
            if (!f.enabled || piece < 0 || piece >= pieces.Count) return false;
            var p = pieces[piece];
            if (p.removed) return false;
            return !p.isFragment || f.fragmentsCanShatter;
        }

        void RequestShatter(int piece, FailureReason reason)
        {
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
                if (p.removed || !p.box.enabled || !p.gameObject.activeInHierarchy) continue;
                Vector3 closest = p.box.ClosestPoint(point);
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
            if (shatterOrder.Count == 0) return;
            var f = Settings.fragments;
            bool topologyChanged = false;

            foreach (int i in shatterOrder)
            {
                var reason = shatterRequests[i];
                var p = pieces[i];
                if (p.removed) continue;

                var t = p.transform;
                Vector3 size = t.localScale; // includes any debris shrink; cluster parents are unit scale
                int wanted = Fragmenter.TargetCount(size, f.targetSize, f.minPerPiece, f.maxPerPiece);
                int room = f.maxLiveFragments - stats.liveFragments;
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

                RemovePieceFromWorld(i);

                var boxes = Fragmenter.Split(size, count, f.minSize, Fragmenter.SeedFor(f.seed, i));
                for (int k = 0; k < boxes.Count; k++)
                {
                    var b = boxes[k];
                    Vector3 center = pos + rot * b.center;
                    var def = PieceDef.Box($"{baseName} frag {k}", center, b.size, PieceKind.Rubble, material);
                    def.rotation = rot;
                    int fi = Graph.AddLoosePiece(def, Settings);
                    CreatePieceObject(fi, StaticCluster);
                    StaticCluster.pieces.Remove(fi);
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

                stats.shatteredPieces++;
                var e = new BreakEvent
                {
                    time = SimTime, connection = -1, pieceA = baseName, pieceB = "",
                    from = ConnectionState.Structural, to = ConnectionState.Severed,
                    reason = reason, fragments = boxes.Count,
                };
                log.Add(e);
                RaiseBreak(e);
                topologyChanged = true;
            }

            shatterRequests.Clear();
            shatterOrder.Clear();
            if (topologyChanged) Recluster(null);
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
            colliderToPiece.Remove(p.box);
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
