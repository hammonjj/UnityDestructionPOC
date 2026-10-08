using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>Loads CraneTest.unity and drives the crane through its public controls.</summary>
    public sealed class CraneTests
    {
        CraneTestBootstrap boot;
        CraneRig crane;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("CraneTest");
            yield return null;
            boot = Object.FindAnyObjectByType<CraneTestBootstrap>();
            Assert.IsNotNull(boot, "bootstrap");
            crane = boot.Crane;
            Assert.IsNotNull(crane, "crane built");
            yield return new WaitForSeconds(1.5f); // let the ball settle on its rope
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            // The scene stays loaded for later fixtures; leave it empty so its world, ground and ball cannot leak into them.
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(go);
            ScenarioLibrary.WarehouseModel = null;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            yield return null;
            yield return null;
        }

        static IEnumerator Hold(float seconds, System.Action<float> apply)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                apply(Time.deltaTime);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator BallHangsFromBoomTipClearOfGroundAndCrane()
        {
            var ball = crane.Ball.position;
            var tip = crane.Boom.GetComponentsInChildren<Transform>()[1].position; // Anchor_BoomTipCable
            Assert.Greater(tip.y, 9f, "boom tip is high");
            Assert.Less(Mathf.Abs(ball.x - tip.x) + Mathf.Abs(ball.z - tip.z), 0.6f, "ball hangs below the tip");
            Assert.Greater(ball.y - 0.75f, 2f, "ball clear of the ground");
            Assert.LessOrEqual(Vector3.Distance(tip, ball + Vector3.up * 1.13f), crane.CableLength + 0.1f, "rope not overstretched");
            yield return null;
        }

        [UnityTest]
        public IEnumerator WinchLowersAndRaisesBall()
        {
            float y0 = crane.Ball.position.y;
            yield return Hold(2f, _ => crane.Winch(1f));
            yield return new WaitForSeconds(1.5f);
            Assert.Less(crane.Ball.position.y, y0 - 2f, "pay out lowers the ball");
            yield return Hold(3f, _ => crane.Winch(-1f));
            yield return new WaitForSeconds(1f);
            Assert.Greater(crane.Ball.position.y, y0 - 1.5f, "reeling in lifts it again");
        }

        [UnityTest]
        public IEnumerator SlewCarriesTheBallAround()
        {
            var before = crane.Ball.position;
            yield return Hold(3f, _ => crane.Slew(1f));
            yield return new WaitForSeconds(1f);
            Assert.Greater(crane.SlewAngle, 30f);
            Assert.Greater(Vector3.Distance(before, crane.Ball.position), 3f, "ball follows the boom");
        }

        [UnityTest]
        public IEnumerator LuffRaisesAndLowersBoom()
        {
            float a0 = crane.BoomAngle;
            var tipOf = new System.Func<float>(() => crane.Boom.GetComponentsInChildren<Transform>()[1].position.y);
            float h0 = tipOf();
            yield return Hold(2f, _ => crane.Luff(-1f));
            Assert.Less(crane.BoomAngle, a0);
            Assert.Less(tipOf(), h0 - 0.5f, "lowering the boom lowers the tip");
            yield return Hold(4f, _ => crane.Luff(1f));
            Assert.Greater(tipOf(), h0 - 0.1f, "raising brings it back");
        }

        [UnityTest]
        public IEnumerator DriveMovesWholeCraneForward()
        {
            var p0 = crane.transform.position;
            yield return Hold(1.5f, dt => crane.Drive(1f, 0f));
            crane.Drive(0f, 0f);
            yield return null;
            Assert.Greater(Vector3.Distance(p0, crane.transform.position), 2.5f);
            Assert.Greater(crane.transform.position.z, p0.z, "drives toward the building (+Z)");
        }

        [UnityTest]
        public IEnumerator BallDrivenIntoWallBreaksTheWarehouse()
        {
            int before = boot.World.log.Total;
            yield return Hold(1.2f, _ => crane.Winch(1f)); // bring the ball down to wall height
            yield return Hold(2.5f, dt => crane.Drive(1f, 0f));
            crane.Drive(0f, 0f);
            yield return Hold(3f, _ => crane.Slew(-1f)); // then drag it along the wall
            yield return new WaitForSeconds(2f);
            Assert.Greater(boot.World.log.Total, before, "the ball broke something");
        }

        [UnityTest]
        public IEnumerator CraneStallsAgainstTheWarehouseInsteadOfDrivingThrough()
        {
            yield return Hold(1.5f, _ => crane.Winch(-1f)); // ball up, clear of the wall
            yield return Hold(8f, dt => crane.Drive(1f, 0f));
            crane.Drive(0f, 0f);
            Assert.Greater(crane.Blocked, 0, "the warehouse stopped the crane");
            // Warehouse south face is at z = -3.5; the crane body reaches ~2.5 m ahead of its origin.
            Assert.Less(crane.transform.position.z, -4.5f, "crane did not drive into the building");
        }

        [UnityTest]
        public IEnumerator OverheadCameraStaysFixedWhileCarriageTurns()
        {
            var player = boot.Player;
            player.SetView(CranePlayer.CameraView.Overhead); // no longer reachable in play; still supported by the component
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode =
                UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            player.transform.position = crane.DoorPosition + Vector3.up * 0.1f;
            yield return null;
            var kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.E));
            yield return null;
            yield return null;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            Assert.IsTrue(player.InCab);
            var rot0 = player.cam.transform.rotation;
            float slew0 = crane.SlewAngle;
            yield return Hold(3f, _ => crane.Slew(1f));
            yield return new WaitForSeconds(1.5f); // coast to a stop
            Assert.Greater(crane.SlewAngle - slew0, 20f, "carriage turned");
            Assert.Less(Quaternion.Angle(rot0, player.cam.transform.rotation), 0.01f, "camera orientation is fixed");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(kb);
        }

        [UnityTest]
        public IEnumerator OverheadCameraIsFixedAngleWithOneListener()
        {
            var cam = Camera.main;
            boot.Player.SetView(CranePlayer.CameraView.Overhead);
            yield return null;
            Assert.IsNotNull(cam.GetComponent<CraneOverheadCamera>());
            Assert.IsFalse(cam.orthographic, "perspective");
            Assert.AreEqual(35f, cam.transform.eulerAngles.x, 0.01f, "pitch");
            Assert.AreEqual(45f, cam.transform.eulerAngles.y, 0.01f, "yaw");
            int listeners = 0;
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                if (l.enabled) listeners++;
            Assert.AreEqual(1, listeners, "one active AudioListener");
            // Player is near the screen centre.
            var vp = cam.WorldToViewportPoint(boot.Player.transform.position + Vector3.up);
            Assert.AreEqual(0.5f, vp.x, 0.05f);
            Assert.AreEqual(0.5f, vp.y, 0.05f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WalkingIsScreenRelativeAtConstantSpeed()
        {
            var player = boot.Player;
            var cam = Camera.main;
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode =
                UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            // Headless runs can exceed 1000 fps, where each step is below CharacterController.minMoveDistance (1 mm)
            // and is dropped. Cap to a realistic frame rate.
            int vsync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            // Start on open ground so the crane tracks or the warehouse cannot block the walk.
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = new Vector3(-40f, 0.1f, 10f); // clear of the excavator yard to the south
            cc.enabled = true;
            var kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            yield return null;
            yield return null;

            var p0 = player.transform.position;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            yield return new WaitForSeconds(0.5f);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            var up = player.transform.position - p0;
            up.y = 0f;
            var screenUp = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up).normalized;
            Assert.Greater(Vector3.Dot(up.normalized, screenUp), 0.99f, "W moves toward the top of the screen");

            p0 = player.transform.position;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.D));
            yield return new WaitForSeconds(0.5f);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            var right = player.transform.position - p0;
            right.y = 0f;
            Assert.Greater(Vector3.Dot(right.normalized, cam.transform.right), 0.99f, "D moves toward the right of the screen");

            p0 = player.transform.position;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.D));
            yield return new WaitForSeconds(0.5f);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            var diag = player.transform.position - p0;
            diag.y = 0f;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = vsync;
            Assert.Greater(up.magnitude, 1.5f, "walked a real distance");
            Assert.AreEqual(up.magnitude, diag.magnitude, up.magnitude * 0.1f, "diagonal speed matches straight speed");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(kb);
        }

        [UnityTest]
        public IEnumerator CameraSnapsOnTeleportAndReset()
        {
            var cam = Camera.main;
            boot.Player.GetComponent<CharacterController>().enabled = false;
            boot.Player.transform.position = new Vector3(40f, 0.1f, 30f);
            boot.Player.GetComponent<CharacterController>().enabled = true;
            yield return null;
            yield return null;
            var vp = cam.WorldToViewportPoint(boot.Player.transform.position + Vector3.up);
            Assert.AreEqual(0.5f, vp.x, 0.03f, "snapped after teleport");
            Assert.AreEqual(0.5f, vp.y, 0.03f);
        }

        [UnityTest]
        public IEnumerator PlayerCanEnterAndLeaveCab()
        {
            var player = boot.Player;
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode =
                UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            player.transform.position = crane.DoorPosition + Vector3.up * 0.1f;
            yield return null;
            var kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var inputTest = new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.E);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, inputTest);
            yield return null;
            yield return null;
            Assert.IsTrue(player.InCab, "E near the door enters the cab");
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, inputTest);
            yield return null;
            yield return null;
            Assert.IsFalse(player.InCab, "E again leaves");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(kb);
        }
    }
}
