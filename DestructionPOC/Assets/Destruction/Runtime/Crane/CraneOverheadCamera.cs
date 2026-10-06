using UnityEngine;

namespace DestructionLab
{
    /// <summary>
    /// Fixed-angle, three-quarter overhead camera for CraneTest (Hades / Moonlighter style). The orientation never
    /// changes; only the position follows <see cref="target"/>. Lives on the scene's Main Camera so the values can be
    /// tuned in the Inspector. Nothing outside CraneTest references it.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)] // after CranePlayer / CraneRig have moved this frame
    public sealed class CraneOverheadCamera : MonoBehaviour
    {
        [Header("View")]
        [Tooltip("Downward tilt in degrees. 90 is straight down; lower shows more of the sides of objects.")]
        [Range(20f, 80f)] public float pitch = 35f;
        [Tooltip("Compass heading of the view in degrees (0 looks toward +Z, 90 toward +X). 45 gives the diagonal look.")]
        public float yaw = 45f;
        [Tooltip("Orthographic half-height in world metres. Bigger shows more of the working area.")]
        [Min(1f)] public float orthographicSize = 13f;
        [Tooltip("Perspective projection. The camera keeps the same angle and focus and sits back just far enough that the view at the focus matches the orthographic size, so the framing stays the same but depth shows.")]
        public bool perspective = true;
        [Tooltip("Vertical field of view in degrees when perspective is on. Lower is flatter and further away; higher is more dramatic and closer.")]
        [Range(10f, 70f)] public float fieldOfView = 35f;
        [Tooltip("Orthographic only: distance the camera sits back from the focus along its view direction. Only matters for clipping.")]
        [Min(5f)] public float distance = 80f;
        public float nearClip = 1f;
        public float farClip = 250f;

        [Header("Follow")]
        [Tooltip("Subject to follow. Set at run time by CraneTestBootstrap (the player).")]
        public Transform target;
        [Tooltip("World-space offset added to the target to get the focus point. Y raises the aim point (about chest height).")]
        public Vector3 focusOffset = new Vector3(0f, 1f, 0f);
        [Tooltip("Slides the view on screen, in orthographic world units (x right, y up). Positive y shows more above the subject, so it sits lower on screen.")]
        public Vector2 framingOffset = Vector2.zero;
        [Tooltip("Seconds for the camera to close ~63% of the gap to the target. Frame-rate independent. 0 = rigid.")]
        [Min(0f)] public float followSmoothing = 0.12f;
        [Tooltip("Vertical movement smaller than this (metres) is ignored, so jumps and steps do not bob the frame.")]
        [Min(0f)] public float verticalDeadZone = 1.5f;
        [Tooltip("If the target moves further than this in one frame (respawn / teleport) the camera snaps instead of gliding.")]
        [Min(0.5f)] public float teleportDistance = 15f;

        Camera cam;
        Vector3 focus;      // smoothed focus point
        float trackedY;
        Vector3 lastTarget;
        bool needsSnap = true;
        Vector3 shake;

        public Camera Camera => cam != null ? cam : (cam = GetComponent<Camera>());

        /// <summary>Jump straight to the correct framing next LateUpdate (init, reset, respawn).</summary>
        public void Snap() => needsSnap = true;

        /// <summary>World-space camera shake for this frame; cleared after it is applied.</summary>
        public void AddShake(Vector3 worldOffset) => shake += worldOffset;

        /// <summary>Keep one AudioListener alive: the one on this camera. Others are disabled.</summary>
        public void EnsureSingleAudioListener()
        {
            var mine = GetComponent<AudioListener>();
            if (mine == null) mine = gameObject.AddComponent<AudioListener>();
            mine.enabled = true;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                if (l != mine) l.enabled = false;
        }

        void OnEnable() => needsSnap = true;

        void LateUpdate()
        {
            Apply();
        }

        void OnValidate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            Apply(); // live tuning in Play mode
        }

        /// <summary>Camera-relative ground axes: where "up on screen" and "right on screen" point in the world.</summary>
        public void GroundAxes(out Vector3 up, out Vector3 right)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            up = q * Vector3.forward;
            right = q * Vector3.right;
        }

        void Apply()
        {
            var c = Camera;
            c.orthographic = !perspective;
            c.orthographicSize = orthographicSize;
            c.fieldOfView = fieldOfView;
            float back = perspective ? orthographicSize / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) : distance;
            c.nearClipPlane = nearClip;
            c.farClipPlane = Mathf.Max(farClip, back + 50f);

            var rot = Quaternion.Euler(pitch, yaw, 0f);

            if (target != null)
            {
                Vector3 t = target.position;
                if (needsSnap || (t - lastTarget).sqrMagnitude > teleportDistance * teleportDistance)
                {
                    focus = t;
                    trackedY = t.y;
                    needsSnap = false;
                }
                else
                {
                    // Only drag the tracked height once the target leaves the dead zone.
                    float dy = t.y - trackedY;
                    if (Mathf.Abs(dy) > verticalDeadZone) trackedY += dy - Mathf.Sign(dy) * verticalDeadZone;

                    // Scaled dt: hit-stop slows the world, so the camera slows with it.
                    float k = followSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / followSmoothing);
                    focus.x = Mathf.Lerp(focus.x, t.x, k);
                    focus.z = Mathf.Lerp(focus.z, t.z, k);
                    focus.y = Mathf.Lerp(focus.y, trackedY, k);
                }
                lastTarget = t;
            }

            Vector3 aim = focus + focusOffset + rot * new Vector3(framingOffset.x, framingOffset.y, 0f);
            transform.SetPositionAndRotation(aim - rot * Vector3.forward * back + shake, rot);
            shake = Vector3.zero;
        }
    }
}
