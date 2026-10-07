using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Root of a level scene. Everything in a level is authored in the scene view; this component only wires it
    /// together when the level starts:
    ///
    ///   structures   every <see cref="DestructibleStructure"/> in the scene becomes destructible pieces
    ///   machines     every machine prefab (<see cref="ILevelRig"/>) is built and given the world, ledger and ground
    ///   container    the <see cref="CollectionContainer"/> rubble is delivered to (optional)
    ///   spawns       <see cref="PlayerSpawn"/> points, read by the <see cref="PlayerManager"/> in the Player scene
    ///   blast        the T key's demolition charge, at <see cref="blastPoint"/>
    ///
    /// Players are not part of a level. The Player scene spawns them at the spawn points once <see cref="Ready"/>
    /// fires, and removes them before the level unloads.
    ///
    /// Controls the level owns (keyboard player): R or Backspace reset everything, T fires the blast, 1-4 + LMB the
    /// destroy tools, P pause, . step, [ ] slow motion. They are ignored while any player sits in a cab, where the same
    /// letters drive the machine.
    /// </summary>
    public sealed class GameLevel : MonoBehaviour
    {
        public string displayName = "Level";
        [Tooltip("The scene's destruction world. Its settings asset tunes structure, damage and fragmentation.")]
        public DestructionWorld world;
        [Tooltip("Ground collider. Machines and the container ignore it for their own collision tests.")]
        public Collider ground;
        [Tooltip("Height of the ground plane, m.")]
        public float groundY;
        [Tooltip("Where rubble is delivered. Optional.")]
        public CollectionContainer container;

        [Header("Demolition charge (T)")]
        public Transform blastPoint;
        public string blastLabel = "Blow out the corner";
        [Min(0.1f)] public float blastRadius = 2.6f;
        public float blastDamage = 1.8f;
        public float blastImpulse = 16000f;

        [Header("Players")]
        [Tooltip("Overhead camera zoom for this level (orthographic size, or the framing size when the camera is perspective). 0 keeps the player prefab's value.")]
        public float cameraSize = 15f;

        [Header("Debug")]
        [Tooltip("Keep the lab's destroy tools and time controls (1-4, LMB, T, P, ., [ ]) available in this level.")]
        public bool devTools = true;

        public static GameLevel Current { get; private set; }
        /// <summary>Raised once a level has built its world and machines.</summary>
        public static event System.Action<GameLevel> Ready;
        /// <summary>Raised when a level is being destroyed (its scene unloads), before its machines are gone.</summary>
        public static event System.Action<GameLevel> Unloading;

        public bool IsReady { get; private set; }
        public DestructionWorld World => world;
        public LabController Controller { get; private set; }
        public CleanupLedger Ledger { get; } = new CleanupLedger();
        public Scenario Scenario { get; private set; }
        public readonly List<DestructibleStructure> Structures = new List<DestructibleStructure>();
        /// <summary>Every machine a player can enter.</summary>
        public readonly List<MonoBehaviour> Rigs = new List<MonoBehaviour>();
        public readonly List<PlayerSpawn> Spawns = new List<PlayerSpawn>();

        public CraneRig Crane => First<CraneRig>();
        public LoaderRig SkidSteer => Rigs.Find(r => r is LoaderRig l && l.kind == LoaderKind.Skid) as LoaderRig;
        public DozerRig Dozer => First<DozerRig>();
        public ExcavatorRig Excavator => First<ExcavatorRig>();

        T First<T>() where T : Component
        {
            foreach (var r in Rigs)
            {
                if (r is T t) return t;
                if (r is CraneOperable op && typeof(T) == typeof(CraneRig)) return op.GetComponent<T>();
            }
            return null;
        }

        void Awake()
        {
            SceneDirector.EnsureLoaded();
        }

        void OnDestroy()
        {
            if (IsReady) Unloading?.Invoke(this);
            if (Current == this) Current = null;
        }

        void Start()
        {
            Time.fixedDeltaTime = 0.02f;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;

            if (world == null)
            {
                Debug.LogError($"[DestructionLab] level '{displayName}' has no DestructionWorld assigned.");
                return;
            }

            Collect();
            Scenario = BuildScenario();
            // The authored objects are only the blueprint; the world builds the live pieces from them.
            foreach (var s in Structures) s.gameObject.SetActive(false);

            var dev = new GameObject("Dev Tools");
            dev.transform.SetParent(transform, false);
            Controller = dev.AddComponent<LabController>();
            Controller.DiagnosticsVisible = false;
            Controller.pauseKey = UnityEngine.InputSystem.Key.P;   // Space jumps
            Controller.scenarioSwitching = false;
            Controller.keysBlocked = () => !devTools || CranePlayer.All.Exists(p => p.InCab);
            // A machine that works on the left mouse button (the excavator's breaker) owns the click while occupied.
            Controller.clickBlocked = () => !devTools || CranePlayer.All.Exists(p => p.Current is ExcavatorRig);
            Controller.onResetRequested = ResetAll;
            Controller.Init(world, null, new List<Scenario> { Scenario });

            if (container != null) container.Init(world, Ledger, ground);
            foreach (var mb in GetComponentsInScene<MonoBehaviour>())
            {
                if (mb is ILevelRig rig) rig.BuildInLevel(world, Ledger, ground);
            }
            foreach (var mb in GetComponentsInScene<MonoBehaviour>())
            {
                if (mb is IOperableRig) Rigs.Add(mb);
            }
            ResetLedger();

            Current = this;
            IsReady = true;
            Ready?.Invoke(this);
        }

        void Collect()
        {
            Structures.Clear();
            Structures.AddRange(GetComponentsInScene<DestructibleStructure>());
            Spawns.Clear();
            Spawns.AddRange(GetComponentsInScene<PlayerSpawn>());
            Spawns.Sort((a, b) => a.playerIndex.CompareTo(b.playerIndex));
        }

        /// <summary>Components of this level's scene only (other scenes are loaded alongside it).</summary>
        List<T> GetComponentsInScene<T>() where T : Component
        {
            var list = new List<T>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                list.AddRange(root.GetComponentsInChildren<T>(true));
            return list;
        }

        /// <summary>The level as a scenario: the world rebuilds from it on every reset, re-reading the authored structures.</summary>
        Scenario BuildScenario()
        {
            DestructionSettings settings = null; // handed over by configure, which runs before build
            return new Scenario
            {
                id = gameObject.scene.name,
                title = displayName,
                instruction = "",
                expected = "",
                configure = s => settings = s,
                build = () => ReadStructures(settings),
                triggerLabel = blastLabel,
                trigger = blastPoint == null ? null : (System.Action<DestructionWorld>)(w => w.Explode(blastPoint.position, blastRadius, blastDamage, blastImpulse)),
            };
        }

        List<PieceDef> ReadStructures(DestructionSettings settings)
        {
            var all = new List<PieceDef>();
            foreach (var s in Structures)
            {
                var result = s.Read(settings, groundY);
                foreach (var issue in result.issues)
                {
                    if (issue.fatal) Debug.LogError($"[DestructionLab] {s.name}: import {issue}", s);
                    else Debug.LogWarning($"[DestructionLab] {s.name}: import {issue}", s);
                }
                all.AddRange(result.pieces);
            }
            return all;
        }

        void ResetLedger()
        {
            var skid = SkidSteer;
            Ledger.Reset(world, skid != null ? skid.tuning.maxPieceMassKg : 0f);
            Ledger.ResetBuilding(world);
        }

        /// <summary>Level reset (Backspace / Start, or R on foot): every player steps out of any machine back to their
        /// spawn, every machine returns to its start pose, the structures are rebuilt and the gauge restarts at 0 %.</summary>
        public void ResetAll()
        {
            foreach (var p in CranePlayer.All.ToArray()) p.ForceExit(p.spawnPosition, p.spawnYaw);
            // Loaders let go of any load before the world is rebuilt, so no body is left held. The crane goes last,
            // after the rebuild, as before.
            foreach (var r in Rigs)
                if (r is ILevelRig rig) rig.ResetPose();   // CraneOperable is not one; the crane's CraneRig is
            Controller.Reset();
            if (container != null) container.ResetState();
            ResetLedger();
            var crane = Crane;
            if (crane != null) crane.ResetPose();
            foreach (var p in CranePlayer.All) p.SnapCamera();
        }

        /// <summary>Start point and facing for player <paramref name="index"/>.</summary>
        public void SpawnFor(int index, out Vector3 position, out float yaw)
        {
            if (Spawns.Count == 0)
            {
                position = transform.position + Vector3.right * (index * 2.5f);
                yaw = transform.eulerAngles.y;
                return;
            }
            var exact = Spawns.Find(s => s.playerIndex == index);
            if (exact != null)
            {
                position = exact.transform.position;
                yaw = exact.Yaw;
                return;
            }
            var first = Spawns[0];
            position = first.transform.position + first.transform.right * (index * 2.5f);
            yaw = first.Yaw;
        }

        void OnDrawGizmos()
        {
            if (blastPoint == null) return;
            Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.7f);
            Gizmos.DrawWireSphere(blastPoint.position, blastRadius);
        }
    }
}
