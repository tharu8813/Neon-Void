using System;
using NeonVoid.Core;

namespace NeonVoid.Combat.Stats
{
    /// <summary>
    /// How often an attacker may shoot, in whole ticks.
    /// <para>
    /// [확정] docs/02 section 3: 공격속도에 상한은 있지만 값 미확정 - a cap exists and its value
    /// is not decided. The cap is therefore a parameter, marked [미확정] where it is defined,
    /// rather than left out: leaving it out would make "there is a cap" false, and 발사속도 증가
    /// stacking without one ends at an interval of one tick, which is sixty attacks a second.
    /// </para>
    /// <para>
    /// The result is whole ticks because the simulation has no finer unit. Rounding rather
    /// than truncating, so a rate that lands between two intervals goes to the nearer one and
    /// a long chain of small buffs does not systematically lose time.
    /// </para>
    /// </summary>
    public static class AttackRateMath
    {
        /// <summary>Slowest an attack may be allowed to get, so a debuff cannot stop it forever.</summary>
        public const int MaxIntervalTicks = 600;

        /// <summary>
        /// Attacks per second after buffs, debuffs and the cap.
        /// <para>
        /// The multiplier is applied to the base rather than compounded, for the same reason
        /// stat fractions are of the base: otherwise the result depends on the order the
        /// contributions arrived in.
        /// </para>
        /// </summary>
        public static float EffectiveAttacksPerSecond(
            float baseAttacksPerSecond, float multiplier, float maxAttacksPerSecond)
        {
            if (float.IsNaN(baseAttacksPerSecond) || baseAttacksPerSecond <= 0f)
                return 0f;

            float effective = baseAttacksPerSecond * Sanitize(multiplier);
            if (effective <= 0f)
                return 0f;

            if (maxAttacksPerSecond > 0f && effective > maxAttacksPerSecond)
                return maxAttacksPerSecond;

            return effective;
        }

        /// <summary>
        /// The interval in whole ticks for a rate, at least one tick and at most
        /// <see cref="MaxIntervalTicks"/>.
        /// </summary>
        public static int IntervalTicks(float attacksPerSecond)
        {
            if (float.IsNaN(attacksPerSecond) || attacksPerSecond <= 0f)
                return MaxIntervalTicks;

            int ticks = (int)Math.Round(DesignConstants.SimulationTicksPerSecond / attacksPerSecond);
            if (ticks < 1)
                return 1;

            return ticks > MaxIntervalTicks ? MaxIntervalTicks : ticks;
        }

        /// <summary>Both steps together, which is what a shooter actually asks for.</summary>
        public static int IntervalTicksFor(
            float baseAttacksPerSecond, float multiplier, float maxAttacksPerSecond) =>
            IntervalTicks(EffectiveAttacksPerSecond(baseAttacksPerSecond, multiplier, maxAttacksPerSecond));

        private static float Sanitize(float multiplier)
        {
            if (float.IsNaN(multiplier) || multiplier < 0f)
                return 0f;

            return float.IsInfinity(multiplier) ? 0f : multiplier;
        }
    }
}
