using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace DestructionLab
{
    /// <summary>
    /// Single entry point placed in DestructionLab.unity. Builds the ground, world, camera rig, UI and
    /// diagnostics at runtime so the scene file stays trivial and nothing needs manual wiring.
    /// </summary>
    public sealed class LabBootstrap : MonoBehaviour
    {
        public DestructionSettings settings;
        [Tooltip("Optional authored blockout (a Blender FBX). When set, it is offered as the last scenario.")]
        public GameObject authoredModel;
        [Tooltip("Optional warehouse blockout (a Blender FBX). When set, it is offered as a scenario after the authored building.")]
        public GameObject warehouseModel;
        [Tooltip("Optional convenience-store lot blockout (a Blender FBX). When set, it is offered as a scenario after the warehouse.")]
        public GameObject convenienceStoreModel;
        [Tooltip("Scenario loaded on Play (index into the scenario list).")]
        public int startScenario = 4;
        [Tooltip("When set, overrides startScenario with the scenario of this id (e.g. \"store\").")]
        public string startScenarioId = "";
        [Tooltip("Optional asphalt lot pad (centre x/z, size x/z in metres) drawn over the ground. Zero size disables it.")]
        public Vector2 lotCenter;
        public Vector2 lotSize;

        [Tooltip("Optional drivable yard (crane, dozer, skid steer, roll-off container, walking player, cleanup gauge). Built after the scenario loads.")]
        public StoreYard yard;

        public DestructionWorld World { get; private set; }
        public LabController Controller { get; private set; }
        public Collider Ground { get; private set; }

        void Start()
        {
            Time.fixedDeltaTime = 0.02f;
            Physics.simulationMode = SimulationMode.FixedUpdate;

            ScenarioLibrary.AuthoredModel = authoredModel;
            ScenarioLibrary.WarehouseModel = warehouseModel;
            ScenarioLibrary.ConvenienceStoreModel = convenienceStoreModel;
            Ground = CreateGround(settings).GetComponent<Collider>();
            if (lotSize.x > 0f && lotSize.y > 0f) CreateLotPad();

            var worldGo = new GameObject("Destruction World");
            worldGo.SetActive(false);
            World = worldGo.AddComponent<DestructionWorld>();
            World.settingsAsset = settings;
            worldGo.SetActive(true);

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            var labCam = cam.GetComponent<LabCamera>();
            if (labCam == null) labCam = cam.gameObject.AddComponent<LabCamera>();

            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var labGo = new GameObject("Lab");
            Controller = labGo.AddComponent<LabController>();
            Controller.Init(World, labCam);
            // The lab's debug view (stress lines, body markers, tint, stats panel) is for the plain lab; the game
            // scene with a drivable yard shows none of it.
            if (yard == null)
            {
                var overlay = new GameObject("Diagnostics Overlay").AddComponent<DiagnosticsOverlay>();
                var hud = labGo.AddComponent<LabHud>();
                overlay.Init(World, Controller);
                hud.Init(Controller, World);
            }
            else Controller.DiagnosticsVisible = false;
            int start = startScenario;
            if (!string.IsNullOrEmpty(startScenarioId))
            {
                int found = Controller.Scenarios.FindIndex(s => s.id == startScenarioId);
                if (found >= 0) start = found;
                else Debug.LogWarning($"[DestructionLab] start scenario '{startScenarioId}' not found; using index {startScenario}.");
            }
            Controller.LoadScenario(start);
            if (yard != null) yard.Begin(this, cam, labCam);
        }

        /// <summary>Visual-only asphalt slab a hair above the ground (no collider; the ground still carries everything).</summary>
        void CreateLotPad()
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Lot Asphalt";
            Destroy(pad.GetComponent<BoxCollider>());
            pad.transform.position = new Vector3(lotCenter.x, 0.005f, lotCenter.y);
            pad.transform.localScale = new Vector3(lotSize.x, 0.01f, lotSize.y);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh != null)
            {
                var mat = new Material(sh);
                mat.SetColor("_BaseColor", new Color(0.16f, 0.16f, 0.17f));
                mat.SetFloat("_Smoothness", 0.15f);
                pad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        internal static GameObject CreateGround(DestructionSettings settings)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(200f, 1f, 200f);
            var mr = ground.GetComponent<MeshRenderer>();
            if (settings != null && settings.groundMaterial != null) mr.sharedMaterial = settings.groundMaterial;
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh != null) mr.sharedMaterial = new Material(sh) { color = new Color(0.36f, 0.4f, 0.36f) };
            }
            ground.GetComponent<BoxCollider>().sharedMaterial = new PhysicsMaterial("Ground")
            {
                dynamicFriction = 0.8f, staticFriction = 0.8f, bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            return ground;
        }
    }
}
