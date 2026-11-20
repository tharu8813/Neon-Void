using System.Collections.Generic;
using NeonVoid.Combat.Status;

namespace NeonVoid.Combat.Stats
{
    /// <summary>
    /// Turns the status effects an actor is carrying into stat contributions and a movement
    /// multiplier.
    /// <para>
    /// Until this existed the status catalogue was decorative. Applying 강화 tracked a buff with
    /// a duration and a stack count that nothing read, so the attack damage never changed; the
    /// 부식 표식 a plugin had just been verified as applying reduced no armour at all. The
    /// container knew; the damage pipeline never asked.
    /// </para>
    /// <para>
    /// The mapping lives here, in one tested place, for the same reason <see cref="SkillGate"/>
    /// keeps the control effects together: a call site that resolved one of these itself would
    /// get one of them subtly wrong, and a status that modifies nothing is indistinguishable
    /// from a status that is not applied.
    /// </para>
    /// <para>
    /// Magnitudes are read from the container, which already applied the stacking policy, so
    /// 중첩 상한 is the catalogue's business and not repeated here.
    /// </para>
    /// </summary>
    public static class StatusStatContribution
    {
        /// <summary>
        /// [초안] how much one stack of 강화 adds, as a fraction of base attack damage. The
        /// magnitude carried on the instance scales this, so a stronger source hits harder.
        /// </summary>
        public const float EmpowerFractionPerMagnitude = 1f;

        /// <summary>[초안] 나약함: the same, downward.</summary>
        public const float WeakenFractionPerMagnitude = 1f;

        /// <summary>
        /// [초안] 방화벽 차단: resistance points removed per unit of magnitude, FLAT.
        /// <para>
        /// Flat and not proportional, and that is the whole point. A percentage reduction does
        /// nothing at all to a target with no armour, and every enemy in the prototype has
        /// zero - so the mark applied, stacked, displayed and reduced exactly nothing, with no
        /// error anywhere. A percentage of nothing is nothing.
        /// </para>
        /// <para>
        /// docs/03 G12 says 물리·마법 방어 감소 중첩 and leaves 중첩 상한·재적용 규칙
        /// [미확정]; it does not say flat or proportional. Flat is the reading that still works
        /// on an unarmoured target, which is the only kind the game currently has.
        /// </para>
        /// </summary>
        public const float FirewallBreachArmorPerMagnitude = 1f;

        /// <summary>[초안] 스킬 가속 granted per unit of magnitude.</summary>
        public const float SkillHastePerMagnitude = 1f;

        /// <summary>[초안] 발사속도 증가/감소: fraction of base attack rate per unit of magnitude.</summary>
        public const float AttackSpeedFractionPerMagnitude = 1f;

        /// <summary>[초안] 탄환속도 증가: fraction of base projectile speed per unit of magnitude.</summary>
        public const float ProjectileSpeedFractionPerMagnitude = 1f;

        /// <summary>
        /// [초안] 기능고장 is 공격속도 감소 plus a chance the shot fails. docs/02 gives one
        /// magnitude and does not split it, so the same number drives both: a magnitude of 0.3
        /// is a 30% slower attack and a 30% chance of nothing coming out.
        /// </summary>
        public const float MalfunctionFailureChancePerMagnitude = 100f;

        /// <summary>[초안] extra targets one stack of 관통 adds to a shot.</summary>
        public const int PierceTargetsPerStack = 1;

        /// <summary>
        /// Appends contributions for every status present. Nothing is appended for a status
        /// that only denies actions - 정지, 속박 and 제한 are the skill gate's job, not a stat's.
        /// </summary>
        public static void Collect(StatusContainer status, List<StatModifier> into)
        {
            if (status == null || into == null)
                return;

            IReadOnlyList<StatusInstance> instances = status.Instances;
            for (int i = 0; i < instances.Count; ++i)
            {
                StatusInstance instance = instances[i];
                float magnitude = instance.TotalMagnitude;

                switch (instance.Id)
                {
                    case StatusEffectId.Empower:
                        into.Add(StatModifier.Scale(
                            ModifiedStat.AttackDamage, magnitude * EmpowerFractionPerMagnitude));
                        break;

                    case StatusEffectId.Weaken:
                        into.Add(StatModifier.Scale(
                            ModifiedStat.AttackDamage, -magnitude * WeakenFractionPerMagnitude));
                        break;

                    case StatusEffectId.SkillHaste:
                        into.Add(StatModifier.Add(
                            ModifiedStat.SkillHaste, magnitude * SkillHastePerMagnitude));
                        break;

                    case StatusEffectId.FirewallBreach:
                        // 물리·마법 방어 감소: both, which is what docs/03 G12 asks for.
                        // Flat points, so an unarmoured target is pushed below zero rather
                        // than left untouched. DamageMath amplifies from there.
                        float points = -magnitude * FirewallBreachArmorPerMagnitude;
                        into.Add(StatModifier.Add(ModifiedStat.PhysicalArmor, points));
                        into.Add(StatModifier.Add(ModifiedStat.MagicResistance, points));
                        break;
                }
            }
        }

        /// <summary>
        /// Fraction of base attack rate this actor may use: 발사속도 증가 raises it, 발사속도
        /// 감소 and 기능고장 lower it.
        /// <para>
        /// Added rather than multiplied together, so two sources cannot compound into a figure
        /// nobody tuned, and floored at zero rather than allowed to invert.
        /// </para>
        /// </summary>
        public static float AttackSpeedMultiplier(StatusContainer status)
        {
            if (status == null)
                return 1f;

            float multiplier = 1f
                + status.TotalMagnitudeOf(StatusEffectId.AttackSpeedUp) * AttackSpeedFractionPerMagnitude
                - status.TotalMagnitudeOf(StatusEffectId.AttackSpeedDown) * AttackSpeedFractionPerMagnitude
                - status.TotalMagnitudeOf(StatusEffectId.Malfunction) * AttackSpeedFractionPerMagnitude;

            if (float.IsNaN(multiplier) || multiplier < 0f)
                return 0f;

            return multiplier;
        }

        /// <summary>Fraction of base projectile speed, from 탄환속도 증가.</summary>
        public static float ProjectileSpeedMultiplier(StatusContainer status)
        {
            if (status == null)
                return 1f;

            float multiplier = 1f
                + status.TotalMagnitudeOf(StatusEffectId.ProjectileSpeedUp)
                * ProjectileSpeedFractionPerMagnitude;

            if (float.IsNaN(multiplier) || multiplier <= 0f)
                return 1f;

            return multiplier;
        }

        /// <summary>
        /// Extra distinct targets a shot may damage, from 관통.
        /// <para>
        /// [확정] docs/02 section 5 is explicit that 관통 lets projectiles pierce but one
        /// projectile still may not hit one enemy twice - which is the ledger's job, not this
        /// number's. All this does is raise how many different enemies one shot may reach.
        /// </para>
        /// </summary>
        public static int ExtraPierceTargets(StatusContainer status)
        {
            if (status == null)
                return 0;

            int stacks = status.StacksOf(StatusEffectId.Pierce);
            return stacks > 0 ? stacks * PierceTargetsPerStack : 0;
        }

        /// <summary>
        /// Chance in percent that a shot simply fails, from 기능고장.
        /// <para>
        /// Rolled by the attacker from its own combat stream, so a malfunctioning weapon is
        /// reproducible rather than merely unlucky.
        /// </para>
        /// </summary>
        public static float ShotFailureChancePercent(StatusContainer status)
        {
            if (status == null)
                return 0f;

            float chance = status.TotalMagnitudeOf(StatusEffectId.Malfunction)
                           * MalfunctionFailureChancePerMagnitude;

            if (float.IsNaN(chance) || chance <= 0f)
                return 0f;

            return chance > 100f ? 100f : chance;
        }

        /// <summary>
        /// The movement multiplier 구속 and 이동속도 증가 produce, with 정지 and 속박 handled as
        /// a complete stop.
        /// <para>
        /// One function for both sides. The enemy agent was already reading 구속 off its own
        /// container while the player's movement read nothing at all, so a slowed player moved
        /// at full speed - a rule that applied to one side only.
        /// </para>
        /// </summary>
        public static float MoveSpeedMultiplier(StatusContainer status)
        {
            if (status == null)
                return 1f;

            if (status.Has(StatusEffectId.Stun) || status.Has(StatusEffectId.Root))
                return 0f;

            float multiplier = 1f + status.TotalMagnitudeOf(StatusEffectId.Haste)
                                  - status.TotalMagnitudeOf(StatusEffectId.Slow);

            if (float.IsNaN(multiplier) || multiplier < 0f)
                return 0f;

            return multiplier;
        }
    }
}
