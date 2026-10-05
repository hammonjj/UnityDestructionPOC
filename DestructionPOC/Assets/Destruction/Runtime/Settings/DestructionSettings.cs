using System;
using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Per-material constants. Strength values are game-tuned interface ("bond") strengths, deliberately far
    /// below real reinforced concrete so that failures happen at demonstration scale. All values are SI.
    /// </summary>
    [Serializable]
    public class MaterialSpec
    {
        public string name = "Concrete";
        [Tooltip("Density, kg/m³.")] public float density = 2400f;
        [Tooltip("Interface tensile strength, Pa (N/m²). Also used for bending through the section modulus.")]
        public float tensileStrength = 0.35e6f;
        [Tooltip("Interface compressive strength, Pa.")] public float compressiveStrength = 8e6f;
        [Tooltip("Interface shear strength, Pa.")] public float shearStrength = 0.6e6f;
        [Tooltip("Residual (rebar) force capacity per metre of interface width, N/m.")]
        public float residualForcePerMeter = 21000f;
        [Tooltip("Residual plastic-hinge yield moment per metre of interface width, N·m/m.")]
        public float residualYieldMomentPerMeter = 6000f;
        [Tooltip("Impact energy one m² of interface absorbs before failing, J/m².")]
        public float impactToughness = 40000f;
        public Color color = new Color(0.72f, 0.72f, 0.70f);
        [Range(0f, 1.5f)] public float friction = 0.8f;
    }

    [Serializable]
    public class StructureSettings
    {
        [Tooltip("Intact capacities are raised to at least intact load × this factor (calibration).")]
        [Min(1f)] public float safetyFactor = 2.5f;
        [Tooltip("Design live (occupancy) load on slab tops used during calibration, Pa. Lets floors carry some debris.")]
        [Min(0f)] public float designLiveLoad = 5000f;
        [Tooltip("Ground (foundation) connections are this many times stronger than an equivalent interface.")]
        [Min(1f)] public float groundCapacityFactor = 8f;
        [Tooltip("Global multiplier on authored interface strengths (applies on reset).")]
        [Min(0.01f)] public float strengthMultiplier = 1f;
        [Tooltip("Damage rate k in D += dt·k·max(0, q−1)^p, 1/s.")]
        [Min(0f)] public float damageRate = 1.5f;
        [Tooltip("Damage exponent p.")] [Min(0.5f)] public float damageExponent = 2f;
        [Tooltip("Load ratio at which a structural connection fails immediately.")]
        [Min(1f)] public float instantFailRatio = 2f;
        [Tooltip("Share weight for connections whose support lies below the piece (bearing).")]
        public float bearingWeight = 1f;
        [Tooltip("Share weight for connections whose support lies above the piece (hanging).")]
        public float hangingWeight = 0.6f;
        [Tooltip("Share weight for side (vertical-plane) connections.")]
        public float lateralWeight = 0.35f;
        [Tooltip("Include resting contact loads from dynamic debris on static structure.")]
        public bool contactLoads = true;
        [Tooltip("Faces closer than this are considered touching, m.")]
        public float contactTolerance = 0.005f;
        [Tooltip("Minimum overlap on each in-plane axis for a connection, m.")]
        public float minOverlap = 0.05f;
        [Tooltip("Colliders are inset by this much on every side so neighbours never touch, m.")]
        public float colliderInset = 0.01f;
        [Tooltip("Uniform scale applied to a piece once it has no live connections (crumbled edges), so it cannot wedge in the exact-fit hole it came from.")]
        [Range(0.8f, 1f)] public float debrisScale = 0.96f;
    }

    [Serializable]
    public class ResidualSettings
    {
        [Tooltip("Overload failures leave a residual attachment. Off: every failure severs.")]
        public bool enabled = true;
        [Tooltip("Global multiplier on residual force and yield capacity.")]
        [Min(0.01f)] public float strengthMultiplier = 1f;
        [Tooltip("Maximum residual joints per rigid cluster; extra (or non-parallel) ones are severed.")]
        [Range(1, 4)] public int maxJointsPerCluster = 2;
        [Tooltip("Initial hinge limit, degrees either side of the authored pose.")]
        public float initialSagDegrees = 1f;
        [Tooltip("Maximum plastic hinge rotation, degrees.")]
        public float maxSagDegrees = 80f;
        [Tooltip("Plastic sag speed at twice the yield moment, degrees per second.")]
        public float sagRateDegreesPerSecond = 20f;
        [Tooltip("Residual damage rate k, 1/s.")] public float damageRate = 1.5f;
        [Tooltip("Residual damage exponent p.")] public float damageExponent = 2f;
        [Tooltip("EMA smoothing factor for joint force readings per fixed step.")]
        [Range(0.01f, 1f)] public float forceSmoothing = 0.12f;
        [Tooltip("Fixed steps after joint creation during which readings are ignored.")]
        public int warmupSteps = 5;
        [Tooltip("Raw load ratio that severs immediately when held for three consecutive steps.")]
        public float catastrophicRatio = 6f;
        [Tooltip("Off-axis bending capacity as a multiple of the hinge yield moment.")]
        public float offAxisMomentFactor = 3f;
        [Tooltip("Swing limit about the two non-hinge axes, degrees.")]
        public float swingLimitDegrees = 4f;
        [Tooltip("Slerp damper per tonne of the lighter body, N·m·s/rad.")]
        public float damperPerTonne = 600f;
        [Tooltip("Joint mass ratio is clamped to at most this value using mass scaling.")]
        public float maxMassRatio = 10f;
    }

    [Serializable]
    public class PhysicsSettings
    {
        public int solverIterations = 12;
        public int solverVelocityIterations = 2;
        public int jointedSolverIterations = 16;
        public int jointedSolverVelocityIterations = 4;
        public float maxDepenetrationVelocity = 3f;
        public float maxAngularVelocity = 20f;
        public float killPlaneY = -40f;
        public bool interpolate = true;
    }

    [Serializable]
    public class ImpactSettings
    {
        public bool enabled = true;
        [Tooltip("Minimum pre-impact relative normal speed for impact damage, m/s.")]
        public float minRelativeSpeed = 2f;
        [Tooltip("Multiplier on impact energy before it is converted to damage.")]
        public float damageScale = 1f;
        [Tooltip("Energy per connection, as a multiple of its toughness, that severs without a residual.")]
        public float violentEnergyFactor = 4f;
        [Tooltip("Seconds before the same piece pair can cause impact damage again.")]
        public float pairCooldown = 0.2f;
        [Tooltip("Seconds before the same connection can receive impact damage again.")]
        public float connectionCooldown = 0.25f;
        [Tooltip("Maximum connection transitions committed per fixed step; the rest wait, never dropped.")]
        public int maxFailuresPerStep = 8;
    }

    [Serializable]
    public class ToolSettings
    {
        [Tooltip("Damage added to every connection of the clicked piece.")]
        [Range(0.05f, 1.5f)] public float clickDamage = 0.35f;
        [Range(0.5f, 8f)] public float explosionRadius = 2.5f;
        [Tooltip("Damage at the explosion centre, falling off linearly to zero at the radius.")]
        [Range(0.1f, 4f)] public float explosionDamage = 1.5f;
        [Tooltip("Connections inside this fraction of the radius are severed violently.")]
        [Range(0f, 1f)] public float explosionInnerFraction = 0.35f;
        [Tooltip("Impulse at the centre, N·s, applied once per rigid body.")]
        public float explosionImpulse = 12000f;
        [Tooltip("Largest velocity change an explosion gives any single body, m/s.")]
        public float explosionMaxDeltaV = 9f;
        [Tooltip("Mass of a dropped block, kg.")] public float dropBlockMass = 2000f;
        public float dropBlockHeight = 4f;
    }

    /// <summary>Fragmentation: violent failures shatter pieces into rubble; overload failures never do.</summary>
    [Serializable]
    public class FragmentSettings
    {
        public bool enabled = true;
        [Tooltip("Approximate fragment edge length, m.")]
        [Range(0.3f, 3f)] public float targetSize = 0.8f;
        [Range(2, 32)] public int minPerPiece = 3;
        [Range(2, 32)] public int maxPerPiece = 12;
        [Tooltip("Smallest fragment dimension, m.")]
        public float minSize = 0.15f;
        [Tooltip("Live fragment budget. Over budget, a piece that should shatter detaches whole instead.")]
        public int maxLiveFragments = 300;
        [Tooltip("Accumulated direct (click) damage on a piece that shatters it.")]
        public float directShatterDamage = 1f;
        public bool shatterOnExplosionCore = true;
        [Tooltip("Impact energy per kg of the struck piece that shatters it, J/kg. A 3 m fall onto concrete gives about 13 J/kg.")]
        public float impactShatterEnergyPerKg = 20f;
        [Tooltip("Fragments of a shattered piece never shatter again (avoids gravel).")]
        public bool fragmentsCanShatter = false;
        public int seed = 1729;
    }

    [CreateAssetMenu(menuName = "Destruction Lab/Settings", fileName = "DestructionSettings")]
    public class DestructionSettings : ScriptableObject
    {
        public StructureSettings structure = new StructureSettings();
        public ResidualSettings residual = new ResidualSettings();
        public PhysicsSettings physics = new PhysicsSettings();
        public ImpactSettings impact = new ImpactSettings();
        public ToolSettings tools = new ToolSettings();
        public FragmentSettings fragments = new FragmentSettings();
        public List<MaterialSpec> materials = DefaultMaterials();

        [Header("Rendering (assigned by the setup menu; created at runtime when empty)")]
        public Material pieceMaterial;
        public Material groundMaterial;
        public Material overlayMaterial;

        public const int Concrete = 0;
        public const int Wood = 1;
        public const int Brick = 2;

        public static List<MaterialSpec> DefaultMaterials()
        {
            return new List<MaterialSpec>
            {
                new MaterialSpec(),
                new MaterialSpec
                {
                    name = "Wood", density = 600f, tensileStrength = 0.5e6f, compressiveStrength = 4e6f,
                    shearStrength = 0.4e6f, residualForcePerMeter = 9000f, residualYieldMomentPerMeter = 2500f,
                    impactToughness = 15000f, color = new Color(0.62f, 0.45f, 0.28f), friction = 0.6f,
                },
                new MaterialSpec
                {
                    name = "Brick", density = 1900f, tensileStrength = 0.2e6f, compressiveStrength = 6e6f,
                    shearStrength = 0.35e6f, residualForcePerMeter = 6000f, residualYieldMomentPerMeter = 1500f,
                    impactToughness = 20000f, color = new Color(0.66f, 0.36f, 0.28f), friction = 0.8f,
                },
            };
        }

        public static DestructionSettings CreateDefault()
        {
            var s = CreateInstance<DestructionSettings>();
            s.name = "DestructionSettings (runtime default)";
            return s;
        }

        public MaterialSpec Material(int index)
        {
            if (materials == null || materials.Count == 0) materials = DefaultMaterials();
            return materials[Mathf.Clamp(index, 0, materials.Count - 1)];
        }
    }
}
