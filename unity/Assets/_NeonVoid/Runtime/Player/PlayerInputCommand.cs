using UnityEngine;

namespace NeonVoid.Player
{
    /// <summary>
    /// One tick's worth of structured player intent, as required by the combat contract
    /// (MovementVector / AimWorldPoint / Dash / Skill1..3 / Interact).
    /// <para>
    /// This is intent, never device state. The input layer reads devices on render frames
    /// and <b>latches button presses</b> into this command, so a press that happens between
    /// two logic ticks is still delivered. Simulation code never touches a device.
    /// </para>
    /// Movement and aim live on the XZ plane; <see cref="Vector2.x"/> is world X and
    /// <see cref="Vector2.y"/> is world Z. Y (height) is not part of the simulation.
    /// </summary>
    public readonly struct PlayerInputCommand
    {
        /// <summary>Desired movement direction, magnitude clamped to 1.</summary>
        public readonly Vector2 MoveXZ;

        /// <summary>Aim point on the XZ ground plane, in world units.</summary>
        public readonly Vector2 AimWorldXZ;

        public readonly bool DashPressed;
        public readonly bool Skill1Pressed;
        public readonly bool Skill2Pressed;
        public readonly bool Skill3Pressed;
        public readonly bool InteractPressed;

        public PlayerInputCommand(
            Vector2 moveXZ,
            Vector2 aimWorldXZ,
            bool dashPressed = false,
            bool skill1Pressed = false,
            bool skill2Pressed = false,
            bool skill3Pressed = false,
            bool interactPressed = false)
        {
            MoveXZ = Vector2.ClampMagnitude(Sanitize(moveXZ), 1f);
            AimWorldXZ = Sanitize(aimWorldXZ);
            DashPressed = dashPressed;
            Skill1Pressed = skill1Pressed;
            Skill2Pressed = skill2Pressed;
            Skill3Pressed = skill3Pressed;
            InteractPressed = interactPressed;
        }

        /// <summary>
        /// A disconnected device, an unfocused window or a bad plane intersection can produce
        /// NaN. Letting one through would poison the player's position permanently, so a
        /// non-finite component is replaced by zero at the boundary.
        /// </summary>
        private static Vector2 Sanitize(Vector2 value)
        {
            float x = float.IsNaN(value.x) || float.IsInfinity(value.x) ? 0f : value.x;
            float y = float.IsNaN(value.y) || float.IsInfinity(value.y) ? 0f : value.y;
            return new Vector2(x, y);
        }

        /// <summary>No movement, no buttons, aiming at the given point.</summary>
        public static PlayerInputCommand Idle(Vector2 aimWorldXZ) =>
            new PlayerInputCommand(Vector2.zero, aimWorldXZ);
    }
}
