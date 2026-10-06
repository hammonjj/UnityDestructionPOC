using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// CraneTest's excavator yard: where each machine stands and the destructible test target in front of it. Targets
    /// are ordinary destruction pieces added to the CraneTest scenario, so the world simulates, breaks and resets them
    /// like the warehouse. Steel is added to CraneTest's runtime settings only.
    /// </summary>
    public static class ExcavatorTestSite
    {
        public const string SteelName = "Steel";

        public struct Bay
        {
            public ExcavatorAttachment kind;
            public Vector3 machine;   // machine position (faces -Z, away from the warehouse)
            public Vector3 target;    // ground centre of its target
            public string label;
        }

        /// <summary>Four bays in a row south of the crane, 16 m apart so a swung boom never reaches a neighbour.</summary>
        public static List<Bay> Bays(float rowZ, float spacing, float targetDistance)
        {
            var kinds = new[] { ExcavatorAttachment.Crusher, ExcavatorAttachment.Shear, ExcavatorAttachment.Breaker, ExcavatorAttachment.Grapple };
            var labels = new[] { "CONCRETE  ·  crusher target", "STEEL  ·  shear target", "SLAB  ·  breaker target", "LOOSE DEBRIS  ·  grapple target" };
            var list = new List<Bay>();
            for (int i = 0; i < kinds.Length; i++)
            {
                float x = (i - 1.5f) * spacing;
                list.Add(new Bay
                {
                    kind = kinds[i],
                    machine = new Vector3(x, 0f, rowZ),
                    target = new Vector3(x, 0f, rowZ - targetDistance),
                    label = labels[i],
                });
            }
            return list;
        }

        /// <summary>Adds the steel material to the (runtime copy of the) settings once; returns its index.</summary>
        public static int EnsureSteel(DestructionSettings s)
        {
            if (s.materials == null || s.materials.Count == 0) s.materials = DestructionSettings.DefaultMaterials();
            int i = s.materials.FindIndex(m => m.name == SteelName);
            if (i >= 0) return i;
            s.materials.Add(new MaterialSpec
            {
                // Effective density of a hollow section, not solid steel: the members are solid boxes here.
                name = SteelName, density = 2200f, tensileStrength = 30e6f, compressiveStrength = 40e6f,
                shearStrength = 18e6f, residualForcePerMeter = 250000f, residualYieldMomentPerMeter = 60000f,
                impactToughness = 600000f, color = new Color(0.42f, 0.47f, 0.54f), friction = 0.5f,
            });
            return s.materials.Count - 1;
        }

        public static void AddTargets(List<PieceDef> list, List<Bay> bays, int steel)
        {
            foreach (var bay in bays)
            {
                switch (bay.kind)
                {
                    case ExcavatorAttachment.Crusher: Concrete(list, bay.target); break;
                    case ExcavatorAttachment.Shear: SteelFrame(list, bay.target, steel); break;
                    case ExcavatorAttachment.Breaker: Slab(list, bay.target); break;
                    case ExcavatorAttachment.Grapple: Debris(list, bay.target); break;
                }
            }
        }

        const int C = DestructionSettings.Concrete;

        /// <summary>A capped concrete wall (two panels thin enough to bite) between two free columns.</summary>
        static void Concrete(List<PieceDef> l, Vector3 c)
        {
            l.Add(PieceDef.Box("Crusher wall L", c + new Vector3(-0.7f, 0.9f, 0f), new Vector3(1.4f, 1.8f, 0.35f), PieceKind.Wall, C));
            l.Add(PieceDef.Box("Crusher wall R", c + new Vector3(0.7f, 0.9f, 0f), new Vector3(1.4f, 1.8f, 0.35f), PieceKind.Wall, C));
            l.Add(PieceDef.Box("Crusher cap", c + new Vector3(0f, 1.95f, 0f), new Vector3(2.8f, 0.3f, 0.4f), PieceKind.Slab, C));
            l.Add(PieceDef.Box("Crusher column L", c + new Vector3(-2.4f, 1.1f, 0f), new Vector3(0.45f, 2.2f, 0.45f), PieceKind.Column, C));
            l.Add(PieceDef.Box("Crusher column R", c + new Vector3(2.4f, 1.1f, 0f), new Vector3(0.45f, 2.2f, 0.45f), PieceKind.Column, C));
        }

        /// <summary>A steel portal: two columns, a top beam and a mid rail, both built from short segments so the shear
        /// parts a member where it bites.</summary>
        static void SteelFrame(List<PieceDef> l, Vector3 c, int steel)
        {
            const float colH = 2.8f, col = 0.25f, half = 1.8f;
            Add(l, PieceDef.Box("Steel column L", c + new Vector3(-half, colH * 0.5f, 0f), new Vector3(col, colH, col), PieceKind.Column, steel));
            Add(l, PieceDef.Box("Steel column R", c + new Vector3(half, colH * 0.5f, 0f), new Vector3(col, colH, col), PieceKind.Column, steel));
            // Top beam sits on the column caps.
            float x0 = -half - col * 0.5f, len = 2f * half + col;
            for (int i = 0; i < 4; i++)
            {
                float seg = len / 4f;
                Add(l, PieceDef.Box($"Steel beam {i}", c + new Vector3(x0 + seg * (i + 0.5f), colH + 0.15f, 0f), new Vector3(seg, 0.3f, 0.2f), PieceKind.Slab, steel));
            }
            // Mid rail spans between the column faces.
            x0 = -half + col * 0.5f;
            len = 2f * half - col;
            for (int i = 0; i < 3; i++)
            {
                float seg = len / 3f;
                Add(l, PieceDef.Box($"Steel rail {i}", c + new Vector3(x0 + seg * (i + 0.5f), 1.4f, 0f), new Vector3(seg, 0.2f, 0.15f), PieceKind.Slab, steel));
            }
        }

        static void Add(List<PieceDef> l, PieceDef d)
        {
            d.noShatter = true; // steel bends and parts, it does not shatter into chunks
            l.Add(d);
        }

        /// <summary>A 3 × 3 panel concrete slab on the ground.</summary>
        static void Slab(List<PieceDef> l, Vector3 c)
        {
            const float p = 1.3f, t = 0.3f;
            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
                l.Add(PieceDef.Box($"Breaker slab {ix + 1}{iz + 1}", c + new Vector3(ix * p, t * 0.5f, iz * p), new Vector3(p, t, p), PieceKind.Slab, C));
        }

        /// <summary>Loose chunks and timbers to sort, plus one block deliberately over the grapple's mass limit.</summary>
        static void Debris(List<PieceDef> l, Vector3 c)
        {
            var rng = new System.Random(4321); // same pile every reset
            int n = 0;
            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                if (ix == 1 && iz == 1) continue;
                var size = new Vector3(0.5f + (float)rng.NextDouble() * 0.4f, 0.4f + (float)rng.NextDouble() * 0.3f, 0.5f + (float)rng.NextDouble() * 0.4f);
                var pos = c + new Vector3(ix * 1.5f, size.y * 0.5f + 0.02f, iz * 1.5f);
                Loose(l, $"Debris chunk {n++}", pos, size, C, (float)rng.NextDouble() * 90f);
            }
            Loose(l, "Debris timber 0", c + new Vector3(-0.2f, 0.15f, 2.6f), new Vector3(2f, 0.25f, 0.25f), DestructionSettings.Wood, 10f);
            Loose(l, "Debris timber 1", c + new Vector3(0.4f, 0.15f, -2.6f), new Vector3(2f, 0.25f, 0.25f), DestructionSettings.Wood, -15f);
            // ~4.6 t: too heavy to lift.
            Loose(l, "Debris heavy block", c + new Vector3(2.9f, 0.52f, 1.6f), new Vector3(1.6f, 1f, 1.2f), C, 0f);
        }

        static void Loose(List<PieceDef> l, string name, Vector3 pos, Vector3 size, int material, float yaw)
        {
            var d = PieceDef.Box(name, pos, size, PieceKind.Rubble, material);
            d.loose = true;
            d.rotation = Quaternion.Euler(0f, yaw, 0f);
            l.Add(d);
        }
    }
}
