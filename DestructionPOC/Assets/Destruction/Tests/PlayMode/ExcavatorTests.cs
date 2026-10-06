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
    /// <summary>Loads CraneTest.unity and drives the four excavators through simulated keyboard and gamepad input.</summary>
    public sealed class ExcavatorTests
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
            boot = Object.FindAnyObjectByType<CraneTestBootstrap>();
            Assert.IsNotNull(boot, "bootstrap");
            player = boot.Player;
            kb = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            pad = InputSystem.AddDevice<Gamepad>();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            yield return new WaitForSeconds(0.5f); // loose debris settles
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            foreach (var d in new InputDevice[] { kb, mouse, pad })
                if (d != null && d.added) InputSystem.RemoveDevice(d);
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(go);
            ScenarioLibrary.WarehouseModel = null;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Application.targetFrameRate = -1;
            yield return null;
            yield return null;
        }

        ExcavatorRig Rig(ExcavatorAttachment a) => boot.Excavators.First(r => r.attachment == a);

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

        IEnumerator Pad(GamepadState s)
        {
            InputSystem.QueueStateEvent(pad, s);
            yield return null;
            yield return null;
        }

        IEnumerator PadTap(GamepadButton b)
        {
            yield return Pad(new GamepadState().WithButton(b));
            yield return Pad(new GamepadState());
        }

        IEnumerator HoldKeys(float seconds, params Key[] keys)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return new WaitForSeconds(0.4f); // the machine has momentum: let it coast to a stop
        }

        IEnumerator HoldPad(float seconds, GamepadState s)
        {
            InputSystem.QueueStateEvent(pad, s);
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return new WaitForSeconds(0.4f); // coast to a stop
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

        /// <summary>Move a machine so its work point sits over a ground position (keeps its height and pose).</summary>
        static void PlaceWorkPointAt(ExcavatorRig rig, Vector3 target)
        {
            Vector3 d = target - rig.WorkPoint.position;
            d.y = 0f;
            var body = rig.GetComponent<Rigidbody>();
            rig.transform.position += d;
            body.position = rig.transform.position;
            Physics.SyncTransforms();
        }

        int PieceIndex(string name) => boot.World.Graph.pieces.FindIndex(p => p.name == name);

        Vector3 PieceCentre(string name) => boot.World.pieces[PieceIndex(name)].shape.bounds.center;

        // ------------------------------------------------------------------ setup

        [UnityTest]
        public IEnumerator YardHasFourDistinctExcavatorsTargetsAndTheCrane()
        {
            Assert.AreEqual(4, boot.Excavators.Count);
            CollectionAssert.AreEquivalent(
                new[] { ExcavatorAttachment.Crusher, ExcavatorAttachment.Shear, ExcavatorAttachment.Breaker, ExcavatorAttachment.Grapple },
                boot.Excavators.Select(r => r.attachment));
            Assert.IsNotNull(boot.Crane);
            Assert.AreEqual(5 + boot.Loaders.Count + (boot.Dozer != null ? 1 : 0), player.rigs.Count, "crane + 4 excavators + the loaders and dozer are enterable");
            foreach (var n in new[] { "Crusher wall L", "Steel beam 1", "Breaker slab 11", "Debris chunk 0" })
                Assert.GreaterOrEqual(PieceIndex(n), 0, n);
            Assert.AreEqual(ExcavatorTestSite.SteelName, boot.World.Settings.Material(boot.SteelMaterial).name);
            // Each fitted attachment exists under the wrist; the others were removed.
            foreach (var r in boot.Excavators)
            {
                Assert.IsTrue(r.Attachment.IsChildOf(r.Wrist));
                Assert.AreEqual(1, r.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("Att_") && t.parent == r.Wrist));
            }
            // Machines are well apart (clear walking paths, no overlapping swing circles).
            for (int i = 1; i < boot.Excavators.Count; i++)
                Assert.Greater(Vector3.Distance(boot.Excavators[i].transform.position, boot.Excavators[i - 1].transform.position), 15f);
            yield return null;
        }

        // ------------------------------------------------------------------ enter / exit

        [UnityTest]
        public IEnumerator KeyboardEntersOperatesAndExitsEveryExcavator()
        {
            foreach (var rig in boot.Excavators)
            {
                StandAtDoor(rig);
                yield return null;
                Assert.AreSame(rig, player.NearestRig, $"{rig.attachment}: door is the nearest");
                yield return Tap(Key.E);
                Assert.AreSame(rig, player.Current, $"{rig.attachment}: E enters");
                Assert.IsFalse(player.Input.onFoot.enabled, "on-foot actions off");
                Assert.IsTrue(player.Input.excavator.enabled);
                Assert.IsFalse(player.Input.crane.enabled, "other rig maps off");

                float boom0 = rig.BoomAngle;
                yield return HoldKeys(0.6f, Key.F); // boom down
                Assert.Less(rig.BoomAngle, boom0 - 3f, $"{rig.attachment}: F lowers the boom");

                yield return Tap(Key.E);
                Assert.IsNull(player.Current, $"{rig.attachment}: E exits");
                Assert.IsTrue(player.Input.onFoot.enabled);
                Assert.IsFalse(player.Input.excavator.enabled);
                // Safe exit: the capsule is clear of every collider.
                var cc = player.GetComponent<CharacterController>();
                Vector3 p = player.transform.position;
                var blocking = Physics.OverlapCapsule(p + Vector3.up * (cc.radius + 0.05f), p + Vector3.up * (cc.height - cc.radius),
                    cc.radius - 0.02f, ~0, QueryTriggerInteraction.Ignore).Where(c => c != cc).ToArray();
                Assert.IsEmpty(blocking, $"{rig.attachment}: exit point is unblocked");
                float held = rig.BoomAngle;
                yield return new WaitForSeconds(0.5f);
                Assert.AreEqual(held, rig.BoomAngle, 0.01f, "pose is held after exit");
            }
        }

        [UnityTest]
        public IEnumerator EnterPressDoesNotAlsoExitOrOperate()
        {
            var rig = Rig(ExcavatorAttachment.Grapple);
            StandAtDoor(rig);
            yield return null;
            // Hold E for several frames: one enter, no exit.
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.E));
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreSame(rig, player.Current);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            // A mouse button already held when climbing in must not work the tool until released.
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Tap(Key.E);
            Assert.IsNull(player.Current);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);
            float c0 = rig.Closure;
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(c0, rig.Closure, 1e-4f, "held button ignored until released");
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            yield return new WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            Assert.Greater(rig.Closure, c0 + 0.1f, "a fresh press closes the claws");
        }

        [UnityTest]
        public IEnumerator OnlyTheOccupiedRigReceivesInput()
        {
            var rig = Rig(ExcavatorAttachment.Shear);
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);
            var others = boot.Excavators.Where(r => r != rig).ToArray();
            var otherPos = others.Select(r => r.transform.position).ToArray();
            var otherBoom = others.Select(r => r.BoomAngle).ToArray();
            var cranePos = boot.Crane.transform.position;
            float craneSlew = boot.Crane.SlewAngle;
            var playerPos = player.transform.position - rig.SeatPosition;
            var p0 = rig.transform.position;
            yield return HoldKeys(1f, Key.W, Key.R, Key.A);
            Assert.Greater(Vector3.Distance(p0, rig.transform.position), 0.5f, "occupied rig drives");
            for (int i = 0; i < others.Length; i++)
            {
                Assert.AreEqual(otherPos[i], others[i].transform.position, "other excavators stay put");
                Assert.AreEqual(otherBoom[i], others[i].BoomAngle, 1e-4f);
            }
            Assert.AreEqual(cranePos, boot.Crane.transform.position, "crane stays put");
            Assert.AreEqual(craneSlew, boot.Crane.SlewAngle, 1e-4f);
            Assert.AreEqual(playerPos.magnitude, (player.transform.position - rig.SeatPosition).magnitude, 0.05f, "player rides the seat, does not walk");
        }

        // ------------------------------------------------------------------ articulation

        [UnityTest]
        public IEnumerator KeyboardReachesEveryAxis()
        {
            var rig = Rig(ExcavatorAttachment.Crusher);
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);

            float v = rig.SwingAngle;
            yield return HoldKeys(0.5f, Key.C);
            Assert.Greater(rig.SwingAngle, v + 5f, "C swings right");
            v = rig.SwingAngle;
            yield return HoldKeys(0.5f, Key.Z);
            Assert.Less(rig.SwingAngle, v - 5f, "Z swings left");

            v = rig.BoomAngle;
            yield return HoldKeys(0.5f, Key.R);
            Assert.Greater(rig.BoomAngle, v + 3f, "R raises the boom");
            v = rig.StickAngle;
            yield return HoldKeys(0.5f, Key.T);
            Assert.Greater(rig.StickAngle, v + 3f, "T extends the stick");
            v = rig.StickAngle;
            yield return HoldKeys(0.5f, Key.G);
            Assert.Less(rig.StickAngle, v - 3f, "G retracts it");
            v = rig.CurlAngle;
            yield return HoldKeys(0.5f, Key.Y);
            Assert.Greater(rig.CurlAngle, v + 5f, "Y curls in");
            v = rig.CurlAngle;
            yield return HoldKeys(0.5f, Key.H);
            Assert.Less(rig.CurlAngle, v - 5f, "H curls out");

            var p0 = rig.transform.position;
            var f0 = rig.transform.forward;
            yield return HoldKeys(1f, Key.W);
            Assert.Greater(Vector3.Dot(rig.transform.position - p0, f0), 0.5f, "W drives along the chassis forward");
            float yaw0 = rig.transform.eulerAngles.y;
            float swing0 = rig.SwingAngle;
            yield return HoldKeys(0.8f, Key.D);
            Assert.Greater(Mathf.DeltaAngle(yaw0, rig.transform.eulerAngles.y), 5f, "D turns the tracks right");
            Assert.AreEqual(swing0, rig.SwingAngle, 1e-3f, "turning the tracks does not swing the body");

            yield return HoldKeys(0.4f);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
            yield return new WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            float c = rig.Closure;
            Assert.Greater(c, 0.1f, "left mouse closes the jaws");
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            yield return new WaitForSeconds(0.2f);
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            Assert.Less(rig.Closure, c - 0.05f, "right mouse opens them");
        }

        [UnityTest]
        public IEnumerator GamepadCompleteWorkflowWithExclusiveModifier()
        {
            var rig = Rig(ExcavatorAttachment.Grapple);
            StandAtDoor(rig);
            yield return null;
            yield return PadTap(GamepadButton.West);
            Assert.AreSame(rig, player.Current, "X enters");
            Assert.IsTrue(player.Input.UsingGamepad, "hints follow the gamepad");

            // Right stick Y alone: boom only.
            float boom0 = rig.BoomAngle, stick0 = rig.StickAngle, swing0 = rig.SwingAngle, curl0 = rig.CurlAngle;
            yield return HoldPad(0.5f, new GamepadState { rightStick = new Vector2(0f, 1f) });
            Assert.Greater(rig.BoomAngle, boom0 + 3f, "right stick up raises the boom");
            Assert.AreEqual(stick0, rig.StickAngle, 1e-3f, "stick untouched without LB");

            // LB + right stick Y: stick only.
            boom0 = rig.BoomAngle;
            yield return HoldPad(0.5f, new GamepadState { rightStick = new Vector2(0f, 1f) }.WithButton(GamepadButton.LeftShoulder));
            Assert.Greater(rig.StickAngle, stick0 + 3f, "LB + right stick up extends the stick");
            Assert.AreEqual(boom0, rig.BoomAngle, 1e-3f, "boom untouched while LB is held");

            // Right stick X alone: swing only.
            curl0 = rig.CurlAngle;
            yield return HoldPad(0.5f, new GamepadState { rightStick = new Vector2(1f, 0f) });
            Assert.Greater(rig.SwingAngle, swing0 + 3f, "right stick right swings right");
            Assert.AreEqual(curl0, rig.CurlAngle, 1e-3f, "curl untouched without LB");

            // LB + right stick X: curl only (left = in).
            swing0 = rig.SwingAngle;
            yield return HoldPad(0.5f, new GamepadState { rightStick = new Vector2(-1f, 0f) }.WithButton(GamepadButton.LeftShoulder));
            Assert.Greater(rig.CurlAngle, curl0 + 3f, "LB + right stick left curls in");
            Assert.AreEqual(swing0, rig.SwingAngle, 1e-3f, "swing untouched while LB is held");

            // Small stick drift is inside the dead zone.
            boom0 = rig.BoomAngle;
            yield return HoldPad(0.4f, new GamepadState { rightStick = new Vector2(0f, 0.1f) });
            Assert.AreEqual(boom0, rig.BoomAngle, 1e-3f, "dead zone");

            // Left stick drives and turns.
            var p0 = rig.transform.position;
            yield return HoldPad(1f, new GamepadState { leftStick = new Vector2(0f, 1f) });
            Assert.Greater(Vector3.Distance(p0, rig.transform.position), 0.5f, "left stick drives");
            float yaw0 = rig.transform.eulerAngles.y;
            yield return HoldPad(0.6f, new GamepadState { leftStick = new Vector2(1f, 0f) });
            Assert.Greater(Mathf.DeltaAngle(yaw0, rig.transform.eulerAngles.y), 3f, "left stick X turns");

            // Triggers: proportional close / open.
            float c0 = rig.Closure;
            yield return HoldPad(0.3f, new GamepadState { rightTrigger = 0.5f });
            float half = rig.Closure - c0;
            Assert.Greater(half, 0.05f, "RT closes");
            c0 = rig.Closure;
            yield return HoldPad(0.3f, new GamepadState { rightTrigger = 1f });
            Assert.Greater(rig.Closure - c0, half * 1.4f, "full trigger closes faster than half");
            c0 = rig.Closure;
            yield return HoldPad(0.3f, new GamepadState { leftTrigger = 1f });
            Assert.Less(rig.Closure, c0 - 0.1f, "LT opens");

            yield return PadTap(GamepadButton.East);
            Assert.IsNull(player.Current, "B exits");

            // And walks again on the left stick.
            var w0 = player.transform.position;
            yield return HoldPad(0.5f, new GamepadState { leftStick = new Vector2(0f, 1f) });
            Assert.Greater(Vector3.Distance(w0, player.transform.position), 0.5f, "on-foot movement restored");
        }

        [UnityTest]
        public IEnumerator CraneStillEntersAndRunsOnGamepad()
        {
            var crane = player.rigs.OfType<CraneOperable>().First();
            StandAtDoor(crane);
            yield return null;
            yield return PadTap(GamepadButton.West);
            Assert.AreSame(crane, player.Current);
            Assert.IsTrue(player.Input.crane.enabled && !player.Input.excavator.enabled);
            float slew = boot.Crane.SlewAngle, boom = boot.Crane.BoomAngle, cable = boot.Crane.CableLength;
            yield return HoldPad(1f, new GamepadState { leftStick = new Vector2(1f, -1f), rightTrigger = 1f });
            Assert.Greater(boot.Crane.SlewAngle, slew + 3f, "left stick X slews");
            Assert.Less(boot.Crane.BoomAngle, boom - 1f, "left stick Y luffs");
            Assert.Greater(boot.Crane.CableLength, cable + 0.5f, "RT pays out");
            var p0 = boot.Crane.transform.position;
            yield return HoldPad(1f, new GamepadState().WithButton(GamepadButton.DpadUp));
            Assert.Greater(Vector3.Distance(p0, boot.Crane.transform.position), 0.3f, "D-pad drives");
            yield return PadTap(GamepadButton.East);
            Assert.IsNull(player.Current);
        }

        // ------------------------------------------------------------------ attachments

        [UnityTest]
        public IEnumerator CrusherCrushesConcreteInTheBite()
        {
            var rig = Rig(ExcavatorAttachment.Crusher);
            rig.SetPose(0f, 0f, 0f, 0f);
            int wall = PieceIndex("Crusher wall L");
            PlaceWorkPointAt(rig, PieceCentre("Crusher wall L"));
            yield return new WaitForFixedUpdate();
            int log0 = boot.World.log.Total;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            Assert.Greater(rig.Bites, 2, "repeated bites at the crush interval");
            Assert.LessOrEqual(rig.Bites, Mathf.CeilToInt(3f / rig.crushInterval) + 1, "not every frame");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(boot.World.pieces[wall].removed || boot.World.log.Total > log0, "the wall broke");
        }

        [UnityTest]
        public IEnumerator CrusherIgnoresEmptyAir()
        {
            var rig = Rig(ExcavatorAttachment.Crusher);
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            Assert.AreEqual(1f, rig.Closure, 1e-3f, "jaws close fully on nothing");
            Assert.AreEqual(0, rig.Bites);
        }

        [UnityTest]
        public IEnumerator ShearCutsASteelSegment()
        {
            var rig = Rig(ExcavatorAttachment.Shear);
            rig.SetPose(0f, 0f, 0f, 0f);
            int rail = PieceIndex("Steel rail 1");
            PlaceWorkPointAt(rig, PieceCentre("Steel rail 1"));
            yield return new WaitForFixedUpdate();
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            Assert.GreaterOrEqual(rig.Cuts, 1, "a cut");
            yield return new WaitForSeconds(0.3f);
            foreach (int cid in boot.World.Graph.adjacency[rail])
                Assert.AreEqual(ConnectionState.Severed, boot.World.Graph.connections[cid].state, "segment parted from its neighbours");
        }

        [UnityTest]
        public IEnumerator BreakerHammersTheSlabOnlyWhileHeldAndInContact()
        {
            var rig = Rig(ExcavatorAttachment.Breaker);
            // In the air: strikes but no hits.
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            Assert.Greater(rig.Strikes, 2, "hammer runs");
            Assert.AreEqual(0, rig.Hits, "no target, no hits");
            rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            yield return null;
            Assert.IsFalse(rig.Hammering, "release stops hammering");

            rig.SetPose(0f, 0f, 0f, 0f);
            int panel = PieceIndex("Breaker slab 11");
            PlaceWorkPointAt(rig, PieceCentre("Breaker slab 11"));
            yield return new WaitForFixedUpdate();
            int strikes0 = rig.Strikes;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            Assert.Greater(rig.Hits, 3, "repeated localised impacts");
            Assert.LessOrEqual(rig.Strikes - strikes0, Mathf.CeilToInt(3f * rig.hammerRate) + 1, "strikes at the hammer rate");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(boot.World.pieces[panel].removed, "the panel under the bit broke up");
        }

        [UnityTest]
        public IEnumerator GrappleGrabsCarriesHoldsThroughExitAndReleases()
        {
            var rig = Rig(ExcavatorAttachment.Grapple);
            int chunk = PieceIndex("Debris chunk 4"); // centre of the pile
            var body = boot.World.pieces[chunk].cluster.body;
            Assert.IsNotNull(body);
            // Lower the claws onto the chunk.
            rig.SetPose(0f, -12f, 0f, 0f);
            PlaceWorkPointAt(rig, body.worldCenterOfMass);
            for (int i = 0; i < 60 && rig.WorkPoint.position.y > body.worldCenterOfMass.y + 0.2f; i++)
            {
                rig.SetPose(0f, rig.BoomAngle - 1f, 0f, 0f);
                PlaceWorkPointAt(rig, body.worldCenterOfMass);
            }
            yield return new WaitForFixedUpdate();
            for (float t = 0f; t < 1f && !rig.Holding; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            Assert.IsTrue(rig.Holding, "closed on the chunk");
            Assert.AreSame(body, rig.HeldBody);

            float y0 = body.position.y;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f);
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(body.position.y, y0 + 0.5f, "carried up with the boom");

            // Climb in and out: the grip survives the exit.
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);
            yield return Tap(Key.E);
            Assert.IsNull(player.Current);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(rig.Holding, "still held after exit");

            for (float t = 0f; t < 1f && rig.Holding; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
                yield return null;
            }
            Assert.IsFalse(rig.Holding, "opening releases");
            yield return new WaitForSeconds(1f);
            Assert.Less(body.position.y, y0 + 0.5f, "load fell");
        }

        [UnityTest]
        public IEnumerator GrappleRefusesHeavyBlocksAndStructure()
        {
            var rig = Rig(ExcavatorAttachment.Grapple);
            var heavy = boot.World.pieces[PieceIndex("Debris heavy block")].cluster.body;
            Assert.Greater(heavy.mass, rig.maxGrabMass, "test block is over the limit");
            rig.SetPose(0f, -12f, 0f, 0f);
            for (int i = 0; i < 60 && rig.WorkPoint.position.y > heavy.worldCenterOfMass.y + 0.3f; i++)
                rig.SetPose(0f, rig.BoomAngle - 1f, 0f, 0f);
            PlaceWorkPointAt(rig, heavy.worldCenterOfMass);
            yield return new WaitForFixedUpdate();
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            Assert.IsFalse(rig.Holding, "too heavy");

            // Intact (static) concrete is not grabbable either.
            rig.SetPose(0f, 0f, 0f, 0f);
            PlaceWorkPointAt(rig, PieceCentre("Crusher wall R"));
            yield return new WaitForFixedUpdate();
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, -1f, 1f);
                yield return null;
            }
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f);
                yield return null;
            }
            Assert.IsFalse(rig.Holding, "intact structure");
        }

        // ------------------------------------------------------------------ collision

        [UnityTest]
        public IEnumerator ArmCannotBeLoweredThroughTheWall()
        {
            var rig = Rig(ExcavatorAttachment.Crusher);
            var cap = PieceCentre("Crusher cap");
            float capTop = boot.World.pieces[PieceIndex("Crusher cap")].shape.bounds.max.y;
            rig.SetPose(0f, rig.boomMax, 0f, 0f);
            PlaceWorkPointAt(rig, cap);
            yield return new WaitForFixedUpdate();
            Assert.Greater(rig.WorkPoint.position.y, capTop + 1f, "starts above the wall");
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, 0f, -1f, 0f, 0f, 0f, 0f);
                yield return null;
            }
            Assert.Greater(rig.Blocked, 0, "the wall stopped the boom");
            Assert.Greater(rig.BoomAngle, rig.boomMin + 1f, "boom stopped before its own limit");
            Assert.Greater(rig.WorkPoint.position.y, capTop - 0.6f, "tool rests on the wall instead of passing through");
        }

        [UnityTest]
        public IEnumerator TracksStallAgainstStructure()
        {
            var rig = Rig(ExcavatorAttachment.Crusher);
            var p0 = rig.transform.position;
            for (float t = 0f; t < 5f; t += Time.deltaTime)
            {
                rig.Command(1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                yield return null;
            }
            rig.Command(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            Assert.Greater(rig.Blocked, 0, "driving into the wall is refused");
            // The raised arm clears the low wall; the tracks and body stall against it (~10 m unblocked).
            float wallFace = boot.World.pieces[PieceIndex("Crusher wall L")].shape.bounds.max.z;
            Assert.Less(Vector3.Distance(p0, rig.transform.position), 6f, "stopped at the wall");
            Assert.Greater(rig.transform.position.z - wallFace, 1f, "slew axis still on the near side of the wall");
            // Backing away is always allowed.
            var p1 = rig.transform.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                rig.Command(-1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
                yield return null;
            }
            Assert.Greater(Vector3.Distance(p1, rig.transform.position), 0.5f, "reverses out");
        }

        [UnityTest]
        public IEnumerator LooseDebrisIsPushedNotBlocking()
        {
            var rig = Rig(ExcavatorAttachment.Grapple);
            var body = boot.World.pieces[PieceIndex("Debris chunk 4")].cluster.body;
            var c0 = body.position;
            rig.SetPose(0f, -10f, 0f, 0f);
            for (int i = 0; i < 60 && rig.WorkPoint.position.y > 0.6f; i++) rig.SetPose(0f, rig.BoomAngle - 1f, 0f, 0f);
            var start = body.worldCenterOfMass + rig.transform.right * 2.5f;
            PlaceWorkPointAt(rig, start);
            yield return new WaitForFixedUpdate();
            // Swing the claws through the chunk, stopping 1 m past it (short of the 4.6 t block, which would block).
            Vector3 toward = (body.worldCenterOfMass - start).normalized;
            for (float t = 0f; t < 3f && Vector3.Dot(rig.WorkPoint.position - body.worldCenterOfMass, toward) < 1f; t += Time.deltaTime)
            {
                rig.Command(0f, 0f, -1f, 0f, 0f, 0f, 0f, 0f);
                yield return null;
            }
            Assert.AreEqual(0, rig.Blocked, "loose debris never blocks the machine");
            Assert.Greater(Vector3.Distance(c0, body.position), 0.03f, "the chunk was pushed out of the claws' way");
        }

        // ------------------------------------------------------------------ reset and HUD

        [UnityTest]
        public IEnumerator ResetClearsPossessionGripPoseAndTargets()
        {
            var rig = Rig(ExcavatorAttachment.Breaker);
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);
            yield return HoldKeys(0.6f, Key.R, Key.W);
            int builds = boot.World.BuildCount;
            yield return Tap(Key.Backspace);
            Assert.IsNull(player.Current, "reset leaves the rig");
            Assert.IsTrue(player.Input.onFoot.enabled && !player.Input.excavator.enabled && !player.Input.vehicle.enabled, "input back on foot");
            Assert.AreEqual(builds + 1, boot.World.BuildCount, "targets rebuilt");
            Assert.AreEqual(rig.startBoomAngle, rig.BoomAngle, 1e-3f, "pose reset");
            Assert.AreEqual(0f, rig.Closure);
            Assert.Less(Vector3.Distance(player.transform.position, boot.playerPosition), 0.3f, "player back at the start");
            yield return new WaitForSeconds(0.2f); // let the interpolated teleport settle
            var p0 = rig.transform.position;
            yield return HoldKeys(0.5f, Key.W);
            Assert.AreEqual(p0, rig.transform.position, "no stale vehicle input after reset");
        }

        [UnityTest]
        public IEnumerator PanelShowsLiveBindingsForTheActiveDevice()
        {
            var panel = player.GetComponent<RigControlsPanel>();
            panel.Refresh();
            Assert.IsFalse(panel.Visible, "hidden on foot");

            var rig = Rig(ExcavatorAttachment.Breaker);
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            panel.Refresh();
            Assert.IsTrue(panel.Visible);
            StringAssert.Contains("Hydraulic breaker", panel.LastTitle);
            CollectionAssert.Contains(panel.lastRows, "W / S|Drive forward / back");
            CollectionAssert.Contains(panel.lastRows, "Z / C|Swing body left / right");
            CollectionAssert.Contains(panel.lastRows, "E|Exit");
            Assert.IsTrue(panel.lastRows.Any(r => r.StartsWith("LMB") && r.Contains("Run breaker")));
            Assert.IsFalse(panel.lastRows.Any(r => r.Contains("Open")), "breaker shows no jaw-open action");

            // Remapping an action changes the hint.
            player.Input.Exit.ApplyBindingOverride(0, "<Keyboard>/q");
            panel.Refresh();
            CollectionAssert.Contains(panel.lastRows, "Q|Exit");
            player.Input.Exit.RemoveAllBindingOverrides();

            // Gamepad: hints switch, and LB highlights the alternate pair.
            yield return HoldPad(0.3f, new GamepadState { leftStick = new Vector2(0f, 0.9f) });
            panel.Refresh();
            Assert.IsTrue(player.Input.UsingGamepad);
            CollectionAssert.Contains(panel.lastRows, "L-stick ↕|Drive forward / back");
            CollectionAssert.Contains(panel.lastRows, "LB + R-stick ↕|Stick out / in");
            CollectionAssert.Contains(panel.lastRows, "B / X|Exit");
            Assert.IsTrue(panel.lastRows.Any(r => r.StartsWith("RT") && r.Contains("Run breaker")));

            yield return Pad(new GamepadState().WithButton(GamepadButton.LeftShoulder));
            var hints = new System.Collections.Generic.List<ControlHint>();
            rig.ControlHints(player.Input, hints);
            Assert.IsTrue(hints.First(h => h.label == "Stick out / in").highlight, "modifier pair highlighted");
            Assert.IsTrue(hints.First(h => h.label == "Boom up / down").dim, "primary pair dimmed");
            yield return Pad(new GamepadState());

            // Crane panel lists the crane's own controls.
            yield return PadTap(GamepadButton.East);
            var crane = player.rigs.OfType<CraneOperable>().First();
            StandAtDoor(crane);
            yield return null;
            yield return Tap(Key.E);
            panel.Refresh();
            StringAssert.Contains("WRECKING CRANE", panel.LastTitle);
            CollectionAssert.Contains(panel.lastRows, "A / D|Slew left / right");
            CollectionAssert.Contains(panel.lastRows, "R / F|Ball up / down");
        }
    }
}
