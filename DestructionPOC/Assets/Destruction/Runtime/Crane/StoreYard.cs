using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// The drivable half of ConvenienceStore.unity. Sits next to <see cref="LabBootstrap"/> and, once the store
    /// scenario is loaded, adds the machines from CraneTest (wrecking crane, landfill dozer, skid steer), a large
    /// roll-off container for the rubble, the walking player and the "rubble cleared" gauge. The lab keeps working: the
    /// destroy tools, T and R behave as in the plain lab.
    ///
    /// Layout (Unity metres; the lot is x -25..25, z -20..20 and the store is at x 4..16, z -18..-10, front toward +Z):
    ///
    ///     z = -15   parking line facing +Z, west of the store:
    ///                 excavator (-22.5), dozer (-15.5), skid steer (-8.5), crane (-1.5), 7 m apart   [ store x 4..16 ]
    ///     z =  -2   player start (4.5)  cars, pump canopy (x -14..-2)
    ///     z = 6.6..11.4   roll-off container, x 6..22 (loaders reach its near wall from the store side)
    ///
    /// Controls. The lab's keys and the player's keys overlap, so they are split by context:
    ///   On foot  WASD move, Shift run, Space jump, E climb into a machine, V first-person / overhead view,
    ///            R or Backspace reset everything, T trigger (blow the store corner), 1-4 tools, LMB use tool,
    ///            P pause (Space is jump here), . step, [ ] slow motion, F1/F2 overlays, H hide stats.
    ///   In a rig The rig's own keys only (crane: A/D slew, W/S boom, R/F ball, arrows drive; loaders: W/S, A/D, R/F,
    ///            Z/C). Every lab shortcut is ignored in a cab, so R winches instead of resetting. E exits;
    ///            Backspace still resets (it is bound to nothing else). The mouse tools stay live, except in the excavator,
    ///            whose breaker runs on the left mouse button (hold LMB; the destroy tool is not fired).
    ///   Disabled N / B / PageUp / PageDown scenario switching (the machines belong to the store scenario), and the
    ///            lab's orbit camera, WASD/QE panning and F re-frame (the overhead player camera replaces it).
    /// </summary>
    public sealed class StoreYard : MonoBehaviour
    {
        [Header("Models (CraneTest's rigs)")]
        public GameObject craneModel;
        public GameObject skidSteerModel;
        public GameObject dozerModel;
        public DozerTuning dozerTuning = new DozerTuning();
        public LoaderTuning skidSteerTuning = LoaderTuning.SkidSteer();

        [Tooltip("Excavator FBX (base + attachments). The yard fits the hydraulic breaker (the jackhammer).")]
        public GameObject excavatorModel;
        public ExcavatorAttachment excavatorAttachment = ExcavatorAttachment.Breaker;
        public float excavatorJawCloseAngle = 30f;
        public float excavatorBitStroke = 0.25f;

        [Header("Layout (Unity metres, outside the building footprint)")]
        [Tooltip("The machines park in one line along the lot's west side of the store (z = parkingRowZ), all facing +Z (out into the lot), with the crane at the end nearest the store.")]
        public float parkingRowZ = -15f;
        public float parkingYaw = 0f;
        public Vector3 cranePosition = new Vector3(-1.5f, 0f, -15f);
        public Vector3 skidSteerPosition = new Vector3(-8.5f, 0f, -15f);
        public Vector3 dozerPosition = new Vector3(-15.5f, 0f, -15f);
        public Vector3 excavatorPosition = new Vector3(-22.5f, 0f, -15f);
        public Vector3 containerPosition = new Vector3(14f, 0f, 9f);
        [Tooltip("Interior of the roll-off container: length (x), wall height (y), width (z). Big enough to take a store's worth of rubble; the walls stay low enough for the skid steer to dump over.")]
        public Vector3 containerInterior = new Vector3(16f, 1.1f, 4.8f);
        public Vector3 playerPosition = new Vector3(4.5f, 0.1f, -2f);
        public float playerYaw = 0f;

        public CleanupLedger Ledger { get; } = new CleanupLedger();
        public LabBootstrap Lab { get; private set; }
        public DestructionWorld World { get; private set; }
        public CraneRig Crane { get; private set; }
        public LoaderRig SkidSteer { get; private set; }
        public DozerRig Dozer { get; private set; }
        public ExcavatorRig Excavator { get; private set; }
        public CollectionContainer Container { get; private set; }
        public CranePlayer Player { get; private set; }

        Collider ground;

        /// <summary>Called by <see cref="LabBootstrap"/> after the store scenario has loaded.</summary>
        public void Begin(LabBootstrap lab, Camera cam, LabCamera labCam)
        {
            Lab = lab;
            World = lab.World;
            ground = lab.Ground;
            Time.timeScale = 1f;

            // The overhead player camera replaces the lab's orbit camera (which would pan on WASD / QE).
            if (labCam != null) Destroy(labCam);
            lab.Controller.labCamera = null;
            var overhead = cam.gameObject.AddComponent<CraneOverheadCamera>();
            overhead.orthographicSize = 15f;
            overhead.EnsureSingleAudioListener();

            BuildContainer();
            BuildCrane();
            if (skidSteerModel != null) BuildSkidSteer();
            if (dozerModel != null) BuildDozer();
            if (excavatorModel != null) BuildExcavator();
            BuildPlayer(cam, overhead);

            var lab2 = lab.Controller;
            lab2.pauseKey = Key.P;                 // Space jumps
            lab2.scenarioSwitching = false;
            lab2.keysBlocked = () => Player != null && Player.InCab;
            // The excavator's breaker / jaws run on the left mouse button, so a click must not also fire the destroy tool.
            lab2.clickBlocked = () => Player != null && Excavator != null && Player.Current == (IOperableRig)Excavator;
            lab2.onResetRequested = ResetAll;
            Ledger.Reset(World, SkidSteer != null ? SkidSteer.tuning.maxPieceMassKg : 0f);
            Ledger.ResetBuilding(World);
        }

        void BuildContainer()
        {
            var go = new GameObject("Roll-off Container");
            go.transform.position = containerPosition;
            Container = go.AddComponent<CollectionContainer>();
            Container.interior = containerInterior;
            Container.world = World;
            Container.ledger = Ledger;
            Container.ground = ground;
            Container.Build();
        }

        void BuildCrane()
        {
            var go = new GameObject("Wrecking Crane", typeof(Rigidbody));
            go.transform.SetPositionAndRotation(cranePosition, Quaternion.Euler(0f, parkingYaw, 0f));
            Crane =go.AddComponent<CraneRig>();
            Crane.Build(Instantiate(craneModel));
            go.AddComponent<CraneOperable>();
            Crane.Collision.ignore.Add(ground);
        }

        void BuildSkidSteer()
        {
            var go = new GameObject("Skid-Steer Loader", typeof(Rigidbody));
            go.transform.SetPositionAndRotation(skidSteerPosition, Quaternion.Euler(0f, parkingYaw, 0f));
            SkidSteer = go.AddComponent<LoaderRig>();
            SkidSteer.world = World;
            SkidSteer.ledger = Ledger;
            SkidSteer.tuning = skidSteerTuning;
            SkidSteer.Collision.ignore.Add(ground);
            SkidSteer.Build(Instantiate(skidSteerModel), LoaderKind.Skid);
        }

        void BuildDozer()
        {
            var go = new GameObject("Landfill Dozer", typeof(Rigidbody));
            go.transform.SetPositionAndRotation(dozerPosition, Quaternion.Euler(0f, parkingYaw, 0f));
            Dozer = go.AddComponent<DozerRig>();
            Dozer.world = World;
            Dozer.tuning = dozerTuning;
            Dozer.Collision.ignore.Add(ground);
            Dozer.Build(Instantiate(dozerModel));
        }

        void BuildExcavator()
        {
            var go = new GameObject($"Excavator ({ExcavatorRig.AttachmentLabel(excavatorAttachment)})", typeof(Rigidbody));
            go.transform.SetPositionAndRotation(excavatorPosition, Quaternion.Euler(0f, parkingYaw, 0f));
            Excavator = go.AddComponent<ExcavatorRig>();
            Excavator.world = World;
            Excavator.jawCloseAngle = excavatorJawCloseAngle;
            Excavator.bitStroke = excavatorBitStroke;
            Excavator.Collision.ignore.Add(ground);
            Excavator.Build(Instantiate(excavatorModel), excavatorAttachment);
        }

        void BuildPlayer(Camera cam, CraneOverheadCamera overhead)
        {
            var playerGo = new GameObject("Player", typeof(CharacterController));
            playerGo.transform.SetPositionAndRotation(playerPosition, Quaternion.Euler(0f, playerYaw, 0f));
            Player = playerGo.AddComponent<CranePlayer>();
            Player.crane = Crane;
            Player.rigs.Add(Crane.GetComponent<CraneOperable>());
            if (SkidSteer != null) Player.rigs.Add(SkidSteer);
            if (Dozer != null) Player.rigs.Add(Dozer);
            if (Excavator != null) Player.rigs.Add(Excavator);
            Player.cam = cam;
            Player.world = World;
            Player.onReset = ResetAll;

            var focus = new GameObject("Camera Focus").transform;
            focus.position = playerPosition;
            overhead.target = focus;
            Player.overhead = overhead;
            Player.cameraFocus = focus;
            Player.avatar = BuildAvatar(playerGo.transform);

            var panel = playerGo.AddComponent<RigControlsPanel>();
            panel.labels.Add(new RigControlsPanel.WorldLabel { position = containerPosition + Vector3.up * 3f, text = "ROLL-OFF CONTAINER  ·  rubble goes here" });
            panel.labels.Add(new RigControlsPanel.WorldLabel { position = Vector3.up * 8f, text = "WRECKING CRANE", onFootOnly = true, follow = Crane.transform });
            if (SkidSteer != null)
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = Vector3.up * 3.4f, text = "SKID STEER", onFootOnly = true, follow = SkidSteer.transform });
            if (Dozer != null)
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = Vector3.up * 5.2f, text = "LANDFILL DOZER", onFootOnly = true, follow = Dozer.transform });
            if (Excavator != null)
                panel.labels.Add(new RigControlsPanel.WorldLabel { position = Vector3.up * 4.2f, text = "EXCAVATOR  ·  " + ExcavatorRig.AttachmentLabel(excavatorAttachment).ToUpperInvariant(), onFootOnly = true, follow = Excavator.transform });

            var hud = playerGo.AddComponent<LoaderHud>();
            hud.ledger = Ledger;

            // The 25 t ball would otherwise shove the player's capsule around.
            Physics.IgnoreCollision(Crane.Ball.GetComponent<Collider>(), playerGo.GetComponent<CharacterController>());
        }

        /// <summary>Scene reset (R on foot, Backspace anywhere): the player steps out of any rig, every machine returns
        /// to its start pose, the store is rebuilt and the gauge restarts at 0 % from the rebuilt building.</summary>
        public void ResetAll()
        {
            Player.ForceExit(playerPosition, playerYaw);
            // Loaders let go of any load before the world is rebuilt, so no body is left held.
            if (SkidSteer != null) SkidSteer.ResetPose();
            if (Dozer != null) Dozer.ResetPose();
            if (Excavator != null) Excavator.ResetPose();
            Lab.Controller.Reset();
            Container.ResetState();
            Ledger.Reset(World, SkidSteer != null ? SkidSteer.tuning.maxPieceMassKg : 0f);
            Ledger.ResetBuilding(World);
            Crane.ResetPose();
            Player.SnapCamera();
        }

        // The overhead view needs a body for the player: a capsule with a nose shows facing.
        static GameObject BuildAvatar(Transform parent)
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
                r.material.color = new Color(0.95f, 0.35f, 0.2f);
                Destroy(r.GetComponent<Collider>());
            }
            return root;
        }
    }
}
