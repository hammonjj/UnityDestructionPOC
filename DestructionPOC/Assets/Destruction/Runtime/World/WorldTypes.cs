using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>Scene object for one structural piece (box). Owned by <see cref="DestructionWorld"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class Piece : MonoBehaviour
    {
        public int index;
        public RigidCluster cluster;
        public BoxCollider box;
        public MeshRenderer meshRenderer;
        public bool removed;
        public bool shrunk;
    }

    /// <summary>A rigid group of pieces. Static clusters have no Rigidbody (the intact anchored structure).</summary>
    [DisallowMultipleComponent]
    public sealed class RigidCluster : MonoBehaviour
    {
        public int id;
        public bool isStatic;
        public Rigidbody body;
        public readonly List<int> pieces = new List<int>();
        public Color debugColor = Color.white;
        public float createdTime;
    }

    public struct BreakEvent
    {
        public float time;
        public int connection;
        public string pieceA;
        public string pieceB;
        public ConnectionState from;
        public ConnectionState to;
        public FailureReason reason;
        public float q;
        public float damage;

        public override string ToString()
        {
            string arrow = from == ConnectionState.Structural
                ? (to == ConnectionState.Residual ? "structural → residual" : "structural → severed")
                : "residual → severed";
            return $"{time,6:0.00}s  #{connection} {pieceA}–{pieceB}  {arrow}  ({FailureReasonText.Label(reason)}, q={q:0.00}, D={damage:0.00})";
        }
    }

    /// <summary>Fixed-capacity ring buffer of break events for the HUD and tests.</summary>
    public sealed class EventLog
    {
        readonly BreakEvent[] items;
        int start, count;
        public int Total { get; private set; }
        public float LastEventTime { get; private set; } = -1f;
        public readonly int[] reasonCounts = new int[8];

        public EventLog(int capacity = 64) { items = new BreakEvent[capacity]; }

        public void Add(BreakEvent e)
        {
            int idx = (start + count) % items.Length;
            if (count < items.Length) count++;
            else start = (start + 1) % items.Length;
            items[idx] = e;
            Total++;
            LastEventTime = e.time;
            reasonCounts[(int)e.reason]++;
        }

        public int Count => count;
        public BreakEvent this[int i] => items[(start + i) % items.Length]; // oldest first

        public void Clear()
        {
            start = count = 0;
            Total = 0;
            LastEventTime = -1f;
            System.Array.Clear(reasonCounts, 0, reasonCounts.Length);
        }
    }

    public struct WorldStats
    {
        public int pieces;
        public int staticPieces;
        public int dynamicBodies;
        public int sleepingBodies;
        public int activeJoints;
        public int structural, residual, severed;
        public int failuresThisStep;
        public int pendingFailures;
        public int contactsThisStep;
        public int impactsThisStep;
        public float structuralMs;
        public float maxStructuralMs;
        public int maxDynamicBodies;
        public int maxActiveJoints;
    }
}
