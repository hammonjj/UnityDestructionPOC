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
        /// <summary>Never solid for any machine: colliders that only hold loose debris (a loader's bucket shell).</summary>
        public static readonly HashSet<Collider> debrisOnly = new HashSet<Collider>();

        /// <summary>Running gear (wheels, tracks, low undercarriage) that rolls over obstacles no taller than
        /// <see cref="stepHeight"/> above <see cref="root"/>'s ground plane. Set up by <see cref="RigTerrain"/>.</summary>
        public readonly HashSet<Collider> climbers = new HashSet<Collider>();
        public float stepHeight;
        public Transform root;

        static readonly Collider[] probe = new Collider[64];

        /// <summary>A solid obstacle low enough for the running gear to roll over. Other machines never are.</summary>
        bool Climbable(Collider o)
        {
            if (stepHeight <= 0f || root == null) return false;
            var rb = o.attachedRigidbody;
            if (rb != null && rb.isKinematic) return false;
            return o.bounds.max.y <= root.position.y + stepHeight + 0.01f;
        }

        public bool Blocks(Collider o)
        {
            if (own.Contains(o) || ignore.Contains(o) || debrisOnly.Contains(o) || o is CharacterController) return false;
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
                bool climber = climbers.Contains(c);
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
                    if (climber && Climbable(o)) continue;
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

        /// <summary>Colliders overlapping <paramref name="b"/> grown by <paramref name="pad"/> on every side, tested in the
        /// box's own frame (the world AABB of a turned blade reaches well past its ends).</summary>
        public static int OverlapBox(BoxCollider b, float pad, Collider[] into)
        {
            var t = b.transform;
            Vector3 s = t.lossyScale;
            Vector3 half = new Vector3(Mathf.Abs(s.x) * b.size.x, Mathf.Abs(s.y) * b.size.y, Mathf.Abs(s.z) * b.size.z) * 0.5f
                           + new Vector3(pad, pad, pad);
            return Physics.OverlapBoxNonAlloc(t.TransformPoint(b.center), half, into, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        }

        static readonly Collider[] hop = new Collider[64];
        readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();
        readonly List<Collider> front = new List<Collider>();

        /// <summary>Total mass (kg) of loose debris that <paramref name="mine"/> (box colliders) pushes along
        /// <paramref name="forward"/>: every pushable body touching them plus, one contact further, the bodies those press
        /// on ahead of them (the pile in front of a blade). Solid things are not counted; they block instead.</summary>
        public float PushLoad(List<Collider> mine, Vector3 forward)
        {
            pushed.Clear();
            front.Clear();
            foreach (var c in mine)
            {
                if (!(c is BoxCollider box) || !c.enabled) continue;
                int n = OverlapBox(box, 0.1f, probe);
                for (int k = 0; k < n; k++)
                {
                    var rb = Loose(probe[k]);
                    if (rb == null) continue;
                    pushed.Add(rb);
                    front.Add(probe[k]);
                }
            }
            foreach (var f in front)
            {
                float ahead = Vector3.Dot(f.attachedRigidbody.worldCenterOfMass, forward);
                var b = f.bounds;
                int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + new Vector3(0.05f, 0.05f, 0.05f), hop, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var rb = Loose(hop[k]);
                    if (rb != null && Vector3.Dot(rb.worldCenterOfMass, forward) > ahead) pushed.Add(rb);
                }
            }
            float sum = 0f;
            foreach (var rb in pushed) sum += rb.mass;
            return sum;
        }

        Rigidbody Loose(Collider o)
        {
            if (own.Contains(o) || ignore.Contains(o)) return null;
            var rb = o.attachedRigidbody;
            return rb != null && !rb.isKinematic && rb.mass <= pushableMass ? rb : null;
        }
    }
}
