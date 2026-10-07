using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>The additive scene stack: Bootstrap starts the game, the Player scene stays loaded across content
    /// scenes, levels come from their scene files, and the pause menu stops time.</summary>
    public sealed class SceneFlowTests
    {
        Keyboard kb;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            GameSession.Clear();
            kb = InputSystem.AddDevice<Keyboard>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (kb != null && kb.added) InputSystem.RemoveDevice(kb);
            yield return GameScenes.UnloadAll();
            GameSession.Clear();
        }

        static bool Loaded(string scene) => SceneManager.GetSceneByName(scene).isLoaded;

        IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            yield return null;
        }

        static IEnumerator WaitFor(System.Func<bool> done, string what, float timeout = 15f)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!done())
            {
                if (Time.realtimeSinceStartup > until) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator BootstrapLoadsThePlayerSceneAndTheTitle()
        {
            yield return SceneManager.LoadSceneAsync(SceneDirector.BootstrapScene);
            yield return WaitFor(() => SceneDirector.Instance != null && SceneDirector.Instance.Ready, "the director");
            Assert.IsTrue(Loaded("Player"), "Player scene loaded");
            Assert.IsTrue(Loaded("Title"), "Title loaded");
            Assert.AreEqual("Title", SceneDirector.Instance.CurrentScene);
            Assert.AreEqual("Title", SceneManager.GetActiveScene().name, "the content scene is the active one");
            Assert.IsNotNull(PlayerManager.Instance);
            Assert.AreEqual(0, PlayerManager.Instance.Players.Count, "nobody plays on the title screen");
        }

        [UnityTest]
        public IEnumerator BeginSwapsTheTitleForTheLevelAndQuitSwapsBack()
        {
            yield return GameScenes.LoadTitle();
            Assert.IsTrue(GameSession.JoinKeyboard());
            Object.FindAnyObjectByType<TitleMenu>().BeginGame();
            yield return WaitFor(() => GameLevel.Current != null && PlayerManager.Instance.Players.Count == 1 && !SceneDirector.Instance.Busy, "the level");
            Assert.IsFalse(Loaded("Title"), "the title unloads");
            Assert.IsTrue(Loaded("Bootstrap") && Loaded("Player"), "Bootstrap and Player stay");
            Assert.AreEqual("ConvenienceStore", SceneDirector.Instance.CurrentScene);
            var player = PlayerManager.Instance.Player;
            Assert.AreEqual("Player", player.gameObject.scene.name, "players live in the Player scene, not the level");

            SceneDirector.Instance.LoadTitle();
            yield return WaitFor(() => Loaded("Title") && !SceneDirector.Instance.Busy, "the title");
            Assert.IsFalse(Loaded("ConvenienceStore"));
            Assert.AreEqual(0, PlayerManager.Instance.Players.Count, "players are removed with the level");
            Assert.IsTrue(player == null, "the player object is destroyed");
            Assert.AreEqual(0, CranePlayer.All.Count);
        }

        [UnityTest]
        public IEnumerator TheLevelIsBuiltFromWhatIsAuthoredInTheScene()
        {
            yield return GameScenes.LoadLevel("ConvenienceStore");
            var level = GameLevel.Current;
            Assert.AreEqual(1, level.Structures.Count, "the store is a DestructibleStructure in the scene");
            var store = level.Structures[0];
            Assert.IsFalse(store.gameObject.activeSelf, "the authored blueprint is hidden at runtime");
            Assert.Greater(level.World.Graph.PieceCount, 50, "the world built the store's pieces");
            Assert.AreEqual("ConvenienceStore", level.Crane.gameObject.scene.name, "machines are scene objects of the level");
            Assert.AreEqual(4, level.Rigs.Count, "crane, skid steer, dozer, excavator");
            Assert.AreEqual(2, level.Spawns.Count);
            Vector3 spawn = level.Spawns[0].transform.position;
            Vector3 p = PlayerManager.Instance.Player.transform.position;
            Assert.Less(new Vector2(p.x - spawn.x, p.z - spawn.z).magnitude, 0.5f, "player 1 starts at spawn 1");
        }

        [UnityTest]
        public IEnumerator MovingAnAuthoredPieceMovesItInTheWorld()
        {
            yield return GameScenes.LoadLevel("ConvenienceStore");
            var level = GameLevel.Current;
            var store = level.Structures[0];
            // Lift the whole blueprint 5 m and rebuild: the live pieces follow what is in the scene.
            float before = MinPieceY(level.World);
            store.transform.position += Vector3.up * 5f;
            level.ResetAll();
            yield return null;
            Assert.AreEqual(before + 5f, MinPieceY(level.World), 0.01f);
        }

        static float MinPieceY(DestructionWorld world)
        {
            float y = float.MaxValue;
            foreach (var d in world.Graph.pieces) y = Mathf.Min(y, d.center.y - d.size.y * 0.5f);
            return y;
        }

        [UnityTest]
        public IEnumerator EscPausesAndResumes()
        {
            yield return GameScenes.LoadLevel("ConvenienceStore");
            yield return Tap(Key.Escape);
            Assert.IsTrue(PauseMenu.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);
            yield return Tap(Key.Escape);
            Assert.IsFalse(PauseMenu.IsPaused);
            Assert.AreEqual(1f, Time.timeScale);
        }
    }
}
