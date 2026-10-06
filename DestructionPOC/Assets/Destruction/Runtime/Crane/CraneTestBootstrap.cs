using System.Collections.Generic;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Entry point of CraneTest.unity: open plane, the brick warehouse as a destructible structure, the wrecking
    /// crane, a yard of four excavators (crusher, shear, breaker, grapple) each with a destructible test target, and a
    /// player who can walk up to any machine and climb in. Built at Play time, like the lab, so the scene file stays
    /// trivial.
    /// </summary>
    public sealed class CraneTestBootstrap : MonoBehaviour
    {
        public DestructionSettings settings;
        public GameObject craneModel;
        public GameObject warehouseModel;
        [Tooltip("Excavator FBX: shared base plus all four attachments (Tools/blender/build_excavator.py).")]
        public GameObject excavatorModel;
        [Header("Loader yard (Tools/blender/WheelLoader, Tools/blender/SkidSteer)")]
        public GameObject wheelLoaderModel;
        public GameObject skidSteerModel;
        [Tooltip("Landfill dozer FBX (Tools/blender/build_landfill_dozer.py). Starts at loaderSite.dozer facing a rubble windrow.")]
        public GameObject dozerModel;
        public DozerTuning dozerTuning = new DozerTuning();
        public LoaderTuning wheelLoaderTuning = LoaderTuning.WheelLoader();
        public LoaderTuning skidSteerTuning = LoaderTuning.SkidSteer();
        [Tooltip("Machine start positions, debris piles and the collection container. Machines start facing +Z.")]
        public LoaderTestSite.Layout loaderSite = LoaderTestSite.Default();
        [Tooltip("Interior size of the collection container: length, wall height, width. Both loaders must be able to dump over the wall.")]
        public Vector3 containerInterior = new Vector3(12f, 1.1f, 2.8f);

        public Vector3 cranePosition = new Vector3(0f, 0f, -14f);
        public Vector3 playerPosition = new Vector3(-7f, 0.1f, -13f);
        public float playerYaw = 90f;

        [Header("Excavator yard")]
        [Tooltip("Z of the excavator row. The machines face -Z, away from the warehouse and crane.")]
        public float excavatorRowZ = -30f;
        [Tooltip("Distance between machines; more than twice the working radius so swung booms never meet.")]
        public float excavatorSpacing = 16f;
        [Tooltip("Distance from a machine to the centre of its test target.")]
        public float targetDistance = 7f;
        [Tooltip("Jaw travel from open to closed, per attachment (deg): crusher, shear, breaker (unused), grapple. From Tools/blender/Excavator/HANDOFF.md.")]
        public Vector4 jawCloseAngles = new Vector4(50.4f, 40f, 0f, 59.55f);
        [Tooltip("Breaker bit stroke (m).")]
        public float breakerStroke = 0.25f;

        [Tooltip("Use the fixed three-quarter overhead camera (tuned on the Main Camera's CraneOverheadCamera). Off restores the first-person view.")]
        public bool overheadCamera = true;
        [Tooltip("Tint of the visible player body in the overhead view.")]
        public Color avatarColor = new Color(0.95f, 0.35f, 0.2f);

        public DestructionWorld World { get; private set; }
        public CraneRig Crane { get; private set; }
        public CranePlayer Player { get; private set; }
        public readonly List<ExcavatorRig> Excavators = new List<ExcavatorRig>();
        public readonly List<LoaderRig> Loaders = new List<LoaderRig>();
        public DozerRig Dozer { get; private set; }
        public CollectionContainer Container { get; private set; }
        public CleanupLedger Ledger { get; } = new CleanupLedger();
        public int SteelMaterial { get; private set; } = -1;

        Scenario scenario;

        void Start()
        {
            Time.fixedDeltaTime = 0.02f;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;

            ScenarioLibrary.WarehouseModel = warehouseModel;
            var ground = LabBootstrap.CreateGround(settings).GetComponent<Collider>();

            var worldGo = new GameObject("Destruction World");
            worldGo.SetActive(false);
            World = worldGo.AddComponent<DestructionWorld>();
            World.settingsAsset = settings;
            worldGo.SetActive(true);
            var bays = ExcavatorTestSite.Bays(excavatorRowZ, excavatorSpacing, targetDistance);
            scenario = BuildScenario(bays);
            World.Build(scenario);

            var craneGo = new GameObject("Wrecking Crane", typeof(Rigidbody));
            craneGo.transform.position = cranePosition;
            Crane = craneGo.AddComponent<CraneRig>();
            Crane.Build(Instantiate(craneModel));
            var craneOp = craneGo.AddComponent<CraneOperable>();
            Crane.Collision.ignore.Add(ground);

            if (excavatorModel != null)
            {
                foreach (var bay in bays)
                {
                    var go = new GameObject($"Excavator ({ExcavatorRig.AttachmentLabel(bay.kind)})", typeof(Rigidbody));
                    go.transform.SetPositionAndRotation(bay.machine, Quaternion.Euler(0f, 180f, 0f));
                    var rig = go.AddComponent<ExcavatorRig>();
                    rig.world = World;
                    rig.steelMaterial = SteelMaterial;
                    rig.jawCloseAngle = jawCloseAngles[(int)bay.kind];
                    rig.bitStroke = breakerStroke;
                    rig.Collision.ignore.Add(ground);
                    rig.Build(Instantiate(excavatorModel), bay.kind);
                    Excavators.Add(rig);
                }
            }
            else Debug.LogWarning("[DestructionLab] CraneTest has no excavator model assigned; run Destruction Lab/Build Crane Test Scene.");

            BuildLoaderYard(ground);
            BuildDozer(ground);

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }
            CraneOverheadCamera overhead = null;
            if (overheadCamera)
            {
                overhead = cam.GetComponent<CraneOverheadCamera>();
                if (overhead == null) overhead = cam.gameObject.AddComponent<CraneOverheadCamera>();
                overhead.EnsureSingleAudioListener();
            }
            else
            {
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 600f;
            }

            var playerGo = new GameObject("Player", typeof(CharacterController));
            playerGo.transform.SetPositionAndRotation(playerPosition, Quaternion.Euler(0f, playerYaw, 0f));
            Player = playerGo.AddComponent<CranePlayer>();
            Player.crane = Crane;
            Player.rigs.Add(craneOp);
            foreach (var rig in Excavators) Player.rigs.Add(rig);
            foreach (var rig in Loaders) Player.rigs.Add(rig);
            if (Dozer != null) Player.rigs.Add(Dozer);
            Player.cam = cam;
            Player.world = World;
            Player.onReset = ResetAll;
            if (overhead != null)
            {
                var focus = new GameObject("Camera Focus").transform;
                focus.position = playerPosition;
                overhead.target = focus;
                Player.overhead = overhead;
                Player.cameraFocus = focus;
                Player.avatar = BuildAvatar(playerGo.transform);
            }

            var panel = playerGo.AddComponent<RigControlsPanel>();
            foreach (var bay in bays)
            {
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = bay.target + Vector3.up * 3.6f, text = bay.label });
                panel.labels.Add(new RigControlsPanel.WorldLabel
                {
                    position = bay.machine + Vector3.up * 4.2f,
                    text = $"EXCAVATOR  ·  {ExcavatorRig.AttachmentLabel(bay.kind).ToUpperInvariant()}",
                    onFootOnly = true,
                });
            }

            if (Dozer != null)
            {
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = loaderSite.dozerPile + Vector3.up * 2.4f, text = "RUBBLE  ·  landfill dozer", onFootOnly = true });
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = Vector3.up * 5.2f, text = "LANDFILL DOZER", onFootOnly = true, follow = Dozer.transform });
            }

            var loaderHud = playerGo.AddComponent<LoaderHud>();
            loaderHud.ledger = Ledger;
            if (Container != null)
            {
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = loaderSite.container + Vector3.up * 3.2f, text = "COLLECTION CONTAINER" });
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = loaderSite.wheelPile + Vector3.up * 2.6f, text = "RUBBLE  ·  wheel loader", onFootOnly = true });
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = loaderSite.skidPile + Vector3.up * 2.4f, text = "RUBBLE  ·  skid steer", onFootOnly = true });
                foreach (var rig in Loaders)
                    panel.labels.Add(new RigControlsPanel.WorldLabel
                    {
                        position = Vector3.up * (rig.kind == LoaderKind.Wheel ? 4.4f : 3.4f),
                        text = rig.RigName.ToUpperInvariant(),
                        onFootOnly = true,
                        follow = rig.transform,
                    });
            }

            // The 6 t ball would otherwise shove the player's capsule around.
            Physics.IgnoreCollision(Crane.Ball.GetComponent<Collider>(), playerGo.GetComponent<CharacterController>());
        }

        /// <summary>CraneTest's own scenario: the warehouse plus the excavator targets, with steel added to this
        /// world's runtime settings only.</summary>
        Scenario BuildScenario(List<ExcavatorTestSite.Bay> bays)
        {
            var warehouse = ScenarioLibrary.ById("warehouse");
            return new Scenario
            {
                id = "cranetest",
                title = warehouse.title + " + excavator yard",
                instruction = warehouse.instruction,
                expected = warehouse.expected,
                configure = s =>
                {
                    warehouse.configure?.Invoke(s);
                    SteelMaterial = ExcavatorTestSite.EnsureSteel(s);
                },
                build = () =>
                {
                    var list = warehouse.build();
                    ExcavatorTestSite.AddTargets(list, bays, SteelMaterial);
                    if (loaderYardEnabled || dozerModel != null) LoaderTestSite.AddDebris(list, loaderSite, dozerModel != null);
                    return list;
                },
                postBuild = warehouse.postBuild,
                triggerLabel = warehouse.triggerLabel,
                trigger = warehouse.trigger,
                cameraPivot = warehouse.cameraPivot,
            };
        }

        /// <summary>Scene reset: the player steps out of any rig back to the start, held loads are let go, every
        /// machine returns to its start pose and the structures and targets are rebuilt.</summary>
        void ResetAll()
        {
            Player.ForceExit(playerPosition, playerYaw);
            foreach (var rig in Excavators) rig.ResetPose();
            // Loaders let go of their load before the world is rebuilt, so no body is left held, and the ledger
            // restarts from the rebuilt debris.
            foreach (var rig in Loaders) rig.ResetPose();
            if (Dozer != null) Dozer.ResetPose();
            World.Build(scenario);
            if (Container != null) Container.ResetState();
            Ledger.Reset(World, MaxLoadableKg());
            Crane.ResetPose();
            Player.SnapCamera();
        }

        bool loaderYardEnabled => wheelLoaderModel != null || skidSteerModel != null;

        float MaxLoadableKg()
        {
            float m = 0f;
            foreach (var rig in Loaders) m = Mathf.Max(m, rig.tuning.maxPieceMassKg);
            return m;
        }

        /// <summary>Wheel loader, skid steer and the collection container in the yard east of the excavators.</summary>
        void BuildLoaderYard(Collider ground)
        {
            if (!loaderYardEnabled) return;
            var cgo = new GameObject("Collection Container");
            cgo.transform.position = loaderSite.container;
            Container = cgo.AddComponent<CollectionContainer>();
            Container.interior = containerInterior;
            Container.world = World;
            Container.ledger = Ledger;
            Container.ground = ground;
            Container.Build();

            AddLoader(wheelLoaderModel, LoaderKind.Wheel, wheelLoaderTuning, loaderSite.wheelLoader, ground);
            AddLoader(skidSteerModel, LoaderKind.Skid, skidSteerTuning, loaderSite.skidSteer, ground);
            Ledger.Reset(World, MaxLoadableKg());
        }

        void BuildDozer(Collider ground)
        {
            if (dozerModel == null) return;
            var go = new GameObject("Landfill Dozer", typeof(Rigidbody));
            go.transform.SetPositionAndRotation(loaderSite.dozer, Quaternion.identity);
            Dozer = go.AddComponent<DozerRig>();
            Dozer.world = World;
            Dozer.tuning = dozerTuning;
            Dozer.Collision.ignore.Add(ground);
            Dozer.Build(Instantiate(dozerModel));
        }

        void AddLoader(GameObject model, LoaderKind kind, LoaderTuning tuning, Vector3 position, Collider ground)
        {
            if (model == null) return;
            var go = new GameObject(kind == LoaderKind.Wheel ? "Wheel Loader" : "Skid-Steer Loader", typeof(Rigidbody));
            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            var rig = go.AddComponent<LoaderRig>();
            rig.world = World;
            rig.ledger = Ledger;
            rig.tuning = tuning;
            rig.Collision.ignore.Add(ground);
            rig.Build(Instantiate(model), kind);
            Loaders.Add(rig);
        }

        // The first-person player has no body; the overhead view needs one. A capsule with a nose shows facing.
        GameObject BuildAvatar(Transform parent)
        {
            var root = new GameObject("Avatar");
            root.transform.SetParent(parent, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.transform.SetParent(root.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.3f, 0.34f);
            nose.transform.localScale = new Vector3(0.22f, 0.22f, 0.3f);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.material.color = avatarColor;
                Destroy(r.GetComponent<Collider>());
            }
            return root;
        }
    }
}
