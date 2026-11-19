using System;
using NeonVoid.Core;

namespace NeonVoid.Combat
{
    /// <summary>What a single hit healed, split by source.</summary>
    public readonly struct LifestealResult
    {
        public readonly float FromOnHit;
        public readonly float FromOmnivamp;

        /// <summary>Efficiency omnivamp was paid at: 1.0 single target, 0.33 area or periodic.</summary>
        public readonly float OmnivampEfficiency;

        /// <summary>Damage the healing was actually computed from, after policy.</summary>
        public readonly float QualifyingDamage;

        public LifestealResult(float fromOnHit, float fromOmnivamp, float omnivampEfficiency, float qualifyingDamage)
        {
            FromOnHit = fromOnHit;
            FromOmnivamp = fromOmnivamp;
            OmnivampEfficiency = omnivampEfficiency;
            QualifyingDamage = qualifyingDamage;
        }

        public float Total => FromOnHit + FromOmnivamp;
    }

    /// <summary>
    /// Healing from damage, per docs/02 section 4.
    /// <para>
    /// The rule this type exists to protect: omnivamp pays at
    /// <see cref="DesignConstants.AreaOrPeriodicOmnivampEfficiency"/> when a hit is area
    /// <b>or</b> periodic, and <b>still only once</b> when it is area <b>and</b> periodic. The
    /// obvious implementation multiplies a factor per flag and quietly produces 0.33 * 0.33 =
    /// 0.1089 for an area damage-over-time effect, which is a third of the intended healing.
    /// Efficiency is therefore chosen by a branch, never accumulated.
    /// </para>
    /// Pure math; the undecided parts are supplied as <see cref="LifestealPolicy"/>.
    /// </summary>
    public static class LifestealMath
    {
        /// <summary>
        /// [확정] 1.0 for a single instant hit, 0.33 the moment a hit is area or periodic,
        /// whether it is one of them or both.
        /// </summary>
        public static float OmnivampEfficiency(bool isArea, bool isPeriodic) =>
            isArea || isPeriodic ? DesignConstants.AreaOrPeriodicOmnivampEfficiency : 1f;

        /// <summary>
        /// Computes healing for one resolved hit.
        /// </summary>
        /// <param name="request">The attack, which carries the area and periodic flags.</param>
        /// <param name="resolution">The resolved damage, before shields.</param>
        /// <param name="attacker">Supplies the two percentages.</param>
        /// <param name="policy">The still-undecided parts of the rule.</param>
        /// <param name="shieldAbsorbed">Damage a shield swallowed on this hit.</param>
        /// <param name="targetHealthBeforeHit">Used only to discount overkill.</param>
        /// <param name="isOnHitEvent">
        /// Whether this event counts as an "on hit" for on-hit lifesteal. Which attacks qualify
        /// is a combat-design question, so it is supplied by the caller rather than guessed from
        /// the request here.
        /// </param>
        public static LifestealResult Compute(
            in DamageRequest request,
            in DamageResolution resolution,
            in CombatStats attacker,
            in LifestealPolicy policy,
            float shieldAbsorbed,
            float targetHealthBeforeHit,
            bool isOnHitEvent)
        {
            float qualifying = QualifyingDamage(resolution, policy, shieldAbsorbed, targetHealthBeforeHit);
            float efficiency = OmnivampEfficiency(request.IsArea, request.IsPeriodic);

            float omnivamp = qualifying * Percent(attacker.OmnivampPercent) * efficiency;

            float onHit = 0f;
            if (isOnHitEvent && (policy.AllowOnHitAndOmnivampOnSameEvent || omnivamp <= 0f))
                onHit = qualifying * Percent(attacker.OnHitLifestealPercent);

            return new LifestealResult(
                Math.Max(0f, onHit),
                Math.Max(0f, omnivamp),
                efficiency,
                qualifying);
        }

        /// <summary>
        /// Damage that counts toward healing once the undecided questions are answered by the
        /// supplied policy.
        /// </summary>
        public static float QualifyingDamage(
            in DamageResolution resolution,
            in LifestealPolicy policy,
            float shieldAbsorbed,
            float targetHealthBeforeHit)
        {
            float applied = Math.Max(0f, resolution.AppliedBeforeShield);
            float absorbed = Math.Max(0f, shieldAbsorbed);

            float dealt = policy.CountShieldAbsorbedAsDealt
                ? applied
                : Math.Max(0f, applied - absorbed);

            if (!policy.CountOverkill)
            {
                float health = Math.Max(0f, targetHealthBeforeHit);
                // Only the part that could actually be removed from health counts. The shield
                // has already been taken off above when the policy says so, so clamping here
                // cannot discount the same damage twice.
                dealt = Math.Min(dealt, health);
            }

            return float.IsNaN(dealt) ? 0f : dealt;
        }

        /// <summary>Converts a percentage stat into a multiplier, rejecting nonsense.</summary>
        private static float Percent(float percent)
        {
            if (float.IsNaN(percent) || float.IsInfinity(percent) || percent <= 0f)
                return 0f;
            return percent / 100f;
        }
    }
}
