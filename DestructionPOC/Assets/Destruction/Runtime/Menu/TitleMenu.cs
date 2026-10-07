using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DestructionLab
{
    /// <summary>
    /// Title.unity: a title screen with Start / Quit, and after Start a lobby where up to two players join (any key on
    /// the keyboard, or A on a gamepad) before the game scene loads in split screen. Everything is drawn with IMGUI and
    /// driven by the Input System directly, so it works with the mouse, the keyboard and gamepads.
    ///
    /// Title   W/S or ↑/↓ or D-pad to choose, Enter / Space / A to confirm, or click.
    /// Lobby   any key or A joins; Enter / Start begins; Esc / B goes back.
    /// </summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        public string gameTitle = "DESTRUCTION LAB";
        public string tagline = "Bring the building down. Clear the rubble.";
        [Tooltip("Scene loaded when the lobby begins.")]
        public string gameScene = "ConvenienceStore";

        enum Page { Title, Lobby }

        struct Button
        {
            public string label;
            public Rect rect;
            public bool enabled;
        }

        static readonly Color[] PlayerColors = { new Color(0.95f, 0.35f, 0.2f), new Color(0.2f, 0.55f, 0.95f) };

        Page page = Page.Title;
        int selected;
        int pageFrame;
        string notice;
        readonly List<Button> buttons = new List<Button>();
        GUIStyle titleStyle, tagStyle, buttonStyle, cardTitle, cardText, noticeStyle;
        Texture2D white;
        int styleHeight;

        /// <summary>For tests.</summary>
        public bool InLobby => page == Page.Lobby;

        void Awake()
        {
            SceneDirector.EnsureLoaded();
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameSession.Clear();
        }

        void Start() => Layout();

        // ------------------------------------------------------------------ input

        void Update()
        {
            GameSession.Prune();
            Layout();
            if (Time.frameCount == pageFrame) return; // the press that changed page is not also input for the new one

            var mouse = Mouse.current;
            int hover = -1;
            bool click = false;
            if (mouse != null)
            {
                Vector2 m = mouse.position.ReadValue();
                m.y = Screen.height - m.y;
                for (int i = 0; i < buttons.Count; i++)
                    if (buttons[i].rect.Contains(m)) hover = i;
                click = mouse.leftButton.wasPressedThisFrame;
            }

            if (page == Page.Title)
            {
                int nav = Nav();
                if (nav != 0) selected = (selected + nav + buttons.Count) % buttons.Count;
                if (hover >= 0 && mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0f) selected = hover;
                if (click && hover >= 0) Activate(hover);
                else if (Confirm()) Activate(selected);
            }
            else
            {
                JoinPressed();
                if (click && hover >= 0) Activate(hover);
                else if (BeginPressed()) Activate(0);
                else if (BackPressed()) Activate(1);
            }
        }

        static int Nav()
        {
            var kb = Keyboard.current;
            int n = 0;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) n--;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) n++;
            }
            foreach (var pad in Gamepad.all)
            {
                if (pad.dpad.up.wasPressedThisFrame) n--;
                if (pad.dpad.down.wasPressedThisFrame) n++;
            }
            return Mathf.Clamp(n, -1, 1);
        }

        static bool Confirm()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) return true;
            foreach (var pad in Gamepad.all)
                if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) return true;
            return false;
        }

        static bool BeginPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) return true;
            foreach (var pad in Gamepad.all)
                if (pad.startButton.wasPressedThisFrame) return true;
            return false;
        }

        static bool BackPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            foreach (var pad in Gamepad.all)
                if (pad.buttonEast.wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>Lobby: any key joins the keyboard (Enter and Esc have other jobs), A joins that gamepad.</summary>
        void JoinPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame && !kb.enterKey.wasPressedThisFrame && !kb.numpadEnterKey.wasPressedThisFrame && !kb.escapeKey.wasPressedThisFrame)
                Join(GameSession.JoinKeyboard());
            foreach (var pad in Gamepad.all)
                if (pad.buttonSouth.wasPressedThisFrame) Join(GameSession.JoinGamepad(pad));
        }

        void Join(bool joined)
        {
            if (joined) notice = null;
        }

        void Activate(int index)
        {
            if (index < 0 || index >= buttons.Count || !buttons[index].enabled)
            {
                if (page == Page.Lobby && index == 0) notice = "Join first: press any key, or A on a gamepad.";
                return;
            }
            if (page == Page.Title)
            {
                if (index == 0) Go(Page.Lobby);
                else Quit();
            }
            else if (index == 0) BeginGame();
            else
            {
                GameSession.Clear();
                Go(Page.Title);
            }
        }

        void Go(Page p)
        {
            page = p;
            selected = 0;
            notice = null;
            pageFrame = Time.frameCount;
            Layout();
        }

        /// <summary>Load the game scene with the joined players. Public so tests can drive it.</summary>
        public void BeginGame()
        {
            if (GameSession.Slots.Count == 0) return;
            if (SceneDirector.Instance != null) SceneDirector.Instance.LoadLevel(gameScene);
            else SceneManager.LoadScene(gameScene);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ layout and drawing

        float Scale => Mathf.Max(0.5f, Screen.height / 1080f);

        void Layout()
        {
            buttons.Clear();
            float s = Scale, w = 360f * s, h = 66f * s, gap = 18f * s;
            float cx = Screen.width * 0.5f;
            if (page == Page.Title)
            {
                float y = Screen.height * 0.52f;
                Add("START", new Rect(cx - w * 0.5f, y, w, h), true);
                Add("QUIT", new Rect(cx - w * 0.5f, y + h + gap, w, h), true);
            }
            else
            {
                float y = Screen.height * 0.72f;
                Add("BEGIN", new Rect(cx - w - gap * 0.5f, y, w, h), GameSession.Slots.Count > 0);
                Add("BACK", new Rect(cx + gap * 0.5f, y, w, h), true);
            }
        }

        void Add(string label, Rect rect, bool enabled) => buttons.Add(new Button { label = label, rect = rect, enabled = enabled });

        void OnGUI()
        {
            EnsureStyles();
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.07f, 0.08f, 0.1f));
            Fill(new Rect(0f, Screen.height * 0.62f, Screen.width, Screen.height * 0.38f), new Color(0.1f, 0.11f, 0.13f));
            Fill(new Rect(0f, Screen.height * 0.62f, Screen.width, 4f * Scale), new Color(0.98f, 0.8f, 0.2f));

            float s = Scale;
            GUI.Label(new Rect(0f, Screen.height * 0.16f, Screen.width, 120f * s), gameTitle, titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.16f + 112f * s, Screen.width, 50f * s), tagline, tagStyle);

            if (page == Page.Lobby) DrawLobby();

            for (int i = 0; i < buttons.Count; i++) DrawButton(buttons[i], page == Page.Title && i == selected);

            if (!string.IsNullOrEmpty(notice))
                GUI.Label(new Rect(0f, Screen.height * 0.66f, Screen.width, 40f * s), notice, noticeStyle);
            string hint = page == Page.Title ? "W / S or D-pad to choose   Enter / A to confirm"
                : "Any key or A to join   Enter / Start to begin   Esc / B to go back";
            GUI.Label(new Rect(0f, Screen.height - 56f * s, Screen.width, 40f * s), hint, noticeStyle);
        }

        void DrawLobby()
        {
            float s = Scale, w = 420f * s, h = 220f * s, gap = 40f * s;
            float x = Screen.width * 0.5f - w - gap * 0.5f, y = Screen.height * 0.34f;
            GUI.Label(new Rect(0f, y - 54f * s, Screen.width, 40f * s), "WHO'S PLAYING?  Up to 2 players, side by side", tagStyle);
            for (int i = 0; i < GameSession.MaxPlayers; i++)
            {
                var card = new Rect(x + i * (w + gap), y, w, h);
                bool joined = i < GameSession.Slots.Count;
                Color c = PlayerColors[i % PlayerColors.Length];
                Fill(card, new Color(0.14f, 0.15f, 0.18f));
                Fill(new Rect(card.x, card.y, card.width, 8f * s), joined ? c : new Color(c.r, c.g, c.b, 0.3f));
                GUI.Label(new Rect(card.x + 20f * s, card.y + 24f * s, card.width - 40f * s, 50f * s), $"PLAYER {i + 1}", cardTitle);
                string text = joined ? $"{GameSession.Slots[i].label}\nReady" : "Press any key\nor A on a gamepad\nto join";
                GUI.Label(new Rect(card.x + 20f * s, card.y + 84f * s, card.width - 40f * s, 120f * s), text, cardText);
            }
        }

        void DrawButton(Button b, bool highlighted)
        {
            var mouse = Mouse.current;
            bool hot = highlighted;
            if (mouse != null)
            {
                Vector2 m = mouse.position.ReadValue();
                m.y = Screen.height - m.y;
                if (b.rect.Contains(m)) hot = true;
            }
            Color bg = !b.enabled ? new Color(0.16f, 0.17f, 0.2f) : hot ? new Color(0.98f, 0.8f, 0.2f) : new Color(0.2f, 0.22f, 0.26f);
            Fill(b.rect, bg);
            var prev = buttonStyle.normal.textColor;
            buttonStyle.normal.textColor = !b.enabled ? new Color(1f, 1f, 1f, 0.3f) : hot ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
            GUI.Label(b.rect, b.label, buttonStyle);
            buttonStyle.normal.textColor = prev;
        }

        void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = prev;
        }

        void EnsureStyles()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (titleStyle != null && styleHeight == Screen.height) return;
            styleHeight = Screen.height;
            float s = Scale;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(96f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(0.98f, 0.8f, 0.2f);
            tagStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(28f * s), alignment = TextAnchor.MiddleCenter };
            tagStyle.normal.textColor = new Color(0.86f, 0.88f, 0.9f);
            buttonStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30f * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            cardTitle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(32f * s), fontStyle = FontStyle.Bold };
            cardTitle.normal.textColor = Color.white;
            cardText = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24f * s) };
            cardText.normal.textColor = new Color(0.86f, 0.88f, 0.9f);
            noticeStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20f * s), fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter };
            noticeStyle.normal.textColor = new Color(0.86f, 0.88f, 0.9f, 0.75f);
        }

        void OnDestroy()
        {
            if (white != null) Destroy(white);
        }
    }
}
