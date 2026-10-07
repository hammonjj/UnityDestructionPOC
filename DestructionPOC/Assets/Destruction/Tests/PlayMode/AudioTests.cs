using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>Loads CraneTest.unity and checks the sound wiring: clip banks, engines that start and stop with the
    /// operator, levels that follow the controls, and break events that carry what the destruction sounds need.</summary>
    public sealed class AudioTests
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
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (kb != null && kb.added) InputSystem.RemoveDevice(kb);
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(go);
            ScenarioLibrary.WarehouseModel = null;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.FixedUpdate;
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

        [Test]
        public void ClipBanksLoadFromResources()
        {
            foreach (var bank in new[]
                     {
                         "engine_heavy_start", "engine_heavy_idle_loop", "engine_heavy_load_loop", "engine_heavy_stop",
                         "engine_light_idle_loop", "tracks_clank_loop", "hydraulic_move_loop", "crane_winch_loop",
                         "concrete_hit", "chunk_land", "debris_small", "collapse_medium", "collapse_large",
                         "wood_snap", "metal_shear_tear_short", "footstep_gravel", "ui_select",
                     })
                Assert.IsTrue(Sfx.Has(bank), bank);
            Assert.IsFalse(Sfx.Has("amb_construction_site_loop"), "no ambience beds");
            var seen = new HashSet<AudioClip>();
            for (int i = 0; i < 20; i++) seen.Add(Sfx.Clip("concrete_hit"));
            Assert.Greater(seen.Count, 1, "variants of a bank are mixed");
        }

        [UnityTest]
        public IEnumerator EveryMachineStartsWithItsOperatorAndStopsWhenTheyLeave()
        {
            foreach (var c in player.rigs.ToList())
            {
                var rig = (IOperableRig)c;
                Assert.IsNotNull(c.GetComponent<IMachineSound>(), $"{rig.RigName} reports its sound levels");
                StandAtDoor(rig);
                yield return null;
                yield return Tap(Key.E);
                Assert.AreSame(rig, player.Current, $"{rig.RigName}: E enters");
                var audio = c.GetComponent<MachineAudio>();
                Assert.IsNotNull(audio, $"{rig.RigName}: has machine audio");
                Assert.IsTrue(audio.EngineRunning, $"{rig.RigName}: engine starts");
                var loops = c.GetComponents<AudioSource>().Where(s => s.loop && s.clip != null).Select(s => s.clip.name).ToList();
                Assert.IsTrue(loops.Any(n => n.Contains("idle_loop")), $"{rig.RigName}: idle loop ({string.Join(", ", loops)})");
                yield return new WaitForSeconds(3f);
                Assert.Greater(audio.EngineGain, 0.5f, $"{rig.RigName}: engine comes up after the starter");

                yield return Tap(Key.E);
                Assert.IsNull(player.Current, $"{rig.RigName}: E leaves");
                Assert.IsFalse(audio.EngineRunning, $"{rig.RigName}: engine stops");
                yield return new WaitForSeconds(0.5f);
                Assert.Less(audio.EngineGain, 0.05f, $"{rig.RigName}: engine falls silent");
            }
        }

        [UnityTest]
        public IEnumerator DrivingRaisesTheDriveLevel()
        {
            var rig = boot.Loaders.First(l => l.kind == LoaderKind.Skid);
            Assert.IsTrue(rig.SoundProfile.lightEngine, "skid steer has the small engine");
            Assert.IsFalse(boot.Loaders.First(l => l.kind == LoaderKind.Wheel).SoundProfile.lightEngine);
            StandAtDoor(rig);
            yield return null;
            yield return Tap(Key.E);
            Assert.AreSame(rig, player.Current);
            Assert.Less(rig.DriveActivity, 0.05f, "parked");
            yield return Keys(Key.W);
            yield return new WaitForSeconds(0.8f);
            Assert.Greater(rig.DriveActivity, 0.3f, "driving");
            yield return Keys();
            yield return Tap(Key.E);
        }

        [UnityTest]
        public IEnumerator BreaksCarryWhereTheyHappenedAndTheMaterial()
        {
            var world = boot.World;
            Assert.IsNotNull(world.GetComponent<DestructionAudio>(), "world has destruction audio");
            var events = new List<BreakEvent>();
            world.OnBreak += events.Add;
            int target = -1;
            for (int i = 0; i < world.Graph.pieces.Count && target < 0; i++)
                if (!world.pieces[i].removed && world.pieces[i].cluster != null && world.pieces[i].cluster.isStatic &&
                    world.Graph.adjacency[i].Count > 0)
                    target = i;
            Assert.GreaterOrEqual(target, 0, "a structural piece");
            world.Damage(target, 1f);
            yield return new WaitForSeconds(0.5f);
            world.OnBreak -= events.Add;
            Assert.IsNotEmpty(events, "damage breaks joints");
            var e = events[0];
            Assert.AreNotEqual(Vector3.zero, e.point, "break position");
            Assert.Greater(e.mass, 0f, "piece mass");
            Assert.GreaterOrEqual(e.material, 0, "material index");
        }
    }
}
