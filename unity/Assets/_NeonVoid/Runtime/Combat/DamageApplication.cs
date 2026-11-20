namespace NeonVoid.Combat
{
    /// <summary>Everything one landed hit did.</summary>
    public readonly struct DamageOutcome
    {
        public readonly DamageResolution Resolution;

        /// <summary>Health actually removed, which is less than the damage on a killing blow.</summary>
        public readonly float HealthRemoved;

        public readonly bool Killed;

        /// <summary>True when the target was invulnerable and took nothing.</summary>
        public readonly bool Nullified;

        public readonly LifestealResult Lifesteal;

        public DamageOutcome(
            DamageResolution resolution, float healthRemoved, bool killed, bool nullified, LifestealResult lifesteal)
        {
            Resolution = resolution;
            HealthRemoved = healthRemoved;
            Killed = killed;
            Nullified = nullified;
            Lifesteal = lifesteal;
        }
    }

    /// <summary>
    /// Joins the pieces P1 built: resolve damage, nullify it when the target is invulnerable,
    /// remove health, then compute healing from what actually landed.
    /// <para>
    /// Order is the point of this type. Lifesteal must be computed from <b>health actually
    /// removed</b>, not from the damage the attack asked for. Computing it earlier would heal
    /// the attacker for overkill on every killing blow and for the full amount against an
    /// invulnerable target, which is free sustain for hitting something that took nothing.
    /// </para>
    /// <para>
    /// Shield absorption is still absent: docs/08 D-004 leaves multi-shield ordering
    /// unresolved, so the shield parameter is carried through to the lifesteal policy but no
    /// shield pool is invented here.
    /// </para>
    /// Pure: no Unity types, no side effects beyond the health it is handed by reference.
    /// </summary>
    public static class DamageApplication
    {
        public static DamageOutcome Apply(
            in DamageRequest request,
            in CombatStats attacker,
            in CombatStats defender,
            ref HealthPool defenderHealth,
            bool defenderIsInvulnerable,
            in LifestealPolicy lifestealPolicy,
            bool critRolled,
            bool isOnHitEvent,
            float shieldAbsorbed = 0f)
        {
            DamageResolution resolution = DamageMath.Resolve(request, attacker, defender, critRolled);

            // [원안 수치] docs/03 R01 처형 알고리즘, whose exception column asks for
            // 피해 확정 시 체력 기준 스냅샷. The fraction is read here, before any health is
            // removed, so a hit that drops the target under the threshold does not also get
            // the bonus for having done so - and two hits resolving in the same tick are both
            // judged against the health the target had when each one landed, not after.
            resolution = ApplyLowHealthBonus(resolution, attacker, defenderHealth);

            if (defenderIsInvulnerable)
            {
                // [확정] docs/02 section 5: invulnerability nullifies damage. No health is lost
                // and, because healing is computed from health lost, no lifesteal is paid.
                var nothing = LifestealMath.Compute(
                    request, new DamageResolution(resolution.Raw, resolution.MitigationMultiplier, 0f,
                        resolution.IsCritical, resolution.Type),
                    attacker, lifestealPolicy, 0f, 0f, isOnHitEvent);

                return new DamageOutcome(resolution, 0f, false, true, nothing);
            }

            float healthBefore = defenderHealth.Current;
            float removed = defenderHealth.Remove(resolution.AppliedBeforeShield);
            bool killed = healthBefore > 0f && !defenderHealth.IsAlive;

            LifestealResult healing = LifestealMath.Compute(
                request,
                resolution,
                attacker,
                lifestealPolicy,
                shieldAbsorbed,
                healthBefore,
                isOnHitEvent);

            return new DamageOutcome(resolution, removed, killed, false, healing);
        }

        /// <summary>
        /// Raises the resolved damage when the defender is already below the threshold.
        /// <para>
        /// The mitigated figure is scaled rather than the raw one, so the bonus is not itself
        /// reduced by resistance a second time. A target at exactly the threshold does NOT
        /// qualify: 50% 미만 is the document's wording, and an inclusive reading would make the
        /// bonus apply to a full-health target whose threshold happens to be 1.
        /// </para>
        /// </summary>
        private static DamageResolution ApplyLowHealthBonus(
            in DamageResolution resolution, in CombatStats attacker, in HealthPool defenderHealth)
        {
            float bonusPercent = attacker.LowHealthDamageBonusPercent;
            if (bonusPercent <= 0f || float.IsNaN(bonusPercent) || float.IsInfinity(bonusPercent))
                return resolution;

            float threshold = attacker.LowHealthThresholdFraction;
            if (threshold <= 0f || defenderHealth.Max <= 0f)
                return resolution;

            if (defenderHealth.Fraction >= threshold)
                return resolution;

            float multiplier = 1f + bonusPercent / 100f;
            return new DamageResolution(
                resolution.Raw,
                resolution.MitigationMultiplier,
                resolution.AppliedBeforeShield * multiplier,
                resolution.IsCritical,
                resolution.Type);
        }
    }
}
