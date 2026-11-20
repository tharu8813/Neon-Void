using System.Collections.Generic;

namespace NeonVoid.Combat.Stats
{
    /// <summary>
    /// Turns base stats plus every contribution into the combat snapshot an attack resolves
    /// against.
    /// <para>
    /// [확정] docs/02 section 2 fixes the order:
    /// <c>base → 승인 영구 투자 → 플러그인/버프·디버프 → 캡·검증 → 최종 전투 스냅샷</c>, and
    /// requires <b>원본 기반 재계산</b> - recompute from the original every time, so the same
    /// investment or plugin cannot be applied twice when a zone is re-entered.
    /// </para>
    /// <para>
    /// That requirement is why this is a function of the base stats rather than a method that
    /// mutates a stat block. A resolver that added onto the current values would be correct the
    /// first time and wrong on every recomputation, and the symptom - stats creeping upward
    /// each time the player crosses a boundary - looks like a balance problem rather than a
    /// bug. <see cref="Resolve"/> called twice with the same inputs returns the same answer,
    /// which is asserted by a test rather than hoped for.
    /// </para>
    /// <para>
    /// Pure: no Unity types, no scene, no clock.
    /// </para>
    /// </summary>
    public static class CombatStatResolution
    {
        /// <summary>
        /// Applies the permanent investment, then everything temporary, then the caps.
        /// Unusable modifiers are skipped rather than clamped, per docs/02's "음수/NaN/무한대/
        /// 잘못된 ID 거부"; <paramref name="rejected"/> counts them so a caller can report it.
        /// </summary>
        public static CombatStats Resolve(
            in CombatStats baseStats,
            IReadOnlyList<StatModifier> permanentInvestment,
            IReadOnlyList<StatModifier> temporary,
            in StatCaps caps,
            out int rejected)
        {
            rejected = 0;
            CombatStats result = baseStats;

            Apply(ref result, baseStats, permanentInvestment, ref rejected);
            Apply(ref result, baseStats, temporary, ref rejected);
            ApplyCaps(ref result, caps);

            return result;
        }

        /// <summary>Convenience overload for callers with nothing permanent to apply.</summary>
        public static CombatStats Resolve(
            in CombatStats baseStats, IReadOnlyList<StatModifier> temporary, in StatCaps caps) =>
            Resolve(baseStats, null, temporary, caps, out _);

        private static void Apply(
            ref CombatStats result,
            in CombatStats baseStats,
            IReadOnlyList<StatModifier> modifiers,
            ref int rejected)
        {
            if (modifiers == null)
                return;

            for (int i = 0; i < modifiers.Count; ++i)
            {
                StatModifier modifier = modifiers[i];
                if (!modifier.IsUsable)
                {
                    rejected++;
                    continue;
                }

                // Fractions are always of the base value, never of the running total, so the
                // order contributions arrive in cannot change the result.
                float delta = modifier.Flat + modifier.FractionOfBase * BaseValueOf(baseStats, modifier.Stat);
                AddTo(ref result, modifier.Stat, delta);
            }
        }

        private static float BaseValueOf(in CombatStats stats, ModifiedStat stat) => stat switch
        {
            ModifiedStat.AttackDamage => stats.AttackDamage,
            ModifiedStat.AbilityPower => stats.AbilityPower,
            ModifiedStat.PhysicalArmor => stats.PhysicalArmor,
            ModifiedStat.MagicResistance => stats.MagicResistance,
            ModifiedStat.PhysicalPenPercent => stats.PhysicalPenPercent,
            ModifiedStat.MagicPenPercent => stats.MagicPenPercent,
            ModifiedStat.SkillHaste => stats.SkillHaste,
            ModifiedStat.TenacityPercent => stats.TenacityPercent,
            ModifiedStat.OmnivampPercent => stats.OmnivampPercent,
            ModifiedStat.CritChancePercent => stats.CritChancePercent,
            _ => 0f,
        };

        private static void AddTo(ref CombatStats stats, ModifiedStat stat, float delta)
        {
            switch (stat)
            {
                case ModifiedStat.AttackDamage:
                    stats.AttackDamage += delta;
                    break;
                case ModifiedStat.AbilityPower:
                    stats.AbilityPower += delta;
                    break;
                case ModifiedStat.PhysicalArmor:
                    stats.PhysicalArmor += delta;
                    break;
                case ModifiedStat.MagicResistance:
                    stats.MagicResistance += delta;
                    break;
                case ModifiedStat.PhysicalPenPercent:
                    stats.PhysicalPenPercent += delta;
                    break;
                case ModifiedStat.MagicPenPercent:
                    stats.MagicPenPercent += delta;
                    break;
                case ModifiedStat.SkillHaste:
                    stats.SkillHaste += delta;
                    break;
                case ModifiedStat.TenacityPercent:
                    stats.TenacityPercent += delta;
                    break;
                case ModifiedStat.OmnivampPercent:
                    stats.OmnivampPercent += delta;
                    break;
                case ModifiedStat.CritChancePercent:
                    stats.CritChancePercent += delta;
                    break;
            }
        }

        /// <summary>
        /// The 캡·검증 step. Damage-dealing stats are floored at zero: a negative attack damage
        /// would heal whatever it hit. Resistances are deliberately NOT floored - 방어 감소
        /// pushing a resistance below zero is a real effect, and DamageMath has a branch for it.
        /// </summary>
        private static void ApplyCaps(ref CombatStats stats, in StatCaps caps)
        {
            stats.AttackDamage = AtLeastZero(stats.AttackDamage);
            stats.AbilityPower = AtLeastZero(stats.AbilityPower);
            stats.MaxHealth = AtLeastZero(stats.MaxHealth);
            stats.OmnivampPercent = AtLeastZero(stats.OmnivampPercent);
            stats.OnHitLifestealPercent = AtLeastZero(stats.OnHitLifestealPercent);

            stats.CritChancePercent = Clamp(stats.CritChancePercent, 0f, caps.MaxCritChancePercent);
            stats.PhysicalPenPercent = Clamp(stats.PhysicalPenPercent, 0f, caps.MaxPenetrationPercent);
            stats.MagicPenPercent = Clamp(stats.MagicPenPercent, 0f, caps.MaxPenetrationPercent);
            stats.TenacityPercent = Clamp(stats.TenacityPercent, 0f, caps.MaxTenacityPercent);
            stats.SkillHaste = Clamp(stats.SkillHaste, 0f, caps.MaxSkillHaste);
        }

        private static float AtLeastZero(float value) => value < 0f ? 0f : value;

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;
    }
}
