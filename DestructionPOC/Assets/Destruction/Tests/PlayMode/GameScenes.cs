using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DestructionLab.Tests
{
    /// <summary>Loads and clears the game's additive scene stack (Bootstrap + Player + one content scene) for tests.</summary>
    public static class GameScenes
    {
        /// <summary>Open a level the way Play in the editor does: the level loads, pulls in Bootstrap, which loads Player,
        /// which spawns the players. Waits until they exist.</summary>
        public static IEnumerator LoadLevel(string scene, float timeout = 15f)
        {
            yield return SceneManager.LoadSceneAsync(scene);
            float until = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < until)
            {
                if (SceneDirector.Instance != null && SceneDirector.Instance.Ready && !SceneDirector.Instance.Busy &&
                    PlayerManager.Instance != null && PlayerManager.Instance.Players.Count > 0)
                    yield break;
                yield return null;
            }
            Assert.Fail($"{scene} did not finish loading the Bootstrap and Player scenes and spawning players within {timeout} s");
        }

        /// <summary>Open the title scene (with Bootstrap and Player underneath) and wait for the director to settle.</summary>
        public static IEnumerator LoadTitle(float timeout = 15f)
        {
            yield return SceneManager.LoadSceneAsync("Title");
            float until = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < until)
            {
                if (SceneDirector.Instance != null && SceneDirector.Instance.Ready && !SceneDirector.Instance.Busy) yield break;
                yield return null;
            }
            Assert.Fail($"Title did not finish loading within {timeout} s");
        }

        /// <summary>Unload every scene, leaving one empty one, so nothing from the game stack leaks into the next test.</summary>
        public static IEnumerator UnloadAll()
        {
            var empty = SceneManager.CreateScene("Empty " + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s != empty && s.isLoaded) yield return SceneManager.UnloadSceneAsync(s);
            }
            Time.timeScale = 1f;
        }
    }
}
