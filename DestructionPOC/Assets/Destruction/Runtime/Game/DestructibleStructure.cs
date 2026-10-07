using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Marks an authored structure in a level scene. Every mesh renderer under this object becomes a destructible
    /// piece when the level starts, read by <see cref="StructureImporter"/> from its world-space bounds, so moving,
    /// duplicating or deleting children in the scene view changes the building. The usual blockout rules apply:
    /// axis-aligned boxes that touch face to face, material and kind from name suffixes (Wall_A__brick, Slab_2, ...).
    /// The authored objects are hidden at runtime; the destruction world owns the pieces it builds from them.
    /// </summary>
    public sealed class DestructibleStructure : MonoBehaviour
    {
        [Tooltip("Shift the pieces so the structure's lowest point sits on the ground. Off keeps them exactly where they are authored.")]
        public bool dropToGround;

        /// <summary>Read the pieces (works while this object is hidden).</summary>
        public ImportResult Read(DestructionSettings settings, float groundY) =>
            StructureImporter.Read(gameObject, settings, groundY, dropToGround);

        void OnDrawGizmosSelected()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
