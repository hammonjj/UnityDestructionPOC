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

        // ---- Whole-building gauge (ConvenienceStore). Independent of the staged-debris numbers above.
        //
        // What counts as "the building": every authored piece of the lot model (store, pump canopy, awning, cars,
        // light poles, pylon sign, dumpster enclosure, bins, bollards, boundary wall and fence) EXCEPT ground-level
        // paving, i.e. any piece whose top surface is within BuildingPavingTop of the ground (sidewalk slabs, curbs,
        // pump-island slab). Paving is the lot surface, not something a demolition crew carts away, and
        // being anchored flat to the ground it can never be dumped. Cars and props are structure pieces in the model, so
        // they are part of the building.
        //
        // The denominator is the summed mass of those pieces as authored, read once from a freshly built world and then
        // frozen. Shattered pieces are replaced by fragments (named "<piece> frag <k>") whose masses add up to the
        // parent's, and a piece accepted by the container is marked no-shatter, so mass is conserved and the percentage
        // is the same whether the rubble is whole pieces or crumbs. Pieces that did not come from the building (loader
        // debris, tool-dropped blocks) are ignored in both numerator and denominator.
        public const float BuildingPavingTop = 0.25f;
        const string FragmentMarker = " frag ";

        readonly HashSet<string> buildingRoots = new HashSet<string>();
        /// <summary>Mass of the complete building as authored, kg. Fixed at <see cref="ResetBuilding"/>.</summary>
        public float BuildingMassKg { get; private set; }
        public float BuildingVolumeM3 { get; private set; }
        public int BuildingPieces { get; private set; }
        /// <summary>Mass of building rubble (whole pieces or fragments) accepted by the container, kg.</summary>
        public float BuildingClearedKg { get; private set; }
        public float BuildingClearedVolumeM3 { get; private set; }
        public bool HasBuilding => BuildingMassKg > 0f;
        /// <summary>Fraction (0-1) of the building's mass delivered to the container.</summary>
        public float BuildingProgress => BuildingMassKg <= 0f ? 0f : Mathf.Clamp01(BuildingClearedKg / BuildingMassKg);

        public event Action Changed;

        /// <summary>The authored piece a fragment descends from ("wall frag 3 frag 1" is "wall").</summary>
        public static string RootName(string name)
        {
            int i = name.IndexOf(FragmentMarker, StringComparison.Ordinal);
            return i < 0 ? name : name.Substring(0, i);
        }

        /// <summary>True for authored pieces that count towards the building (everything but ground-level paving).</summary>
        public static bool CountsAsBuilding(PieceDef def) =>
            !def.loose && def.center.y + def.size.y * 0.5f > BuildingPavingTop;

        /// <summary>Read the building's total from a freshly built world. Fragments are skipped, so calling this on a
        /// world that has already shattered gives the same answer as on the intact one.</summary>
        public void ResetBuilding(DestructionWorld world)
        {
            buildingRoots.Clear();
            BuildingMassKg = BuildingVolumeM3 = BuildingClearedKg = BuildingClearedVolumeM3 = 0f;
            BuildingPieces = 0;
            if (world != null && world.Graph != null)
            {
                var g = world.Graph;
                for (int i = 0; i < g.PieceCount; i++)
                {
                    var def = g.pieces[i];
                    if (def.name.Contains(FragmentMarker) || !CountsAsBuilding(def)) continue;
                    // Loose pieces added after the build (tool-dropped blocks, debris) are not part of the building.
                    if (world.pieces.Count > i && world.pieces[i].isFragment) continue;
                    buildingRoots.Add(def.name);
                    BuildingMassKg += g.mass[i];
                    BuildingVolumeM3 += def.Volume;
                    BuildingPieces++;
                }
            }
            Changed?.Invoke();
        }

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
            BuildingClearedKg = BuildingClearedVolumeM3 = 0f; // the building total is re-read by ResetBuilding
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
            if (buildingRoots.Contains(RootName(def.name)))
            {
                BuildingClearedKg += world.Graph.mass[piece];
                BuildingClearedVolumeM3 += def.Volume;
            }
            Changed?.Invoke();
            return true;
        }
    }
}
