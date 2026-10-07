using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Lets a kinematic machine roll over minor obstacles (curbs, slab edges, low chunks). The machine's root stays on
    /// its ground plane and keeps driving there; this class only does two things:
    ///
    ///   Clearance  The running gear (wheels, tracks, the low undercarriage) is registered with the machine's
    ///              <see cref="RigCollision"/> as climbers, so obstacles no taller than <see cref="StepHeight"/> above
    ///              the ground do not block it. Attachments (bucket, blade, arms) still collide with them.
    ///   Ride       Each physics step the ground under the wheel or track footprints is sampled, a plane is fitted
    ///              through it and raised until nothing is below it, and the model under the root is lifted and tilted
    ///              toward that plane at a limited rate, so the machine visibly climbs onto and rolls off obstacles.
    ///
    /// Climbable: static colliders and dynamic bodies too heavy to push. Light debris is pushed, never climbed, and
    /// other machines are never climbable.
    /// </summary>
    public sealed class RigTerrain
    {
        /// <summary>Tallest obstacle the running gear rolls over, m.</summary>
        public float StepHeight { get; private set; }
        /// <summary>Largest pitch or roll the ride may reach, degrees.</summary>
        public float maxTilt = 15f;
        /// <summary>How fast the model rises or settles, m/s, and tilts, deg/s.</summary>
        public float liftRate = 1.2f, tiltRate = 40f;

        /// <summary>Current ride: height of the model above the root (m), pitch (+ nose up) and roll (+ right side up), degrees.</summary>
        public float Lift { get; private set; }
        public float Pitch { get; private set; }
        public float Roll { get; private set; }

        Transform root, model;
        RigCollision collision;
        Vector3 basePos;
        Quaternion baseRot;
        readonly List<Vector3> supports = new List<Vector3>(); // root-local footprint points on the ground plane
        float[] heights = new float[0];
        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>Call once the model is built and at its rest pose. <paramref name="runningGear"/> are the machine's
        /// own colliders that are not attachments; those reaching below <paramref name="stepHeight"/> become climbers,
        /// and those touching the ground give the footprint.</summary>
        public void Setup(Transform rootTransform, Transform modelTransform, IEnumerable<Collider> runningGear, RigCollision rigCollision, float stepHeight)
        {
            root = rootTransform;
            model = modelTransform;
            collision = rigCollision;
            StepHeight = Mathf.Max(0f, stepHeight);
            basePos = model.localPosition;
            baseRot = model.localRotation;
            collision.root = root;
            collision.stepHeight = StepHeight;
            supports.Clear();
            foreach (var c in runningGear)
            {
                if (c == null) continue;
                LocalBox(c, out Vector3 min, out Vector3 max);
                if (min.y >= StepHeight) continue;
                collision.climbers.Add(c);
                if (min.y > 0.05f) continue;
                // Along the contact patch, a little inside the ends, at most 0.5 m apart (a track rests on a curb
                // anywhere along its length, not only at its ends).
                float x = (min.x + max.x) * 0.5f, inset = Mathf.Min(0.1f, (max.z - min.z) * 0.25f);
                float z0 = min.z + inset, z1 = max.z - inset;
                int steps = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / 0.5f));
                for (int s = 0; s <= steps; s++) supports.Add(new Vector3(x, 0f, Mathf.Lerp(z0, z1, s / (float)steps)));
            }
            heights = new float[supports.Count];
            if (supports.Count < 3) Debug.LogWarning($"[DestructionLab] {root.name}: no ground footprint found, it will not ride over obstacles.");
            Reset();
        }

        /// <summary>Bounds of a collider in the root's frame, from its own shape and the transforms (Collider.bounds
        /// lags behind transform changes made while the machine is built).</summary>
        void LocalBox(Collider c, out Vector3 min, out Vector3 max)
        {
            Bounds b;
            Transform frame = c.transform;
            if (c is BoxCollider box) b = new Bounds(box.center, box.size);
            else if (c is MeshCollider mc && mc.sharedMesh != null) b = mc.sharedMesh.bounds;
            else
            {
                Physics.SyncTransforms();
                b = c.bounds;
                frame = null;
            }
            min = Vector3.one * float.MaxValue;
            max = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 p = root.InverseTransformPoint(frame != null ? frame.TransformPoint(corner) : corner);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        /// <summary>Footprint points sampled each step (along each wheel or track, at least two each).</summary>
        public int SupportCount => supports.Count;

        public void Reset()
        {
            Lift = Pitch = Roll = 0f;
            Apply();
        }

        /// <summary>Sample the ground under the footprint and move the ride toward it. Call after the root has moved.</summary>
        public void Step(float dt)
        {
            if (model == null || supports.Count < 3) return;
            float groundY = root.position.y;
            Vector3 up = Vector3.up;
            for (int i = 0; i < supports.Count; i++)
            {
                Vector3 p = root.TransformPoint(supports[i]);
                p.y = groundY + StepHeight + 0.05f;
                heights[i] = 0f;
                int n = Physics.RaycastNonAlloc(p, -up, hits, StepHeight + 0.1f, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    if (!Climbable(hits[k].collider)) continue;
                    heights[i] = Mathf.Max(heights[i], Mathf.Clamp(hits[k].point.y - groundY, 0f, StepHeight));
                }
            }
            FitPlane(out float a, out float b, out float c);
            float pitch = Mathf.Clamp(Mathf.Atan(c) * Mathf.Rad2Deg, -maxTilt, maxTilt);
            float roll = Mathf.Clamp(Mathf.Atan(b) * Mathf.Rad2Deg, -maxTilt, maxTilt);
            Lift = Mathf.MoveTowards(Lift, Mathf.Clamp(a, 0f, StepHeight), liftRate * dt);
            Pitch = Mathf.MoveTowards(Pitch, pitch, tiltRate * dt);
            Roll = Mathf.MoveTowards(Roll, roll, tiltRate * dt);
            Apply();
        }

        bool Climbable(Collider o)
        {
            if (collision.own.Contains(o) || RigCollision.debrisOnly.Contains(o)) return false;
            if (collision.ignore.Contains(o)) return false; // the ground plane itself: height 0
            var rb = o.attachedRigidbody;
            if (rb == null) return true;
            return !rb.isKinematic && rb.mass > collision.pushableMass;
        }

        /// <summary>Least-squares plane h = a + b·x + c·z through the sampled heights (root frame), raised until no
        /// sample is above it: the machine rests on its highest supports.</summary>
        void FitPlane(out float a, out float b, out float c)
        {
            int n = supports.Count;
            double sx = 0, sz = 0, sxx = 0, szz = 0, sxz = 0, sh = 0, sxh = 0, szh = 0;
            for (int i = 0; i < n; i++)
            {
                double x = supports[i].x, z = supports[i].z, h = heights[i];
                sx += x; sz += z; sxx += x * x; szz += z * z; sxz += x * z; sh += h; sxh += x * h; szh += z * h;
            }
            // Normal equations [n sx sz; sx sxx sxz; sz sxz szz] [a b c] = [sh sxh szh], solved by Cramer's rule.
            double det = n * (sxx * szz - sxz * sxz) - sx * (sx * szz - sxz * sz) + sz * (sx * sxz - sxx * sz);
            if (System.Math.Abs(det) < 1e-9)
            {
                a = 0f;
                for (int i = 0; i < n; i++) a = Mathf.Max(a, heights[i]);
                b = c = 0f;
                return;
            }
            double da = sh * (sxx * szz - sxz * sxz) - sx * (sxh * szz - sxz * szh) + sz * (sxh * sxz - sxx * szh);
            double db = n * (sxh * szz - szh * sxz) - sh * (sx * szz - sxz * sz) + sz * (sx * szh - sxh * sz);
            double dc = n * (sxx * szh - sxz * sxh) - sx * (sx * szh - sxh * sz) + sh * (sx * sxz - sxx * sz);
            a = (float)(da / det);
            b = (float)(db / det);
            c = (float)(dc / det);
            float raise = 0f;
            for (int i = 0; i < n; i++) raise = Mathf.Max(raise, heights[i] - (a + b * supports[i].x + c * supports[i].z));
            a += raise;
        }

        void Apply()
        {
            if (model == null) return;
            Quaternion tilt = Quaternion.AngleAxis(Roll, Vector3.forward) * Quaternion.AngleAxis(-Pitch, Vector3.right);
            model.localPosition = basePos + Vector3.up * Lift;
            model.localRotation = tilt * baseRot;
        }
    }
}
