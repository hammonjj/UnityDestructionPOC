using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>One fragment of a piece, as an axis-aligned box in the piece's local frame (metres).</summary>
    public struct FragmentBox
    {
        public Vector3 center;
        public Vector3 size;
        public float Volume => size.x * size.y * size.z;
    }

    /// <summary>
    /// Deterministic fragment pattern for a box piece: recursive splits of the largest fragment along its
    /// longest axis at a seeded fraction. Fragments tile the box exactly, so volume and mass are conserved.
    /// Pure C#; the same seed always gives the same pattern.
    /// </summary>
    public static class Fragmenter
    {
        /// <summary>Fragment count for a box: roughly one fragment per target-sized cell, clamped.</summary>
        public static int TargetCount(Vector3 size, float targetSize, int min, int max)
        {
            float t = Mathf.Max(0.05f, targetSize);
            float n = Mathf.Max(1f, size.x / t) * Mathf.Max(1f, size.y / t) * Mathf.Max(1f, size.z / t);
            return Mathf.Clamp(Mathf.RoundToInt(n), min, max);
        }

        public static List<FragmentBox> Split(Vector3 size, int count, float minSize, int seed)
        {
            var boxes = new List<FragmentBox> { new FragmentBox { center = Vector3.zero, size = size } };
            var rng = new System.Random(seed);
            while (boxes.Count < count)
            {
                int best = -1;
                float bestVolume = 0f;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var b = boxes[i];
                    if (Longest(b.size) < 2f * minSize) continue;
                    if (b.Volume > bestVolume) { bestVolume = b.Volume; best = i; }
                }
                if (best < 0) break;

                var box = boxes[best];
                int axis = LongestAxis(box.size);
                float fraction = 0.35f + 0.3f * (float)rng.NextDouble();
                float len = box.size[axis];
                float a = Mathf.Max(minSize, Mathf.Min(len - minSize, len * fraction));

                var s1 = box.size; s1[axis] = a;
                var s2 = box.size; s2[axis] = len - a;
                var c1 = box.center; c1[axis] = box.center[axis] - len * 0.5f + a * 0.5f;
                var c2 = box.center; c2[axis] = box.center[axis] + len * 0.5f - (len - a) * 0.5f;
                boxes[best] = new FragmentBox { center = c1, size = s1 };
                boxes.Add(new FragmentBox { center = c2, size = s2 });
            }
            return boxes;
        }

        static float Longest(Vector3 s) => Mathf.Max(s.x, Mathf.Max(s.y, s.z));

        static int LongestAxis(Vector3 s) => s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;

        /// <summary>Seed for a piece: stable across resets for the same scenario and piece.</summary>
        public static int SeedFor(int baseSeed, int piece) => unchecked(baseSeed * 73856093 ^ (piece + 1) * 19349663);
    }
}
