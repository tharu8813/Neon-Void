namespace NeonVoid.Combat.Status
{
    /// <summary>
    /// [초안] How a second application of an effect combines with the one already running.
    /// <para>
    /// docs/02 section 5 is explicit that this must be per-effect data and that hardcoding one
    /// behaviour for every status is wrong. The policy therefore lives on the definition, and
    /// the container only executes whichever one it is handed.
    /// </para>
    /// </summary>
    public enum StatusStackPolicy
    {
        /// <summary>
        /// One instance; reapplying resets its timer. The usual slow or silence.
        /// </summary>
        RefreshDuration = 0,

        /// <summary>
        /// Every application is its own instance with its own timer, and they expire
        /// separately. Magnitudes add while they overlap.
        /// </summary>
        IndependentStacks = 1,

        /// <summary>
        /// One instance with a stack count, bounded by MaxStacks. Magnitude scales with the
        /// count and reapplying also refreshes the timer.
        /// </summary>
        AdditiveStacks = 2,

        /// <summary>
        /// One instance; a new application replaces it only when it is stronger. A weaker
        /// application is ignored outright and does not refresh the timer, so a weak source
        /// cannot keep a strong effect alive forever.
        /// </summary>
        ReplaceStronger = 3,

        /// <summary>
        /// First application wins; later ones are ignored entirely, timer included.
        /// </summary>
        Unique = 4,

        /// <summary>
        /// Stacks without an upper bound. docs/02 section 6 names unbounded Dismantle against
        /// thousands of enemies as a specific performance risk, so the count is tracked and
        /// exposed rather than quietly capped: capping it would change a game rule, and the
        /// document forbids dropping rules silently to save frame time.
        /// </summary>
        UnboundedAggregated = 5,
    }
}
