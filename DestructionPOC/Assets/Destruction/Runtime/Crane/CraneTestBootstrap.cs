using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Entry point of CraneTest.unity: open plane, the brick warehouse as a destructible structure, the wrecking
    /// crane, and a first-person player who can climb into the cab. Built at Play time, like the lab, so the scene
    /// file stays trivial.
    /// </summary>
    public sealed class CraneTestBootstrap : MonoBehaviour
    {
        public DestructionSettings settings;
        public GameObject craneModel;
        public GameObject warehouseModel;
        public Vector3 cranePosition = new Vector3(0f, 0f, -14f);
        public Vector3 playerPosition = new Vector3(-7f, 0.1f, -13f);
        public float playerYaw = 90f;

        public DestructionWorld World { get; private set; }
        public CraneRig Crane { get; private set; }
        public CranePlayer Player { get; private set; }

        Scenario scenario;

        void Start()
        {
            Time.fixedDeltaTime = 0.02f;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;

            ScenarioLibrary.WarehouseModel = warehouseModel;
            LabBootstrap.CreateGround(settings);

            var worldGo = new GameObject("Destruction World");
            worldGo.SetActive(false);
            World = worldGo.AddComponent<DestructionWorld>();
            World.settingsAsset = settings;
            worldGo.SetActive(true);
            scenario = ScenarioLibrary.ById("warehouse");
            World.Build(scenario);

            var craneGo = new GameObject("Wrecking Crane", typeof(Rigidbody));
            craneGo.transform.position = cranePosition;
            Crane = craneGo.AddComponent<CraneRig>();
            Crane.Build(Instantiate(craneModel));

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                cam = camGo.GetComponent<Camera>();
            }
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 600f;

            var playerGo = new GameObject("Player", typeof(CharacterController));
            playerGo.transform.SetPositionAndRotation(playerPosition, Quaternion.Euler(0f, playerYaw, 0f));
            Player = playerGo.AddComponent<CranePlayer>();
            Player.crane = Crane;
            Player.cam = cam;
            Player.world = World;
            Player.onReset = ResetAll;

            // The 6 t ball would otherwise shove the player's capsule around.
            Physics.IgnoreCollision(Crane.Ball.GetComponent<Collider>(), playerGo.GetComponent<CharacterController>());
        }

        void ResetAll()
        {
            World.Build(scenario);
            Crane.ResetPose();
        }
    }
}
