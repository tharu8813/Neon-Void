namespace NeonVoid.Combat
{
    /// <summary>
    /// The outcome of one resolved hit, before shields.
    /// <para>
    /// <see cref="MitigationMultiplier"/> is kept separate from the final number because
    /// docs/02 section 3 requires the UI to show the real reduction rather than the internal
    /// resistance value, and a HUD that recomputed it from raw and applied would drift from
    /// the pipeline.
    /// </para>
    /// </summary>
    public readonly struct DamageResolution
    {
        /// <summary>Damage before resistance, after the critical multiplier.</summary>
        public readonly float Raw;

        /// <summary>Fraction of <see cref="Raw"/> that survived resistance. 1.0 for true damage.</summary>
        public readonly float MitigationMultiplier;

        /// <summary>Damage that reaches the target, before any shield absorbs it.</summary>
        public readonly float AppliedBeforeShield;

        /// <summary>Whether this hit both could crit and did.</summary>
        public readonly bool IsCritical;

        public readonly DamageType Type;

        public DamageResolution(
            float raw,
            float mitigationMultiplier,
            float appliedBeforeShield,
            bool isCritical,
            DamageType type)
        {
            Raw = raw;
            MitigationMultiplier = mitigationMultiplier;
            AppliedBeforeShield = appliedBeforeShield;
            IsCritical = isCritical;
            Type = type;
        }

        /// <summary>
        /// Reduction as a percentage, for display. Positive means damage was reduced, negative
        /// means it was amplified by negative resistance.
        /// </summary>
        public float ReductionPercent => (1f - MitigationMultiplier) * 100f;

        public override string ToString() =>
            $"{Type} raw={Raw:0.##} x{MitigationMultiplier:0.###} -> {AppliedBeforeShield:0.##}" +
            (IsCritical ? " (crit)" : string.Empty);
    }
}
