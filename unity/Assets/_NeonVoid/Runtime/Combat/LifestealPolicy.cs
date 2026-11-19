using System;

namespace NeonVoid.Combat
{
    /// <summary>
    /// The parts of healing that docs/02 section 4 marks <b>[미확정]</b>, isolated so they can be
    /// switched when the design decides, and so no default silently becomes the rule.
    /// <para>
    /// CLAUDE.md requires undecided policy to live behind an interface that can wait for a
    /// decision. These are exactly those questions: does damage a shield swallowed count as
    /// damage dealt, does overkill beyond the target's remaining health count, and do on-hit
    /// lifesteal and omnivamp stack on one event. The defaults below are the conservative
    /// reading (heal only for damage that really landed on health) and are <b>not approved</b>.
    /// </para>
    /// <para>
    /// Nothing here may be wired to permanent rewards or saved progression before the decision
    /// is made.
    /// </para>
    /// </summary>
    [Serializable]
    public struct LifestealPolicy
    {
        /// <summary>
        /// [미확정] Whether damage absorbed by a shield counts as damage dealt for healing.
        /// Default false: only damage that reduced health heals.
        /// </summary>
        public bool CountShieldAbsorbedAsDealt;

        /// <summary>
        /// [미확정] Whether damage beyond the target's remaining health counts. Default false:
        /// a 1000 damage hit on a 10 health target heals as if it dealt 10.
        /// </summary>
        public bool CountOverkill;

        /// <summary>
        /// [미확정] Whether on-hit lifesteal and omnivamp both pay out on the same event.
        /// Default true, because docs/02 section 4 calls them separate resources and triggers;
        /// only the combination ORDER is listed as unresolved.
        /// </summary>
        public bool AllowOnHitAndOmnivampOnSameEvent;

        /// <summary>The conservative provisional reading. Not an approved decision.</summary>
        public static LifestealPolicy Draft() => new LifestealPolicy
        {
            CountShieldAbsorbedAsDealt = false,
            CountOverkill = false,
            AllowOnHitAndOmnivampOnSameEvent = true,
        };
    }
}
