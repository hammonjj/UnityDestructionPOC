using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>Rigid-cluster membership: union-find over Structural piece-to-piece connections only.</summary>
    public static class ClusterMath
    {
        /// <summary>Returns a component id per piece. Residual and Severed edges never join pieces.</summary>
        public static int[] Components(StructureGraph g, out bool[] anchoredComponent)
        {
            int n = g.PieceCount;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            foreach (var c in g.connections)
            {
                if (c.state != ConnectionState.Structural || c.IsGround) continue;
                int ra = Find(parent, c.a), rb = Find(parent, c.b);
                if (ra != rb) parent[ra] = rb;
            }

            var id = new int[n];
            var map = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                if (!map.TryGetValue(r, out int k)) { k = map.Count; map[r] = k; }
                id[i] = k;
            }

            anchoredComponent = new bool[map.Count];
            foreach (var c in g.connections)
                if (c.IsGround && c.state == ConnectionState.Structural) anchoredComponent[id[c.a]] = true;
            return id;
        }

        static int Find(int[] p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        /// <summary>Rigid-motion inheritance: v_child = v_parent + ω × (c_child − c_parent), ω_child = ω_parent.</summary>
        public static Vector3 InheritedVelocity(Vector3 parentVelocity, Vector3 parentAngular, Vector3 parentCom, Vector3 childCom)
        {
            return parentVelocity + Vector3.Cross(parentAngular, childCom - parentCom);
        }
    }

    public static class HingeMath
    {
        /// <summary>
        /// Sign relating our twist measure to Unity's ConfigurableJoint angular X. Verified by the
        /// residual-hinge PlayMode tests (recreating a joint mid-sag must not let the slab rotate further).
        /// </summary>
        public const float UnityTwistSign = -1f;

        /// <summary>Signed twist of rotation q about a unit axis, degrees in (−180, 180].</summary>
        public static float TwistDegrees(Quaternion q, Vector3 axis)
        {
            float proj = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float ang = 2f * Mathf.Atan2(proj, q.w) * Mathf.Rad2Deg;
            if (ang > 180f) ang -= 360f;
            if (ang <= -180f) ang += 360f;
            return ang;
        }

        /// <summary>
        /// Joint X limits relative to the joint's creation pose that keep the absolute twist inside
        /// [−sag, +sag], given the absolute twist at creation.
        /// </summary>
        public static void Limits(float sagDegrees, float twistAtCreation, float sign, out float low, out float high)
        {
            float l = sign * (-sagDegrees - twistAtCreation);
            float h = sign * (sagDegrees - twistAtCreation);
            low = Mathf.Clamp(Mathf.Min(l, h), -177f, 177f);
            high = Mathf.Clamp(Mathf.Max(l, h), -177f, 177f);
            if (high - low < 0.2f) { high = low + 0.2f; }
        }

        /// <summary>
        /// Plastic sag: while the hinge moment exceeds the yield moment, the allowed rotation grows.
        /// Stops when the moment drops (e.g. contact support) or the maximum is reached. Not timer-driven.
        /// </summary>
        public static float Sag(float sagDegrees, float axisMoment, float yieldMoment, float dt, float rate, float max)
        {
            if (yieldMoment <= 1e-3f) return max;
            float over = axisMoment / yieldMoment - 1f;
            if (over <= 0f) return sagDegrees;
            // Ease into the final stop so the hinge does not slam into its limit.
            float ease = Mathf.Clamp((max - sagDegrees) / 15f, 0.1f, 1f);
            return Mathf.Min(max, sagDegrees + dt * rate * ease * Mathf.Min(over, 1.5f));
        }

        /// <summary>
        /// Picks the interface edge a hinge forms on: the edge most aligned with gravity and the moving piece's
        /// centre of mass, so the rotating piece swings away from its support instead of into it.
        /// </summary>
        public static Vector3 HingePoint(Vector3 center, Vector3 depthDirWorld, float depth, Vector3 movingCom)
        {
            Vector3 toCom = movingCom - center;
            toCom.y = 0f;
            Vector3 bias = Vector3.down + (toCom.sqrMagnitude > 1e-6f ? toCom.normalized : Vector3.zero);
            Vector3 e = depthDirWorld.normalized * (0.5f * depth);
            return Vector3.Dot(e, bias) >= 0f ? center + e : center - e;
        }
    }

    /// <summary>Contact-to-damage filter. Pure, so the resting/impact distinction can be unit tested.</summary>
    public static class ImpactMath
    {
        /// <summary>
        /// Energy-like measure of an impact, J: ½·|impulse|·relative normal speed. Returns 0 below the speed
        /// gate, so resting contact (≈ m·g·dt impulse at ≈ 0 m/s) never qualifies regardless of mass.
        /// </summary>
        public static float Energy(float impulse, float relativeNormalSpeed, float minSpeed)
        {
            if (relativeNormalSpeed < minSpeed || impulse <= 0f) return 0f;
            return 0.5f * impulse * relativeNormalSpeed;
        }

        /// <summary>
        /// Damage for every live connection of a struck piece. The piece absorbs the energy over the total
        /// toughness of its live interfaces (J/m² × m²), so large interfaces are not out-voted by tiny ones.
        /// </summary>
        public static float PieceDamage(float energy, float totalEnergyCapacity, float scale)
        {
            if (energy <= 0f || totalEnergyCapacity <= 0f) return 0f;
            return scale * energy / Mathf.Max(1f, totalEnergyCapacity);
        }
    }
}
