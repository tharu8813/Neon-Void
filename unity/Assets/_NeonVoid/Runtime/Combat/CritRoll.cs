using NeonVoid.Core;

namespace NeonVoid.Combat
{
    /// <summary>
    /// Whether one attack crits, drawn from a deterministic stream.
    /// <para>
    /// [확정] docs/02 section 3: 치명타 가능 여부는 공격별 데이터 - whether an attack may crit
    /// at all is declared per attack, and the chance is the attacker's stat. Both were wired
    /// through <see cref="DamageMath"/> from the start and every caller passed
    /// <c>critRolled: false</c>, so crit chance was a stat that did nothing: the third thing in
    /// this project to be fully built and never actually reached.
    /// </para>
    /// <para>
    /// Pure, and the generator is passed in rather than owned, because a roll that reached for
    /// its own randomness could not be replayed - and docs/06 keeps combat randomness in its
    /// own stream precisely so a fight can be.
    /// </para>
    /// </summary>
    public static class CritRoll
    {
        /// <summary>
        /// Resolution of the roll: hundredths of a percent, so a 12.5% chance is exact rather
        /// than rounded to 12 or 13.
        /// </summary>
        public const int Resolution = 10000;

        /// <summary>
        /// Rolls for a critical hit.
        /// <para>
        /// No number is drawn when the outcome is already decided - an attack that cannot crit,
        /// a chance of zero, or a chance at or above certainty. That keeps the stream from being
        /// advanced by attacks whose result was never in doubt, so a basic attack sequence
        /// consumes the same draws whether or not a damage-over-time tick happened to land
        /// between two of them.
        /// </para>
        /// </summary>
        public static bool Roll(in DamageRequest request, in CombatStats attacker, DeterministicRng rng)
        {
            if (!request.CanCrit)
                return false;

            float chance = attacker.CritChancePercent;
            if (float.IsNaN(chance) || chance <= 0f)
                return false;

            if (chance >= 100f)
                return true;

            if (rng == null)
                return false;

            // NextInt is [min, max), so a 25% chance passes on 0..2499 of 10000 - exactly a
            // quarter, with no off-by-one at either end.
            int threshold = (int)(chance * (Resolution / 100f));
            return rng.NextInt(0, Resolution) < threshold;
        }
    }
}
