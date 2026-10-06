using System;
using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Cleanup accounting for CraneTest. Material counts as cleared once, when a destruction piece has been unloaded
    /// and accepted inside the <see cref="CollectionContainer"/>; being carried, or sitting in a bucket above the
    /// container, never counts. Credit is by mass (and the matching volume), not by fragment count.
    ///
    /// Conservation: credit is keyed by piece index, so a piece can be credited only once per world build, and an
    /// accepted piece is marked no-shatter so it cannot split into fragments that would be credited a second time.
    /// Pieces that fragment before acceptance are removed by the world and their fragments carry the same total mass.
    /// </summary>
    public sealed class CleanupLedger
    {
        readonly HashSet<int> accepted = new HashSet<int>();
        readonly HashSet<Rigidbody> held = new HashSet<Rigidbody>();
        int buildCount = -1;

        /// <summary>Mass accepted by the container, kg.</summary>
        public float ClearedMassKg { get; private set; }
        public float ClearedVolumeM3 { get; private set; }
        public int ClearedPieces => accepted.Count;
        /// <summary>Mass of the staged loose debris the loaders can carry (oversized chunks excluded), kg.</summary>
        public float StagedMassKg { get; private set; }
        public int StagedPieces { get; private set; }
        /// <summary>Staged chunks too big for either loader; they need breaking or the grapple.</summary>
        public int OversizedPieces { get; private set; }
        public float Progress => StagedMassKg <= 0f ? 0f : Mathf.Clamp01(ClearedMassKg / StagedMassKg);

        public event Action Changed;

        public bool IsAccepted(int piece) => accepted.Contains(piece);

        /// <summary>A loader is carrying this body; it cannot be accepted until it is released.</summary>
        public bool IsHeld(Rigidbody rb) => rb != null && held.Contains(rb);
        public void SetHeld(Rigidbody rb, bool isHeld)
        {
            if (rb == null) return;
            if (isHeld) held.Add(rb); else held.Remove(rb);
        }

        /// <summary>Forget everything and read the staged debris from a freshly built world.</summary>
        public void Reset(DestructionWorld world, float maxLoadableKg)
        {
            accepted.Clear();
            held.Clear();
            ClearedMassKg = ClearedVolumeM3 = 0f;
            StagedMassKg = 0f;
            StagedPieces = OversizedPieces = 0;
            buildCount = world != null ? world.BuildCount : -1;
            if (world != null && world.Graph != null)
            {
                for (int i = 0; i < world.Graph.PieceCount; i++)
                {
                    if (!world.Graph.pieces[i].name.StartsWith(LoaderTestSite.DebrisPrefix, StringComparison.Ordinal)) continue;
                    float m = world.Graph.mass[i];
                    if (m > maxLoadableKg) OversizedPieces++;
                    else
                    {
                        StagedMassKg += m;
                        StagedPieces++;
                    }
                }
            }
            Changed?.Invoke();
        }

        /// <summary>Credit a piece. Returns false (and credits nothing) if it was already credited, was removed, or the
        /// world was rebuilt since the ledger was reset.</summary>
        public bool TryAccept(DestructionWorld world, int piece)
        {
            if (world == null || world.Graph == null || world.BuildCount != buildCount) return false;
            if (piece < 0 || piece >= world.pieces.Count || world.pieces[piece].removed) return false;
            if (!accepted.Add(piece)) return false;
            var def = world.Graph.pieces[piece];
            def.noShatter = true; // already counted: never turn it into fragments that would be counted again
            world.Graph.pieces[piece] = def;
            ClearedMassKg += world.Graph.mass[piece];
            ClearedVolumeM3 += def.Volume;
            Changed?.Invoke();
            return true;
        }
    }
}
