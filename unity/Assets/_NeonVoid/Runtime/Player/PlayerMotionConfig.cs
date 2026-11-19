using System;

namespace NeonVoid.Player
{
    /// <summary>
    /// Tunable movement values. Every number here is <b>[초안]</b>: none has been approved
    /// as balance. They are expressed so a designer can move them to a ScriptableObject
    /// without touching simulation code.
    /// <para>
    /// Durations are whole <b>ticks</b>, not seconds. A dash measured in seconds would land
    /// mid-tick and make its length depend on rounding; in ticks it is exact and identical
    /// on every machine.
    /// </para>
    /// <para>
    /// Deliberately absent: dash invincibility frames. The legacy 2D build used 0.32 s of
    /// invincibility against a 0.145 s dash — invincibility outlasting the dash itself —
    /// and docs/08 (C06) marks that unresolved, so no value is invented here.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PlayerMotionConfig
    {
        /// <summary>Walk speed in Unity units per second. [초안] docs/02 §2.</summary>
        public float MoveSpeedUnitsPerSecond;

        /// <summary>Dash speed in Unity units per second. [초안] no approved value exists.</summary>
        public float DashSpeedUnitsPerSecond;

        /// <summary>Dash length in ticks. [초안] 9 ticks = 0.150 s at 60 Hz.</summary>
        public int DashDurationTicks;

        /// <summary>
        /// Ticks before another dash may start, counted from the tick the dash began, so it
        /// includes the dash itself. [초안] 45 ticks = 0.750 s at 60 Hz.
        /// </summary>
        public int DashCooldownTicks;

        public static PlayerMotionConfig Draft() => new PlayerMotionConfig
        {
            MoveSpeedUnitsPerSecond = 5f,
            DashSpeedUnitsPerSecond = 14f,
            DashDurationTicks = 9,
            DashCooldownTicks = 45,
        };

        /// <summary>
        /// Rejects values that would make the simulation meaningless (negative speed, a dash
        /// that never ends, a cooldown shorter than the dash it gates).
        /// </summary>
        public void Validate()
        {
            if (!(MoveSpeedUnitsPerSecond >= 0f))
                throw new InvalidOperationException($"{nameof(MoveSpeedUnitsPerSecond)} must be finite and non-negative.");
            if (!(DashSpeedUnitsPerSecond >= 0f))
                throw new InvalidOperationException($"{nameof(DashSpeedUnitsPerSecond)} must be finite and non-negative.");
            if (DashDurationTicks < 1)
                throw new InvalidOperationException($"{nameof(DashDurationTicks)} must be at least one tick.");
            if (DashCooldownTicks < DashDurationTicks)
                throw new InvalidOperationException(
                    $"{nameof(DashCooldownTicks)} is counted from the dash start, so it cannot be shorter than {nameof(DashDurationTicks)}.");
        }
    }
}
