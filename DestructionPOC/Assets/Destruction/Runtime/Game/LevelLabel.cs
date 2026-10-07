using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>A floating name tag drawn in every player's view (machine names, "ROLL-OFF CONTAINER"). It follows this
    /// object, so put it on a machine to have the tag drive with it.</summary>
    public sealed class LevelLabel : MonoBehaviour
    {
        public string text = "LABEL";
        [Tooltip("World-space offset from this object.")]
        public Vector3 offset = new Vector3(0f, 3f, 0f);
        [Tooltip("Hidden while the player operates a machine (machine name tags).")]
        public bool onFootOnly;

        /// <summary>Every enabled label, for the HUD.</summary>
        public static readonly List<LevelLabel> All = new List<LevelLabel>();

        public Vector3 Position => transform.position + offset;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, Position);
            Gizmos.DrawWireSphere(Position, 0.2f);
        }
    }
}
