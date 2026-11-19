using System;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Skill haste, from docs/02 section 3: <c>baseCooldown / (1 + H/100)</c>.
    /// <para>
    /// Haste is deliberately NOT a percentage reduction. With 100 haste the cooldown halves;
    /// with 200 it is a third. A linear "reduce by H percent" would hit zero at H = 100 and
    /// turn negative beyond it, which is why the document specifies this shape. The document
    /// also states the minimum cooldown must be above zero but leaves the exact floor
    /// <b>[미확정]</b>; one tick is used here and marked as provisional.
    /// </para>
    /// </summary>
    public static class CooldownMath
    {
        /// <summary>
        /// [초안] Smallest cooldown any amount of haste can produce. docs/02 section 3 requires
        /// a floor above zero; the value itself is not approved.
        /// </summary>
        public const int MinimumCooldownTicks = 1;

        /// <summary>
        /// Effective cooldown in ticks for the given base and skill haste.
        /// </summary>
        /// <param name="baseCooldownTicks">Cooldown before haste.</param>
        /// <param name="skillHaste">Haste points. 0 means no reduction. Negative is clamped away.</param>
        public static int EffectiveCooldownTicks(int baseCooldownTicks, float skillHaste)
        {
            if (baseCooldownTicks <= 0)
                return 0;

            float haste = float.IsNaN(skillHaste) || skillHaste <= 0f ? 0f : skillHaste;
            if (float.IsInfinity(haste))
                return MinimumCooldownTicks;

            double reduced = baseCooldownTicks / (1.0 + haste / 100.0);
            int ticks = (int)Math.Round(reduced, MidpointRounding.AwayFromZero);

            return Math.Max(MinimumCooldownTicks, Math.Min(baseCooldownTicks, ticks));
        }

        /// <summary>
        /// How many casts per second the haste is worth, for a HUD that wants to show the
        /// benefit rather than the raw stat.
        /// </summary>
        public static float CastsPerSecond(int baseCooldownTicks, float skillHaste, int ticksPerSecond)
        {
            int ticks = EffectiveCooldownTicks(baseCooldownTicks, skillHaste);
            if (ticks <= 0 || ticksPerSecond <= 0)
                return 0f;
            return ticksPerSecond / (float)ticks;
        }
    }
}
