using UnityEngine;

namespace NeonVoid.Player
{
    /// <summary>
    /// Where the top-down camera should look, given the player and the cursor.
    /// <para>
    /// <b>Why the cursor is passed as an offset from the screen centre, not as a world
    /// point.</b> Converting a mouse position to a world point needs the camera, so a world
    /// cursor point is a function of camera position. Feeding it back in would close a loop:
    /// focus = player + f*(cursor - player) with cursor = camera + s solves to
    /// camera = player + s*f/(1-f), i.e. at f = 0.5 the camera walks all the way to the
    /// cursor instead of stopping halfway, and the aim ray drifts while it travels.
    /// </para>
    /// <para>
    /// The cursor's offset from the <i>screen centre</i>, expressed in world units, depends
    /// only on the mouse position and the orthographic size — never on where the camera is.
    /// That breaks the loop by construction, and the test suite asserts it by computing the
    /// same focus from different camera positions.
    /// </para>
    /// Pure math on the XZ plane; no Camera, no Transform, no frame time.
    /// </summary>
    public static class LookaheadCameraMath
    {
        /// <summary>
        /// Focus point for the camera on the XZ plane.
        /// </summary>
        /// <param name="playerXZ">Player position.</param>
        /// <param name="cursorOffsetFromScreenCentreXZ">
        /// Cursor offset from the screen centre converted to world units. Camera-independent.
        /// </param>
        /// <param name="lookaheadFraction">[확정] 0.5 — half of the offset (docs/08 R-016).</param>
        /// <param name="maxLookaheadUnits">[초안] hard cap on how far the camera may lead.</param>
        /// <param name="boundsMinXZ">Inclusive lower corner the focus is clamped to.</param>
        /// <param name="boundsMaxXZ">Inclusive upper corner the focus is clamped to.</param>
        public static Vector2 ComputeFocusXZ(
            Vector2 playerXZ,
            Vector2 cursorOffsetFromScreenCentreXZ,
            float lookaheadFraction,
            float maxLookaheadUnits,
            Vector2 boundsMinXZ,
            Vector2 boundsMaxXZ)
        {
            Vector2 lookahead = Vector2.ClampMagnitude(
                cursorOffsetFromScreenCentreXZ * lookaheadFraction,
                Mathf.Max(0f, maxLookaheadUnits));

            Vector2 focus = playerXZ + lookahead;

            // A caller that supplies an inverted or empty rect would otherwise get a silent
            // NaN out of Mathf.Clamp; collapse to the midpoint instead so the camera stays put.
            float x = boundsMinXZ.x <= boundsMaxXZ.x
                ? Mathf.Clamp(focus.x, boundsMinXZ.x, boundsMaxXZ.x)
                : 0.5f * (boundsMinXZ.x + boundsMaxXZ.x);
            float y = boundsMinXZ.y <= boundsMaxXZ.y
                ? Mathf.Clamp(focus.y, boundsMinXZ.y, boundsMaxXZ.y)
                : 0.5f * (boundsMinXZ.y + boundsMaxXZ.y);

            return new Vector2(x, y);
        }

        /// <summary>
        /// Converts a viewport point (0..1, origin bottom-left) into a world-unit offset from
        /// the screen centre for an orthographic top-down camera.
        /// <paramref name="orthographicSize"/> is half the visible height; width follows the
        /// aspect ratio, which is why an ultrawide screen leads further on X for the same
        /// cursor position.
        /// </summary>
        public static Vector2 ViewportToWorldOffsetXZ(Vector2 viewportPoint, float orthographicSize, float aspect)
        {
            float visibleHeight = 2f * orthographicSize;
            float visibleWidth = visibleHeight * aspect;
            return new Vector2(
                (viewportPoint.x - 0.5f) * visibleWidth,
                (viewportPoint.y - 0.5f) * visibleHeight);
        }

        /// <summary>
        /// Frame-rate independent exponential smoothing factor. Using
        /// <c>1 - exp(-sharpness * dt)</c> instead of <c>sharpness * dt</c> keeps the camera's
        /// settle time the same at 30 and 144 fps.
        /// </summary>
        public static float SmoothingAlpha(float sharpness, float deltaSeconds)
        {
            if (sharpness <= 0f || deltaSeconds <= 0f)
                return 0f;
            return 1f - Mathf.Exp(-sharpness * deltaSeconds);
        }
    }
}
