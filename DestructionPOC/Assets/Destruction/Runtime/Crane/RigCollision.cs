using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Collision test for CraneTest's kinematic machines. A machine moves its parts by transform, so physics alone
    /// would let it pass through buildings. Before each move the rig compares how deep its colliders sit in solid
    /// things before and after, and refuses the move if it adds penetration. Moves that keep or reduce overlap are
    /// allowed, so a machine can always back out.
    ///
    /// Solid: static structure, kinematic bodies (other machines) and dynamic bodies heavier than
    /// <see cref="pushableMass"/> (collapsed wall sections). Lighter loose debris is pushed instead.
    /// </summary>
    public sealed class RigCollision
    {
        /// <summary>Dynamic bodies at or below this mass (kg) are pushed aside rather than blocking.</summary>
        public float pushableMass = 1500f;
        /// <summary>Penetration (m) a move may add before it is refused.</summary>
        public float tolerance = 0.004f;

        public readonly HashSet<Collider> own = new HashSet<Collider>();
        /// <summary>Never solid for this machine (the ground under the tracks, a load in the grapple).</summary>
        public readonly HashSet<Collider> ignore = new HashSet<Collider>();

        static readonly Collider[] probe = new Collider[64];

        public bool Blocks(Collider o)
        {
            if (own.Contains(o) || ignore.Contains(o) || o is CharacterController) return false;
            var rb = o.attachedRigidbody;
            return rb == null || rb.isKinematic || rb.mass > pushableMass;
        }

        /// <summary>Total penetration depth of the given colliders into solid colliders at their current poses
        /// (call <see cref="Physics.SyncTransforms"/> first). Wakes light debris it touches so the solver pushes it.</summary>
        public float Penetration(List<Collider> mine)
        {
            float sum = 0f;
            foreach (var c in mine)
            {
                if (c == null || !c.enabled) continue;
                var b = c.bounds;
                int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + new Vector3(0.02f, 0.02f, 0.02f), probe, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var o = probe[k];
                    if (!Blocks(o))
                    {
                        // Sleeping debris is not woken by colliders that move with their transform.
                        var orb = o.attachedRigidbody;
                        if (orb != null && !orb.isKinematic && !own.Contains(o) && orb.IsSleeping()) orb.WakeUp();
                        continue;
                    }
                    var ct = c.transform;
                    var ot = o.transform;
                    if (Physics.ComputePenetration(c, ct.position, ct.rotation, o, ot.position, ot.rotation, out _, out float d))
                        sum += d;
                }
            }
            return sum;
        }

        /// <summary>Would moving <paramref name="root"/> (and everything under it) from its physics pose to this pose
        /// drive <paramref name="mine"/> further into something solid? Restores the transform afterwards.</summary>
        public bool MoveBlocked(Transform root, Rigidbody body, List<Collider> mine, Vector3 pos, Quaternion rot)
        {
            Vector3 p0 = root.position;
            Quaternion r0 = root.rotation;
            root.SetPositionAndRotation(body.position, body.rotation); // physics pose, not the interpolated one
            Physics.SyncTransforms();
            float before = Penetration(mine);
            root.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
            float after = Penetration(mine);
            root.SetPositionAndRotation(p0, r0);
            Physics.SyncTransforms();
            return after > before + tolerance;
        }
    }
}
