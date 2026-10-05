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
                if (!IsAxisAligned(renderer, bounds, out float error))
                {
                    result.issues.Add(new ImportIssue
                    {
                        piece = name,
                        message = $"is rotated or not a box, so its bounding box is {error:P0} larger than the mesh. " +
                                  "Only unrotated boxes are supported; it was skipped.",
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
        /// A rotated or non-box mesh has a world bounding box noticeably bigger than the mesh itself. Comparing
        /// the two volumes catches both without needing the exporter's axis convention.
        /// </summary>
        static bool IsAxisAligned(MeshRenderer renderer, Bounds world, out float error)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var local = filter.sharedMesh.bounds;
            Vector3 scale = renderer.transform.lossyScale;
            float meshVolume = Mathf.Abs(local.size.x * scale.x * local.size.y * scale.y * local.size.z * scale.z);
            float boxVolume = world.size.x * world.size.y * world.size.z;
            if (meshVolume <= 1e-6f) { error = 0f; return false; }
            error = boxVolume / meshVolume - 1f;
            return error < 0.02f;
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
