using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>Loads ConvenienceStore.unity: the yard (crane, skid steer, dozer, roll-off container, player), the
    /// "rubble cleared" gauge and its denominator, and the lab keys alongside the player's.</summary>
    public sealed class StoreYardTests
    {
        StoreYard yard;
        DestructionWorld world;
        CranePlayer player;
        Keyboard kb;

        [UnitySetUp]
        public IEnumerator Load()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            yield return SceneManager.LoadSceneAsync("ConvenienceStore");
            yield return null;
            yard = UnityEngine.Object.FindAnyObjectByType<StoreYard>();
            Assert.IsNotNull(yard, "StoreYard in the scene");
            world = yard.World;
            player = yard.Player;
            kb = InputSystem.AddDevice<Keyboard>();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (kb != null && kb.added) InputSystem.RemoveDevice(kb);
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) UnityEngine.Object.Destroy(go);
            ScenarioLibrary.ConvenienceStoreModel = null;
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

        IEnumerator Tap(Key key)
        {
            yield return Keys(key);
            yield return Keys();
        }

        void StandAtDoor(IOperableRig rig)
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            var p = rig.DoorPosition;
            player.transform.position = new Vector3(p.x, 0.1f, p.z);
            cc.enabled = true;
            Physics.SyncTransforms();
        }

        CleanupLedger Ledger => yard.Ledger;

        /// <summary>Drop up to n loose building bodies (rubble, fragments or detached pieces) into the container.</summary>
        float Deliver(int n)
        {
            var seen = new HashSet<Rigidbody>();
            int moved = 0;
            float kg = 0f;
            for (int i = 0; i < world.pieces.Count && moved < n; i++)
            {
                var p = world.pieces[i];
                if (p == null || p.removed || p.cluster == null || p.cluster.isStatic || p.cluster.body == null) continue;
                var rb = p.cluster.body;
                if (!seen.Add(rb) || !CleanupLedger.CountsAsBuilding(world.Graph.pieces[i])) continue;
                float mass = 0f;
                foreach (int pi in p.cluster.pieces) mass += world.Graph.mass[pi];
                rb.position = yard.Container.transform.position + new Vector3(-6f + moved * 0.4f, 1.0f + (moved % 3) * 0.3f, ((moved % 5) - 2) * 0.5f);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                moved++;
                kg += mass;
            }
            return kg;
        }

        IEnumerator BlowOutStore()
        {
            yard.Lab.Controller.Trigger();
            world.Explode(new Vector3(13f, 1.5f, -10.5f), 3.5f, 2f, 20000f);
            world.Explode(new Vector3(7f, 1.5f, -10.5f), 3.5f, 2f, 20000f);
            yield return new WaitForSeconds(3f);
        }

        // ------------------------------------------------------------------ setup

        [UnityTest]
        public IEnumerator YardHasTheThreeRigsContainerAndPlayerOutsideTheStore()
        {
            Assert.IsNotNull(yard.Crane);
            Assert.IsNotNull(yard.Crane.Ball, "wrecking ball");
            Assert.IsNotNull(yard.SkidSteer);
            Assert.IsNotNull(yard.Dozer);
            Assert.IsNotNull(yard.Container);
            Assert.AreEqual(3, player.rigs.Count, "crane + skid steer + dozer are enterable");

            // Machines stand on the ground and clear of the store (x 4..16, z -18..-10) and the pump canopy (x -14..-2, z -6..2).
            var store = new Bounds(new Vector3(10f, 2f, -14f), new Vector3(14f, 10f, 10f));
            var canopy = new Bounds(new Vector3(-8f, 2f, -2f), new Vector3(14f, 10f, 10f));
            foreach (var t in new[] { yard.Crane.transform, yard.SkidSteer.transform, yard.Dozer.transform, yard.Container.transform })
            {
                Assert.AreEqual(0f, t.position.y, 0.3f, $"{t.name} on the ground");
                Assert.IsFalse(store.Contains(t.position), $"{t.name} outside the store");
                Assert.IsFalse(canopy.Contains(t.position), $"{t.name} outside the pump canopy");
            }
            // The crane can swing its ball over the store.
            Assert.Less(Vector3.Distance(yard.Crane.transform.position, new Vector3(10f, 0f, -14f)), 30f, "store within the crane's reach");
            // Container is bigger than the CraneTest one and still low enough to dump over.
            Assert.GreaterOrEqual(yard.Container.interior.x, 14f);
            Assert.Less(yard.Container.WallTop, 2.3f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerWalksToEachRigAndEntersIt()
        {
            foreach (IOperableRig rig in new IOperableRig[] { yard.SkidSteer, yard.Dozer, yard.Crane.GetComponent<CraneOperable>() })
            {
                StandAtDoor(rig);
                yield return null;
                Assert.AreSame(rig, player.NearestRig, $"{rig.RigName}: door is nearest");
                yield return Tap(Key.E);
                Assert.AreSame(rig, player.Current, $"{rig.RigName}: E enters");
                yield return Tap(Key.E);
                Assert.IsNull(player.Current, $"{rig.RigName}: E exits");
            }
        }

        [UnityTest]
        public IEnumerator SkidSteerAndDozerDriveAcrossTheLotAndTheCraneSlews()
        {
            foreach (var rig in new MonoBehaviour[] { yard.SkidSteer, yard.Dozer })
            {
                var op = (IOperableRig)rig;
                StandAtDoor(op);
                yield return null;
                yield return Tap(Key.E);
                Assert.AreSame(op, player.Current, op.RigName);
                Vector3 start = rig.transform.position;
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
                yield return new WaitForSeconds(1.5f);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return new WaitForSeconds(0.5f);
                Assert.Greater(Vector3.Distance(start, rig.transform.position), 1f, $"{op.RigName} drives");
                Assert.AreEqual(0f, rig.transform.position.y, 0.3f, $"{op.RigName} stays on the ground");
                yield return Tap(Key.E);
                Assert.IsNull(player.Current);
            }

            var crane = yard.Crane.GetComponent<CraneOperable>();
            StandAtDoor(crane);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(crane, player.Current);
            float slew0 = yard.Crane.SlewAngle;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            yield return new WaitForSeconds(1f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Assert.AreNotEqual(slew0, yard.Crane.SlewAngle, "A/D slews the crane");
        }

        // ------------------------------------------------------------------ gauge

        [UnityTest]
        public IEnumerator GaugeStartsAtZeroWithTheWholeBuildingAsDenominator()
        {
            var l = Ledger;
            Assert.IsTrue(l.HasBuilding);
            Assert.AreEqual(0f, l.BuildingProgress);
            Assert.AreEqual(0f, l.BuildingClearedKg);
            // 232 authored pieces; the 24 ground-level paving pieces (sidewalk, curb, pump island, wheel stops) do not count.
            Assert.AreEqual(world.Graph.PieceCount - 24, l.BuildingPieces);
            // Hand-summed from the graph: every non-paving authored piece.
            float sum = 0f;
            for (int i = 0; i < world.Graph.PieceCount; i++)
                if (CleanupLedger.CountsAsBuilding(world.Graph.pieces[i])) sum += world.Graph.mass[i];
            Assert.AreEqual(sum, l.BuildingMassKg, 1f);
            Assert.Greater(l.BuildingMassKg, 100000f, "a store's worth of mass");
            var hud = player.GetComponent<LoaderHud>();
            yield return null;
            StringAssert.StartsWith("RUBBLE CLEARED  0.0%", hud.CleanupText);
        }

        [UnityTest]
        public IEnumerator DenominatorIsFixedWhileThePiecesFragment()
        {
            float denom = Ledger.BuildingMassKg;
            int pieces = Ledger.BuildingPieces;
            int graph0 = world.Graph.PieceCount;
            yield return BlowOutStore();
            Assert.Greater(world.Graph.PieceCount, graph0 + 20, "the blast shattered pieces into fragments");
            Assert.Greater(world.LiveFragments, 20);

            Assert.AreEqual(denom, Ledger.BuildingMassKg, 0f, "denominator did not move");
            Assert.AreEqual(pieces, Ledger.BuildingPieces);

            // Re-reading the fragmented world gives the same total (fragments are skipped, not summed twice).
            var again = new CleanupLedger();
            again.ResetBuilding(world);
            Assert.AreEqual(denom, again.BuildingMassKg, 0.5f, "recomputing from a fragmented world matches");
            Assert.AreEqual(pieces, again.BuildingPieces);

            // Conservation: the mass still in the world (intact pieces + fragments of building pieces) is the denominator.
            float live = 0f;
            for (int i = 0; i < world.Graph.PieceCount; i++)
            {
                if (world.pieces[i].removed) continue;
                var def = world.Graph.pieces[i];
                if (CleanupLedger.CountsAsBuilding(def) || def.name.Contains(" frag ")) live += world.Graph.mass[i];
            }
            Assert.AreEqual(denom, live, denom * 0.02f, "fragments carry their parents' mass (paving fragments aside)");
            Assert.AreEqual(0f, Ledger.BuildingProgress, "nothing delivered yet");
        }

        [UnityTest]
        public IEnumerator GaugeRisesAsRubbleIsDeliveredAndNeverExceedsTheTotal()
        {
            yield return BlowOutStore();
            float denom = Ledger.BuildingMassKg;
            float kg = Deliver(25);
            Assert.Greater(kg, 1000f);
            yield return new WaitForSeconds(3f);

            Assert.Greater(Ledger.BuildingClearedKg, 1000f, "container accepted rubble");
            Assert.Greater(Ledger.BuildingProgress, 0.001f);
            Assert.Less(Ledger.BuildingProgress, 0.5f);
            Assert.AreEqual(Ledger.BuildingClearedKg / denom, Ledger.BuildingProgress, 1e-5f);
            Assert.AreEqual(denom, Ledger.BuildingMassKg, 0f);
            yield return null;
            var hud = player.GetComponent<LoaderHud>();
            StringAssert.Contains(LoaderHud.Percent(Ledger.BuildingProgress), hud.CleanupText);

            // The percentage is the same fraction whether it is read as mass or as volume of whole pieces: delivering
            // more only ever raises it, and it is bounded.
            float before = Ledger.BuildingProgress;
            Deliver(60);
            yield return new WaitForSeconds(3f);
            Assert.GreaterOrEqual(Ledger.BuildingProgress, before);
            Assert.LessOrEqual(Ledger.BuildingProgress, 1f);
        }

        [UnityTest]
        public IEnumerator ResetReturnsEverythingToZeroPercent()
        {
            yield return BlowOutStore();
            Deliver(25);
            yield return new WaitForSeconds(3f);
            Assert.Greater(Ledger.BuildingProgress, 0f);
            float denom = Ledger.BuildingMassKg;
            int builds = world.BuildCount;

            yield return Tap(Key.R); // on foot: R resets the whole scene, as in the lab
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(world.BuildCount, builds, "world rebuilt");
            Assert.AreEqual(0f, Ledger.BuildingProgress);
            Assert.AreEqual(0f, Ledger.BuildingClearedKg);
            Assert.AreEqual(denom, Ledger.BuildingMassKg, 0.5f, "same building, same denominator");
            Assert.AreEqual(0, world.LiveFragments);
            Assert.AreEqual(0, yard.Container.FrozenPieces);

            // Backspace (the player's reset) does the same.
            yield return BlowOutStore();
            Deliver(10);
            yield return new WaitForSeconds(3f);
            Assert.Greater(Ledger.BuildingProgress, 0f);
            yield return Tap(Key.Backspace);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0f, Ledger.BuildingProgress);
        }

        // ------------------------------------------------------------------ keys

        [UnityTest]
        public IEnumerator SpaceJumpsOnFootAndPauseMovesToP()
        {
            var cc = player.GetComponent<CharacterController>();
            yield return new WaitForSeconds(0.5f);
            float y0 = player.transform.position.y;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(player.transform.position.y, y0 + 0.2f, "Space jumps");
            Assert.IsFalse(world.Paused, "Space does not pause");
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return new WaitForSeconds(1f);
            yield return Tap(Key.P);
            Assert.IsTrue(world.Paused, "P pauses");
            yield return Tap(Key.P);
            Assert.IsFalse(world.Paused);
            Assert.IsTrue(cc.enabled);
        }

        [UnityTest]
        public IEnumerator ScenarioSwitchingIsOffAndTheStoreStaysLoaded()
        {
            int scenario = yard.Lab.Controller.ScenarioIndex;
            yield return Tap(Key.N);
            yield return Tap(Key.B);
            Assert.AreEqual(scenario, yard.Lab.Controller.ScenarioIndex);
            Assert.AreEqual("store", yard.Lab.Controller.Scenarios[scenario].id);
        }

        [UnityTest]
        public IEnumerator LabShortcutsAreIgnoredInACabSoRWinchesInsteadOfResetting()
        {
            var op = yard.Crane.GetComponent<CraneOperable>();
            yield return BlowOutStore();
            Deliver(15);
            yield return new WaitForSeconds(3f);
            float cleared = Ledger.BuildingProgress;
            Assert.Greater(cleared, 0f);
            int builds = world.BuildCount;

            StandAtDoor(op);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(op, player.Current);
            Assert.IsTrue(yard.Lab.Controller.keysBlocked());

            float cable0 = yard.Crane.CableLength;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.R)); // crane: R pays the cable in
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Assert.AreEqual(builds, world.BuildCount, "R did not reset the scene");
            Assert.AreEqual(cleared, Ledger.BuildingProgress, 0.02f, "gauge kept");
            Assert.AreNotEqual(cable0, yard.Crane.CableLength, "R drove the winch");

            // Lab tool keys are ignored in the cab; Backspace still resets from anywhere.
            var tool = yard.Lab.Controller.Tool;
            yield return Tap(Key.Digit3);
            Assert.AreEqual(tool, yard.Lab.Controller.Tool, "1-4 ignored in the cab");
            yield return Tap(Key.Backspace);
            yield return new WaitForSeconds(0.3f);
            Assert.IsNull(player.Current, "reset puts the player back on foot");
            Assert.AreEqual(0f, Ledger.BuildingProgress);
        }

        [UnityTest]
        public IEnumerator MouseDestroyToolStillBreaksTheStoreWithThePlayerPresent()
        {
            var tools = yard.Lab.Controller;
            Assert.AreEqual(LabTool.Damage, tools.Tool);
            yield return Tap(Key.Digit2);
            Assert.AreEqual(LabTool.Explosion, tools.Tool, "1-4 select tools on foot");
            yield return Tap(Key.T);
            Assert.IsTrue(tools.TriggerUsed, "T triggers the store blast");
            yield return new WaitForSeconds(2f);
            Assert.Greater(world.stats.severed + world.stats.residual, 0);
        }
    }
}
