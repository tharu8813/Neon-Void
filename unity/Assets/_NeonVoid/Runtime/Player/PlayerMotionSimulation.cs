using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Player
{
    /// <summary>
    /// The player's movement rules for exactly one 60 Hz logic tick.
    /// <para>
    /// A pure static function over value types: same state + same command + same config
    /// always produces the same result, on any machine, at any render frame rate. There is
    /// no <c>Time.deltaTime</c> anywhere, which is what makes the frame-rate independence
    /// test meaningful rather than a coincidence.
    /// </para>
    /// <para>
    /// This replaces the Update-driven <c>PrototypePlayerController</c>. It does not resolve
    /// collision: it reports the displacement the rules ask for, and the Unity adapter feeds
    /// that to a CharacterController and writes the resolved position back. Collision is
    /// therefore the one part of movement that is not bit-deterministic, as docs/02 §1
    /// requires to be stated rather than assumed away.
    /// </para>
    /// </summary>
    public static class PlayerMotionSimulation
    {
        /// <summary>Below this squared length a stick or key combination counts as "no input".</summary>
        public const float MovementDeadZoneSqr = 1e-6f;

        /// <summary>
        /// Advances one tick.
        /// </summary>
        /// <param name="state">State at the start of the tick.</param>
        /// <param name="command">Latched intent for this tick.</param>
        /// <param name="config">Tunables; assumed already validated.</param>
        /// <param name="desiredDisplacementXZ">
        /// Movement the rules request this tick. The caller applies it (directly, or through
        /// a collider) and reports the achieved position back via <see cref="PlayerMotionState.PositionXZ"/>.
        /// </param>
        /// <returns>State at the end of the tick.</returns>
        public static PlayerMotionState Step(
            in PlayerMotionState state,
            in PlayerInputCommand command,
            in PlayerMotionConfig config,
            out Vector2 desiredDisplacementXZ,
            float speedMultiplier = 1f)
        {
            PlayerMotionState next = state;

            // 구속 slows, 정지 and 속박 stop outright. A non-finite or negative multiplier is
            // treated as a stop rather than propagated: a NaN here would reach the position and
            // poison it permanently.
            float speed = float.IsNaN(speedMultiplier) || speedMultiplier < 0f ? 0f : speedMultiplier;
            bool canMove = speed > 0f;

            // The cooldown is READ here and SPENT at the end of the tick. Spending it first
            // would give the dash tick itself a free decrement, so a 45 tick cooldown would
            // only free up on tick T+46. Read-then-spend makes T+45 exact.
            //
            // canMove also gates starting a dash, which is D-009's "정지 중 대시 미발동".
            // Without it a stunned player would start a dash, travel nowhere, and pay the
            // cooldown for it - the worst of both readings.
            bool wantsDash = command.DashPressed
                             && canMove
                             && !next.IsDashing
                             && next.DashCooldownTicksRemaining <= 0
                             && command.MoveXZ.sqrMagnitude > MovementDeadZoneSqr;

            if (wantsDash)
            {
                // [확정] The dash follows the movement vector. The cursor must never choose
                // it (docs/08 C05), which is why command.AimWorldXZ is not read here at all.
                next.DashDirectionXZ = command.MoveXZ.normalized;
                next.DashTicksRemaining = config.DashDurationTicks;
                next.DashCooldownTicksRemaining = config.DashCooldownTicks;
            }

            Vector2 velocityUnitsPerSecond;
            if (next.DashTicksRemaining > 0)
            {
                // The multiplier scales a dash already in flight too, so being rooted or
                // stunned mid-dash stops it rather than letting it carry the player away from
                // the effect that was supposed to hold them. Whether 구속 should also slow a
                // dash, rather than only stopping it outright, is [미확정].
                velocityUnitsPerSecond = next.DashDirectionXZ * config.DashSpeedUnitsPerSecond * speed;
                next.DashTicksRemaining--;
            }
            else
            {
                velocityUnitsPerSecond = command.MoveXZ * config.MoveSpeedUnitsPerSecond * speed;
            }

            desiredDisplacementXZ = velocityUnitsPerSecond * (float)SimulationClock.TickDeltaSeconds;
            next.PositionXZ = state.PositionXZ + desiredDisplacementXZ;

            next.FacingXZ = ResolveFacing(state.PositionXZ, command.AimWorldXZ, state.FacingXZ);

            // One tick of cooldown is spent at the end of every tick, the dash tick included.
            if (next.DashCooldownTicksRemaining > 0)
                next.DashCooldownTicksRemaining--;

            return next;
        }

        /// <summary>
        /// Faces the aim point. When the cursor sits exactly on the character the direction
        /// is undefined, so the previous facing is kept rather than snapping to an arbitrary
        /// axis and making the character spin.
        /// </summary>
        public static Vector2 ResolveFacing(Vector2 positionXZ, Vector2 aimWorldXZ, Vector2 previousFacingXZ)
        {
            Vector2 toAim = aimWorldXZ - positionXZ;
            return toAim.sqrMagnitude > MovementDeadZoneSqr ? toAim.normalized : previousFacingXZ;
        }
    }
}
