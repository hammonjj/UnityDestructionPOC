using System;
using UnityEngine;

namespace DestructionLab
{
    public enum PieceKind { Wall, Slab, Column, Block, Rubble }

    /// <summary>Authored description of one box piece. Pieces are axis-aligned unless <see cref="loose"/>.</summary>
    [Serializable]
    public struct PieceDef
    {
        public string name;
        public Vector3 center;
        public Vector3 size;
        public int material;
        public PieceKind kind;
        /// <summary>Loose pieces (rubble, dropped blocks) get no connections and start dynamic.</summary>
        public bool loose;
        public Quaternion rotation;
        public Vector3 initialVelocity;
        /// <summary>Irregular fragments carry their own convex mesh, centred on their centre of mass.</summary>
        public Mesh mesh;
        /// <summary>Volume of <see cref="mesh"/>, m³. Boxes derive it from <see cref="size"/> instead.</summary>
        public float meshVolume;
        /// <summary>Authored pieces tagged __noshatter break off whole but never fragment.</summary>
        public bool noShatter;

        public static PieceDef Box(string name, Vector3 center, Vector3 size, PieceKind kind, int material = 0)
        {
            return new PieceDef
            {
                name = name, center = center, size = size, kind = kind, material = material,
                rotation = Quaternion.identity,
            };
        }

        public float Volume => mesh != null ? meshVolume : size.x * size.y * size.z;
    }

    public enum ConnectionState { Structural, Residual, Severed }

    public enum DamageSource { None, Direct, Explosion, Overload, Impact, Residual }

    public enum FailureReason
    {
        None,
        DirectDamage,
        Explosion,
        StructuralOverload,
        ResidualJointFailure,
        Impact,
        ResidualBudget,
    }

    public static class FailureReasonText
    {
        public static string Label(FailureReason r)
        {
            switch (r)
            {
                case FailureReason.DirectDamage: return "direct damage";
                case FailureReason.Explosion: return "direct damage (explosion)";
                case FailureReason.StructuralOverload: return "structural overload";
                case FailureReason.ResidualJointFailure: return "residual-joint failure";
                case FailureReason.Impact: return "impact";
                case FailureReason.ResidualBudget: return "residual budget (non-parallel or excess hinge)";
                default: return "-";
            }
        }
    }

    /// <summary>
    /// One interface between two pieces, or between a piece and the ground (<see cref="b"/> == <see cref="Ground"/>).
    /// Geometry is stored in piece A's local frame. Because pieces are authored unrotated, the authored local
    /// frame equals the authored world frame.
    /// </summary>
    public sealed class Connection
    {
        public const int Ground = -1;

        public int id;
        public int a;
        public int b;

        // Geometry (authored, piece-A local offsets).
        public Vector3 centerOffsetA;
        public Vector3 normal;   // from A towards B
        public Vector3 tangent;  // along the longer interface side; residual hinge axis
        public Vector3 depthDir; // the shorter in-plane side
        public float width;      // along tangent, m
        public float depth;      // along depthDir, m
        public float area;       // m²

        // Capacities. Authored values are kept so calibration can be re-run.
        public float authoredTension, authoredCompression, authoredShear, authoredBending;
        public float capTension, capCompression, capShear, capBending; // N, N, N, N·m
        public float residualForceCapacity;  // N
        public float residualYieldMoment;    // N·m
        public float impactEnergyCapacity;   // J

        // Structural state.
        public ConnectionState state = ConnectionState.Structural;
        public float damage;          // [0,1], structural
        public float residualDamage;  // [0,1], residual attachment
        public DamageSource lastDamageSource;
        public DamageSource residualSource;
        public bool violentPending;
        public FailureReason reason;
        public float failTime = -1f;
        public float residualFailTime = -1f;
        public float lastImpactTime = -100f;

        // Last structural solve (N, N, N·m); loadNormal > 0 is tension.
        public float loadNormal, loadShear, loadBending;
        public float qNormal, qShear, qBending, q;
        public float intactNormal, intactShear, intactBending;

        // Residual attachment runtime state.
        public float sagDegrees;
        public bool hingeChosen;
        public Vector3 hingeOffsetA; // hinge point in piece A's local frame
        public Vector3 emaForce;     // world, N
        public float emaAxisTorque;  // N·m about the hinge axis
        public float emaOffAxisTorque;
        public float qResidual;
        public int catastrophicSteps;
        public bool jointActive;

        public bool IsGround => b == Ground;
        public int Other(int piece) => piece == a ? b : a;

        public float StrengthFactor => Mathf.Max(0f, 1f - damage);
        public float ResidualFactor => Mathf.Max(0f, 1f - residualDamage);

        public void ResetRuntime()
        {
            state = ConnectionState.Structural;
            damage = residualDamage = 0f;
            lastDamageSource = DamageSource.None;
            residualSource = DamageSource.None;
            violentPending = false;
            reason = FailureReason.None;
            failTime = residualFailTime = -1f;
            lastImpactTime = -100f;
            loadNormal = loadShear = loadBending = 0f;
            qNormal = qShear = qBending = q = 0f;
            sagDegrees = 0f;
            hingeChosen = false;
            emaForce = Vector3.zero;
            emaAxisTorque = emaOffAxisTorque = qResidual = 0f;
            catastrophicSteps = 0;
            jointActive = false;
        }
    }
}
