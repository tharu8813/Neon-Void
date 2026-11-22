using System.Collections.Generic;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// Which carried plugins 터널감염 switches off.
    /// <para>
    /// [확정] docs/00 section 7: 터널감염(미학습 소지 플러그인 1~2개 비활성/역효과). [확정]
    /// docs/01 section 4: 터널감염은 <b>학습된 플러그인 제외</b>. So the eligible set is exactly
    /// what the vaccine picked up inside this 의뢰 - the cards it walked in with came off the
    /// 출전 목록 and are 학습된.
    /// </para>
    /// <para>
    /// <b>비활성, not 역효과.</b> docs/00 offers both and nothing chooses between them. Switching
    /// a card off is the one of the two that cannot be mistaken for a bug: a card whose effect
    /// quietly inverted would read as the plugin being broken rather than as the environment
    /// doing something. 역효과 stays available as a later reading and would reuse this choice of
    /// which cards are hit.
    /// </para>
    /// <para>
    /// <b>Deterministic from the area's seed.</b> Walking into the same area twice must infect
    /// the same cards, or the environment would be a reroll - and docs/01 section 6 check 4
    /// asks for an area to be reproducible from its key.
    /// </para>
    /// Pure C#: no Unity types.
    /// </summary>
    public static class TunnelInfection
    {
        /// <summary>[초안] docs/00 states 1~2.</summary>
        public const int DraftMinInfected = 1;

        /// <summary>[초안] the other end of 1~2.</summary>
        public const int DraftMaxInfected = 2;

        /// <summary>
        /// How many cards this area's infection takes, before looking at what is carried.
        /// </summary>
        /// <param name="seed">
        /// The area's own seed. Taken from the high bits for the reason AreaSeeds gives: the low
        /// bit of an xorshift state is the least mixed, and a 1-or-2 choice made from it would
        /// correlate with the map that state generates.
        /// </param>
        public static int CountFor(uint seed)
        {
            int span = DraftMaxInfected - DraftMinInfected + 1;
            return DraftMinInfected + (int)((seed >> 16) % (uint)span);
        }

        /// <summary>
        /// The proposal ids to switch off: up to <paramref name="count"/> of the carried cards
        /// that are not 학습된, chosen from the seed.
        /// <para>
        /// Fewer than asked for is a normal answer, not a failure. A player who walked in with
        /// only 출전 cards has nothing the infection may touch, and docs/01's exception is the
        /// reason - it is the environment being survived, not the environment misfiring.
        /// </para>
        /// </summary>
        public static List<string> Choose(PluginLoadout loadout, int count, uint seed)
        {
            var chosen = new List<string>();
            if (loadout == null || count <= 0)
                return chosen;

            var eligible = new List<string>();
            IReadOnlyList<PluginDefinition> carried = loadout.Carried;
            for (int i = 0; i < carried.Count; ++i)
            {
                string id = carried[i].ProposalId;
                if (!string.IsNullOrEmpty(id) && !loadout.IsLearned(id))
                    eligible.Add(id);
            }

            if (eligible.Count == 0)
                return chosen;

            // Walked from a seeded offset with a stride that is coprime with nothing in
            // particular - a plain step would always start at the card the player picked up
            // first, which is the one they have had longest and would feel like a rule.
            int at = (int)((seed >> 8) % (uint)eligible.Count);
            int take = count < eligible.Count ? count : eligible.Count;

            for (int i = 0; i < take; ++i)
            {
                chosen.Add(eligible[at]);
                at = (at + 1) % eligible.Count;
            }

            return chosen;
        }
    }
}
