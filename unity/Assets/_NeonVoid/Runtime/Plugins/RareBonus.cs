using System.Collections.Generic;
using NeonVoid.Core;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// [확정] D-003 (2026-11-22): G15 희귀 획득 gives **one** 레어, nothing when there is no
    /// valid candidate, and never twice on one receipt.
    /// <para>
    /// A plain function over two lists so the two cases that matter - an exhausted pool and a
    /// vaccine that already carries every 레어 - can be driven without a scene. The pool being
    /// empty is not an error: docs/03's own exception column names 후보 고갈 as a case, and
    /// giving nothing is the approved answer.
    /// </para>
    /// </summary>
    public static class RareBonus
    {
        /// <summary>
        /// One 레어 the carrier does not already have, drawn from <paramref name="rng"/>, or a
        /// default definition when there is none.
        /// </summary>
        /// <param name="pool">Every authored card. Only the 레어 are considered.</param>
        /// <param name="owned">What the vaccine carries, including the G15 that is granting.</param>
        public static PluginDefinition Pick(
            IReadOnlyList<PluginDefinition> pool,
            IReadOnlyList<PluginDefinition> owned,
            DeterministicRng rng)
        {
            int eligible = Count(pool, owned);
            if (eligible == 0)
                return default;

            // With no stream to draw from the first eligible card is taken rather than none:
            // a bonus that silently depended on a caller passing an RNG would be a card that
            // works in the game and not in a test, or the other way round.
            int wanted = rng != null ? rng.NextInt(0, eligible) : 0;

            for (int i = 0; i < pool.Count; ++i)
            {
                if (!IsEligible(pool[i], owned))
                    continue;

                if (wanted == 0)
                    return pool[i];

                --wanted;
            }

            return default;
        }

        /// <summary>How many 레어 the carrier could still be given.</summary>
        public static int Count(IReadOnlyList<PluginDefinition> pool, IReadOnlyList<PluginDefinition> owned)
        {
            if (pool == null)
                return 0;

            int eligible = 0;
            for (int i = 0; i < pool.Count; ++i)
            {
                if (IsEligible(pool[i], owned))
                    ++eligible;
            }

            return eligible;
        }

        private static bool IsEligible(in PluginDefinition candidate, IReadOnlyList<PluginDefinition> owned)
        {
            if (candidate.Rarity != PluginRarity.Rare || string.IsNullOrEmpty(candidate.ProposalId))
                return false;

            // A bonus that granted another granting card would make one pick a chain of them.
            // Nothing in the 레어 tier does that today; refusing it here means nothing can.
            if (candidate.Effect == PluginEffectKind.GrantRandomRare)
                return false;

            if (owned == null)
                return true;

            for (int i = 0; i < owned.Count; ++i)
            {
                if (owned[i].ProposalId == candidate.ProposalId)
                    return false;
            }

            return true;
        }
    }
}
