using System;

namespace NeonVoid.Combat
{
    /// <summary>
    /// The combat stat block from docs/02 section 2.
    /// <para>
    /// Status tags on each field are the document's, not a suggestion: <b>[확정]</b> values are
    /// approved and must not be changed as tuning, <b>[초안]</b> values are provisional. The
    /// distinction is kept in code so a later reader cannot mistake one for the other.
    /// </para>
    /// <para>
    /// This is a plain value type with no Unity dependency. The derivation order from docs/02
    /// (base, then approved permanent investment, then plugins and buffs, then caps) belongs to
    /// the Progression and Plugins domains; this struct is only the resulting snapshot a single
    /// hit is resolved against.
    /// </para>
    /// </summary>
    [Serializable]
    public struct CombatStats
    {
        // --- Offence -------------------------------------------------------------------
        public float AttackDamage;              // [확정] start 5
        public float AbilityPower;              // [확정] start 0
        public float CritDamageMultiplier;      // [확정] start 1.20
        public float CritChancePercent;         // [초안] start 0

        // --- Penetration ---------------------------------------------------------------
        public float PhysicalPenFlat;           // [초안] start 0
        public float PhysicalPenPercent;        // [초안] start 0, expressed as 0..1
        public float MagicPenFlat;              // [초안] start 0
        public float MagicPenPercent;           // [초안] start 0, expressed as 0..1

        // --- Defence -------------------------------------------------------------------
        public float PhysicalArmor;             // [초안] start 0, may be negative
        public float MagicResistance;           // [초안] start 0, may be negative
        public float MaxHealth;                 // [초안] start 100

        // --- Sustain -------------------------------------------------------------------
        public float OnHitLifestealPercent;     // [확정] start 0
        public float OmnivampPercent;           // [확정] start 0

        // --- Skills and control ---------------------------------------------------------
        /// <summary>
        /// [초안] start 0. Haste points, not a percentage: the cooldown formula is
        /// base / (1 + H/100), so 100 haste halves a cooldown rather than removing it.
        /// </summary>
        public float SkillHaste;

        /// <summary>
        /// [초안] start 0. Shortens CONTROL status effects only; see TenacityMath, which
        /// refuses to apply it to damage over time or stat reductions.
        /// </summary>
        public float TenacityPercent;

        /// <summary>
        /// The documented starting block. Offence values carry their [확정] status; the rest
        /// are [초안] and exist so tests have something concrete to run against.
        /// </summary>
        public static CombatStats StartingVaccine() => new CombatStats
        {
            AttackDamage = 5f,
            AbilityPower = 0f,
            CritDamageMultiplier = 1.20f,
            CritChancePercent = 0f,
            PhysicalPenFlat = 0f,
            PhysicalPenPercent = 0f,
            MagicPenFlat = 0f,
            MagicPenPercent = 0f,
            PhysicalArmor = 0f,
            MagicResistance = 0f,
            MaxHealth = 100f,
            OnHitLifestealPercent = 0f,
            OmnivampPercent = 0f,
            SkillHaste = 0f,
            TenacityPercent = 0f,
        };

        /// <summary>Resistance consulted for the given damage type. True damage reads none.</summary>
        public float ResistanceFor(DamageType type) => type switch
        {
            DamageType.Physical => PhysicalArmor,
            DamageType.Magic => MagicResistance,
            _ => 0f,
        };

        /// <summary>Flat penetration applied against the given damage type.</summary>
        public float FlatPenetrationFor(DamageType type) => type switch
        {
            DamageType.Physical => PhysicalPenFlat,
            DamageType.Magic => MagicPenFlat,
            _ => 0f,
        };

        /// <summary>Percent penetration (0..1) applied against the given damage type.</summary>
        public float PercentPenetrationFor(DamageType type) => type switch
        {
            DamageType.Physical => PhysicalPenPercent,
            DamageType.Magic => MagicPenPercent,
            _ => 0f,
        };

        /// <summary>
        /// docs/02 section 2 requires negative, NaN and infinite stats to be rejected rather
        /// than fed into the damage formula, where one NaN would silently make every later
        /// number NaN. Resistances are exempt: they are allowed to go negative by design.
        /// </summary>
        public void Validate()
        {
            RequireFinite(AttackDamage, nameof(AttackDamage));
            RequireFinite(AbilityPower, nameof(AbilityPower));
            RequireFinite(CritDamageMultiplier, nameof(CritDamageMultiplier));
            RequireFinite(CritChancePercent, nameof(CritChancePercent));
            RequireFinite(PhysicalArmor, nameof(PhysicalArmor), allowNegative: true);
            RequireFinite(MagicResistance, nameof(MagicResistance), allowNegative: true);
            RequireFinite(MaxHealth, nameof(MaxHealth));
            RequireFinite(OnHitLifestealPercent, nameof(OnHitLifestealPercent));
            RequireFinite(OmnivampPercent, nameof(OmnivampPercent));
            RequireFinite(SkillHaste, nameof(SkillHaste));
            RequireFinite(TenacityPercent, nameof(TenacityPercent));
            RequireFinite(PhysicalPenFlat, nameof(PhysicalPenFlat));
            RequireFinite(MagicPenFlat, nameof(MagicPenFlat));
            RequireFraction(PhysicalPenPercent, nameof(PhysicalPenPercent));
            RequireFraction(MagicPenPercent, nameof(MagicPenPercent));

            if (CritDamageMultiplier < 1f)
                throw new InvalidOperationException(
                    $"{nameof(CritDamageMultiplier)} below 1 would make a critical hit weaker than a normal one.");
            if (MaxHealth <= 0f)
                throw new InvalidOperationException($"{nameof(MaxHealth)} must be greater than zero.");
        }

        private static void RequireFinite(float value, string name, bool allowNegative = false)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidOperationException($"{name} must be a finite number.");
            if (!allowNegative && value < 0f)
                throw new InvalidOperationException($"{name} must not be negative.");
        }

        private static void RequireFraction(float value, string name)
        {
            RequireFinite(value, name);
            if (value > 1f)
                throw new InvalidOperationException($"{name} is a fraction in 0..1, not a percentage.");
        }
    }
}
