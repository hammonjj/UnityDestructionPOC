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
            yield return Hold(1.5f, dt => crane.Winch(1f, dt));
            yield return new WaitForSeconds(1f);
            Assert.Less(crane.Ball.position.y, y0 - 2f, "pay out lowers the ball");
            yield return Hold(2f, dt => crane.Winch(-1f, dt));
            yield return new WaitForSeconds(1f);
            Assert.Greater(crane.Ball.position.y, y0 - 1.5f, "reeling in lifts it again");
        }

        [UnityTest]
        public IEnumerator SlewCarriesTheBallAround()
        {
            var before = crane.Ball.position;
            yield return Hold(2f, dt => crane.Slew(1f, dt));
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
            yield return Hold(1.5f, dt => crane.Luff(-1f, dt));
            Assert.Less(crane.BoomAngle, a0);
            Assert.Less(tipOf(), h0 - 0.5f, "lowering the boom lowers the tip");
            yield return Hold(3f, dt => crane.Luff(1f, dt));
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
            yield return Hold(0.8f, dt => crane.Winch(1f, dt)); // bring the ball down to wall height
            yield return Hold(1.5f, dt => crane.Drive(1f, 0f));
            crane.Drive(0f, 0f);
            yield return Hold(2f, dt => crane.Slew(-1f, dt)); // then drag it along the wall
            yield return new WaitForSeconds(2f);
            Assert.Greater(boot.World.log.Total, before, "the ball broke something");
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
