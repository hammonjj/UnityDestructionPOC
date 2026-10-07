using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Stationary open-top skip that the loaders dump into. Built from boxes (floor, four walls, ribs, corner posts),
    /// so the walls are real colliders. Accepts debris that has been released and stays inside the interior volume for
    /// <see cref="dwellSeconds"/>; accepted pieces stay in the skip as a visible, growing rubble pile. Pieces held by a
    /// loader are never accepted, so a loaded bucket hanging above the container earns nothing.
    /// </summary>
    public sealed class CollectionContainer : MonoBehaviour
    {
        [Tooltip("Interior size: x along the length, y wall height above the floor, z across.")]
        public Vector3 interior = new Vector3(12f, 1.1f, 2.8f);
        public float wallThickness = 0.15f;
        public float floorThickness = 0.2f;
        [Tooltip("A released piece must stay inside this long before it is accepted.")]
        public float dwellSeconds = 0.5f;
        public Color bodyColor = new Color(0.20f, 0.36f, 0.42f);
        public Color trimColor = new Color(0.12f, 0.14f, 0.16f);
        [Tooltip("Optional material assets for the walls and the trim. Empty makes runtime materials from the colours.")]
        public Material bodyMaterial;
        public Material trimMaterial;

        public DestructionWorld world;
        [System.NonSerialized] public CleanupLedger ledger;
        public Collider ground;

        /// <summary>Has walls under it already (authored in a level scene, or built earlier).</summary>
        public bool HasGeometry => transform.childCount > 0;

        /// <summary>Level start: hand over the level's services. A container authored in the scene keeps its walls and
        /// only stops them colliding with the ground; an empty one builds them.</summary>
        public void Init(DestructionWorld levelWorld, CleanupLedger levelLedger, Collider levelGround)
        {
            world = levelWorld;
            ledger = levelLedger;
            ground = levelGround;
            if (!HasGeometry)
            {
                Build();
                return;
            }
            if (ground == null) return;
            foreach (var c in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, ground, true);
        }

        /// <summary>Delete and rebuild the walls from the current size (editor: after changing Interior).</summary>
        [ContextMenu("Rebuild Geometry")]
        public void RebuildGeometry()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            Build();
        }

        readonly Dictionary<Rigidbody, float> dwell = new Dictionary<Rigidbody, float>();
        readonly List<Rigidbody> drop = new List<Rigidbody>();
        readonly HashSet<Rigidbody> seen = new HashSet<Rigidbody>();
        static readonly Collider[] hits = new Collider[256];

        /// <summary>Wall top height above the ground.</summary>
        public float WallTop => floorThickness + interior.y;
        /// <summary>Interior floor surface height above the ground.</summary>
        public float FloorTop => floorThickness;
        public Bounds InteriorBounds => new Bounds(transform.position + Vector3.up * (FloorTop + interior.y * 0.5f), interior);
        public int AcceptedPieces => ledger != null ? ledger.ClearedPieces : 0;

        public void Build()
        {
            var steel = bodyMaterial != null ? bodyMaterial : MakeMaterial(bodyColor);
            var trim = trimMaterial != null ? trimMaterial : MakeMaterial(trimColor);
            float hx = interior.x * 0.5f, hz = interior.z * 0.5f, t = wallThickness;
            Box("Floor", new Vector3(0f, floorThickness * 0.5f, 0f), new Vector3(interior.x + 2f * t, floorThickness, interior.z + 2f * t), trim);
            float wy = floorThickness + interior.y * 0.5f;
            Box("Wall S", new Vector3(0f, wy, -hz - t * 0.5f), new Vector3(interior.x + 2f * t, interior.y, t), steel);
            Box("Wall N", new Vector3(0f, wy, hz + t * 0.5f), new Vector3(interior.x + 2f * t, interior.y, t), steel);
            Box("Wall W", new Vector3(-hx - t * 0.5f, wy, 0f), new Vector3(t, interior.y, interior.z), steel);
            Box("Wall E", new Vector3(hx + t * 0.5f, wy, 0f), new Vector3(t, interior.y, interior.z), steel);
            // Rim, corner posts and vertical ribs read as a steel skip from the overhead camera.
            float top = WallTop + 0.05f;
            Box("Rim S", new Vector3(0f, top, -hz - t * 0.5f), new Vector3(interior.x + 2f * t + 0.1f, 0.1f, t + 0.12f), trim, false);
            Box("Rim N", new Vector3(0f, top, hz + t * 0.5f), new Vector3(interior.x + 2f * t + 0.1f, 0.1f, t + 0.12f), trim, false);
            Box("Rim W", new Vector3(-hx - t * 0.5f, top, 0f), new Vector3(t + 0.12f, 0.1f, interior.z), trim, false);
            Box("Rim E", new Vector3(hx + t * 0.5f, top, 0f), new Vector3(t + 0.12f, 0.1f, interior.z), trim, false);
            int ribs = Mathf.Max(2, Mathf.RoundToInt(interior.x / 1.5f));
            for (int i = 0; i <= ribs; i++)
            {
                float x = -hx + interior.x * i / ribs;
                Box($"Rib S{i}", new Vector3(x, wy, -hz - t - 0.04f), new Vector3(0.14f, interior.y, 0.08f), trim, false);
                Box($"Rib N{i}", new Vector3(x, wy, hz + t + 0.04f), new Vector3(0.14f, interior.y, 0.08f), trim, false);
            }
            // Loading chute: a sloped plate leaning in from the near (south) wall top. Material that lands on the rim or the
            // wall top slides into the skip, so a loader whose bucket only just reaches over the wall can still unload.
            const float chuteLen = 1.1f, chuteAngle = 58f;
            float dz = chuteLen * Mathf.Cos(chuteAngle * Mathf.Deg2Rad), dy = chuteLen * Mathf.Sin(chuteAngle * Mathf.Deg2Rad);
            Box("Chute S", new Vector3(0f, top + 0.05f - dy * 0.5f, -hz - t - 0.1f + dz * 0.5f), new Vector3(interior.x, 0.08f, chuteLen), steel, true, Quaternion.Euler(chuteAngle, 0f, 0f));
            // Skids under the floor so it reads as a stationary bin.
            Box("Skid A", new Vector3(-hx * 0.6f, 0.05f, 0f), new Vector3(0.3f, 0.1f, interior.z + 2f * t + 0.3f), trim, false);
            Box("Skid B", new Vector3(hx * 0.6f, 0.05f, 0f), new Vector3(0.3f, 0.1f, interior.z + 2f * t + 0.3f), trim, false);
        }

        static Material MakeMaterial(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh != null ? sh : Shader.Find("Standard")) { color = c };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            return m;
        }

        void Box(string name, Vector3 localPos, Vector3 size, Material mat, bool collide = true, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            if (rotation.HasValue) go.transform.localRotation = rotation.Value;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (collide)
            {
                if (ground != null && Application.isPlaying) Physics.IgnoreCollision(go.GetComponent<Collider>(), ground, true);
            }
            else if (Application.isPlaying) Destroy(go.GetComponent<Collider>());
            else DestroyImmediate(go.GetComponent<Collider>());
        }

        void FixedUpdate()
        {
            if (world == null || ledger == null || world.Graph == null) return;
            FreezeSettled();
            var b = InteriorBounds;
            // Inside the walls, from the floor up to the rim: material hovering above the rim has not been unloaded yet.
            var half = new Vector3(interior.x * 0.5f - 0.05f, interior.y * 0.5f + 0.2f, interior.z * 0.5f - 0.05f);
            int n = Physics.OverlapBoxNonAlloc(b.center + transform.up * 0.22f, half, hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            seen.Clear();
            for (int k = 0; k < n; k++)
            {
                if (!world.TryGetPiece(hits[k], out int i)) continue;
                var piece = world.pieces[i];
                if (piece == null || piece.removed || piece.cluster == null || piece.cluster.isStatic) continue;
                var rb = piece.cluster.body;
                if (rb == null || rb.isKinematic || ledger.IsHeld(rb)) continue;
                if (!Inside(rb.worldCenterOfMass)) continue;
                seen.Add(rb);
            }
            float dt = Time.fixedDeltaTime;
            foreach (var rb in seen)
            {
                dwell.TryGetValue(rb, out float t);
                t += dt;
                if (t >= dwellSeconds)
                {
                    Accept(rb);
                    drop.Add(rb);
                }
                else dwell[rb] = t;
            }
            foreach (var rb in dwell.Keys) if (rb == null || !seen.Contains(rb)) drop.Add(rb);
            foreach (var rb in drop) dwell.Remove(rb);
            drop.Clear();
        }

        /// <summary>Is a world point inside the interior volume (between the floor and the rim)?</summary>
        public bool Inside(Vector3 p)
        {
            Vector3 l = transform.InverseTransformPoint(p);
            return Mathf.Abs(l.x) <= interior.x * 0.5f && Mathf.Abs(l.z) <= interior.z * 0.5f &&
                   l.y >= FloorTop && l.y <= WallTop + 0.45f; // a little headroom: chunks can rest on the loading chute
        }

        /// <summary>Accepted pieces that are still moving. Once one comes to rest inside it is frozen in place: the rubble
        /// pile stays where it fell (visible and growing) and costs no physics, and a later bump cannot throw credited
        /// debris back out of the skip.</summary>
        readonly List<Rigidbody> settling = new List<Rigidbody>();
        public int FrozenPieces { get; private set; }

        void FreezeSettled()
        {
            for (int i = settling.Count - 1; i >= 0; i--)
            {
                var rb = settling[i];
                if (rb == null) { settling.RemoveAt(i); continue; }
                if (rb.isKinematic) { settling.RemoveAt(i); continue; }
                if (!Inside(rb.worldCenterOfMass)) continue; // still tumbling; keep waiting
                if (rb.linearVelocity.sqrMagnitude > 0.04f || rb.angularVelocity.sqrMagnitude > 0.25f) continue;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                FrozenPieces++;
                settling.RemoveAt(i);
            }
        }

        void Accept(Rigidbody rb)
        {
            if (!settling.Contains(rb)) settling.Add(rb);
            var piece = rb.GetComponentInChildren<Piece>();
            var cluster = piece != null ? piece.cluster : null;
            if (cluster == null) return;
            foreach (int i in new List<int>(cluster.pieces)) ledger.TryAccept(world, i);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + transform.up * (FloorTop + interior.y * 0.5f), transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, interior);
        }

        /// <summary>Forget in-progress dwell timers (scene reset).</summary>
        public void ResetState()
        {
            dwell.Clear();
            settling.Clear();
            FrozenPieces = 0;
        }
    }
}
