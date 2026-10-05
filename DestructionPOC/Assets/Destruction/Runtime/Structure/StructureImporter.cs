using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>One problem found while reading an authored model.</summary>
    public struct ImportIssue
    {
        public string piece;
        public string message;
        public bool fatal;
        public override string ToString() => $"{(fatal ? "error" : "warning")}: {piece} — {message}";
    }

    public sealed class ImportResult
    {
        public readonly List<PieceDef> pieces = new List<PieceDef>();
        public readonly List<ImportIssue> issues = new List<ImportIssue>();
        public Vector3 size;
        public int Skipped { get; set; }
        public bool HasErrors
        {
            get
            {
                foreach (var i in issues) if (i.fatal) return true;
                return false;
            }
        }
    }

    /// <summary>
    /// Turns an authored model (a Blender blockout exported as FBX, or any prefab) into structural pieces.
    ///
    /// The convention is deliberately thin: one mesh object per structural piece, each an unrotated box, in
    /// metres. Everything else is read from the object's world-space bounds, so it does not matter how the
    /// exporter mapped Blender's Z-up axes onto Unity's Y-up ones.
    ///
    /// Name suffixes carry the rest: `__concrete` (default), `__wood`, `__brick`, and `__noshatter`.
    /// </summary>
    public static class StructureImporter
    {
        public const string MaterialPrefix = "__";
        public const string NoShatterTag = "__noshatter";

        /// <summary>
        /// Reads every mesh renderer under `root`. `groundY` is the height treated as the ground plane;
        /// pieces are shifted so the lowest point of the model sits there.
        /// </summary>
        public static ImportResult Read(GameObject root, DestructionSettings settings, float groundY = 0f,
            bool dropToGround = true, Vector3 offset = default)
        {
            var result = new ImportResult();
            if (root == null)
            {
                result.issues.Add(new ImportIssue { piece = "(root)", message = "no model assigned", fatal = true });
                return result;
            }

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                result.issues.Add(new ImportIssue { piece = root.name, message = "the model has no mesh objects", fatal = true });
                return result;
            }

            var raw = new List<(string name, Bounds bounds, MeshRenderer renderer)>(renderers.Length);
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                raw.Add((CleanName(r.gameObject.name), r.bounds, r));
            }
            if (raw.Count == 0)
            {
                result.issues.Add(new ImportIssue { piece = root.name, message = "the model has no readable meshes", fatal = true });
                return result;
            }

            var total = raw[0].bounds;
            foreach (var p in raw) total.Encapsulate(p.bounds);
            result.size = total.size;

            Vector3 shift = offset;
            if (dropToGround) shift += new Vector3(0f, groundY - total.min.y, 0f);

            foreach (var (name, bounds, renderer) in raw)
            {
                if (!IsBoxShaped(renderer, bounds, out float error, out string reason))
                {
                    result.issues.Add(new ImportIssue
                    {
                        piece = name,
                        message = $"{reason} Its bounding box holds {error:P0} more volume than the mesh itself, " +
                                  "and only axis-aligned boxes are supported, so it was skipped.",
                        fatal = false,
                    });
                    result.Skipped++;
                    continue;
                }

                Vector3 size = bounds.size;
                if (size.x < 0.02f || size.y < 0.02f || size.z < 0.02f)
                {
                    result.issues.Add(new ImportIssue
                    {
                        piece = name, fatal = false,
                        message = $"is thinner than 2 cm ({size.x:0.###} x {size.y:0.###} x {size.z:0.###} m) and was skipped.",
                    });
                    result.Skipped++;
                    continue;
                }

                var def = PieceDef.Box(name, bounds.center + shift, size, KindFor(name, size), MaterialFor(name, settings));
                def.noShatter = IsNoShatter(name);
                result.pieces.Add(def);
            }

            if (result.pieces.Count == 0)
                result.issues.Add(new ImportIssue { piece = root.name, message = "no usable pieces were found", fatal = true });

            WarnAboutOverlaps(result);
            return result;
        }

        /// <summary>
        /// A piece must fill its own world bounding box, which only an axis-aligned box does. Comparing the
        /// mesh's true volume with that box catches rotation, round or tapered shapes, and walls with holes
        /// cut into them, without needing to know the exporter's axis convention.
        /// </summary>
        static bool IsBoxShaped(MeshRenderer renderer, Bounds world, out float error, out string reason)
        {
            error = 0f;
            reason = "is not a box.";
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter.sharedMesh;
            if (!mesh.isReadable)
            {
                // Without Read/Write enabled only the bounding box is visible, so rotation is all we can test.
                var localBounds = mesh.bounds;
                Vector3 s = renderer.transform.lossyScale;
                float boxed = Mathf.Abs(localBounds.size.x * s.x * localBounds.size.y * s.y * localBounds.size.z * s.z);
                if (boxed <= 1e-6f) { reason = "has no volume."; return false; }
                error = world.size.x * world.size.y * world.size.z / boxed - 1f;
                if (error >= 0.02f) { reason = "is rotated off the world axes."; return false; }
                return true;
            }

            float volume = Mathf.Abs(SignedVolume(mesh, renderer.transform.lossyScale));
            if (volume <= 1e-6f) { reason = "has no volume, or its mesh is not closed."; return false; }
            float boxVolume = world.size.x * world.size.y * world.size.z;
            error = boxVolume / volume - 1f;
            if (error < 0.02f) return true;

            // Separate the two failures so the warning says something useful.
            var local = mesh.bounds;
            Vector3 scale = renderer.transform.lossyScale;
            float localBox = Mathf.Abs(local.size.x * scale.x * local.size.y * scale.y * local.size.z * scale.z);
            bool rotated = localBox > 1e-6f && boxVolume / localBox - 1f >= 0.02f;
            bool hollow = localBox > 1e-6f && localBox / volume - 1f >= 0.02f;
            reason = rotated && hollow ? "is rotated off the world axes and is not a solid box."
                : rotated ? "is rotated off the world axes."
                : "is not a solid box (it is rounded, tapered, or has holes cut into it).";
            return false;
        }

        /// <summary>Mesh volume from a tetrahedron fan over its triangles. Open meshes give a meaningless result.</summary>
        static float SignedVolume(Mesh mesh, Vector3 scale)
        {
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            float v6 = 0f;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = Vector3.Scale(verts[tris[i]], scale);
                Vector3 b = Vector3.Scale(verts[tris[i + 1]], scale);
                Vector3 c = Vector3.Scale(verts[tris[i + 2]], scale);
                v6 += Vector3.Dot(a, Vector3.Cross(b, c));
            }
            return v6 / 6f;
        }

        public static string CleanName(string name)
        {
            // FBX import can append ".001" style suffixes for duplicates; keep them, they stay unique.
            return name.Trim();
        }

        public static int MaterialFor(string name, DestructionSettings settings)
        {
            string lower = name.ToLowerInvariant();
            if (settings != null && settings.materials != null)
            {
                for (int i = 0; i < settings.materials.Count; i++)
                {
                    string tag = MaterialPrefix + settings.materials[i].name.ToLowerInvariant();
                    if (lower.Contains(tag)) return i;
                }
            }
            return DestructionSettings.Concrete;
        }

        public static bool IsNoShatter(string name) => name.ToLowerInvariant().Contains(NoShatterTag);

        /// <summary>Guesses a kind from the shape, which only affects the design live load on slabs.</summary>
        public static PieceKind KindFor(string name, Vector3 size)
        {
            string lower = name.ToLowerInvariant();
            if (lower.StartsWith("floor") || lower.StartsWith("slab")) return PieceKind.Slab;
            if (lower.StartsWith("pier") || lower.StartsWith("column") || lower.StartsWith("post")) return PieceKind.Column;
            if (lower.StartsWith("wall") || lower.StartsWith("panel")) return PieceKind.Wall;
            // Flat and wide: treat as a slab so it carries a live load. Tall and thin: a column.
            if (size.y <= 0.4f && size.x > 0.8f && size.z > 0.8f) return PieceKind.Slab;
            if (size.y > 1.5f * Mathf.Max(size.x, size.z)) return PieceKind.Column;
            return PieceKind.Wall;
        }

        /// <summary>Authored pieces that interpenetrate make the connection search unreliable; warn once each.</summary>
        static void WarnAboutOverlaps(ImportResult result)
        {
            const float tolerance = 0.01f;
            for (int i = 0; i < result.pieces.Count; i++)
            {
                for (int j = i + 1; j < result.pieces.Count; j++)
                {
                    Vector3 a = result.pieces[i].center, b = result.pieces[j].center;
                    Vector3 ha = result.pieces[i].size * 0.5f, hb = result.pieces[j].size * 0.5f;
                    float ox = ha.x + hb.x - Mathf.Abs(a.x - b.x);
                    float oy = ha.y + hb.y - Mathf.Abs(a.y - b.y);
                    float oz = ha.z + hb.z - Mathf.Abs(a.z - b.z);
                    if (ox > tolerance && oy > tolerance && oz > tolerance)
                    {
                        result.issues.Add(new ImportIssue
                        {
                            piece = result.pieces[i].name, fatal = false,
                            message = $"overlaps {result.pieces[j].name} by {Mathf.Min(ox, Mathf.Min(oy, oz)):0.###} m. " +
                                      "Pieces should touch, not interpenetrate.",
                        });
                        return; // one warning is enough to point at the problem
                    }
                }
            }
        }
    }
}
