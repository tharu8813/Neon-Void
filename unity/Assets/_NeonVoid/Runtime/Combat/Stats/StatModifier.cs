using System;

namespace NeonVoid.Combat.Stats
{
    /// <summary>
    /// A stat that buffs, debuffs and plugins may change. Only stats something can actually
    /// modify are listed; adding one here is a deliberate act, not a side effect of adding a
    /// field to <see cref="CombatStats"/>.
    /// </summary>
    public enum ModifiedStat
    {
        AttackDamage = 0,
        AbilityPower = 1,
        PhysicalArmor = 2,
        MagicResistance = 3,
        PhysicalPenPercent = 4,
        MagicPenPercent = 5,
        SkillHaste = 6,
        TenacityPercent = 7,
        OmnivampPercent = 8,
        CritChancePercent = 9,
    }

    /// <summary>
    /// One contribution to a stat: a flat amount, a fraction of the base, or both.
    /// <para>
    /// Both forms exist because the design uses both and docs/02 leaves which one each effect
    /// uses as [초안] - 이동속도 증가 is written as "가산/배수 [초안]". Keeping them separate
    /// means retuning an effect from flat to proportional is a data change.
    /// </para>
    /// <para>
    /// <see cref="FractionOfBase"/> is a fraction of the <b>base</b> value, never of the running
    /// total. Multiplying running totals makes the result depend on the order contributions
    /// happen to arrive in, which for a status container means it depends on what the player
    /// walked into first.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct StatModifier
    {
        public readonly ModifiedStat Stat;
        public readonly float Flat;
        public readonly float FractionOfBase;

        public StatModifier(ModifiedStat stat, float flat = 0f, float fractionOfBase = 0f)
        {
            Stat = stat;
            Flat = flat;
            FractionOfBase = fractionOfBase;
        }

        public static StatModifier Add(ModifiedStat stat, float flat) => new StatModifier(stat, flat);

        public static StatModifier Scale(ModifiedStat stat, float fractionOfBase) =>
            new StatModifier(stat, 0f, fractionOfBase);

        /// <summary>
        /// [확정] docs/02 section 2 rejects NaN, infinity and invalid ids outright rather than
        /// letting them through to be clamped later - a NaN that reaches a stat poisons every
        /// comparison downstream, including the ones that decide whether an actor is alive.
        /// </summary>
        public bool IsUsable =>
            Enum.IsDefined(typeof(ModifiedStat), Stat)
            && !float.IsNaN(Flat) && !float.IsInfinity(Flat)
            && !float.IsNaN(FractionOfBase) && !float.IsInfinity(FractionOfBase);

        public override string ToString() =>
            $"{Stat} {(Flat != 0f ? $"{Flat:+0.##;-0.##}" : string.Empty)}" +
            $"{(FractionOfBase != 0f ? $" {FractionOfBase:+0.##%;-0.##%} of base" : string.Empty)}";
    }
}
