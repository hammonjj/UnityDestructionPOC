using System;
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
    /// <summary>Loads CraneTest.unity and drives the wheel loader and skid steer through enter/exit, steering, and the
    /// whole scoop, carry, dump and accounting loop.</summary>
    public sealed class LoaderTests
    {
        CraneTestBootstrap boot;
        CranePlayer player;
        Keyboard kb;
        Gamepad pad;
        Mouse mouse;

        [UnitySetUp]
        public IEnumerator Load()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            yield return SceneManager.LoadSceneAsync("CraneTest");
            yield return null;
            boot = UnityEngine.Object.FindAnyObjectByType<CraneTestBootstrap>();
            Assert.IsNotNull(boot, "bootstrap");
            player = boot.Player;
            kb = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            pad = InputSystem.AddDevice<Gamepad>();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            yield return new WaitForSeconds(1.0f); // loose debris settles
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            foreach (var d in new InputDevice[] { kb, mouse, pad })
                if (d != null && d.added) InputSystem.RemoveDevice(d);
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) UnityEngine.Object.Destroy(go);
            ScenarioLibrary.WarehouseModel = null;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Application.targetFrameRate = -1;
            yield return null;
            yield return null;
        }

        LoaderRig Wheel => boot.Loaders.First(l => l.kind == LoaderKind.Wheel);
        LoaderRig Skid => boot.Loaders.First(l => l.kind == LoaderKind.Skid);

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

        /// <summary>Wait until the condition holds or the timeout passes. Returns whether it held.</summary>
        static IEnumerator Until(Func<bool> cond, float timeout, Action<bool> result = null)
        {
            float end = Time.time + timeout;
            while (Time.time < end)
            {
                if (cond())
                {
                    result?.Invoke(true);
                    yield break;
                }
                yield return null;
            }
            result?.Invoke(cond());
        }

        static IEnumerator Settle(float s) { yield return new WaitForSeconds(s); }

        float PieceMass(int i) => boot.World.Graph.mass[i];

        // ------------------------------------------------------------------ setup

        [UnityTest]
        public IEnumerator YardHasBothLoadersContainerAndRubble()
        {
            Assert.AreEqual(2, boot.Loaders.Count);
            Assert.IsNotNull(boot.Container);
            Assert.AreEqual(7 + (boot.Dozer != null ? 1 : 0), player.rigs.Count, "crane + 4 excavators + 2 loaders (+ dozer) are enterable");
            Assert.Greater(boot.Ledger.StagedMassKg, 1000f);
            Assert.AreEqual(2, boot.Ledger.OversizedPieces);
            foreach (var l in boot.Loaders)
            {
                Assert.AreEqual(4, l.WheelCount);
                Assert.Greater(l.Load.CavityVolume, 0.3f);
                Assert.IsNotNull(l.Load, "bucket load");
            }
            Assert.Greater(Wheel.Load.CavityVolume, Skid.Load.CavityVolume * 2f, "wheel bucket is much bigger");
            Assert.Greater(Wheel.tuning.capacityKg, Skid.tuning.capacityKg * 3f);
            // Container wall must clear the bucket of both machines when raised.
            Assert.Less(boot.Container.WallTop, 2.3f);
            // Machines are clear of each other and of the crane/excavator yard.
            Assert.Greater(Vector3.Distance(Wheel.transform.position, Skid.transform.position), 5f);
            Assert.AreEqual(0f, boot.Ledger.ClearedMassKg);
            yield return null;
        }

        // ------------------------------------------------------------------ enter / exit / input routing

        [UnityTest]
        public IEnumerator KeyboardEntersOperatesAndExitsEachLoader()
        {
            foreach (var rig in boot.Loaders)
            {
                StandAtDoor(rig);
                yield return null;
                Assert.AreSame(rig, player.NearestRig, $"{rig.RigName}: door is nearest");
                yield return Tap(Key.E);
                Assert.AreSame(rig, player.Current, $"{rig.RigName}: E enters");
                Assert.IsTrue(player.Input.loader.enabled);
                Assert.IsFalse(player.Input.onFoot.enabled, "on-foot actions off");
                Assert.IsFalse(player.Input.excavator.enabled);

                // Only the occupied loader receives input.
                var other = boot.Loaders.First(l => l != rig);
                var otherPos = other.transform.position;
                float lift0 = rig.LiftAngle;
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W, Key.R));
                yield return Settle(0.8f);
                Assert.Greater(rig.Speed, 0.5f, $"{rig.RigName}: W drives");
                Assert.Greater(rig.LiftAngle, lift0 + 2f, $"{rig.RigName}: R raises arms");
                Assert.AreEqual(otherPos, other.transform.position, "other loader untouched");
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return Settle(1.2f);

                // Exit: stopped, pose held, character placed clear of colliders.
                yield return Tap(Key.E);
                Assert.IsNull(player.Current, $"{rig.RigName}: E exits");
                Assert.IsTrue(rig.Parked);
                Assert.IsTrue(player.Input.onFoot.enabled);
                Assert.IsFalse(player.Input.loader.enabled);
                var cc = player.GetComponent<CharacterController>();
                Vector3 p = player.transform.position;
                var blocking = Physics.OverlapCapsule(p + Vector3.up * (cc.radius + 0.05f), p + Vector3.up * (cc.height - cc.radius),
                    cc.radius - 0.02f, ~0, QueryTriggerInteraction.Ignore).Where(c => c != cc).ToArray();
                Assert.IsEmpty(blocking, $"{rig.RigName}: exit point is unblocked");
                float held = rig.LiftAngle;
                Vector3 pos = rig.transform.position;
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W, Key.R)); // on-foot input must not leak into the parked loader
                yield return Settle(0.5f);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                Assert.AreEqual(held, rig.LiftAngle, 0.01f, "arm pose held after exit");
                Assert.AreEqual(pos, rig.transform.position, "parking hold");
                // Reposition for the next loop iteration.
                rig.ResetPose();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ControlsPanelShowsBindingsAndLoadForTheActiveDevice()
        {
            var rig = Wheel;
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            var panel = player.GetComponent<RigControlsPanel>();
            panel.Refresh();
            yield return Tap(Key.Space); // keyboard activity selects the keyboard hints
            panel.Refresh();
            Assert.IsTrue(panel.Visible);
            StringAssert.Contains("WHEEL LOADER", panel.LastTitle);
            var rows = string.Join("\n", panel.lastRows);
            foreach (var s in new[] { "W / S|Drive", "A / D|Steer", "R / F|Raise", "Z / C|Curl back", "E|Exit" })
                StringAssert.Contains(s, rows);
            StringAssert.Contains("Load 0 / 4,500 kg", rig.Telemetry);
            // Device switch: gamepad bindings, never the exit key alone.
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0f, 0.6f) });
            yield return null;
            yield return null;
            panel.Refresh();
            rows = string.Join("\n", panel.lastRows);
            StringAssert.Contains("L-stick", rows);
            StringAssert.Contains("R-stick", rows);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }

        // ------------------------------------------------------------------ steering

        [UnityTest]
        public IEnumerator WheelLoaderSteersByBendingAtThePivot()
        {
            var rig = Wheel;
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            float yaw0 = rig.transform.eulerAngles.y;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W, Key.D));
            yield return Settle(1.5f);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            yield return Settle(1.5f);
            Assert.AreEqual(rig.tuning.maxArticulation, rig.Articulation, 1.5f, "D reaches the articulation limit");
            Assert.Greater(Vector3.Angle(rig.FrontFrame.forward, rig.transform.forward), rig.tuning.maxArticulation - 2f, "front frame bends visibly");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yaw0, rig.transform.eulerAngles.y)), 3f, "machine turns");
            float rightYaw = Mathf.DeltaAngle(yaw0, rig.transform.eulerAngles.y);
            Assert.Greater(rightYaw, 0f, "D turns right (clockwise from above)");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.A));
            yield return Settle(3.5f);
            Assert.Less(rig.Articulation, -rig.tuning.maxArticulation + 1.5f);
            for (int i = 0; i < rig.WheelCount; i++) Assert.AreNotEqual(0f, rig.WheelSpinDegrees(i), $"wheel {i} rolls");
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Settle(1f);
        }

        /// <summary>A static box standing on the ground across the machine's path, <paramref name="ahead"/> m in front.</summary>
        static Transform Obstacle(Transform machine, float ahead, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Test curb";
            go.transform.localScale = size;
            go.transform.SetPositionAndRotation(machine.position + machine.forward * ahead + Vector3.up * size.y * 0.5f, machine.rotation);
            Physics.SyncTransforms();
            return go.transform;
        }

        [UnityTest]
        public IEnumerator SkidSteerRollsOverACurbButNotAWall()
        {
            // Reverse over it, away from the rubble pile ahead, with the bucket off the ground (a dragging bucket catches it).
            var rig = Skid;
            Assert.Greater(rig.Terrain.StepHeight, 0.15f, "a 15 cm curb is within the step height");
            Assert.GreaterOrEqual(rig.Terrain.SupportCount, 8, "two footprint points per wheel");
            rig.Command(0f, 0f, 1f, 0f);
            yield return Until(() => rig.LiftAngle >= 15f, 3f);
            var curb = Obstacle(rig.transform, -1.6f, new Vector3(3f, 0.15f, 0.25f));
            float Behind(Transform t) => Vector3.Dot(t.position - rig.transform.position, rig.transform.forward) * -1f;
            float peak = 0f;
            rig.Command(-1f, 0f, 0f, 0f);
            yield return Until(() =>
            {
                peak = Mathf.Max(peak, rig.Terrain.Lift);
                return Behind(curb) < -1.6f;
            }, 5f);
            rig.Command(0f, 0f, 0f, 0f);
            Assert.Less(Behind(curb), -1.6f, $"drove over the curb (blocked {rig.Blocked})");
            Assert.Greater(peak, 0.08f, "the machine rose onto the curb");
            yield return Settle(1f);
            Assert.AreEqual(0f, rig.Terrain.Lift, 0.01f, "settles back onto the ground once past");

            var wall = Obstacle(rig.transform, -1.6f, new Vector3(3f, 0.6f, 0.25f));
            rig.Command(-1f, 0f, 0f, 0f);
            yield return Settle(2f);
            rig.Command(0f, 0f, 0f, 0f);
            Assert.Greater(Behind(wall), 0.5f, $"a 60 cm wall still blocks (blocked {rig.Blocked})");
        }

        [UnityTest]
        public IEnumerator SkidSteerTurnsInPlaceByDifferentialDrive()
        {
            var rig = Skid;
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Vector3 p0 = rig.transform.position;
            float yaw0 = rig.transform.eulerAngles.y;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.D));
            yield return Settle(1.0f);
            Assert.Less(Vector3.Distance(p0, rig.transform.position), 0.35f, "spins about its own centre");
            float turned = Mathf.DeltaAngle(yaw0, rig.transform.eulerAngles.y);
            Assert.Greater(turned, 40f, "D turns right in place");
            Assert.Greater(rig.YawRate, 60f);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.A));
            yield return Settle(2.0f);
            Assert.Less(Mathf.DeltaAngle(yaw0, rig.transform.eulerAngles.y), turned - 40f, "A turns back left");
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Settle(1f);
        }

        [UnityTest]
        public IEnumerator GamepadDrivesAndWorksBothSticks()
        {
            var rig = Wheel;
            StandAtDoor(rig);
            yield return null;
            yield return PadEnter();
            Assert.AreSame(rig, player.Current, "X enters");
            float lift0 = rig.LiftAngle, tilt0 = rig.TiltAngle;
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0.4f, 1f), rightStick = new Vector2(1f, 1f) });
            yield return Settle(0.9f);
            Assert.Greater(rig.Speed, 0.5f, "left stick Y drives");
            Assert.Greater(rig.Articulation, 3f, "left stick X steers");
            Assert.Greater(rig.LiftAngle, lift0 + 2f, "right stick Y raises");
            Assert.Greater(rig.TiltAngle, tilt0 + 2f, "right stick X tips forward");
            // Drift inside the dead zone does nothing.
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0.1f, 0.1f), rightStick = new Vector2(0.1f, -0.1f) });
            yield return Settle(1.5f);
            float l = rig.LiftAngle, v = rig.Speed;
            yield return Settle(0.5f);
            Assert.AreEqual(l, rig.LiftAngle, 0.01f, "dead zone: no lift");
            Assert.AreEqual(0f, v, 0.01f, "dead zone: no drive");
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }

        IEnumerator PadEnter()
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.West));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }

        // ------------------------------------------------------------------ the cleanup loop

        /// <summary>Drive a loader straight up its lane: scoop, curl, raise, carry to the container, tip, back off.
        /// Uses direct commands (the player stays on foot, so they are not overwritten).</summary>
        IEnumerator CleanupLoop(LoaderRig rig, float pileZ, Action<float> carried, Action<float> creditedByDump = null)
        {
            // Drive into the pile with the bucket flat.
            rig.Command(1f, 0f, 0f, 0f);
            bool got = false;
            yield return Until(() => rig.Load.Count > 0 && rig.Load.MassKg >= rig.tuning.capacityKg * 0.2f, 14f, r => got = r);
            Assert.IsTrue(got, $"{rig.RigName}: scooping by driving the edge into debris collects material");
            // Keep going a little so the bucket fills, then stop and curl back.
            yield return Settle(1.0f);
            rig.Command(0f, 0f, 0f, 0f);
            yield return Settle(0.8f);
            Assert.LessOrEqual(rig.Load.MassKg, rig.tuning.capacityKg + 1f, "capacity respected");
            // Curl back (which can still scoop a little more), lift, carry: the load must stay.
            rig.Command(0f, 0f, 0f, -1f);
            yield return Until(() => rig.TiltAngle <= -15f, 4f);
            yield return Settle(0.5f);
            float loaded = rig.Load.MassKg;
            // The wheel loader's bucket can only tip so far relative to its arms, so it dumps at a moderate lift.
            float liftTarget = rig.kind == LoaderKind.Wheel ? 34f : rig.tuning.liftMaxAngle - 1f;
            rig.Command(0f, 0f, 1f, 0f);
            yield return Until(() => rig.LiftAngle >= liftTarget, 8f);
            rig.Command(0f, 0f, 0f, 0f);
            Assert.GreaterOrEqual(rig.Load.MassKg, loaded - 1f, "load kept while raising (it may still gather a little more)");
            loaded = rig.Load.MassKg;
            AssertHeldNeverCredited(rig, "carrying earns no credit");
            carried?.Invoke(loaded);
            // Drive to the container wall and hold the raised bucket over it.
            rig.Command(0.6f, 0f, 0f, 0f);
            yield return Until(() => rig.Edge.position.z > boot.Container.transform.position.z, 20f);
            rig.Command(0f, 0f, 0f, 0f);
            yield return Settle(1.0f);
            Assert.IsTrue(boot.Container.Inside(rig.Edge.position + Vector3.down * 0.3f) || rig.Edge.position.z > boot.Container.transform.position.z - boot.Container.interior.z * 0.5f,
                $"{rig.RigName}: bucket is over the container (edge at {rig.Edge.position})");
            AssertHeldNeverCredited(rig, "holding a loaded bucket above the container earns no credit");
            Assert.AreEqual(loaded, rig.Load.MassKg, 1f, "load kept over the container");
            // Chunks bulldozed ahead of the bucket may already have landed in the skip; they are counted on their own.
            float before = boot.Ledger.ClearedMassKg;
            // Tip forward: material pours, then lands in the container.
            rig.Command(0f, 0f, 0f, 1f);
            yield return Until(() => rig.Load.Count == 0, 8f);
            rig.Command(0f, 0f, 0f, 0f);
            yield return Until(() => boot.Ledger.ClearedMassKg >= before + loaded * 0.9f, 6f);
            creditedByDump?.Invoke(boot.Ledger.ClearedMassKg - before);
        }

        void AssertHeldNeverCredited(LoaderRig rig, string message)
        {
            foreach (var rb in rig.Load.HeldBodies())
            {
                var k = rb.GetComponent<RigidCluster>();
                foreach (int i in k.pieces) Assert.IsFalse(boot.Ledger.IsAccepted(i), message);
            }
        }

        IEnumerator FullLoop(LoaderRig rig, float pileZ)
        {
            float loaded = 0f, credited = 0f;
            yield return CleanupLoop(rig, pileZ, m => loaded = m, c => credited = c);
            Assert.AreEqual(0, rig.Load.Count, "bucket emptied");
            Assert.GreaterOrEqual(credited, loaded * 0.9f, $"{rig.RigName}: the dumped mass is credited");
            Assert.LessOrEqual(credited, loaded * 1.15f + 1f, $"{rig.RigName}: no more than was dumped (plus nothing duplicated)");
            // Conservation: credit is exactly the mass of the accepted pieces, each counted once.
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < boot.World.pieces.Count; i++)
                if (boot.Ledger.IsAccepted(i))
                {
                    sum += PieceMass(i);
                    n++;
                }
            Assert.AreEqual(boot.Ledger.ClearedMassKg, sum, 0.5f, "credit = accepted piece mass");
            Assert.AreEqual(n, boot.Ledger.ClearedPieces);
            // More time in the container must not add credit.
            yield return Settle(3.0f); // last chunks still sliding in
            float cleared = boot.Ledger.ClearedMassKg;
            yield return Settle(2.0f);
            Assert.AreEqual(cleared, boot.Ledger.ClearedMassKg, 0.01f, "credit awarded once");
            // Accepted debris is physically inside the container, and visible.
            int inside = 0, accepted = 0;
            for (int i = 0; i < boot.World.pieces.Count; i++)
            {
                if (!boot.Ledger.IsAccepted(i)) continue;
                accepted++;
                if (boot.Container.Inside(boot.World.pieces[i].transform.position)) inside++;
            }
            Assert.AreEqual(accepted, inside, "accepted pieces sit in the container");
            Assert.Greater(boot.Container.FrozenPieces, 0, "the pile settles in place");
        }

        [UnityTest]
        public IEnumerator WheelLoaderScoopsCarriesAndDumpsIntoTheContainer()
        {
            yield return FullLoop(Wheel, boot.loaderSite.wheelPile.z);
            Assert.AreEqual(0, Wheel.Blocked > 1000 ? 1 : 0, "no runaway blocking");
        }

        [UnityTest]
        public IEnumerator SkidSteerScoopsCarriesAndDumpsIntoTheContainer()
        {
            yield return FullLoop(Skid, boot.loaderSite.skidPile.z);
        }

        [UnityTest]
        public IEnumerator OversizedChunksAreNeverTaken()
        {
            var rig = Wheel;
            int big = boot.World.Graph.pieces.FindIndex(p => p.name == $"{LoaderTestSite.DebrisPrefix} oversized wheel");
            Assert.GreaterOrEqual(big, 0);
            var body = boot.World.pieces[big].cluster.body;
            Assert.Greater(body.mass, rig.tuning.maxPieceMassKg);
            // Park the bucket cavity right on the chunk with the bucket moving: still refused.
            var t = rig.transform;
            Vector3 d = body.position - rig.Load.CavityPosition;
            d.y = 0f;
            t.position += d;
            rig.GetComponent<Rigidbody>().position = t.position;
            Physics.SyncTransforms();
            rig.Command(0.3f, 0f, 0f, 0f);
            yield return Settle(1.0f);
            rig.Command(0f, 0f, 0f, 0f);
            Assert.IsFalse(rig.Load.Holds(body), "oversized chunk is not in the bucket");
            Assert.IsFalse(body.isKinematic);
        }

        [UnityTest]
        public IEnumerator SkidSteerTakesALongPieceLyingAcrossTheBucket()
        {
            var rig = Skid;
            int curb = boot.World.Graph.pieces.FindIndex(p => p.name == $"{LoaderTestSite.DebrisPrefix} long skid");
            Assert.GreaterOrEqual(curb, 0);
            var piece = boot.World.pieces[curb];
            var body = piece.cluster.body;
            Assert.Greater(1.5f, rig.tuning.maxPieceSize, "longer than the piece limit");
            // Park the bucket cavity on the curb, which lies front to back, with the bucket moving.
            var t = rig.transform;
            Vector3 d = body.worldCenterOfMass - rig.Load.CavityPosition;
            d.y = 0f;
            t.position += d;
            rig.GetComponent<Rigidbody>().position = t.position;
            Physics.SyncTransforms();
            rig.Command(0.3f, 0f, 0f, 0f);
            yield return Until(() => rig.Load.Holds(body), 2f);
            rig.Command(0f, 0f, 0f, 0f);
            Assert.IsTrue(rig.Load.Holds(body), "long piece is taken");
            yield return Settle(0.6f);
            Vector3 along = piece.transform.TransformDirection(Vector3.forward);
            Assert.Greater(Mathf.Abs(Vector3.Dot(along, rig.Load.CavityRotation * Vector3.right)), 0.9f, "it lies across the bucket");
        }

        [UnityTest]
        public IEnumerator LooseChunkDoesNotFallThroughAMovingBucket()
        {
            // A chunk the bucket will not take (too heavy) stays a physics body resting on the floor. Working the bucket
            // hard at a low frame rate must not push it out through the plates.
            Application.targetFrameRate = 20;
            var rig = Skid;
            rig.Command(0f, 0f, 1f, 0f);
            yield return Until(() => rig.LiftAngle >= 25f, 5f);
            rig.Command(0f, 0f, 0f, 0f);
            yield return null;
            var load = rig.Load;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Heavy curb";
            go.transform.localScale = new Vector3(1.5f, 0.2f, 0.3f);
            go.transform.SetPositionAndRotation(load.CavityPosition + load.CavityRotation * new Vector3(0f, -load.Half.y + 0.12f, 0f), load.CavityRotation);
            var body = go.AddComponent<Rigidbody>();
            body.mass = rig.tuning.maxPieceMassKg + 100f;
            yield return Settle(0.8f);

            Vector3 Local() => Quaternion.Inverse(load.CavityRotation) * (body.position - load.CavityPosition);
            Vector3 settled = Local();
            float lowest = float.MaxValue;
            string lost = "never";
            void Track(string phase)
            {
                Vector3 l = Local();
                lowest = Mathf.Min(lowest, l.y);
                if (lost == "never" && l.y < -load.Half.y - 0.1f)
                    lost = $"{phase} at lift {rig.LiftAngle:0}°, tilt {rig.BucketLocalAngle:0}°, local {l:F2}, dt {Time.deltaTime:0.000}";
            }
            // Curl back and lift together, then drop the arms, at full rate.
            rig.Command(0f, 0f, 1f, -1f);
            for (float end = Time.time + 1.5f; Time.time < end;) { yield return null; Track("lifting"); }
            rig.Command(0f, 0f, -1f, 0f);
            for (float end = Time.time + 1.5f; Time.time < end;) { yield return null; Track("lowering"); }
            rig.Command(0f, 0f, 0f, 0f);
            Assert.IsFalse(load.Holds(body), "not captured: physics alone keeps it in");
            Assert.Greater(lowest, -load.Half.y - 0.1f, $"chunk stayed on the floor (settled at {settled:F2}, floor at {-load.Half.y:0.00}; lost {lost})");
        }

        [UnityTest]
        public IEnumerator FragmentsFromTheDestructionSystemAreCollectedWithoutExtraCredit()
        {
            // Shatter a pile piece through the world's own path and scoop the fragments.
            var rig = Skid;
            int before = boot.World.pieces.Count;
            int target = boot.World.Graph.pieces.FindIndex(p => p.name.StartsWith($"{LoaderTestSite.DebrisPrefix} skid pile") && p.size.x > 0.3f);
            Assert.GreaterOrEqual(target, 0);
            float mass = boot.World.Graph.mass[target];
            boot.World.Damage(target, 100f);
            yield return Settle(1.5f);
            int shattered = boot.World.pieces.Count - before;
            Assert.Greater(shattered, 1, "piece shattered into fragments (or failed to; adjust damage)");
            rig.Command(1f, 0f, 0f, 0f);
            bool got = false;
            yield return Until(() => rig.Load.Count >= 3, 14f, r => got = r);
            Assert.IsTrue(got, "fragments are collected like any debris");
            rig.Command(0f, 0f, 0f, 0f);
            Assert.AreEqual(0f, boot.Ledger.ClearedMassKg);
        }

        [UnityTest]
        public IEnumerator SpillsWhenTippedAndCapacityLimitsTheLoad()
        {
            var rig = Skid;
            rig.Command(1f, 0f, 0f, 0f);
            yield return Until(() => rig.Load.Count >= 4, 14f);
            yield return Settle(1.2f); // keep pushing into the pile: capacity must hold
            rig.Command(0f, 0f, 0f, 0f);
            Assert.LessOrEqual(rig.Load.MassKg, rig.tuning.capacityKg + 1f);
            int held = rig.Load.Count;
            Assert.Greater(held, 0);
            // Raise the arms first: tipping the bucket on the ground would dig the edge in, which is blocked.
            rig.Command(0f, 0f, 1f, 0f);
            yield return Until(() => rig.LiftAngle >= 30f, 5f);
            rig.Command(0f, 0f, 0f, 1f);
            yield return Until(() => rig.Load.Spilling, 5f);
            Assert.IsTrue(rig.Load.Spilling, "tipping past the dump angle pours");
            yield return Until(() => rig.Load.Count == 0, 6f);
            rig.Command(0f, 0f, 0f, 0f);
            Assert.AreEqual(held, rig.Load.Released, "every held piece came out, none lost");
        }

        // ------------------------------------------------------------------ reset

        [UnityTest]
        public IEnumerator ResetRestoresMachinesDebrisAndAccounting()
        {
            var rig = Skid;
            int pieces0 = boot.World.pieces.Count;
            var pos0 = rig.transform.position;
            rig.Command(1f, 0f, 0f, 0f);
            yield return Until(() => rig.Load.Count >= 2, 14f);
            rig.Command(0f, 0f, 0f, 0f);
            Assert.Greater(rig.Load.Count, 0);
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Backspace));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Settle(0.5f);
            Assert.IsNull(player.Current, "reset returns to on foot");
            Assert.IsTrue(player.Input.onFoot.enabled);
            Assert.AreEqual(0, rig.Load.Count);
            Assert.AreEqual(0f, rig.Load.MassKg);
            Assert.AreEqual(0f, boot.Ledger.ClearedMassKg);
            Assert.AreEqual(0, boot.Ledger.ClearedPieces);
            Assert.AreEqual(pos0, rig.transform.position);
            Assert.AreEqual(0f, rig.LiftAngle);
            Assert.AreEqual(pieces0, boot.World.pieces.Count, "no duplicate or orphaned debris");
            Assert.IsFalse(boot.Ledger.IsHeld(boot.World.pieces.First(p => p.cluster != null && !p.cluster.isStatic).cluster.body));
            yield return null;
        }
    }
}
