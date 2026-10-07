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

        /// <summary>A loose block of <paramref name="mass"/> kg on the ground, 2 m ahead of the blade.</summary>
        static Rigidbody BlockAhead(DozerRig d, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Test block";
            go.transform.localScale = new Vector3(2f, 1f, 1.5f);
            Vector3 edge = d.Edge.position;
            go.transform.position = new Vector3(edge.x, 0.5f, edge.z) + d.transform.forward * 2.8f;
            go.transform.rotation = d.transform.rotation;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            return rb;
        }

        IEnumerator DriveForward(DozerRig d, float seconds, System.Action each)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                yield return new WaitForFixedUpdate();
                each?.Invoke();
            }
            yield return Keys();
        }

        [UnityTest]
        public IEnumerator TracksRollOverACurb()
        {
            // Reverse over a 30 cm curb, away from the windrow ahead of the blade.
            var d = boot.Dozer;
            Assert.Greater(d.Terrain.StepHeight, 0.3f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Test curb";
            go.transform.localScale = new Vector3(5f, 0.3f, 0.3f);
            go.transform.SetPositionAndRotation(d.transform.position - d.transform.forward * 3.4f + Vector3.up * 0.15f, d.transform.rotation);
            Physics.SyncTransforms();
            float Behind() => -Vector3.Dot(go.transform.position - d.transform.position, d.transform.forward);
            float peak = 0f;
            d.Command(-1f, 0f, 0f);
            for (float end = Time.time + 6f; Time.time < end && Behind() > -3.4f;)
            {
                yield return new WaitForFixedUpdate();
                peak = Mathf.Max(peak, d.Terrain.Lift);
            }
            d.Command(0f, 0f, 0f);
            Assert.Less(Behind(), -3.4f, $"drove over the curb (blocked {d.Blocked})");
            Assert.Greater(peak, 0.15f, $"the dozer rose onto the curb ({d.Terrain.SupportCount} footprint points)");
        }

        [UnityTest]
        public IEnumerator HeavyRubbleIsPushedButSlowsTheDozer()
        {
            var d = boot.Dozer;
            Enter(d);
            yield return Keys(Key.E);
            yield return Keys();
            var block = BlockAhead(d, 15000f);
            yield return new WaitForSeconds(0.3f);
            Vector3 before = block.position;
            float peakLoad = 0f, loadedSpeed = 0f, loadedFor = 0f;
            yield return DriveForward(d, 6f, () =>
            {
                peakLoad = Mathf.Max(peakLoad, d.PushLoad);
                // Sample once the machine has had time to bog down from its approach speed.
                loadedFor = d.PushLoad >= 15000f ? loadedFor + Time.fixedDeltaTime : 0f;
                if (loadedFor >= 0.5f) loadedSpeed = Mathf.Max(loadedSpeed, d.Speed);
            });
            float moved = Vector3.Dot(block.position - before, d.transform.forward);
            Assert.Greater(moved, 1f, $"15 t block was pushed (blocked {d.Blocked}, peak load {peakLoad:0} kg)");
            Assert.GreaterOrEqual(peakLoad, 15000f, "block counted as blade load");
            Assert.Greater(loadedSpeed, 0f, "kept moving under load");
            Assert.LessOrEqual(loadedSpeed, d.TopSpeedUnderLoad(15000f) + 0.05f, "speed capped by the load");
            Assert.Less(loadedSpeed, d.tuning.maxSpeed * 0.6f, "a full blade slows the machine well below top speed");
        }

        [UnityTest]
        public IEnumerator StallsAgainstRubblePastTheStallMass()
        {
            var d = boot.Dozer;
            Enter(d);
            yield return Keys(Key.E);
            yield return Keys();
            var block = BlockAhead(d, d.tuning.stallPushMass * 1.5f);
            yield return new WaitForSeconds(0.3f);
            Vector3 before = block.position;
            yield return DriveForward(d, 4f, null);
            Assert.Less(Vector3.Distance(before, block.position), 0.3f, "too heavy to shove");
            Assert.Greater(d.Blocked, 0, "dozer stalled against it");

            Vector3 p = d.transform.position;
            yield return Hold(1.0f, Key.S);
            Assert.Less(Vector3.Dot(d.transform.position - p, d.transform.forward), -0.3f, "can still back away");
        }

        [UnityTest]
        public IEnumerator RammingAWallDamagesIt()
        {
            var d = boot.Dozer;
            var world = boot.World;
            int wall = -1;
            for (int i = 0; i < world.Graph.PieceCount; i++)
                if (world.Graph.pieces[i].name == "Crusher wall L") wall = i;
            Assert.GreaterOrEqual(wall, 0, "crusher target wall exists");
            // Park the dozer on the open side of the wall, facing it, with a few metres of run-up for the blade.
            Vector3 w = world.pieces[wall].transform.position;
            Vector3 c = new Vector3(w.x + 0.7f, 0f, w.z); // between the two wall panels
            var body = d.GetComponent<Rigidbody>();
            float reach = Vector3.Dot(d.Edge.position - d.transform.position, d.transform.forward);
            var rot = Quaternion.LookRotation(Vector3.forward);
            Vector3 pos = c - Vector3.forward * (reach + 3.5f);
            d.transform.SetPositionAndRotation(pos, rot);
            body.position = pos;
            body.rotation = rot;
            Physics.SyncTransforms();
            Enter(d);
            yield return Keys(Key.E);
            yield return Keys();
            Assert.AreSame(d, player.Current, "climbed in");

            float damage0 = world.pieces[wall].shatterDamage + world.pieces[wall + 1].shatterDamage;
            yield return DriveForward(d, 3f, null);
            yield return null;
            float damage1 = world.pieces[wall].shatterDamage + world.pieces[wall + 1].shatterDamage;
            Assert.GreaterOrEqual(d.RamHits, 1, $"blade hit the wall at speed (dozer at {d.transform.position}, blocked {d.Blocked})");
            Assert.Greater(damage1, damage0, "the wall took damage");
            Assert.Less(damage1 - damage0, 0.5f, "one hit does not flatten it");

            // Holding the throttle against the wall builds no speed, so it does no more damage.
            int hits = d.RamHits;
            yield return DriveForward(d, 2f, null);
            Assert.AreEqual(hits, d.RamHits, "pressing against the wall is not ramming");
        }
    }
}
