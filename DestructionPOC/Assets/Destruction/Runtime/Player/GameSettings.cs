using System;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>Player-facing options, owned by the <see cref="PlayerManager"/> and kept in PlayerPrefs between runs.
    /// The defaults are the PlayerManager's inspector values.</summary>
    [Serializable]
    public sealed class GameSettings
    {
        const string PrefsKey = "DestructionLab.Settings";

        [Range(0f, 1f)] public float masterVolume = 1f;
        [Tooltip("First-person mouse look, degrees per mouse count.")]
        [Range(0.02f, 0.5f)] public float mouseLookSensitivity = 0.1f;
        [Tooltip("First-person right-stick look at full deflection, deg/s.")]
        [Range(40f, 400f)] public float stickLookSpeed = 160f;
        [Tooltip("Camera shake from wrecking-ball hits (first-person view).")]
        public bool cameraShake = true;
        [Tooltip("Borderless full screen instead of a window.")]
        public bool fullscreen;

        /// <summary>The saved settings laid over a copy of <paramref name="defaults"/>.</summary>
        public static GameSettings Load(GameSettings defaults)
        {
            var s = defaults != null ? (GameSettings)defaults.MemberwiseClone() : new GameSettings();
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, s); }
                catch (ArgumentException e) { Debug.LogWarning($"[DestructionLab] ignoring unreadable saved settings: {e.Message}"); }
            }
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        /// <summary>Global effects (the per-player ones are applied by <see cref="ApplyTo"/>).</summary>
        public void Apply()
        {
            AudioListener.volume = masterVolume;
            var mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
        }

        public void ApplyTo(CranePlayer player)
        {
            player.lookSensitivity = mouseLookSensitivity;
            player.stickLookSpeed = stickLookSpeed;
            player.cameraShake = cameraShake;
        }
    }
}
