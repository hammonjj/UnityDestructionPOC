using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>Title menu and lobby, split-screen ConvenienceStore (two gamepad players), per-player respawn, the
    /// machine gauges, and the lab's debug HUD staying out of the game view.</summary>
    public sealed class MultiplayerTests
    {
        Gamepad padA, padB;
        Keyboard kb;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            GameSession.Clear();
            padA = InputSystem.AddDevice<Gamepad>();
            padB = InputSystem.AddDevice<Gamepad>();
            kb = InputSystem.AddDevice<Keyboard>();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            foreach (var d in new InputDevice[] { padA, padB, kb })
                if (d != null && d.added) InputSystem.RemoveDevice(d);
            yield return GameScenes.UnloadAll();
            GameSession.Clear();
            ScenarioLibrary.ConvenienceStoreModel = null;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Application.targetFrameRate = -1;
            yield return null;
        }

        IEnumerator LoadStore()
        {
            yield return GameScenes.LoadLevel("ConvenienceStore");
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator Press(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState(button));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            yield return null;
        }

        IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            yield return null;
        }

        static void StandAt(CranePlayer p, Vector3 pos)
        {
            var cc = p.GetComponent<CharacterController>();
            cc.enabled = false;
            p.transform.position = new Vector3(pos.x, 0.1f, pos.z);
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ title and lobby

        [UnityTest]
        public IEnumerator TitleStartsOnTheMenuAndLobbyJoinsPlayers()
        {
            yield return GameScenes.LoadTitle();
            var menu = Object.FindAnyObjectByType<TitleMenu>();
            Assert.IsNotNull(menu, "Title scene has a TitleMenu");
            Assert.IsFalse(menu.InLobby);

            yield return Tap(Key.Enter);          // START
            Assert.IsTrue(menu.InLobby, "Start opens the lobby");

            yield return Tap(Key.Space);          // keyboard player joins
            Assert.AreEqual(1, GameSession.Slots.Count);
            Assert.IsTrue(GameSession.Slots[0].IsKeyboard);

            yield return Press(padA, GamepadButton.South);
            yield return Press(padB, GamepadButton.South);
            Assert.AreEqual(2, GameSession.Slots.Count, "two players at most");
            Assert.IsFalse(GameSession.Slots[1].IsKeyboard);
            CollectionAssert.Contains(GameSession.Slots[1].devices, padA);
            CollectionAssert.DoesNotContain(GameSession.Slots[1].devices, padB, "the lobby was already full");
        }

        [UnityTest]
        public IEnumerator LobbyBackLeavesAnEmptyLineup()
        {
            yield return GameScenes.LoadTitle();
            var menu = Object.FindAnyObjectByType<TitleMenu>();
            yield return Tap(Key.Enter);
            yield return Tap(Key.A);
            Assert.AreEqual(1, GameSession.Slots.Count);
            yield return Tap(Key.Escape);
            Assert.IsFalse(menu.InLobby);
            Assert.AreEqual(0, GameSession.Slots.Count);
        }

        // ------------------------------------------------------------------ split screen

        [UnityTest]
        public IEnumerator TwoGamepadPlayersSplitTheScreenWithTheirOwnCameras()
        {
            Assert.IsTrue(GameSession.JoinGamepad(padA));
            Assert.IsTrue(GameSession.JoinGamepad(padB));
            Assert.IsFalse(GameSession.JoinGamepad(padA), "full, and a pad cannot join twice");
            yield return LoadStore();

            var yard = GameLevel.Current;
            Assert.AreEqual(2, PlayerManager.Instance.Players.Count);
            var p1 = PlayerManager.Instance.Players[0];
            var p2 = PlayerManager.Instance.Players[1];
            Assert.AreNotSame(p1.cam, p2.cam);
            Assert.AreEqual(new Rect(0f, 0f, 0.5f, 1f), p1.cam.rect);
            Assert.AreEqual(new Rect(0.5f, 0f, 0.5f, 1f), p2.cam.rect);
            Assert.AreSame(Camera.main, p1.cam, "the first player's camera stays the main camera");
            int listeners = 0;
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude)) if (l.enabled) listeners++;
            Assert.AreEqual(1, listeners, "one audio listener");
            Assert.AreNotEqual(p1.transform.position, p2.transform.position, "players do not spawn on top of each other");
            Assert.IsFalse(p1.allowViewToggle, "first person is single player only");
        }

        [UnityTest]
        public IEnumerator EachPlayerOnlyReadsTheirOwnPad()
        {
            GameSession.JoinGamepad(padA);
            GameSession.JoinGamepad(padB);
            yield return LoadStore();
            var yard = GameLevel.Current;
            var p1 = PlayerManager.Instance.Players[0];
            var p2 = PlayerManager.Instance.Players[1];
            // Stand both away from machines so nothing is entered, then push pad B's stick: only player 2 moves.
            StandAt(p1, new Vector3(0f, 0f, 8f));
            StandAt(p2, new Vector3(-10f, 0f, 8f));
            yield return null;
            Vector3 a0 = p1.transform.position, b0 = p2.transform.position;
            InputSystem.QueueStateEvent(padB, new GamepadState { leftStick = new Vector2(0f, 1f) });
            yield return new WaitForSeconds(0.5f);
            Assert.Less((p1.transform.position - a0).magnitude, 0.05f, "player 1 ignores pad B");
            Assert.Greater((p2.transform.position - b0).magnitude, 0.5f, "player 2 walks with pad B");
        }

        [UnityTest]
        public IEnumerator TwoPlayersCannotClimbIntoTheSameMachine()
        {
            GameSession.JoinGamepad(padA);
            GameSession.JoinGamepad(padB);
            yield return LoadStore();
            var yard = GameLevel.Current;
            var p1 = PlayerManager.Instance.Players[0];
            var p2 = PlayerManager.Instance.Players[1];
            var crane = yard.Crane.GetComponent<CraneOperable>();
            StandAt(p1, crane.DoorPosition);
            StandAt(p2, crane.DoorPosition);
            yield return null;

            yield return Press(padA, GamepadButton.West);
            Assert.AreSame(crane, p1.Current, "player 1 climbs in");
            yield return Press(padB, GamepadButton.West);
            Assert.IsNull(p2.Current, "the crane is taken");
            Assert.AreNotSame(crane, p2.NearestRig, "an occupied machine is not offered");
        }

        // ------------------------------------------------------------------ respawn

        [UnityTest]
        public IEnumerator RespawnVehicleSendsTheMachineHomeAndKeepsTheDriver()
        {
            yield return LoadStore();
            var yard = GameLevel.Current;
            var player = PlayerManager.Instance.Player;
            var skid = yard.SkidSteer;
            var rb = skid.GetComponent<Rigidbody>();
            Vector3 home = rb.position;

            StandAt(player, skid.DoorPosition);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(skid, player.Current);

            rb.position = home + new Vector3(0f, 0f, 9f);   // "stuck" somewhere else
            skid.transform.position = rb.position;
            Physics.SyncTransforms();
            Assert.Greater((rb.position - home).magnitude, 5f);

            yield return Tap(Key.X);
            Assert.Less((rb.position - home).magnitude, 0.2f, "back at its start");
            Assert.AreSame(skid, player.Current, "the driver stays in the cab");
        }

        [UnityTest]
        public IEnumerator RespawnVehicleFromFootUsesTheNearestMachine()
        {
            yield return LoadStore();
            var yard = GameLevel.Current;
            var player = PlayerManager.Instance.Player;
            var loader = yard.WheelLoader;
            var rb = loader.GetComponent<Rigidbody>();
            Vector3 home = rb.position;
            rb.position = home + new Vector3(0f, 0f, 6f);
            loader.transform.position = rb.position;
            StandAt(player, loader.DoorPosition);
            yield return null;
            Assert.IsNull(player.Current);

            yield return Tap(Key.X);
            Assert.Less((rb.position - home).magnitude, 0.2f, "the loader went home");
            Assert.IsNull(player.Current, "still on foot");
        }

        [UnityTest]
        public IEnumerator RespawnPlayerReturnsToTheSpawnAndLeavesTheMachine()
        {
            yield return LoadStore();
            var yard = GameLevel.Current;
            var player = PlayerManager.Instance.Player;
            Vector3 spawn = player.spawnPosition;

            StandAt(player, yard.SkidSteer.DoorPosition);
            yield return null;
            yield return Tap(Key.E);
            Assert.IsTrue(player.InCab);

            yield return Tap(Key.Q);
            Assert.IsFalse(player.InCab, "respawn takes the player out of the cab");
            Vector3 d = player.transform.position - spawn;
            d.y = 0f;
            Assert.Less(d.magnitude, 0.2f, "standing at the spawn point");
        }

        // ------------------------------------------------------------------ gauges

        [UnityTest]
        public IEnumerator BucketMachinesShowTheBucketTiltGauge()
        {
            yield return LoadStore();
            var yard = GameLevel.Current;
            var player = PlayerManager.Instance.Player;
            var gauge = player.GetComponent<RigGaugeHud>();
            Assert.IsNotNull(gauge);
            yield return null;
            Assert.IsNull(gauge.Mode, "nothing on foot");

            var skid = yard.SkidSteer;
            StandAt(player, skid.DoorPosition);
            yield return null;
            yield return Tap(Key.E);
            yield return null;
            Assert.AreEqual("bucket", gauge.Mode);
            Assert.AreEqual(skid.Load.PitchDown, gauge.BucketPitch, 0.01f);

            // Raise the arms (R) so the lip clears the ground, then tip the bucket forward (C): the shown pitch rises.
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.R));
            yield return new WaitForSeconds(0.8f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            float before = gauge.BucketPitch;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.C));
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Assert.Greater(gauge.BucketPitch, before + 3f, "the gauge follows the bucket");
        }

        [UnityTest]
        public IEnumerator CraneAndExcavatorShowTracksAgainstTheCab()
        {
            yield return LoadStore();
            var yard = GameLevel.Current;
            var player = PlayerManager.Instance.Player;
            var gauge = player.GetComponent<RigGaugeHud>();

            var crane = yard.Crane.GetComponent<CraneOperable>();
            StandAt(player, crane.DoorPosition);
            yield return null;
            yield return Tap(Key.E);
            yield return null;
            Assert.AreEqual("tracks", gauge.Mode);
            Assert.AreEqual(0f, gauge.TracksYaw, 0.5f, "freshly parked, cab and tracks line up");

            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));   // slew the cab
            yield return new WaitForSeconds(0.8f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Assert.AreEqual(-yard.Crane.SlewAngle, gauge.TracksYaw, 0.5f, "tracks sit opposite the slew");
            Assert.Greater(Mathf.Abs(gauge.TracksYaw), 3f);
            yield return Tap(Key.E);

            var ex = yard.Excavator;
            StandAt(player, ex.DoorPosition);
            yield return null;
            yield return Tap(Key.E);
            yield return null;
            Assert.AreEqual("tracks", gauge.Mode);
            yield return Tap(Key.E);
            yield return null;
            Assert.IsNull(gauge.Mode, "gauge hides on foot");

            var loader = yard.WheelLoader;
            StandAt(player, loader.DoorPosition);
            yield return null;
            yield return Tap(Key.E);
            yield return null;
            Assert.AreEqual("bucket", gauge.Mode, "the wheel loader shows its bucket, not tracks");
        }

        // ------------------------------------------------------------------ debug HUD

        [UnityTest]
        public IEnumerator GameViewHasNoLabHudOrDiagnosticOverlay()
        {
            yield return LoadStore();
            Assert.IsNull(Object.FindAnyObjectByType<LabHud>(), "lab HUD is not built in the game");
            Assert.IsNull(Object.FindAnyObjectByType<DiagnosticsOverlay>(), "diagnostic overlay is not built in the game");
            var yard = GameLevel.Current;
            Assert.IsFalse(yard.Controller.DiagnosticsVisible);
        }

        [UnityTest]
        public IEnumerator PlainLabStillHasItsDebugTools()
        {
            yield return SceneManager.LoadSceneAsync("DestructionLab");
            yield return null;
            yield return null;
            Assert.IsNotNull(Object.FindAnyObjectByType<LabHud>());
            Assert.IsNotNull(Object.FindAnyObjectByType<DiagnosticsOverlay>());
        }
    }
}
