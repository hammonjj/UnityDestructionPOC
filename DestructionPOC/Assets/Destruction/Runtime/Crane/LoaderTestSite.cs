using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// CraneTest's loader yard: a wheel loader and a skid steer, each facing its own pile of loose concrete rubble,
    /// and a stationary collection container beyond the piles. Both lanes run straight at the container's south wall,
    /// so either machine can scoop, drive on and dump over the wall. The rubble is ordinary destruction pieces added
    /// to the CraneTest scenario (so it also breaks into fragments, which the loaders handle like any other debris).
    ///
    ///     z = -14   ┌────────── container (12 × 2.8 m, 1.1 m walls) ──────────┐
    ///               │   wheel-loader lane x = 41        skid lane x = 48.5     │
    ///     z = -22   piles
    ///     z = -30   loaders start, facing +Z
    /// </summary>
    public static class LoaderTestSite
    {
        public const string DebrisPrefix = "Loader debris";

        public struct Layout
        {
            public Vector3 wheelLoader, skidSteer;     // start positions (facing +Z)
            public Vector3 wheelPile, skidPile;        // pile centres on the ground
            public Vector3 container;                  // container centre on the ground
            public Vector3 dozer, dozerPile;           // landfill dozer start (facing +Z) and the rubble it pushes
        }

        public static Layout Default() => new Layout
        {
            wheelLoader = new Vector3(41f, 0f, -34f),
            skidSteer = new Vector3(48.5f, 0f, -29f),
            wheelPile = new Vector3(41f, 0f, -23f),
            skidPile = new Vector3(48.5f, 0f, -22f),
            container = new Vector3(44.75f, 0f, -14f),
            dozer = new Vector3(33f, 0f, -36f),
            dozerPile = new Vector3(33f, 0f, -24f),
        };

        const int C = DestructionSettings.Concrete;

        public static void AddDebris(List<PieceDef> list, Layout l, bool dozer = false)
        {
            // A wide, low windrow of rubble for the landfill dozer to push; not counted by the loaders' cleanup ledger.
            if (dozer) Pile(list, l.dozerPile, "dozer pile", seed: 123, cells: 6, cellSize: 1.1f, min: 0.3f, max: 0.8f, layers2: 0.4f, prefix: "Dozer debris");
            Pile(list, l.wheelPile, "wheel pile", seed: 77, cells: 4, cellSize: 1.05f, min: 0.3f, max: 0.75f, layers2: 0.45f);
            // One block over every loader limit: needs the crusher or breaker, or the grapple's jaws.
            Loose(list, $"{DebrisPrefix} oversized wheel", l.wheelPile + new Vector3(2.9f, 0.5f, 1.2f), new Vector3(1.8f, 1.0f, 1.2f), C, 20f);
            Pile(list, l.skidPile, "skid pile", seed: 91, cells: 3, cellSize: 0.8f, min: 0.2f, max: 0.5f, layers2: 0.35f);
            Loose(list, $"{DebrisPrefix} oversized skid", l.skidPile + new Vector3(2.0f, 0.4f, 0.4f), new Vector3(1.3f, 0.8f, 1.0f), C, -10f);
        }

        /// <summary>A jittered grid of chunks, some with a second chunk stacked on top. Same pile every reset.</summary>
        static void Pile(List<PieceDef> l, Vector3 c, string tag, int seed, int cells, float cellSize, float min, float max, float layers2, string prefix = DebrisPrefix)
        {
            var rng = new System.Random(seed);
            int n = 0;
            float origin = -(cells - 1) * 0.5f * cellSize;
            for (int ix = 0; ix < cells; ix++)
            for (int iz = 0; iz < cells; iz++)
            {
                // Rounded footprint: skip the corners.
                if (cells > 2 && (ix == 0 || ix == cells - 1) && (iz == 0 || iz == cells - 1)) continue;
                Vector3 s = RandomSize(rng, min, max);
                Vector3 p = c + new Vector3(origin + ix * cellSize + Jitter(rng, 0.1f), s.y * 0.5f + 0.02f, origin + iz * cellSize + Jitter(rng, 0.1f));
                float yaw = (float)rng.NextDouble() * 90f;
                Loose(l, $"{prefix} {tag} {n++}", p, s, C, yaw);
                if (rng.NextDouble() < layers2)
                {
                    Vector3 s2 = RandomSize(rng, min * 0.7f, max * 0.8f);
                    Loose(l, $"{prefix} {tag} {n++}", p +new Vector3(Jitter(rng, 0.1f), s.y * 0.5f + s2.y * 0.5f + 0.04f, Jitter(rng, 0.1f)), s2, C, (float)rng.NextDouble() * 90f);
                }
            }
        }

        static Vector3 RandomSize(System.Random rng, float min, float max) => new Vector3(
            min + (float)rng.NextDouble() * (max - min),
            min * 0.8f + (float)rng.NextDouble() * (max - min) * 0.7f,
            min + (float)rng.NextDouble() * (max - min));

        static float Jitter(System.Random rng, float amount) => ((float)rng.NextDouble() - 0.5f) * 2f * amount;

        static void Loose(List<PieceDef> l, string name, Vector3 pos, Vector3 size, int material, float yaw)
        {
            var d = PieceDef.Box(name, pos, size, PieceKind.Rubble, material);
            d.loose = true;
            d.rotation = Quaternion.Euler(0f, yaw, 0f);
            l.Add(d);
        }
    }
}
