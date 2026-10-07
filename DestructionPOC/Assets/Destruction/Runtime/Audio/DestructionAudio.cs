using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Sound for the destruction world, added to every <see cref="DestructionWorld"/>. Joints that fail crack in
    /// their material, shattered pieces break, and debris hitting things thuds by weight and material. Failing mass
    /// is tallied over the last second or so: when a lot of the structure lets go at once, one collapse sound plays
    /// instead of hundreds of separate cracks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DestructionAudio : MonoBehaviour
    {
        [Tooltip("Failing mass (kg, decaying over about a second) that reads as part of the structure coming down.")]
        public float mediumCollapseMass = 3000f;
        [Tooltip("Failing mass that reads as a large collapse.")]
        public float largeCollapseMass = 15000f;
        [Tooltip("Impacts dissipating less energy than this are silent, J.")]
        public float minImpactEnergy = 60f;

        DestructionWorld world;
        float failingMass;
        Vector3 failingCentre;
        float nextMedium, nextLarge, settleAt = -1f;
        Vector3 settlePoint;

        void OnEnable()
        {
            world = GetComponent<DestructionWorld>();
            if (world == null) return;
            world.OnBreak += Break;
            world.OnImpact += Impact;
        }

        void OnDisable()
        {
            if (world == null) return;
            world.OnBreak -= Break;
            world.OnImpact -= Impact;
        }

        enum Kind { Concrete, Brick, Wood, Steel }

        Kind KindOf(int material)
        {
            if (material < 0 || world.Settings == null) return Kind.Concrete;
            string n = world.Settings.Material(material).name.ToLowerInvariant();
            if (n.Contains("wood") || n.Contains("timber")) return Kind.Wood;
            if (n.Contains("brick")) return Kind.Brick;
            if (n.Contains("steel") || n.Contains("metal")) return Kind.Steel;
            return Kind.Concrete;
        }

        /// <summary>Quiet for small hits, approaching full for a wrecking-ball blow.</summary>
        static float Loudness(float energy) => Mathf.Clamp(0.15f + 0.2f * Mathf.Log10(Mathf.Max(1f, energy / 50f)), 0.15f, 1f);

        void Break(BreakEvent e)
        {
            var kind = KindOf(e.material);
            if (e.IsShatter)
            {
                float v = Mathf.Clamp(0.5f + e.mass / 4000f, 0.5f, 1f);
                switch (kind)
                {
                    case Kind.Wood: Sfx.PlayAt("wood_break", e.point, v, 1f, 0.08f); break;
                    case Kind.Steel: Sfx.PlayAt("metal_impact_heavy", e.point, v, 1f, 0.15f); break;
                    case Kind.Brick: Sfx.PlayAt("brick_impact", e.point, v, 0.9f, 0.08f); break;
                    default:
                        Sfx.PlayAt(e.mass > 400f ? "concrete_impact_heavy" : "concrete_hit", e.point, v, 1f, 0.1f);
                        break;
                }
                Sfx.PlayAt("debris_small", e.point, v * 0.7f, 1f, 0.12f);
                Tally(e.mass, e.point);
                return;
            }

            // A joint giving way. Losing its last (residual) hold is the louder of the two.
            bool severed = e.to == ConnectionState.Severed;
            float vol = severed ? 0.55f : 0.35f;
            switch (kind)
            {
                case Kind.Wood: Sfx.PlayAt("wood_snap", e.point, vol, 1f, 0.1f); break;
                case Kind.Steel:
                    if (severed) Sfx.PlayAt("metal_collapse", e.point, vol, 1f, 0.4f);
                    else Sfx.PlayAt("metal_groan_stress", e.point, vol, 1.1f, 1.2f);
                    break;
                case Kind.Brick: Sfx.PlayAt("brick_impact", e.point, vol * 0.8f, 1.1f, 0.1f); break;
                default: Sfx.PlayAt("concrete_hit", e.point, vol * 0.8f, 1.05f, 0.1f); break;
            }
            if (severed) Tally(e.mass, e.point);
        }

        void Tally(float mass, Vector3 point)
        {
            float total = failingMass + mass;
            failingCentre = total > 0f ? Vector3.Lerp(failingCentre, point, mass / total) : point;
            failingMass = total;
        }

        void Impact(ImpactEvent e)
        {
            if (e.energy < minImpactEnergy) return;
            float v = Loudness(e.energy);
            var kind = KindOf(e.material);
            if (e.mass > 400f)
            {
                Sfx.PlayAt(kind == Kind.Steel ? "metal_impact_heavy" : kind == Kind.Wood ? "wood_break" : "chunk_land", e.point, v, 1f, 0.08f);
                return;
            }
            if (e.mass > 60f)
            {
                switch (kind)
                {
                    case Kind.Wood: Sfx.PlayAt("wood_debris_fall", e.point, v, 1.1f, 0.12f); break;
                    case Kind.Steel: Sfx.PlayAt("rebar_drop", e.point, v, 1f, 0.1f); break;
                    case Kind.Brick: Sfx.PlayAt("brick_impact", e.point, v, 1f, 0.06f); break;
                    default: Sfx.PlayAt("concrete_hit", e.point, v, 1.1f, 0.06f); break;
                }
                return;
            }
            if (kind == Kind.Steel) Sfx.PlayAt("metal_impact", e.point, v * 0.7f, 1.3f, 0.08f, 3f, 80f);
            else if (kind == Kind.Wood) Sfx.PlayAt("wood_snap", e.point, v * 0.6f, 1.3f, 0.08f, 3f, 80f);
            else Sfx.PlayAt("debris_small", e.point, v, 1.15f, 0.05f, 3f, 80f);
        }

        void Update()
        {
            float now = Time.time;
            if (failingMass >= largeCollapseMass && now >= nextLarge)
            {
                Sfx.PlayAt("collapse_large", failingCentre, 1f, 1f, 0f, 15f, 260f);
                Sfx.PlayAt("debris_whoosh", failingCentre, 0.6f, 0.8f, 0f, 10f, 160f);
                nextLarge = now + 6f;
                nextMedium = now + 3f;
                settleAt = now + 3.5f;
                settlePoint = failingCentre;
                failingMass = 0f;
            }
            else if (failingMass >= mediumCollapseMass && now >= nextMedium)
            {
                Sfx.PlayAt("collapse_medium", failingCentre, 0.85f, 1f, 0f, 10f, 200f);
                Sfx.PlayAt("debris_fall", failingCentre, 0.6f, 1f, 0.5f, 8f, 160f);
                nextMedium = now + 2.5f;
                failingMass *= 0.3f;
            }
            if (settleAt > 0f && now >= settleAt)
            {
                Sfx.PlayAt("debris_settle_dusty", settlePoint, 0.8f, 1f, 2f, 10f, 160f);
                settleAt = -1f;
            }
            // Mass that failed more than about a second ago no longer counts toward a collapse.
            failingMass *= Mathf.Exp(-Time.deltaTime / 0.8f);
        }
    }
}
