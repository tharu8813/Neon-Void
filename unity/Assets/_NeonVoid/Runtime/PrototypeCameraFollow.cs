using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonVoid.Prototype
{
    /// <summary>
    /// Prototype only: top-down 50% cursor lookahead, smoothed and bounded.
    /// Uses a viewport-to-world offset to prevent aim/camera feedback oscillation.
    /// Validated 60 Hz target selection and room bounds are P0-02 work.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PrototypeCameraFollow : MonoBehaviour
    {
        public Transform player;
        public float lookaheadFraction = 0.5f;
        public float maxLookaheadUnits = 3.2f;
        public float smoothness = 12f;
        public float height = 22f;
        public Vector2 cameraMin = new Vector2(-16f, -10f);
        public Vector2 cameraMax = new Vector2(16f, 10f);

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (player == null || _camera == null)
                return;

            Vector2 cursorOffset = Vector2.zero;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 viewport = _camera.ScreenToViewportPoint(mouse.position.ReadValue());
                float fullVisibleHeight = 2f * _camera.orthographicSize;
                float fullVisibleWidth = fullVisibleHeight * _camera.aspect;
                cursorOffset = new Vector2(
                    (viewport.x - 0.5f) * fullVisibleWidth,
                    (viewport.y - 0.5f) * fullVisibleHeight);
            }
            Vector2 lookahead = Vector2.ClampMagnitude(cursorOffset * lookaheadFraction, maxLookaheadUnits);
            Vector3 candidate = player.position + new Vector3(lookahead.x, 0f, lookahead.y);
            candidate.x = Mathf.Clamp(candidate.x, cameraMin.x, cameraMax.x);
            candidate.z = Mathf.Clamp(candidate.z, cameraMin.y, cameraMax.y);
            Vector3 desired = new Vector3(candidate.x, height, candidate.z);
            float alpha = 1f - Mathf.Exp(-smoothness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, alpha);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}
