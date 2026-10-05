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
        [Tooltip("Scenario loaded on Play (index into the scenario list).")]
        public int startScenario = 4;

        public DestructionWorld World { get; private set; }
        public LabController Controller { get; private set; }

        void Start()
        {
            Time.fixedDeltaTime = 0.02f;
            Physics.simulationMode = SimulationMode.FixedUpdate;

            ScenarioLibrary.AuthoredModel = authoredModel;
            ScenarioLibrary.WarehouseModel = warehouseModel;
            CreateGround(settings);

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
            var overlay = new GameObject("Diagnostics Overlay").AddComponent<DiagnosticsOverlay>();
            var hud = labGo.AddComponent<LabHud>();

            Controller.Init(World, labCam);
            overlay.Init(World, Controller);
            hud.Init(Controller, World);
            Controller.LoadScenario(startScenario);
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
