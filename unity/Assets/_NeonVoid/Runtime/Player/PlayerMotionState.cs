using UnityEngine;

namespace NeonVoid.Player
{
    /// <summary>
    /// The whole authoritative movement state of the player at one tick, on the XZ plane.
    /// A value type with no references: copying it snapshots the simulation, which is what
    /// the determinism tests compare tick by tick.
    /// </summary>
    public struct PlayerMotionState
    {
        /// <summary>World position on the XZ plane (x = world X, y = world Z).</summary>
        public Vector2 PositionXZ;

        /// <summary>Unit vector the character faces. Driven by aim, never by movement.</summary>
        public Vector2 FacingXZ;

        /// <summary>
        /// Direction locked in when the current dash started. Meaningless when
        /// <see cref="DashTicksRemaining"/> is zero.
        /// </summary>
        public Vector2 DashDirectionXZ;

        /// <summary>Ticks left in the active dash; zero means not dashing.</summary>
        public int DashTicksRemaining;

        /// <summary>Ticks left before another dash may start.</summary>
        public int DashCooldownTicksRemaining;

        public bool IsDashing => DashTicksRemaining > 0;

        public static PlayerMotionState Spawn(Vector2 positionXZ, Vector2 facingXZ) =>
            new PlayerMotionState
            {
                PositionXZ = positionXZ,
                FacingXZ = facingXZ.sqrMagnitude > 0f ? facingXZ.normalized : Vector2.up,
                DashDirectionXZ = Vector2.zero,
                DashTicksRemaining = 0,
                DashCooldownTicksRemaining = 0,
            };
    }
}
