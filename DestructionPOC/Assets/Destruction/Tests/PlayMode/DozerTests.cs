using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>Loads CraneTest.unity and drives the landfill dozer: setup, enter, drive, blade lift, pushing rubble.</summary>
    public sealed class DozerTests
    {
        CraneTestBootstrap boot;
        CranePlayer player;
        Keyboard kb;

        [UnitySetUp]
        public IEnumerator Load()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            yield return SceneManager.LoadSceneAsync("CraneTest");
            yield return null;
            boot = Object.FindAnyObjectByType<CraneTestBootstrap>();
            Assert.IsNotNull(boot, "bootstrap");
            player = boot.Player;
            kb = InputSystem.AddDevice<Keyboard>();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            yield return new WaitForSeconds(1.0f); // loose debris settles
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (kb != null && kb.added) InputSystem.RemoveDevice(kb);
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(go);
            ScenarioLibrary.WarehouseModel = null;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Application.targetFrameRate = -1;
            yield return null;
            yield return null;
        }

        IEnumerator Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
            yield return null;
            yield return null;
        }

        IEnumerator Hold(float seconds, params Key[] keys)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
            yield return Keys();
        }

        void Enter(DozerRig rig)
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            var p = rig.DoorPosition;
            player.transform.position = new Vector3(p.x, 0.1f, p.z);
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator YardHasDozerWithBladeOnTheGround()
        {
            var d = boot.Dozer;
            Assert.IsNotNull(d, "dozer built");
            Assert.IsTrue(player.rigs.Contains(d), "dozer is enterable");
            Assert.AreEqual(0f, d.EdgeHeight, 0.03f, "edge rests on the ground");
            Assert.Greater(Vector3.Dot(d.Edge.position - d.transform.position, d.transform.forward), 3f, "blade is ahead");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DrivesAndLiftsBlade()
        {
            var d = boot.Dozer;
            Enter(d);
            yield return Keys(Key.E);
            yield return Keys();
            Assert.AreSame(d, player.Current, "climbed in");

            Vector3 p0 = d.transform.position;
            yield return Hold(1.0f, Key.W);
            Assert.Greater(Vector3.Dot(d.transform.position - p0, d.transform.forward), 0.5f, "moves forward");

            yield return Hold(1.5f, Key.R);
            Assert.Greater(d.EdgeHeight, 0.3f, "blade rises");
            yield return Hold(2.5f, Key.F);
            Assert.AreEqual(0f, d.EdgeHeight, 0.03f, "blade lowers to the ground, not below");

            float yaw0 = d.transform.eulerAngles.y;
            yield return Hold(1.0f, Key.D);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw0, d.transform.eulerAngles.y)), 10f, "pivots");
        }

        [UnityTest]
        public IEnumerator PushesRubbleAheadOfTheBlade()
        {
            var d = boot.Dozer;
            Enter(d);
            yield return Keys(Key.E);
            yield return Keys();
            var pieces = boot.World.Graph.pieces;
            // The piece straight ahead of the blade (the first piece sits at the pile's edge, outside the blade width).
            int idx = -1;
            float best = float.MaxValue;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (!pieces[i].name.StartsWith("Dozer debris")) continue;
                float off = Mathf.Abs(boot.World.pieces[i].transform.position.x - d.transform.position.x);
                if (off < best) { best = off; idx = i; }
            }
            Assert.GreaterOrEqual(idx, 0, "windrow exists");
            Vector3 before = boot.World.pieces[idx].transform.position;
            Vector3 d0 = d.transform.position;
            yield return Hold(9f, Key.W);
            Vector3 after = boot.World.pieces[idx].transform.position;
            string hits = "";
            foreach (var c in d.GetComponentsInChildren<BoxCollider>())
            {
                var hs = Physics.OverlapBox(c.bounds.center, c.bounds.extents + Vector3.one * 0.05f, Quaternion.identity);
                foreach (var h in hs)
                    if (!h.transform.IsChildOf(d.transform) && h.name != "Ground" && h.GetComponent<CharacterController>() == null)
                        hits += $"[{c.name} x {h.name} m={(h.attachedRigidbody != null ? h.attachedRigidbody.mass : -1f):0} kin={(h.attachedRigidbody != null && h.attachedRigidbody.isKinematic)}] ";
            }
            Assert.Greater(Vector3.Distance(before, after), 0.5f, $"windrow was pushed (dozer ended {d.transform.position} yaw {d.transform.eulerAngles.y:0}, blocked {d.Blocked}, piece {before} -> {after}) hits: {hits}");
        }
    }
}
