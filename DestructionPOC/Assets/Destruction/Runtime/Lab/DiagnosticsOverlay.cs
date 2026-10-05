using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// X-ray line overlay and piece tinting. Connections: green→yellow→red by structural load ratio q,
    /// magenta = residual hinge with a joint, dim purple = dormant residual, grey = severed. White squares =
    /// ground anchors. Crosses at body centres: orange = active, blue = sleeping. Yellow = joint hinge points.
    /// </summary>
    public sealed class DiagnosticsOverlay : MonoBehaviour
    {
        public DestructionWorld world;
        public LabController lab;

        Mesh mesh;
        MeshRenderer meshRenderer;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> indices = new List<int>();
        float nextTint;

        public static readonly Color Residual = new Color(1f, 0.2f, 0.95f, 1f);
        public static readonly Color Dormant = new Color(0.55f, 0.3f, 0.7f, 0.8f);
        public static readonly Color SeveredColor = new Color(0.35f, 0.35f, 0.35f, 0.5f);
        public static readonly Color Active = new Color(1f, 0.6f, 0.1f, 1f);
        public static readonly Color Sleeping = new Color(0.3f, 0.55f, 1f, 1f);
        public static readonly Color Selected = new Color(0.2f, 1f, 1f, 1f);

        public void Init(DestructionWorld w, LabController l)
        {
            world = w;
            lab = l;
            mesh = new Mesh { name = "Diagnostics lines" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var m = w.Settings.overlayMaterial;
            m.renderQueue = 4000;
            meshRenderer.sharedMaterial = m;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        public static Color LoadColor(float q)
        {
            if (q < 0.5f) return Color.Lerp(new Color(0.1f, 0.9f, 0.3f), new Color(0.95f, 0.95f, 0.1f), q / 0.5f);
            return Color.Lerp(new Color(0.95f, 0.95f, 0.1f), new Color(1f, 0.1f, 0.05f), Mathf.Clamp01((q - 0.5f) / 0.5f));
        }

        void LateUpdate()
        {
            if (world == null || world.Graph == null || mesh == null) return;
            verts.Clear(); colors.Clear(); indices.Clear();
            if (lab.DiagnosticsVisible) BuildLines();
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Lines, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);

            if (Time.unscaledTime >= nextTint)
            {
                nextTint = Time.unscaledTime + 0.1f;
                Tint();
            }
        }

        void Line(Vector3 a, Vector3 b, Color c)
        {
            indices.Add(verts.Count); verts.Add(a); colors.Add(c);
            indices.Add(verts.Count); verts.Add(b); colors.Add(c);
        }

        void Cross(Vector3 p, float s, Color c)
        {
            Line(p - Vector3.right * s, p + Vector3.right * s, c);
            Line(p - Vector3.up * s, p + Vector3.up * s, c);
            Line(p - Vector3.forward * s, p + Vector3.forward * s, c);
        }

        void BuildLines()
        {
            var g = world.Graph;
            foreach (var c in g.connections)
            {
                var pa = world.pieces[c.a];
                if (pa.removed) continue;
                var ta = pa.transform;
                Vector3 center = ta.position + ta.rotation * c.centerOffsetA;
                Vector3 t = ta.rotation * c.tangent * (0.5f * c.width * 0.9f);
                Vector3 n = ta.rotation * c.normal;
                Color col;
                switch (c.state)
                {
                    case ConnectionState.Structural: col = LoadColor(Mathf.Max(c.q, c.damage)); break;
                    case ConnectionState.Residual: col = c.jointActive ? Residual : Dormant; break;
                    default: col = SeveredColor; break;
                }
                if (c.IsGround)
                {
                    Vector3 d = ta.rotation * c.depthDir * (0.5f * c.depth * 0.9f);
                    Color gc = c.state == ConnectionState.Structural ? new Color(1f, 1f, 1f, 0.9f) : col;
                    Line(center - t - d, center + t - d, gc);
                    Line(center + t - d, center + t + d, gc);
                    Line(center + t + d, center - t + d, gc);
                    Line(center - t + d, center - t - d, gc);
                    continue;
                }
                Line(center - t, center + t, col);
                if (c.state != ConnectionState.Severed) Line(center, center + n * 0.15f, col);
                if (c.id == lab.SelectedConnection)
                {
                    Vector3 off = ta.rotation * c.depthDir * 0.04f;
                    Line(center - t + off, center + t + off, Selected);
                    Line(center - t - off, center + t - off, Selected);
                    Line(center, center + n * 0.6f, Selected);
                }
            }

            foreach (var r in world.Joints.Values)
            {
                if (r.ownerCluster == null || r.ownerCluster.body == null) continue;
                Cross(r.lastHingeWorld, 0.15f, new Color(1f, 0.95f, 0.2f));
                Line(r.lastHingeWorld, r.ownerCluster.body.worldCenterOfMass, new Color(1f, 1f, 1f, 0.6f));
            }

            foreach (var k in world.clusters)
            {
                if (k == null || k.isStatic || k.body == null || !k.gameObject.activeSelf) continue;
                Cross(k.body.worldCenterOfMass, 0.25f, k.body.IsSleeping() ? Sleeping : Active);
            }

            if (lab.SelectedPiece >= 0 && lab.SelectedPiece < world.pieces.Count) Outline(lab.SelectedPiece, Selected);
            var s = world.CurrentScenario;
            if (s != null && !lab.TriggerUsed)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                foreach (var name in s.highlight)
                {
                    int i = g.pieces.FindIndex(p => p.name == name);
                    if (i >= 0) Outline(i, new Color(0.2f, 1f, 1f, 0.4f + 0.6f * pulse));
                }
            }
        }

        void Outline(int piece, Color c)
        {
            var t = world.pieces[piece].transform;
            Vector3 h = Vector3.one * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
                p[i] = t.TransformPoint(new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
            int[] e = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
            for (int i = 0; i < e.Length; i += 2) Line(p[e[i]], p[e[i + 1]], c);
        }

        void Tint()
        {
            var g = world.Graph;
            for (int i = 0; i < world.pieces.Count; i++)
            {
                var piece = world.pieces[i];
                if (piece == null || piece.removed) continue;
                var baseColor = world.Settings.Material(g.pieces[i].material).color;
                float worst = 0f;
                bool residual = false;
                foreach (int cid in g.adjacency[i])
                {
                    var c = g.connections[cid];
                    if (c.state == ConnectionState.Structural) worst = Mathf.Max(worst, Mathf.Max(c.q, c.damage));
                    else if (c.state == ConnectionState.Residual && c.jointActive && piece.cluster != null && !piece.cluster.isStatic) residual = true;
                }
                Color col;
                switch (lab.Tint)
                {
                    case TintMode.Damage:
                        // Only meaningful load shows: below q = 0.5 the piece keeps its material colour.
                        col = residual ? Color.Lerp(baseColor, Residual, 0.45f)
                            : worst < 0.5f ? baseColor
                            : Color.Lerp(baseColor, LoadColor(worst), Mathf.Lerp(0.3f, 0.8f, (worst - 0.5f) / 0.5f));
                        break;
                    case TintMode.Cluster:
                        col = piece.cluster != null ? piece.cluster.debugColor : baseColor;
                        break;
                    case TintMode.Sleep:
                        col = piece.cluster == null || piece.cluster.isStatic ? new Color(0.55f, 0.55f, 0.58f)
                            : piece.cluster.body != null && piece.cluster.body.IsSleeping() ? Sleeping : Active;
                        break;
                    default:
                        col = baseColor * (1f - 0.45f * Mathf.Clamp01(worst));
                        col.a = 1f;
                        break;
                }
                world.SetPieceColor(i, col);
            }
        }
    }
}
