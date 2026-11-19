namespace NeonVoid.Combat
{
    /// <summary>
    /// [확정] The three damage types from docs/02 section 3.
    /// </summary>
    public enum DamageType
    {
        /// <summary>Reduced by physical armor; affected by physical penetration.</summary>
        Physical = 0,

        /// <summary>Reduced by magic resistance; affected by magic penetration.</summary>
        Magic = 1,

        /// <summary>
        /// [확정] Bypasses both ordinary resistances and both penetrations. Penetration
        /// against a resist that is not consulted would be meaningless, so true damage reads
        /// neither.
        /// </summary>
        True = 2,
    }
}
