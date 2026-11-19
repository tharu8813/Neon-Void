using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonVoid.Player
{
    /// <summary>
    /// Orthographic top-down camera with the confirmed 50 percent cursor look-ahead
    /// (docs/08 R-016). Replaces <c>PrototypeCameraFollow</c>.
    /// <para>
    /// All the geometry lives in <see cref="LookaheadCameraMath"/>, which is tested without a
    /// scene. This component only supplies the pointer, the lens and the transform, which is
    /// what keeps the camera rule verifiable instead of something that has to be eyeballed.
    /// </para>
    /// <para>
    /// The cursor is converted to an offset from the SCREEN CENTRE before it reaches the math,
    /// never to a world point. See LookaheadCameraMath for why a world cursor point would
    /// close a feedback loop and carry the camera all the way to the cursor.
    /// </para>
    /// Follow runs in LateUpdate so it uses the position the player reached this frame.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class TopDownLookaheadCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Header("Look-ahead")]
        [Tooltip("[확정] 0.5 - the camera leads half way toward the cursor (docs/08 R-016).")]
        [SerializeField, Range(0f, 0.9f)] private float lookaheadFraction = 0.5f;

        [Tooltip("[초안] hard cap on look-ahead distance in world units.")]
        [SerializeField, Min(0f)] private float maxLookaheadUnits = 3.2f;

        [Header("Framing")]
        [SerializeField] private float height = 22f;

        [Tooltip("[초안] higher settles faster; frame-rate independent.")]
        [SerializeField, Min(0f)] private float smoothingSharpness = 12f;

        [Header("Bounds")]
        [SerializeField] private Vector2 boundsMinXZ = new Vector2(-16f, -10f);
        [SerializeField] private Vector2 boundsMaxXZ = new Vector2(16f, 10f);

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (!_camera.orthographic)
            {
                Debug.LogWarning(
                    $"{nameof(TopDownLookaheadCamera)} expects an orthographic camera; " +
                    "look-ahead is computed from orthographicSize and will be wrong in perspective.",
                    this);
            }
        }

        /// <summary>Assigns the follow target at runtime (used by the scene builder).</summary>
        public void SetTarget(Transform newTarget) => target = newTarget;

        /// <summary>Sets the rectangle the camera focus is clamped to.</summary>
        public void SetBounds(Vector2 minXZ, Vector2 maxXZ)
        {
            boundsMinXZ = minXZ;
            boundsMaxXZ = maxXZ;
        }

        private void LateUpdate()
        {
            if (target == null || _camera == null)
                return;

            Vector2 cursorOffset = ReadCursorOffsetFromScreenCentre();

            Vector3 targetPosition = target.position;
            Vector2 focus = LookaheadCameraMath.ComputeFocusXZ(
                new Vector2(targetPosition.x, targetPosition.z),
                cursorOffset,
                lookaheadFraction,
                maxLookaheadUnits,
                boundsMinXZ,
                boundsMaxXZ);

            var desired = new Vector3(focus.x, height, focus.y);
            float alpha = LookaheadCameraMath.SmoothingAlpha(smoothingSharpness, Time.deltaTime);

            transform.position = alpha <= 0f ? desired : Vector3.Lerp(transform.position, desired, alpha);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>
        /// Pointer offset from the screen centre in world units. Depends only on the mouse
        /// position and the lens, never on where this camera currently is.
        /// </summary>
        private Vector2 ReadCursorOffsetFromScreenCentre()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return Vector2.zero;

            Vector2 screen = mouse.position.ReadValue();
            int width = Screen.width;
            int height2 = Screen.height;
            if (width <= 0 || height2 <= 0)
                return Vector2.zero;

            var viewport = new Vector2(screen.x / width, screen.y / height2);
            if (float.IsNaN(viewport.x) || float.IsNaN(viewport.y))
                return Vector2.zero;

            return LookaheadCameraMath.ViewportToWorldOffsetXZ(
                viewport, _camera.orthographicSize, _camera.aspect);
        }
    }
}
