using UnityEngine;

namespace DestructionLab
{
    /// <summary>Where a player starts in a level, facing this object's forward. Player N uses the spawn with
    /// <see cref="playerIndex"/> N, or the lowest-indexed spawn offset sideways when the level has fewer spawns.</summary>
    public sealed class PlayerSpawn : MonoBehaviour
    {
        [Min(0)] public int playerIndex;

        public float Yaw => transform.eulerAngles.y;

        void OnDrawGizmos()
        {
            Gizmos.color = playerIndex == 0 ? new Color(0.95f, 0.35f, 0.2f) : new Color(0.2f, 0.55f, 0.95f);
            Vector3 p = transform.position;
            Gizmos.DrawWireCube(p + Vector3.up * 0.9f, new Vector3(0.7f, 1.8f, 0.7f));
            Gizmos.DrawLine(p + Vector3.up * 1.3f, p + Vector3.up * 1.3f + transform.forward * 1.2f);
        }
    }
}
