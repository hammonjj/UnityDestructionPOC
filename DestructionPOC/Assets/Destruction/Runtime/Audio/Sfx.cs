using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// The game's sound effects. Clips live under Resources/Audio and are grouped into banks by name: variants
    /// <c>concrete_hit_01</c> … <c>_05</c> form the bank <c>concrete_hit</c>, and a play picks one at random (never
    /// the same one twice in a row). One-shots come from a small pool of 3D sources; each bank has a minimum
    /// interval and a voice cap, so a collapse that breaks hundreds of joints in a second still sounds like a
    /// collapse instead of clipping.
    /// </summary>
    public static class Sfx
    {
        const int PoolSize = 32;
        const int VoicesPerBank = 4;

        static Dictionary<string, AudioClip[]> banks;
        static readonly Dictionary<string, int> lastVariant = new Dictionary<string, int>();
        static readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        static AudioSource[] pool;
        static string[] poolBank;
        static AudioSource ui;
        static readonly Regex variantSuffix = new Regex(@"_\d+$");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            banks = null;
            pool = null;
            poolBank = null;
            ui = null;
            lastVariant.Clear();
            lastPlayed.Clear();
        }

        /// <summary>Load every clip before the first scene, so the first sound in play does not stall a frame.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Preload()
        {
            foreach (var clips in Banks.Values)
                foreach (var c in clips)
                    c.LoadAudioData();
            EnsurePool();
        }

        static Dictionary<string, AudioClip[]> Banks
        {
            get
            {
                if (banks != null) return banks;
                var grouped = new Dictionary<string, List<AudioClip>>();
                foreach (var clip in Resources.LoadAll<AudioClip>("Audio"))
                {
                    string bank = variantSuffix.Replace(clip.name, "");
                    if (!grouped.TryGetValue(bank, out var list)) grouped[bank] = list = new List<AudioClip>();
                    list.Add(clip);
                }
                banks = new Dictionary<string, AudioClip[]>();
                foreach (var kv in grouped)
                {
                    kv.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    banks[kv.Key] = kv.Value.ToArray();
                }
                return banks;
            }
        }

        public static bool Has(string bank) => Banks.ContainsKey(bank);

        /// <summary>A random variant of the bank, or null when the bank does not exist.</summary>
        public static AudioClip Clip(string bank)
        {
            if (!Banks.TryGetValue(bank, out var clips) || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            lastVariant.TryGetValue(bank, out int last);
            int i = Random.Range(0, clips.Length - 1);
            if (i >= last) i++;
            lastVariant[bank] = i;
            return clips[i];
        }

        /// <summary>
        /// Play a variant of the bank at a world point. Skipped (returns null) when the same bank played less than
        /// <paramref name="minInterval"/> seconds ago or already has its voices busy.
        /// </summary>
        public static AudioSource PlayAt(string bank, Vector3 position, float volume = 1f, float pitch = 1f,
            float minInterval = 0.05f, float minDistance = 6f, float maxDistance = 140f)
        {
            if (volume <= 0.001f || !Application.isPlaying) return null;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(bank, out float t) && now - t < minInterval) return null;
            var clip = Clip(bank);
            if (clip == null) return null;
            EnsurePool();

            // Reuse a silent voice, else steal the one furthest through its clip.
            int busy = 0, free = -1, oldest = 0;
            float furthest = -1f;
            for (int i = 0; i < pool.Length; i++)
            {
                var s = pool[i];
                if (!s.isPlaying) { if (free < 0) free = i; continue; }
                if (poolBank[i] == bank) busy++;
                if (s.time > furthest) { furthest = s.time; oldest = i; }
            }
            if (busy >= VoicesPerBank) return null;
            int slot = free >= 0 ? free : oldest;

            var src = pool[slot];
            poolBank[slot] = bank;
            src.transform.position = position;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = pitch * Random.Range(0.94f, 1.06f);
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            src.Play();
            lastPlayed[bank] = now;
            return src;
        }

        /// <summary>Menu sounds: 2D, and heard while the game is paused.</summary>
        public static void PlayUI(string bank, float volume = 0.7f)
        {
            if (!Application.isPlaying) return;
            var clip = Clip(bank);
            if (clip == null) return;
            EnsurePool();
            ui.PlayOneShot(clip, volume);
        }

        /// <summary>A looping 3D source on <paramref name="host"/>, silent until its volume is raised.</summary>
        public static AudioSource Loop(GameObject host, string bank, float minDistance = 8f, float maxDistance = 160f)
        {
            if (!Application.isPlaying) return null;
            var clip = Clip(bank);
            if (clip == null) return null;
            var src = host.AddComponent<AudioSource>();
            Configure3D(src, minDistance, maxDistance);
            src.clip = clip;
            src.loop = true;
            src.volume = 0f;
            src.Play();
            // Start each loop at a random point so several machines running together never phase.
            src.time = Random.Range(0f, clip.length * 0.9f);
            return src;
        }

        static void Configure3D(AudioSource s, float minDistance, float maxDistance)
        {
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.dopplerLevel = 0f;
        }

        static void EnsurePool()
        {
            if (pool != null && ui != null) return;
            var root = new GameObject("Sfx");
            Object.DontDestroyOnLoad(root);
            pool = new AudioSource[PoolSize];
            poolBank = new string[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(root.transform, false);
                pool[i] = go.AddComponent<AudioSource>();
                Configure3D(pool[i], 6f, 140f);
            }
            ui = root.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;
            ui.ignoreListenerPause = true;
        }
    }
}
