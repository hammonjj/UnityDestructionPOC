using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Cab glass is modelled as an opaque "CraneCabGlass" material, which blocks the view from the seat. While the
    /// operator is inside any rig, every glass slot under that rig is swapped for a see-through copy of the same
    /// material, and swapped back on exit. Works on any rig, so new models only need to name the material the same.
    /// </summary>
    public static class CabGlass
    {
        const string GlassPrefix = "CraneCabGlass";
        const float ClearAlpha = 0.12f;

        static readonly Dictionary<Material, Material> clearOf = new Dictionary<Material, Material>();
        static readonly Dictionary<Material, Material> opaqueOf = new Dictionary<Material, Material>();

        public static void SetOperatorInside(Component rig, bool inside)
        {
            if (rig == null) return;
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    Material swap = null;
                    if (inside && m.name.StartsWith(GlassPrefix) && !m.name.Contains("(see-through)") && !opaqueOf.ContainsKey(m)) swap = Clear(m);
                    else if (!inside && opaqueOf.TryGetValue(m, out var original)) swap = original;
                    if (swap == null) continue;
                    mats[i] = swap;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static Material Clear(Material opaque)
        {
            if (clearOf.TryGetValue(opaque, out var c) && c != null) return c;
            c = new Material(opaque) { name = opaque.name + " (see-through)" };
            c.SetFloat("_Surface", 1f);
            c.SetFloat("_Blend", 0f);
            c.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            c.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            c.SetFloat("_ZWrite", 0f);
            c.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            c.SetOverrideTag("RenderType", "Transparent");
            c.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            var col = opaque.GetColor("_BaseColor");
            col.a = ClearAlpha;
            c.SetColor("_BaseColor", col);
            clearOf[opaque] = c;
            opaqueOf[c] = opaque;
            return c;
        }
    }
}
