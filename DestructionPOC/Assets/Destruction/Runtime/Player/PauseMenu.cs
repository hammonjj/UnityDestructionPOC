using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// Player.unity's pause menu: Esc (or gamepad Start) in a level stops time and offers Resume, Restart level, Quit to title, and the
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

        const int RowResume = 0, RowRestart = 1, RowQuit = 2, RowVolume = 3, RowMouse = 4, RowStick = 5, RowShake = 6, RowFullscreen = 7, RowCount = 8;

        int focus;
        bool stickUpHeld, stickDownHeld, stickRightHeld, stickLeftHeld;

        void Update()
        {
            var kb = Keyboard.current;
            bool toggle = kb != null && kb.escapeKey.wasPressedThisFrame;
            foreach (var pad in Gamepad.all) toggle |= pad.startButton.wasPressedThisFrame;
            if (toggle)
            {
                if (IsPaused)
                {
                    Sfx.PlayUI("ui_back");
                    Resume();
                }
                else if (GameLevel.Current != null && (SceneDirector.Instance == null || !SceneDirector.Instance.Busy)) Pause();
                return;
            }
            if (IsPaused) Navigate(kb);
        }

        /// <summary>Keyboard and gamepad: up/down moves focus, left/right adjusts a slider or flips the toggle, submit presses.</summary>
        void Navigate(Keyboard kb)
        {
            bool up = false, down = false, left = false, right = false, submit = false, back = false;
            bool leftHeld = false, rightHeld = false;
            if (kb != null)
            {
                up |= kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
                down |= kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
                left |= kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame;
                right |= kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
                leftHeld |= kb.leftArrowKey.isPressed || kb.aKey.isPressed;
                rightHeld |= kb.rightArrowKey.isPressed || kb.dKey.isPressed;
                submit |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            }
            Vector2 stick = Vector2.zero;
            foreach (var pad in Gamepad.all)
            {
                up |= pad.dpad.up.wasPressedThisFrame;
                down |= pad.dpad.down.wasPressedThisFrame;
                left |= pad.dpad.left.wasPressedThisFrame;
                right |= pad.dpad.right.wasPressedThisFrame;
                leftHeld |= pad.dpad.left.isPressed;
                rightHeld |= pad.dpad.right.isPressed;
                submit |= pad.buttonSouth.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
                var ls = pad.leftStick.ReadValue();
                if (ls.sqrMagnitude > stick.sqrMagnitude) stick = ls;
            }
            // The left stick acts as a D-pad: one step per push past the threshold.
            up |= Edge(stick.y > 0.6f, ref stickUpHeld);
            down |= Edge(stick.y < -0.6f, ref stickDownHeld);
            right |= Edge(stick.x > 0.6f, ref stickRightHeld);
            left |= Edge(stick.x < -0.6f, ref stickLeftHeld);
            leftHeld |= stick.x < -0.6f;
            rightHeld |= stick.x > 0.6f;

            if (back)
            {
                Sfx.PlayUI("ui_back");
                Resume();
                return;
            }
            if (up) Move(-1);
            if (down) Move(1);

            var pm = PlayerManager.Instance;
            var st = pm != null ? pm.Settings : null;
            int dir = (rightHeld ? 1 : 0) - (leftHeld ? 1 : 0);
            bool changed = false;
            if (st != null && dir != 0)
            {
                // Held: sweep the slider across its range in about 2 s.
                float step = dir * Time.unscaledDeltaTime * 0.5f;
                switch (focus)
                {
                    case RowVolume: st.masterVolume = Mathf.Clamp(st.masterVolume + step, 0f, 1f); changed = true; break;
                    case RowMouse: st.mouseLookSensitivity = Mathf.Clamp(st.mouseLookSensitivity + step * 0.48f, 0.02f, 0.5f); changed = true; break;
                    case RowStick: st.stickLookSpeed = Mathf.Clamp(st.stickLookSpeed + step * 360f, 40f, 400f); changed = true; break;
                }
            }
            if (st != null && focus == RowShake && (left || right || submit))
            {
                st.cameraShake = !st.cameraShake;
                changed = true;
                Sfx.PlayUI("ui_select");
            }
            if (st != null && focus == RowFullscreen && (left || right || submit))
            {
                st.fullscreen = !st.fullscreen;
                changed = true;
                Sfx.PlayUI("ui_select");
            }
            if (changed) pm.ApplySettings();
            if (submit) Activate(focus);
        }

        static bool Edge(bool now, ref bool held)
        {
            bool fired = now && !held;
            held = now;
            return fired;
        }

        void Move(int delta)
        {
            for (int i = 0; i < RowCount; i++)
            {
                focus = (focus + delta + RowCount) % RowCount;
                if (Enabled(focus)) break;
            }
            Sfx.PlayUI("ui_select");
        }

        bool Enabled(int row) => row != RowQuit || SceneDirector.Instance != null;

        void Activate(int row)
        {
            switch (row)
            {
                case RowResume: Sfx.PlayUI("ui_back"); Resume(); break;
                case RowRestart: Sfx.PlayUI("ui_select"); RestartLevel(); break;
                case RowQuit: if (Enabled(row)) { Sfx.PlayUI("ui_select"); QuitToTitle(); } break;
            }
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
            focus = RowResume;
            stickUpHeld = stickDownHeld = stickLeftHeld = stickRightHeld = true;   // a stick already held does not step on open
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

            // The mouse moves focus only when it moves, so it does not fight the keyboard or gamepad.
            var mouse = Mouse.current;
            bool mouseMoved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f;
            var mp = Event.current.mousePosition;

            var rowRect = new Rect[RowCount];
            rowRect[RowResume] = new Rect(x, y, w, h);
            y += h + gap;
            rowRect[RowRestart] = new Rect(x, y, w, h);
            y += h + gap;
            rowRect[RowQuit] = new Rect(x, y, w, h);
            y += h + gap * 3f;
            float rowH = 36f * s;
            for (int i = RowVolume; i < RowCount; i++)
            {
                rowRect[i] = new Rect(x - 12f * s, y - 3f * s, w + 24f * s, rowH);
                y += 40f * s;
            }
            if (mouseMoved)
                for (int i = 0; i < RowCount; i++)
                    if (Enabled(i) && rowRect[i].Contains(mp) && focus != i) focus = i;

            DrawFocus(rowRect[focus], s);
            y = rowRect[RowResume].y;

            if (GUI.Button(rowRect[RowResume], "RESUME", button))
            {
                Sfx.PlayUI("ui_back");
                Resume();
            }
            if (GUI.Button(rowRect[RowRestart], "RESTART LEVEL", button))
            {
                Sfx.PlayUI("ui_select");
                RestartLevel();
            }
            GUI.enabled = Enabled(RowQuit);
            if (GUI.Button(rowRect[RowQuit], "QUIT TO TITLE", button))
            {
                Sfx.PlayUI("ui_select");
                QuitToTitle();
            }
            GUI.enabled = true;

            var pm = PlayerManager.Instance;
            if (pm == null) return;
            var st = pm.Settings;
            bool changed = false;
            y = rowRect[RowVolume].y + 3f * s;
            changed |= Slider(ref y, x, w, s, "Volume", ref st.masterVolume, 0f, 1f, $"{st.masterVolume * 100f:0}%");
            changed |= Slider(ref y, x, w, s, "Mouse look", ref st.mouseLookSensitivity, 0.02f, 0.5f, $"{st.mouseLookSensitivity:0.00}");
            changed |= Slider(ref y, x, w, s, "Stick look", ref st.stickLookSpeed, 40f, 400f, $"{st.stickLookSpeed:0}°/s");
            bool shake = GUI.Toggle(new Rect(x, y, w, 30f * s), st.cameraShake, "  Camera shake", label);
            if (shake != st.cameraShake)
            {
                st.cameraShake = shake;
                changed = true;
            }
            y += 40f * s;
            bool full = GUI.Toggle(new Rect(x, y, w, 30f * s), st.fullscreen, "  Fullscreen", label);
            if (full != st.fullscreen)
            {
                st.fullscreen = full;
                changed = true;
            }
            if (changed) pm.ApplySettings();
            // A mouse press or drag on a row also takes focus, so what is highlighted is what is being used.
            if (Event.current.type == EventType.MouseDown)
                for (int i = 0; i < RowCount; i++)
                    if (Enabled(i) && rowRect[i].Contains(Event.current.mousePosition)) focus = i;
            GUI.Label(new Rect(0f, Screen.height - 56f * s, Screen.width, 40f * s), "Arrows / D-pad / stick to move, Enter / A to select, Esc / Start to resume", label);
        }

        void DrawFocus(Rect r, float s)
        {
            var prev = GUI.color;
            GUI.color = new Color(0.98f, 0.8f, 0.2f, 0.9f);
            float t = Mathf.Max(2f, 3f * s);
            GUI.DrawTexture(new Rect(r.x - t, r.y - t, r.width + 2f * t, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x - t, r.yMax, r.width + 2f * t, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x - t, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.color = prev;
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
