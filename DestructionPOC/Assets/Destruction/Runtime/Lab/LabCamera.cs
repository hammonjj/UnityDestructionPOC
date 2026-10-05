using UnityEngine;
using UnityEngine.InputSystem;

namespace DestructionLab
{
    /// <summary>Orbit / pan / zoom camera. Right-drag orbit, middle-drag or Shift+right-drag pan, wheel zoom,
    /// WASD/QE move the pivot, F re-frames the scenario. Uses unscaled time so it works while paused.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class LabCamera : MonoBehaviour
    {
        public Vector3 pivot;
        public float distance = 22f;
        public float yaw = -35f;
        public float pitch = 22f;
        public float orbitSpeed = 0.25f;
        public float panSpeed = 0.0015f;
        public float zoomSpeed = 0.0015f;
        public float keyPanSpeed = 8f;

        Vector3 homePivot;
        float homeDistance, homeYaw, homePitch;
        public bool InputBlocked { get; set; }

        public void Frame(Vector3 p, float d, float y, float x)
        {
            homePivot = pivot = p;
            homeDistance = distance = d;
            homeYaw = yaw = y;
            homePitch = pitch = x;
            Apply();
        }

        public void ResetView() => Frame(homePivot, homeDistance, homeYaw, homePitch);

        void LateUpdate()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;

            if (mouse != null && !InputBlocked)
            {
                Vector2 delta = mouse.delta.ReadValue();
                bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                if (mouse.rightButton.isPressed && !shift)
                {
                    yaw += delta.x * orbitSpeed;
                    pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, -5f, 85f);
                }
                else if (mouse.middleButton.isPressed || (mouse.rightButton.isPressed && shift))
                {
                    var r = transform.right;
                    var u = transform.up;
                    pivot -= (r * delta.x + u * delta.y) * panSpeed * distance;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance * (1f - scroll * zoomSpeed * 10f), 3f, 120f);
            }

            if (kb != null && !InputBlocked)
            {
                Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                Vector3 move = Vector3.zero;
                if (kb.wKey.isPressed) move += fwd;
                if (kb.sKey.isPressed) move -= fwd;
                if (kb.dKey.isPressed) move += right;
                if (kb.aKey.isPressed) move -= right;
                if (kb.eKey.isPressed) move += Vector3.up;
                if (kb.qKey.isPressed) move -= Vector3.up;
                pivot += move * keyPanSpeed * dt * Mathf.Max(0.3f, distance / 20f);
                if (kb.fKey.wasPressedThisFrame) ResetView();
            }
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rot * Vector3.forward * distance, rot);
        }
    }
}
