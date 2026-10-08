using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// Lives in Player.unity, which stays loaded for the whole run. Owns everything about the people playing rather
    /// than the place they play in:
    ///
    ///   input      who owns which devices (<see cref="GameSession"/>, filled by the title lobby); unplugged devices
    ///              drop their slot
    ///   settings   <see cref="GameSettings"/>, loaded from and saved to PlayerPrefs
    ///   players    one <see cref="playerPrefab"/> per slot (body, camera, HUD), spawned into each level at its
    ///              <see cref="PlayerSpawn"/> points when the level is ready and removed before it unloads. Two players
    ///              split the screen side by side.
    ///
    /// The player objects live in this scene, not the level's, so a level never has to know who is playing.
    /// </summary>
    public sealed class PlayerManager : MonoBehaviour
    {
        [Tooltip("One player: a root holding Body (CharacterController, CranePlayer, HUD, Avatar), Camera (Camera, CraneOverheadCamera, AudioListener) and Camera Focus.")]
        public GameObject playerPrefab;
        [Tooltip("Defaults for a first run; saved values override them.")]
        public GameSettings defaultSettings = new GameSettings();
        public Color[] playerColors = { new Color(0.95f, 0.35f, 0.2f), new Color(0.2f, 0.55f, 0.95f) };

        public static PlayerManager Instance { get; private set; }

        public GameSettings Settings { get; private set; }
        /// <summary>Every spawned player, in join order.</summary>
        public readonly List<CranePlayer> Players = new List<CranePlayer>();
        /// <summary>The first player, or null.</summary>
        public CranePlayer Player => Players.Count > 0 ? Players[0] : null;
        public GameLevel Level { get; private set; }

        Transform staging;
        readonly List<GameObject> roots = new List<GameObject>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Settings = GameSettings.Load(defaultSettings);
            Settings.Apply();
            // Players are configured under an inactive parent, so they wake up already wired.
            var go = new GameObject("Staging");
            go.SetActive(false);
            staging = go.transform;
            staging.SetParent(transform, false);
        }

        void OnEnable()
        {
            GameLevel.Ready += OnLevelReady;
            GameLevel.Unloading += OnLevelUnloading;
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        void OnDisable()
        {
            GameLevel.Ready -= OnLevelReady;
            GameLevel.Unloading -= OnLevelUnloading;
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // Play pressed on a level in the editor: the level was ready before this scene loaded.
            if (GameLevel.Current != null && GameLevel.Current.IsReady && Level == null) SpawnPlayers(GameLevel.Current);
        }

        void OnLevelReady(GameLevel level) => SpawnPlayers(level);

        void OnLevelUnloading(GameLevel level)
        {
            if (level == Level) DespawnPlayers();
        }

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed) GameSession.Prune();
        }

        /// <summary>One player per joined slot (one on every device when nobody joined).</summary>
        public void SpawnPlayers(GameLevel level)
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[DestructionLab] PlayerManager has no player prefab.");
                return;
            }
            DespawnPlayers();
            Level = level;
            GameSession.Prune();
            int count = GameSession.PlayerCount;
            for (int i = 0; i < count; i++) Players.Add(SpawnPlayer(level, i, count));
        }

        CranePlayer SpawnPlayer(GameLevel level, int index, int count)
        {
            var root = Instantiate(playerPrefab, staging);
            root.name = count > 1 ? $"Player {index + 1}" : "Player";
            var player = root.GetComponentInChildren<CranePlayer>(true);
            var cam = root.GetComponentInChildren<Camera>(true);
            var overhead = cam.GetComponent<CraneOverheadCamera>();
            var focus = root.transform.Find("Camera Focus");

            level.SpawnFor(index, out Vector3 start, out float yaw);
            player.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, yaw, 0f));
            if (focus != null) focus.position = start;

            player.devices = GameSession.DevicesFor(index);
            player.playerIndex = index;
            player.allowViewToggle = true;   // gamepad-only players never touch the cursor lock, so split screen is safe
            player.rigs.Clear();
            player.rigs.AddRange(level.Rigs);
            player.crane = level.Crane;
            player.world = level.World;
            player.onReset = level.ResetAll;
            player.cam = cam;
            player.overhead = overhead;
            player.cameraFocus = focus;
            var avatar = player.transform.Find("Avatar");
            player.avatar = avatar != null ? avatar.gameObject : null;
            if (avatar != null)
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                    r.material.color = playerColors[index % playerColors.Length];
            Settings.ApplyTo(player);

            if (overhead != null)
            {
                overhead.target = focus;
                if (level.cameraSize > 0f) overhead.orthographicSize = level.cameraSize;
            }
            cam.rect = count > 1 ? new Rect(index / (float)count, 0f, 1f / count, 1f) : new Rect(0f, 0f, 1f, 1f);
            cam.depth = index;
            // The first player's camera is Camera.main (the destroy tools aim with it) and the only audio listener.
            cam.tag = index == 0 ? "MainCamera" : "Untagged";
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null && index > 0) Destroy(listener);

            var hud = player.GetComponent<LoaderHud>();
            if (hud != null) hud.ledger = level.Ledger;

            // The 25 t ball would otherwise shove the player's capsule around.
            var cc = player.GetComponent<CharacterController>();
            foreach (var mb in level.Rigs)
            {
                var crane = mb.GetComponent<CraneRig>();
                if (crane != null && crane.Ball != null) Physics.IgnoreCollision(crane.Ball.GetComponent<Collider>(), cc);
            }

            root.transform.SetParent(transform, true);
            roots.Add(root);
            return player;
        }

        public void DespawnPlayers()
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                root.SetActive(false);   // leave CranePlayer.All now, not at the end of the frame
                Destroy(root);
            }
            roots.Clear();
            Players.Clear();
            Level = null;
        }

        /// <summary>Save the settings and push them to every player.</summary>
        public void ApplySettings()
        {
            Settings.Apply();
            foreach (var p in Players) Settings.ApplyTo(p);
            Settings.Save();
        }
    }
}
