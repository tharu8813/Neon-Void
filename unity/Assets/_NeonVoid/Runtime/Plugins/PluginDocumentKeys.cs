using System;
using System.Collections.Generic;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// The stable key a plugin is known by <b>outside</b> combat - in a save file, in a receipt,
    /// in anything that outlives this build.
    /// <para>
    /// <b>Why these are not docs/03's ids.</b> G01, R06, L01 are 제안 ID - development labels on
    /// a document whose own card names are [초안]. docs/04 section 5 says in as many words that
    /// writing one into a save would make a provisional id permanent by accident, which is why
    /// the save held opaque instance ids and nothing about what an instance was. The answer is
    /// not to keep the save ignorant forever: it is a second id that is deliberately stable,
    /// which is exactly what [확정] D-009 already did for skills (<c>skill.arc_pulse</c>).
    /// Renaming a card, renumbering a proposal, or reshuffling docs/03 changes nothing here.
    /// </para>
    /// <para>
    /// <b>These keys are permanent.</b> A key that is written into a player's archive and later
    /// changed turns their learned plugin into one the game cannot name. A test pins the whole
    /// table for that reason: changing one is a deliberate act with a migration behind it, not
    /// a rename nobody noticed.
    /// </para>
    /// </summary>
    public static class PluginDocumentKeys
    {
        /// <summary>Every key starts with this, so a key is recognisable on sight in a file.</summary>
        public const string Prefix = "plugin.";

        private static readonly Dictionary<string, string> ByProposalId =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // --- 일반 (17) ----------------------------------------------------------------
                { "G01", Prefix + "random_patch" },
                { "G02", Prefix + "lightweight" },
                { "G03", Prefix + "expanded_warhead" },
                { "G04", Prefix + "weakness_analysis" },
                { "G05", Prefix + "periodic_barrier" },
                { "G06", Prefix + "throttle_amplifier" },
                { "G07", Prefix + "execution_barrier" },
                { "G08", Prefix + "movement_buffer" },
                { "G09", Prefix + "emergency_recovery" },
                { "G10", Prefix + "arc_chain" },
                { "G11", Prefix + "untouched_acceleration" },
                { "G12", Prefix + "corrosion_mark" },
                { "G13", Prefix + "quad_cache" },
                { "G14", Prefix + "skill_response" },
                { "G15", Prefix + "rare_acquisition" },
                { "G16", Prefix + "dash_detonation" },
                { "G17", Prefix + "detonation_glyph" },

                // --- 레어 (7) -----------------------------------------------------------------
                { "R01", Prefix + "execution_algorithm" },
                { "R02", Prefix + "kill_stack" },
                { "R03", Prefix + "crisis_repulse" },
                { "R04", Prefix + "dismantle_protocol" },
                { "R05", Prefix + "dash_barrier" },
                { "R06", Prefix + "support_round" },
                { "R07", Prefix + "seeker_round" },

                // --- 전설 (3) -----------------------------------------------------------------
                // Present even though [확정] docs/03 forbids learning them: a key is what a card
                // IS, and a receipt or a log may name one. What may be archived is decided where
                // the learning happens, not by leaving a card nameless here.
                { "L01", Prefix + "laser_core" },
                { "L02", Prefix + "boomerang_protocol" },
                { "L03", Prefix + "fever_time" },
            };

        private static readonly Dictionary<string, string> ByKey = BuildInverse();

        /// <summary>The stable key for a card, or empty when it has none.</summary>
        public static string KeyFor(string proposalId)
        {
            if (string.IsNullOrEmpty(proposalId))
                return string.Empty;

            return ByProposalId.TryGetValue(proposalId, out string key) ? key : string.Empty;
        }

        /// <summary>
        /// The card a key names, or empty. Empty for a key written by a <b>later</b> build, which
        /// is a case that has to be survivable: the archive is the player's and this build may
        /// simply not know one of their plugins yet.
        /// </summary>
        public static string ProposalIdFor(string documentKey)
        {
            if (string.IsNullOrEmpty(documentKey))
                return string.Empty;

            return ByKey.TryGetValue(documentKey, out string proposalId) ? proposalId : string.Empty;
        }

        /// <summary>Whether this build knows a card by that key.</summary>
        public static bool Knows(string documentKey) =>
            !string.IsNullOrEmpty(documentKey) && ByKey.ContainsKey(documentKey);

        /// <summary>Whether a card has a key at all.</summary>
        public static bool HasKey(string proposalId) => KeyFor(proposalId).Length > 0;

        /// <summary>How many cards are named here. Held by a test against the catalogue.</summary>
        public static int Count => ByProposalId.Count;

        /// <summary>Every key, for a test that pins the table.</summary>
        public static IEnumerable<KeyValuePair<string, string>> All => ByProposalId;

        private static Dictionary<string, string> BuildInverse()
        {
            var inverse = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in ByProposalId)
            {
                if (inverse.ContainsKey(pair.Value))
                {
                    // Two cards sharing a key would make one of them unrecoverable from an
                    // archive. Thrown at first use rather than silently keeping one.
                    throw new InvalidOperationException(
                        $"'{pair.Value}' names more than one plugin, so an archived instance " +
                        "could not say which it is.");
                }

                inverse.Add(pair.Value, pair.Key);
            }

            return inverse;
        }
    }
}
