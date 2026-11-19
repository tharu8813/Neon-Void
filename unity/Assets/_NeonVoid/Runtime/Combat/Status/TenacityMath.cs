using System;

namespace NeonVoid.Combat.Status
{
    /// <summary>
    /// How long a status actually lasts once tenacity and boss control resistance are applied.
    /// <para>
    /// docs/02 section 5 states two things that constrain this completely:
    /// tenacity applies to <b>control</b> status effects, and it must not be
    /// over-applied so that damage over time or stat reductions are zeroed out. Both are
    /// enforced here rather than left to callers, because a caller that forgets the first rule
    /// hands out free immunity to poison and a caller that forgets the second makes crowd
    /// control vanish entirely at high tenacity.
    /// </para>
    /// <para>
    /// Bosses carry a separate control resistance (docs/02 section 5). It is combined
    /// multiplicatively with tenacity rather than added, so two 50 percent sources leave 25
    /// percent of the duration instead of reaching zero. The combination rule itself is
    /// <b>[초안]</b>.
    /// </para>
    /// </summary>
    public static class TenacityMath
    {
        /// <summary>
        /// A control effect is never reduced below this many ticks while its base duration is
        /// at least this long. Without a floor, stacked reduction turns into total immunity and
        /// crowd control silently stops existing.
        /// [초안] 1 tick.
        /// </summary>
        public const int MinimumControlDurationTicks = 1;

        /// <summary>
        /// Effective duration in ticks.
        /// </summary>
        /// <param name="definition">Supplies whether this effect is control at all.</param>
        /// <param name="baseDurationTicks">Duration the source asked for.</param>
        /// <param name="tenacityPercent">Target's tenacity, 0..100.</param>
        /// <param name="controlResistancePercent">Boss control resistance, 0..100.</param>
        public static int EffectiveDurationTicks(
            in StatusEffectDefinition definition,
            int baseDurationTicks,
            float tenacityPercent,
            float controlResistancePercent)
        {
            if (baseDurationTicks <= 0)
                return 0;

            // Non-control effects are returned untouched. This is the over-application guard:
            // damage over time, stat reduction and every buff pass through unchanged.
            if (!definition.IsControl)
                return baseDurationTicks;

            float remaining = (1f - Fraction(tenacityPercent)) * (1f - Fraction(controlResistancePercent));
            int reduced = (int)Math.Round(baseDurationTicks * remaining, MidpointRounding.AwayFromZero);

            return Math.Max(MinimumControlDurationTicks, Math.Min(baseDurationTicks, reduced));
        }

        /// <summary>
        /// Fraction of the duration that survives, for display. 1.0 means no reduction.
        /// </summary>
        public static float RemainingFraction(float tenacityPercent, float controlResistancePercent) =>
            (1f - Fraction(tenacityPercent)) * (1f - Fraction(controlResistancePercent));

        /// <summary>
        /// Converts a 0..100 percentage into a 0..1 fraction, rejecting nonsense. Values above
        /// 100 are clamped: a 150 percent tenacity stat must not invert into a duration
        /// increase.
        /// </summary>
        private static float Fraction(float percent)
        {
            if (float.IsNaN(percent) || percent <= 0f)
                return 0f;
            if (float.IsInfinity(percent) || percent >= 100f)
                return 1f;
            return percent / 100f;
        }
    }
}
