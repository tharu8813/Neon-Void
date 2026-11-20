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
        /// [초안] 방화벽 차단: fraction of base resistance removed per unit of magnitude. The
        /// result may go below zero, which DamageMath handles as amplified damage.
        /// </summary>
        public const float FirewallBreachFractionPerMagnitude = 1f;

        /// <summary>[초안] 스킬 가속 granted per unit of magnitude.</summary>
        public const float SkillHastePerMagnitude = 1f;

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
                        float fraction = -magnitude * FirewallBreachFractionPerMagnitude;
                        into.Add(StatModifier.Scale(ModifiedStat.PhysicalArmor, fraction));
                        into.Add(StatModifier.Scale(ModifiedStat.MagicResistance, fraction));
                        break;
                }
            }
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
