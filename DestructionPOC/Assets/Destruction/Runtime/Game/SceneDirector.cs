using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DestructionLab
{
    /// <summary>
    /// Lives in Bootstrap.unity (build index 0) and owns every high-level scene transition. The game is a stack of
    /// additive scenes that never use DontDestroyOnLoad:
    ///
    ///     Bootstrap   this director; loaded first and never unloaded
    ///     Player      players, input devices, settings, pause menu (see <see cref="PlayerManager"/>); never unloaded
    ///     content     exactly one of Title or a level scene; swapped by <see cref="LoadTitle"/> / <see cref="LoadLevel"/>
    ///
    /// Pressing Play on a level or the title scene in the editor still works: <see cref="EnsureLoaded"/> (called by
    /// <see cref="GameLevel"/> and <see cref="TitleMenu"/>) adds Bootstrap underneath, and the director adopts the scene
    /// that is already open as the current content instead of loading the title.
    /// </summary>
    public sealed class SceneDirector : MonoBehaviour
    {
        public const string BootstrapScene = "Bootstrap";

        [Tooltip("Scene with the player manager, loaded once alongside Bootstrap.")]
        public string playerScene = "Player";
        [Tooltip("Scene shown at startup and after Quit to Title.")]
        public string titleScene = "Title";
        [Tooltip("Fade to black and back over this many seconds (unscaled) around each transition.")]
        [Min(0f)] public float fadeSeconds = 0.25f;

        public static SceneDirector Instance { get; private set; }

        /// <summary>The loaded content scene (title or level), or empty while none is.</summary>
        public string CurrentScene { get; private set; } = "";
        /// <summary>True while a transition is running; further requests are ignored until it ends.</summary>
        public bool Busy { get; private set; }
        /// <summary>Player scene loaded and a content scene settled.</summary>
        public bool Ready { get; private set; }

        static bool bootstrapRequested;
        float fade;
        Texture2D black;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            bootstrapRequested = false;
        }

        /// <summary>Add Bootstrap underneath the open scene unless the director already runs. Safe to call repeatedly.</summary>
        public static void EnsureLoaded()
        {
            if (Instance != null || bootstrapRequested || SceneManager.GetSceneByName(BootstrapScene).isLoaded) return;
            if (!Application.CanStreamedLevelBeLoaded(BootstrapScene))
            {
                Debug.LogWarning($"[DestructionLab] '{BootstrapScene}' is not in the build settings; running this scene on its own.");
                return;
            }
            bootstrapRequested = true;
            SceneManager.LoadSceneAsync(BootstrapScene, LoadSceneMode.Additive);
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            bootstrapRequested = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (black != null) Destroy(black);
        }

        IEnumerator Start()
        {
            Busy = true;
            if (!SceneManager.GetSceneByName(playerScene).isLoaded)
                yield return SceneManager.LoadSceneAsync(playerScene, LoadSceneMode.Additive);

            var open = FindContentScene();
            if (open.IsValid())
            {
                CurrentScene = open.name;
                SceneManager.SetActiveScene(open);
            }
            else
            {
                yield return SceneManager.LoadSceneAsync(titleScene, LoadSceneMode.Additive);
                CurrentScene = titleScene;
                SceneManager.SetActiveScene(SceneManager.GetSceneByName(titleScene));
            }
            Busy = false;
            Ready = true;
        }

        /// <summary>Any loaded scene that is neither Bootstrap nor Player.</summary>
        Scene FindContentScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name != BootstrapScene && s.name != playerScene) return s;
            }
            return default;
        }

        public void LoadTitle() => Go(titleScene);

        public void LoadLevel(string sceneName) => Go(sceneName);

        /// <summary>Unload and load the current content scene again (Restart level).</summary>
        public void ReloadCurrent()
        {
            if (!string.IsNullOrEmpty(CurrentScene)) Go(CurrentScene);
        }

        void Go(string sceneName)
        {
            if (Busy) return;
            StartCoroutine(Transition(sceneName));
        }

        IEnumerator Transition(string next)
        {
            Busy = true;
            yield return Fade(1f);

            Time.timeScale = 1f;
            if (PlayerManager.Instance != null) PlayerManager.Instance.DespawnPlayers();
            var current = SceneManager.GetSceneByName(CurrentScene);
            CurrentScene = "";
            if (current.IsValid() && current.isLoaded) yield return SceneManager.UnloadSceneAsync(current);

            yield return SceneManager.LoadSceneAsync(next, LoadSceneMode.Additive);
            var loaded = SceneManager.GetSceneByName(next);
            SceneManager.SetActiveScene(loaded);
            CurrentScene = next;

            yield return Fade(0f);
            Busy = false;
        }

        IEnumerator Fade(float target)
        {
            if (fadeSeconds <= 0f)
            {
                fade = target;
                yield break;
            }
            while (!Mathf.Approximately(fade, target))
            {
                fade = Mathf.MoveTowards(fade, target, Time.unscaledDeltaTime / fadeSeconds);
                yield return null;
            }
        }

        void OnGUI()
        {
            if (fade <= 0f) return;
            if (black == null)
            {
                black = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                black.SetPixel(0, 0, Color.black);
                black.Apply();
            }
            GUI.depth = -1000;
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), black);
            GUI.color = prev;
        }
    }
}
