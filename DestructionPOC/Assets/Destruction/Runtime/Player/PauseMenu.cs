using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// Player.unity's pause menu: Esc in a level stops time and offers Resume, Restart level, Quit to title, and the
    /// <see cref="GameSettings"/> (volume, look sensitivity, camera shake). Players and the level's shortcuts ignore
    /// input while it is open. Drawn with IMGUI over every viewport.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        public static bool IsPaused { get; private set; }

        float resumeTimeScale = 1f;
        CursorLockMode resumeLock;
        bool resumeVisible;
        GUIStyle title, label, button;
        Texture2D shade;
        int styleHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsPaused = false;
            AudioListener.pause = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (IsPaused)
            {
                Sfx.PlayUI("ui_back");
                Resume();
            }
            else if (GameLevel.Current != null && (SceneDirector.Instance == null || !SceneDirector.Instance.Busy)) Pause();
        }

        public void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            resumeTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            resumeLock = Cursor.lockState;
            resumeVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // The world goes quiet while paused; menu sounds ignore the listener pause.
            AudioListener.pause = true;
            Sfx.PlayUI("ui_panel_open");
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            AudioListener.pause = false;
            Time.timeScale = resumeTimeScale;
            Cursor.lockState = resumeLock;
            Cursor.visible = resumeVisible;
        }

        public void RestartLevel()
        {
            Resume();
            if (SceneDirector.Instance != null) SceneDirector.Instance.ReloadCurrent();
            else if (GameLevel.Current != null) GameLevel.Current.ResetAll();
        }

        public void QuitToTitle()
        {
            Resume();
            if (SceneDirector.Instance != null) SceneDirector.Instance.LoadTitle();
        }

        void OnDisable()
        {
            if (IsPaused) Resume();
        }

        void OnDestroy()
        {
            if (shade != null) Destroy(shade);
        }

        void OnGUI()
        {
            if (!IsPaused) return;
            EnsureStyles();
            GUI.depth = -500;
            float s = Mathf.Max(0.5f, Screen.height / 1080f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), shade);

            float w = 460f * s, h = 60f * s, gap = 14f * s;
            float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.2f;
            GUI.Label(new Rect(0f, y, Screen.width, 90f * s), "PAUSED", title);
            y += 110f * s;

            if (GUI.Button(new Rect(x, y, w, h), "RESUME", button))
            {
                Sfx.PlayUI("ui_back");
                Resume();
            }
            y += h + gap;
            if (GUI.Button(new Rect(x, y, w, h), "RESTART LEVEL", button))
            {
                Sfx.PlayUI("ui_select");
                RestartLevel();
            }
            y += h + gap;
            bool canQuit = SceneDirector.Instance != null;
            GUI.enabled = canQuit;
            if (GUI.Button(new Rect(x, y, w, h), "QUIT TO TITLE", button))
            {
                Sfx.PlayUI("ui_select");
                QuitToTitle();
            }
            GUI.enabled = true;
            y += h + gap * 3f;

            var pm = PlayerManager.Instance;
            if (pm == null) return;
            var st = pm.Settings;
            bool changed = false;
            changed |= Slider(ref y, x, w, s, "Volume", ref st.masterVolume, 0f, 1f, $"{st.masterVolume * 100f:0}%");
            changed |= Slider(ref y, x, w, s, "Mouse look", ref st.mouseLookSensitivity, 0.02f, 0.5f, $"{st.mouseLookSensitivity:0.00}");
            changed |= Slider(ref y, x, w, s, "Stick look", ref st.stickLookSpeed, 40f, 400f, $"{st.stickLookSpeed:0}°/s");
            bool shake = GUI.Toggle(new Rect(x, y, w, 30f * s), st.cameraShake, "  Camera shake", label);
            if (shake != st.cameraShake)
            {
                st.cameraShake = shake;
                changed = true;
            }
            if (changed) pm.ApplySettings();
            GUI.Label(new Rect(0f, Screen.height - 56f * s, Screen.width, 40f * s), "Esc to resume", label);
        }

        bool Slider(ref float y, float x, float w, float s, string name, ref float value, float min, float max, string readout)
        {
            GUI.Label(new Rect(x, y, w * 0.4f, 30f * s), name, label);
            GUI.Label(new Rect(x + w * 0.8f, y, w * 0.2f, 30f * s), readout, label);
            float v = GUI.HorizontalSlider(new Rect(x + w * 0.4f, y + 10f * s, w * 0.38f, 20f * s), value, min, max);
            y += 40f * s;
            if (Mathf.Approximately(v, value)) return false;
            value = v;
            return true;
        }

        void EnsureStyles()
        {
            if (shade == null)
            {
                shade = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                shade.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.78f));
                shade.Apply();
            }
            if (title != null && styleHeight == Screen.height) return;
            styleHeight = Screen.height;
            float s = Mathf.Max(0.5f, Screen.height / 1080f);
            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(72f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(0.98f, 0.8f, 0.2f);
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22f * s), alignment = TextAnchor.MiddleLeft };
            label.normal.textColor = new Color(0.86f, 0.88f, 0.9f);
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(26f * s), fontStyle = FontStyle.Bold };
        }
    }
}
