using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>
    /// Gentle gamepad rumble for one <see cref="CranePlayer"/>: a faint engine hum that follows driving and working,
    /// a short thump when the machine is stopped by something solid, and a pulse when heavy debris or the wrecking
    /// ball hits near the player. Only runs while the player's last input came from a gamepad, and only on a pad
    /// that player owns, so in split screen each pad rumbles for its own player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Haptics : MonoBehaviour
    {
        [Tooltip("Master scale for all rumble. 0 turns it off.")]
        [Range(0f, 1f)] public float strength = 1f;
        [Tooltip("Heavy debris this close (m) or closer is felt, fading with distance.")]
        public float impactRange = 35f;
        [Tooltip("Debris lighter than this (kg) is not felt.")]
        public float minImpactMass = 150f;

        const float PulseDecay = 3.5f;   // per second
        const float BumpPulse = 0.28f;

        CranePlayer player;
        DestructionWorld world;
        IMachineSound machine;
        int lastBlocked;
        float pulseLow, pulseHigh, nextImpact;
        Gamepad pad;

        void Awake() => player = GetComponent<CranePlayer>();

        void OnDisable()
        {
            Unbind();
            Stop();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) Stop();
        }

        /// <summary>A short thump, 0..1. Low motor carries the weight, high motor a little of the edge.</summary>
        public void Hit(float amount)
        {
            amount = Mathf.Clamp01(amount);
            pulseLow = Mathf.Max(pulseLow, amount);
            pulseHigh = Mathf.Max(pulseHigh, amount * 0.5f);
        }

        void Bind()
        {
            if (world == player.world) return;
            Unbind();
            world = player.world;
            if (world != null) world.OnImpact += OnImpact;
        }

        void Unbind()
        {
            if (world != null) world.OnImpact -= OnImpact;
            world = null;
        }

        void OnImpact(ImpactEvent e)
        {
            if (e.mass < minImpactMass || Time.unscaledTime < nextImpact) return;
            Vector3 at = player.Current != null ? player.Current.SeatPosition : player.transform.position;
            float near = 1f - Vector3.Distance(at, e.point) / impactRange;
            if (near <= 0f) return;
            float loud = Mathf.Clamp(0.15f + 0.2f * Mathf.Log10(Mathf.Max(1f, e.energy / 50f)), 0.15f, 1f);
            Hit(loud * near * near * 0.7f);
            nextImpact = Time.unscaledTime + 0.06f;
        }

        Gamepad FindPad()
        {
            if (pad != null && pad.added) return pad;
            var owned = player.devices;
            if (owned == null || owned.Length == 0) return pad = Gamepad.current;
            foreach (var d in owned) if (d is Gamepad g && g.added) return pad = g;
            return pad = null;
        }

        void Update()
        {
            if (PauseMenu.IsPaused || strength <= 0f || player == null || player.Input == null || !player.Input.UsingGamepad)
            {
                Stop();
                return;
            }
            Bind();
            var g = FindPad();
            if (g == null) return;

            // Engine hum, only while seated in a running machine.
            float low = 0f, high = 0f;
            var rig = player.Current as MonoBehaviour;
            var m = rig != null ? rig.GetComponent<IMachineSound>() : null;
            if (m != machine)
            {
                machine = m;
                lastBlocked = m != null ? m.Blocked : 0;
            }
            if (m != null)
            {
                float gain = MachineAudio.For(rig).EngineGain;
                float drive = Mathf.Clamp01(m.DriveActivity), work = Mathf.Clamp01(m.WorkActivity);
                low = gain * (0.03f + 0.10f * drive);
                high = gain * 0.05f * work;
                if (m.Blocked > lastBlocked) Hit(BumpPulse);
                lastBlocked = m.Blocked;
            }

            float dt = Time.unscaledDeltaTime;
            pulseLow = Mathf.MoveTowards(pulseLow, 0f, PulseDecay * dt);
            pulseHigh = Mathf.MoveTowards(pulseHigh, 0f, PulseDecay * 1.5f * dt);
            g.SetMotorSpeeds(Mathf.Clamp01(Mathf.Max(low, pulseLow) * strength), Mathf.Clamp01(Mathf.Max(high, pulseHigh) * strength));
        }

        void Stop()
        {
            pulseLow = pulseHigh = 0f;
            if (pad != null && pad.added) pad.ResetHaptics();
        }
    }
}
