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
    /// <summary>Loads ConvenienceStore.unity (with Bootstrap and Player underneath): the authored yard (crane, skid
    /// steer, dozer, excavator, roll-off container), the spawned player, the "rubble cleared" gauge and its denominator,
    /// and the lab keys alongside the player's.</summary>
    public sealed class StoreYardTests
    {
        GameLevel level;
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
            GameSession.Clear();
            yield return GameScenes.LoadLevel("ConvenienceStore");
            level = GameLevel.Current;
            Assert.IsNotNull(level, "GameLevel in the scene");
            world = level.World;
            player = PlayerManager.Instance.Player;
            kb = InputSystem.AddDevice<Keyboard>();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (kb != null && kb.added) InputSystem.RemoveDevice(kb);
            yield return GameScenes.UnloadAll();
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Application.targetFrameRate = -1;
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

        CleanupLedger Ledger => level.Ledger;

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
                rb.position = level.container.transform.position + new Vector3(-6f + moved * 0.4f, 1.0f + (moved % 3) * 0.3f, ((moved % 5) - 2) * 0.5f);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                moved++;
                kg += mass;
            }
            return kg;
        }

        IEnumerator BlowOutStore()
        {
            level.Controller.Trigger();
            world.Explode(new Vector3(13f, 1.5f, -10.5f), 3.5f, 2f, 20000f);
            world.Explode(new Vector3(7f, 1.5f, -10.5f), 3.5f, 2f, 20000f);
            yield return new WaitForSeconds(3f);
        }

        // ------------------------------------------------------------------ setup

        // Store x 4..16, z -18..-10; pump canopy x -14..-2, z -6..2 (XZ rectangles, metres).
        static readonly Rect StoreRect = Rect.MinMaxRect(4f, -18f, 16f, -10f);
        static readonly Rect CanopyRect = Rect.MinMaxRect(-14f, -6f, -2f, 2f);

        /// <summary>Ground footprint of a rig: the union of its renderers' bounds (excavator included, boom at rest).</summary>
        static Rect Footprint(Component rig)
        {
            Bounds b = default;
            bool any = false;
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            Assert.IsTrue(any, rig.name + " has renderers");
            return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
        }

        [UnityTest]
        public IEnumerator YardHasTheFourRigsContainerAndPlayerOutsideTheStore()
        {
            Assert.IsNotNull(level.Crane);
            Assert.IsNotNull(level.Crane.Ball, "wrecking ball");
            Assert.IsNotNull(level.SkidSteer);
            Assert.IsNotNull(level.Dozer);
            Assert.IsNotNull(level.Excavator);
            Assert.IsNotNull(level.container);
            Assert.AreEqual(4, player.rigs.Count, "crane + skid steer + dozer + excavator are enterable");
            CollectionAssert.Contains(player.rigs, level.Excavator);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExcavatorIsTheHydraulicBreakerAndKeepsItsControls()
        {
            var ex = level.Excavator;
            Assert.AreEqual(ExcavatorAttachment.Breaker, ex.attachment, "starts on the jackhammer");
            Assert.AreEqual("Excavator", ex.RigName);
            Assert.AreEqual(ExcavatorRig.AttachmentLabel(ExcavatorAttachment.Breaker), ex.AttachmentName);
            StandAtDoor(ex);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(ex, player.Current);
            var panel = player.GetComponent<RigControlsPanel>();
            panel.Refresh();
            StringAssert.Contains("EXCAVATOR", panel.LastTitle);
            StringAssert.Contains("HYDRAULIC BREAKER", panel.LastTitle.ToUpperInvariant());
            Assert.IsTrue(panel.lastRows.Exists(r => r.Contains("Run breaker")), "the controls panel lists the breaker");
            Assert.IsTrue(panel.lastRows.Exists(r => r.Contains("Boom up / down")));
            // Lab shortcuts are ignored in the cab, as for the other rigs.
            Assert.IsTrue(level.Controller.keysBlocked());
            var tool = level.Controller.Tool;
            yield return Tap(Key.Digit3);
            Assert.AreEqual(tool, level.Controller.Tool, "1-4 ignored in the excavator cab");
            // The breaker runs on LMB, so a click there must not also fire the lab's destroy tool; on foot it still does.
            Assert.IsTrue(level.Controller.clickBlocked(), "LMB belongs to the breaker in the cab");
            yield return Tap(Key.E);
            Assert.IsNull(player.Current);
            Assert.IsFalse(level.Controller.clickBlocked(), "LMB fires the destroy tool on foot");
        }

        [UnityTest]
        public IEnumerator BreakerStrikesDamageTheStoreWall()
        {
            var ex = level.Excavator;
            // A brick bulkhead panel of the store's front wall (z ~ -10.2, 0.8 m tall).
            int target = world.Graph.pieces.FindIndex(p => p.name.StartsWith("Wall_Bulkhead_R1"));
            Assert.GreaterOrEqual(target, 0, "a front-wall brick piece");
            Vector3 centre = world.pieces[target].shape.bounds.center;

            // Drive up to the wall from the lot side: turn the machine to face the store (-Z), as a player would.
            var exBody = ex.GetComponent<Rigidbody>();
            ex.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            exBody.rotation = ex.transform.rotation;
            Physics.SyncTransforms();
            // Find a boom/stick pose that puts the bit tip at the piece's height.
            float bestErr = float.MaxValue, bb = 0f, bs = 0f;
            for (float boom = ex.boomMin; boom <= ex.boomMax; boom += 4f)
                for (float stick = ex.stickMin; stick <= ex.stickMax; stick += 5f)
                {
                    ex.SetPose(0f, boom, stick, 0f);
                    float err = Mathf.Abs(ex.WorkPoint.position.y - centre.y);
                    if (err < bestErr) { bestErr = err; bb = boom; bs = stick; }
                }
            ex.SetPose(0f, bb, bs, 0f);
            Assert.Less(bestErr, 0.4f, "arm can reach the wall piece's height");
            Vector3 d = centre - ex.WorkPoint.position;
            d.y = 0f;
            ex.transform.position += d;
            ex.GetComponent<Rigidbody>().position = ex.transform.position;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            int log0 = world.log.Total;
            int hits0 = ex.Hits;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                ex.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            ex.Command(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(ex.Hits - hits0, 3, "breaker strikes land on the wall");
            Assert.IsTrue(world.pieces[target].removed || world.log.Total > log0, "the store wall took damage");
        }

        [UnityTest]
        public IEnumerator ResetReturnsTheExcavatorToItsParkedPose()
        {
            var ex = level.Excavator;
            Vector3 start = ex.transform.position;
            Quaternion rot = ex.transform.rotation;
            float boom0 = ex.BoomAngle;
            StandAtDoor(ex);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(ex, player.Current);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return new WaitForSeconds(2.5f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Vector3.Distance(start, ex.transform.position), 1f, "excavator drove");
            yield return Tap(Key.Backspace);
            yield return new WaitForSeconds(0.3f);
            Assert.IsNull(player.Current);
            Assert.AreEqual(0f, Vector3.Distance(start, ex.transform.position), 0.01f, "back in the line");
            Assert.AreEqual(0f, Quaternion.Angle(rot, ex.transform.rotation), 0.5f);
            Assert.AreEqual(boom0, ex.BoomAngle, 0.01f);
            Assert.AreEqual(0, ex.Hits);
        }

        [UnityTest]
        public IEnumerator AllFourRigsParkInOneEvenlySpacedLineClearOfTheBuildings()
        {
            var rigs = new Component[] { level.Excavator, level.Dozer, level.SkidSteer, level.Crane };
            var containerRect = Rect.MinMaxRect(level.container.transform.position.x - level.container.interior.x * 0.5f - 0.5f,
                level.container.transform.position.z - level.container.interior.z * 0.5f - 0.5f,
                level.container.transform.position.x + level.container.interior.x * 0.5f + 0.5f,
                level.container.transform.position.z + level.container.interior.z * 0.5f + 0.5f);

            // One line: same z and heading, on the ground, evenly spaced along x.
            float z0 = rigs[0].transform.position.z;
            for (int i = 0; i < rigs.Length; i++)
            {
                var t = rigs[i].transform;
                Assert.AreEqual(0f, t.position.y, 0.3f, $"{t.name} on the ground");
                Assert.AreEqual(z0, t.position.z, 0.01f, $"{t.name} in the line");
                Assert.AreEqual(0f, Vector3.Angle(t.forward, rigs[0].transform.forward), 0.5f, $"{t.name} same heading");
                if (i > 0) Assert.AreEqual(rigs[1].transform.position.x - rigs[0].transform.position.x, t.position.x - rigs[i - 1].transform.position.x, 0.01f, $"{t.name} even spacing");
            }

            for (int i = 0; i < rigs.Length; i++)
            {
                var fp = Footprint(rigs[i]);
                Assert.IsFalse(fp.Overlaps(StoreRect), $"{rigs[i].name} outside the store footprint");
                Assert.IsFalse(fp.Overlaps(CanopyRect), $"{rigs[i].name} outside the pump canopy");
                Assert.IsFalse(fp.Overlaps(containerRect), $"{rigs[i].name} outside the roll-off container");
                Assert.GreaterOrEqual(fp.xMin, -25f, $"{rigs[i].name} inside the lot");
                Assert.LessOrEqual(fp.xMax, 25f);
                for (int j = i + 1; j < rigs.Length; j++)
                {
                    var o = Footprint(rigs[j]);
                    var grown = Rect.MinMaxRect(fp.xMin - 1f, fp.yMin, fp.xMax + 1f, fp.yMax);
                    Assert.IsFalse(grown.Overlaps(o), $"{rigs[i].name} and {rigs[j].name} leave at least 1 m between them to drive out");
                }
            }

            // No lot furniture (light poles, crates, pallets) stands in a machine's footprint or on its way out.
            foreach (var rig in rigs)
            {
                var fp = Footprint(rig);
                // The machine's footprint plus a drive-out lane: its own width, 8 m ahead of its origin (booms are overhead).
                var lane = Rect.MinMaxRect(fp.xMin, rig.transform.position.z, fp.xMax, rig.transform.position.z + 8f);
                var swept = Rect.MinMaxRect(Mathf.Min(fp.xMin, lane.xMin), fp.yMin, fp.xMax, Mathf.Max(fp.yMax, lane.yMax));
                for (int i = 0; i < world.pieces.Count; i++)
                {
                    var p = world.pieces[i];
                    if (p == null || p.removed) continue;
                    var b = p.shape.bounds;
                    if (b.max.y < 0.3f || b.min.y > 4f) continue; // paving or overhead
                    var pr = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                    Assert.IsFalse(swept.Overlaps(pr), $"{rig.name} footprint/road is clear of '{world.Graph.pieces[i].name}' {b}");
                }
            }

            // Every machine can drive straight out: nothing solid sits in front of it (the container and the player are clear too).
            foreach (var rig in rigs)
            {
                var fp = Footprint(rig);
                var ahead = Rect.MinMaxRect(fp.xMin, fp.yMax, fp.xMax, fp.yMax + 4f);
                Assert.IsFalse(ahead.Overlaps(StoreRect), $"{rig.name} has a free road ahead");
                Assert.IsFalse(ahead.Overlaps(containerRect));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CraneInTheLineStillReachesTheStore()
        {
            var crane = level.Crane;
            var fp = Footprint(crane);
            float dxWall = StoreRect.xMin - crane.transform.position.x;
            float dxCentre = StoreRect.center.x - crane.transform.position.x;
            // Boom reach at rest = how far the model extends ahead of the crane's origin.
            float boomLength = fp.yMax - crane.transform.position.z;
            Debug.Log($"[StoreYardTests] crane x={crane.transform.position.x:F1} wall {dxWall:F1} m, store centre {dxCentre:F1} m, model ahead of the origin {boomLength:F1} m");
            Assert.Less(dxWall, 15f, "near wall within the boom's reach");

            // Slew the boom toward the store and check the ball can be dropped over the near wall.
            var op = crane.GetComponent<CraneOperable>();
            StandAtDoor(op);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(op, player.Current);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            int hits = 0;
            int log0 = world.log.Total;
            crane.BallHit += (_, __) => hits++;
            // A full turn of the slew sweeps the ball through every bearing; record how far east of the crane it gets.
            float reachedX = float.MinValue;
            for (int i = 0; i < 600; i++)
            {
                yield return null;
                reachedX = Mathf.Max(reachedX, Vector3.Dot(crane.Ball.position - crane.transform.position, Vector3.right));
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Debug.Log($"[StoreYardTests] ball reached {reachedX:F1} m east of the crane (near wall {dxWall:F1} m, centre {dxCentre:F1} m)");
            // The swung ball either reaches the near wall (and strikes it, which also stops the slew) or travels past it.
            Assert.IsTrue(reachedX > dxWall || hits > 0 || world.log.Total > log0,
                $"ball reaches the store (x offset {reachedX:F1} vs wall {dxWall:F1}, ball hits {hits})");
        }

        [UnityTest]
        public IEnumerator ContainerStaysReachableFromTheStoreSide()
        {
            // Container x 6..22 z 6.6..11.4; the store's front is z=-10 so there is a free lane between them.
            var c = level.container.transform.position;
            Assert.Greater(c.z - level.container.interior.z * 0.5f, StoreRect.yMax + 10f, "a lane between the store and the container");
            Assert.Greater(c.z - level.container.interior.z * 0.5f, CanopyRect.yMax + 1f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerWalksToEachRigAndEntersIt()
        {
            foreach (IOperableRig rig in new IOperableRig[] { level.SkidSteer, level.Dozer, level.Excavator, level.Crane.GetComponent<CraneOperable>() })
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
        public IEnumerator ViewKeyCyclesOverheadFirstPersonNearAndFarThirdPerson()
        {
            var cam = player.cam;
            Vector3 Pivot() => player.transform.position + Vector3.up * 1.6f;
            Assert.AreEqual(CranePlayer.CameraView.Overhead, player.View);

            yield return Tap(Key.V);
            Assert.AreEqual(CranePlayer.CameraView.FirstPerson, player.View);
            Assert.IsFalse(player.overhead.enabled, "overhead camera off in first person");

            yield return Tap(Key.V);
            Assert.AreEqual(CranePlayer.CameraView.ThirdPersonNear, player.View);
            yield return null;
            float near = Vector3.Distance(cam.transform.position, Pivot());
            Assert.LessOrEqual(near, player.thirdPersonNear + 0.05f, "near view sits at most the near distance back");
            Assert.Greater(near, 0.25f, "camera is out of the head");
            Assert.Greater(Vector3.Dot(cam.transform.forward, (Pivot() - cam.transform.position).normalized), 0.99f, "camera looks at the character");
            if (player.avatar != null) Assert.IsTrue(player.avatar.activeSelf, "body visible in third person");

            // W walks away from the camera.
            Vector3 camFlat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 start = player.transform.position;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Vector3 moved = Vector3.ProjectOnPlane(player.transform.position - start, Vector3.up);
            Assert.Greater(Vector3.Dot(moved, camFlat), 1f, "W is camera-relative");

            yield return Tap(Key.V);
            Assert.AreEqual(CranePlayer.CameraView.ThirdPersonFar, player.View);
            yield return new WaitForSeconds(0.5f);
            float far = Vector3.Distance(cam.transform.position, Pivot());
            Assert.LessOrEqual(far, player.thirdPersonFar + 0.05f);
            Assert.Greater(far, near + 0.5f, "far view sits further back than the near view");

            // Operating a machine pulls the camera further out, behind the seat.
            var rig = (IOperableRig)level.SkidSteer;
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);
            yield return null;
            float inRig = Vector3.Distance(cam.transform.position, rig.SeatPosition + Vector3.up * 1.2f);
            Assert.Greater(inRig, player.thirdPersonFar * 0.5f, "camera outside the cab");
            Assert.LessOrEqual(inRig, player.thirdPersonFar * player.rigDistanceScale + 0.05f);
            yield return Tap(Key.E);
            Assert.IsNull(player.Current);

            yield return Tap(Key.V);
            Assert.AreEqual(CranePlayer.CameraView.Overhead, player.View, "cycles back to overhead");
            Assert.IsTrue(player.overhead.enabled);
        }

        [UnityTest]
        public IEnumerator SkidSteerAndDozerDriveAcrossTheLotAndTheCraneSlews()
        {
            foreach (var rig in new MonoBehaviour[] { level.SkidSteer, level.Dozer })
            {
                var op = (IOperableRig)rig;
                StandAtDoor(op);
                yield return null;
                yield return Tap(Key.E);
                Assert.AreSame(op, player.Current, op.RigName);
                Vector3 start = rig.transform.position;
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W));
                yield return new WaitForSeconds(2.5f);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return new WaitForSeconds(0.5f);
                Assert.Greater(Vector3.Distance(start, rig.transform.position), 1f, $"{op.RigName} drives");
                Assert.AreEqual(0f, rig.transform.position.y, 0.3f, $"{op.RigName} stays on the ground");
                yield return Tap(Key.E);
                Assert.IsNull(player.Current);
            }

            var crane = level.Crane.GetComponent<CraneOperable>();
            StandAtDoor(crane);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(crane, player.Current);
            float slew0 = level.Crane.SlewAngle;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            yield return new WaitForSeconds(1f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Assert.AreNotEqual(slew0, level.Crane.SlewAngle, "A/D slews the crane");
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
            Assert.AreEqual(0, level.container.FrozenPieces);

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
            int scenario = level.Controller.ScenarioIndex;
            yield return Tap(Key.N);
            yield return Tap(Key.B);
            Assert.AreEqual(scenario, level.Controller.ScenarioIndex);
            Assert.AreEqual(1, level.Controller.Scenarios.Count, "a level runs on its own scenario only");
            Assert.AreEqual("ConvenienceStore", level.Controller.Scenarios[scenario].id);
        }

        [UnityTest]
        public IEnumerator LabShortcutsAreIgnoredInACabSoRWinchesInsteadOfResetting()
        {
            var op = level.Crane.GetComponent<CraneOperable>();
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
            Assert.IsTrue(level.Controller.keysBlocked());

            float cable0 = level.Crane.CableLength;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.R)); // crane: R pays the cable in
            yield return new WaitForSeconds(0.6f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Assert.AreEqual(builds, world.BuildCount, "R did not reset the scene");
            Assert.AreEqual(cleared, Ledger.BuildingProgress, 0.02f, "gauge kept");
            Assert.AreNotEqual(cable0, level.Crane.CableLength, "R drove the winch");

            // Lab tool keys are ignored in the cab; Backspace still resets from anywhere.
            var tool = level.Controller.Tool;
            yield return Tap(Key.Digit3);
            Assert.AreEqual(tool, level.Controller.Tool, "1-4 ignored in the cab");
            yield return Tap(Key.Backspace);
            yield return new WaitForSeconds(0.3f);
            Assert.IsNull(player.Current, "reset puts the player back on foot");
            Assert.AreEqual(0f, Ledger.BuildingProgress);
        }

        [UnityTest]
        public IEnumerator MouseDestroyToolStillBreaksTheStoreWithThePlayerPresent()
        {
            var tools = level.Controller;
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
