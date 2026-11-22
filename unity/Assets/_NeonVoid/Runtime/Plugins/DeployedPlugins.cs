using System.Collections.Generic;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// Where the plugins a 의뢰 starts with come from.
    /// <para>
    /// An interface the Plugins side declares and something else implements, for the reason
    /// <c>IMissionCommitGate</c> exists: what the vaccine is carrying lives in the save, and
    /// Plugins must not reach into Progression.Save to get it. The implementation registers
    /// itself with <see cref="PluginProcDirector"/>, so a scene with no save has no source and
    /// the director says so rather than silently starting empty.
    /// </para>
    /// </summary>
    public interface IDeployedPluginSource
    {
        /// <summary>Whether this source can answer at all. False with no save connected.</summary>
        bool Available { get; }

        /// <summary>
        /// The instance ids the vaccine is 출전 소지 with. [확정] docs/04 section 3.3: 출전
        /// 개수 제한 없음, so this is however many there are.
        /// </summary>
        IReadOnlyList<string> DeployedInstanceIds { get; }
    }

    /// <summary>
    /// Turns the ids the save holds into the cards combat resolves against.
    /// <para>
    /// <b>Why the save does not hold the cards.</b> docs/04 keeps everything about what a plugin
    /// IS out of the player's file - the file holds identity and ownership. So a mission start is
    /// a lookup: each id names a definition through its document key, and this build's catalogue
    /// says what that definition is.
    /// </para>
    /// <para>
    /// <b>An id this build cannot name is counted, not dropped quietly.</b> It is either older
    /// than the document keys or newer than this build, and either way the player owns something
    /// that will not be in their hands this run. That is worth one line in the log rather than a
    /// loadout that is silently shorter than the terminal said.
    /// </para>
    /// Pure C#: no Unity types, so a mission's starting loadout is testable without a scene.
    /// </summary>
    public static class DeployedPlugins
    {
        /// <summary>What <see cref="Resolve"/> found.</summary>
        public readonly struct Resolution
        {
            internal Resolution(List<PluginDefinition> cards, int unnamed, int unknown)
            {
                _cards = cards;
                Unnamed = unnamed;
                Unknown = unknown;
            }

            private readonly List<PluginDefinition> _cards;

            /// <summary>The cards to grant, in the order the save listed them.</summary>
            public IReadOnlyList<PluginDefinition> Cards =>
                (IReadOnlyList<PluginDefinition>)_cards ?? System.Array.Empty<PluginDefinition>();

            /// <summary>Ids carrying no document key at all - written before the keys existed.</summary>
            public int Unnamed { get; }

            /// <summary>Ids whose key this build does not know - written by a later build.</summary>
            public int Unknown { get; }

            /// <summary>Owned instances that will not be in the player's hands this run.</summary>
            public int Missing => Unnamed + Unknown;

            public override string ToString() =>
                Missing == 0
                    ? $"{Cards.Count}종 출전"
                    : $"{Cards.Count}종 출전, {Missing}개는 이 빌드가 알지 못함";
        }

        /// <summary>
        /// Resolves the ids into definitions. Never throws and never invents a card: an id it
        /// cannot name is counted and left out.
        /// </summary>
        public static Resolution Resolve(IReadOnlyList<string> instanceIds)
        {
            var cards = new List<PluginDefinition>();
            int unnamed = 0;
            int unknown = 0;

            if (instanceIds == null)
                return new Resolution(cards, 0, 0);

            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();

            for (int i = 0; i < instanceIds.Count; ++i)
            {
                PluginInstanceId id = PluginInstanceId.Parse(instanceIds[i]);
                if (!id.NamesADefinition)
                {
                    unnamed++;
                    continue;
                }

                string proposalId = id.ProposalId;
                if (proposalId.Length == 0)
                {
                    unknown++;
                    continue;
                }

                PluginDefinition found = default;
                bool match = false;
                for (int c = 0; c < all.Count; ++c)
                {
                    if (all[c].ProposalId == proposalId)
                    {
                        found = all[c];
                        match = true;
                        break;
                    }
                }

                // A key this build knows but a catalogue that has no such card would be a
                // mismatch inside one build, not a save from another one. Counted as unknown
                // rather than thrown: the player's file is not the thing that is wrong.
                if (!match)
                {
                    unknown++;
                    continue;
                }

                cards.Add(found);
            }

            return new Resolution(cards, unnamed, unknown);
        }
    }
}
