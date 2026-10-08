using UnityEngine;

namespace DestructionLab
{
    /// <summary>What a machine sounds like: which engine, whether it runs on tracks, and the loop its working motion plays.</summary>
    public struct MachineSoundProfile
    {
        /// <summary>The skid steer's small diesel instead of the big one.</summary>
        public bool lightEngine;
        public bool tracked;
        /// <summary>Loop bank for the working motion (hydraulics, or the crane's winch).</summary>
        public string workLoop;
    }

    /// <summary>Live levels a rig reports for its sound. Both are 0..1 fractions of full speed.</summary>
    public interface IMachineSound
    {
        MachineSoundProfile SoundProfile { get; }
        /// <summary>How hard the machine is driving (tracks or wheels).</summary>
        float DriveActivity { get; }
        /// <summary>How hard the working gear is moving (boom, arms, blade, winch).</summary>
        float WorkActivity { get; }
        /// <summary>Count of moves refused because they would push into something solid. Rises when the machine bumps.</summary>
        int Blocked { get; }
    }

    /// <summary>
    /// Engine, tracks and hydraulics for one machine. The engine starts when an operator climbs in and stops when
    /// they leave (<see cref="CranePlayer"/> calls <see cref="SetEngine"/>). While it runs, an idle loop and a
    /// loaded loop crossfade by throttle, which follows whichever of driving and working is busier; tracks and the
    /// work loop follow their own activity. Loop sources are created on first start, so parked machines cost nothing.
    /// </summary>
    public sealed class MachineAudio : MonoBehaviour
    {
        IMachineSound machine;
        MachineSoundProfile profile;
        AudioSource idle, load, tracks, work;
        bool running;
        float engineGain, gainTarget, gainRate, gainDelay;
        float drive, working, throttle, lastWork;

        public bool EngineRunning => running;
        /// <summary>Current engine level 0..1 (fades in after the starter catches, out on shutdown).</summary>
        public float EngineGain => engineGain;

        public static MachineAudio For(Component rig)
        {
            var m = rig.GetComponent<MachineAudio>();
            return m != null ? m : rig.gameObject.AddComponent<MachineAudio>();
        }

        public void SetEngine(bool on)
        {
            if (on == running) return;
            if (machine == null)
            {
                machine = GetComponent<IMachineSound>();
                if (machine == null) return;
                profile = machine.SoundProfile;
            }
            running = on;
            string engine = profile.lightEngine ? "engine_light" : "engine_heavy";
            var at = transform.position + Vector3.up * 1.5f;
            if (on)
            {
                EnsureLoops(engine);
                foreach (var s in loops) if (s != null) s.UnPause();
                var start = Sfx.PlayAt(engine + "_start", at, 0.9f, 1f, 0f, 8f, 160f);
                // Let the starter crank, then bring the idle up under the start clip's tail.
                gainDelay = start != null ? start.clip.length * 0.45f : 0f;
                gainTarget = 1f;
                gainRate = 0.8f;
            }
            else
            {
                Sfx.PlayAt(engine + "_stop", at, 0.9f, 1f, 0f, 8f, 160f);
                gainDelay = 0f;
                gainTarget = 0f;
                gainRate = 4f;
            }
        }

        AudioSource[] loops = new AudioSource[0];

        void EnsureLoops(string engine)
        {
            if (idle != null) return;
            idle = Sfx.Loop(gameObject, engine + "_idle_loop");
            if (!profile.lightEngine) load = Sfx.Loop(gameObject, "engine_heavy_load_loop");
            if (profile.tracked) tracks = Sfx.Loop(gameObject, "tracks_clank_loop", 6f, 120f);
            if (!string.IsNullOrEmpty(profile.workLoop)) work = Sfx.Loop(gameObject, profile.workLoop, 5f, 100f);
            loops = new[] { idle, load, tracks, work };
        }

        void Update()
        {
            if (machine == null || idle == null) return;
            float dt = Time.deltaTime;
            if (gainDelay > 0f) gainDelay -= dt;
            else engineGain = Mathf.MoveTowards(engineGain, gainTarget, gainRate * dt);
            if (!running && engineGain <= 0f)
            {
                // Parked and silent: stop mixing the loops until the next start.
                foreach (var s in loops) if (s != null && s.isPlaying) s.Pause();
                return;
            }

            float d = running ? Mathf.Clamp01(machine.DriveActivity) : 0f;
            float w = running ? Mathf.Clamp01(machine.WorkActivity) : 0f;
            drive = Mathf.MoveTowards(drive, d, 3f * dt);
            working = Mathf.MoveTowards(working, w, 4f * dt);
            throttle = Mathf.MoveTowards(throttle, Mathf.Max(d, w), (Mathf.Max(d, w) > throttle ? 1.5f : 0.8f) * dt);

            float g = engineGain;
            if (load != null)
            {
                idle.volume = g * Mathf.Lerp(0.55f, 0.25f, throttle);
                idle.pitch = 1f + 0.12f * throttle;
                load.volume = g * 0.75f * throttle;
                load.pitch = 0.92f + 0.16f * throttle;
            }
            else
            {
                // No loaded recording for the small engine: rev the idle instead.
                idle.volume = g * Mathf.Lerp(0.5f, 0.8f, throttle);
                idle.pitch = 1f + 0.35f * throttle;
            }
            if (tracks != null)
            {
                tracks.volume = g * 0.6f * drive;
                tracks.pitch = 0.8f + 0.35f * drive;
            }
            if (work != null)
            {
                work.volume = g * 0.5f * working;
                work.pitch = 0.9f + 0.2f * working;
            }

            // Hydraulics coming to rest let off pressure.
            if (running && profile.workLoop == "hydraulic_move_loop" && lastWork > 0.4f && w < 0.05f)
                Sfx.PlayAt("hydraulic_release", transform.position + Vector3.up * 2f, 0.35f, 1f, 1.5f, 4f, 60f);
            lastWork = w;
        }
    }
}
