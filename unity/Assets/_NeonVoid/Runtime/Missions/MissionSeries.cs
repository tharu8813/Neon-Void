using System;
using System.Globalization;

namespace NeonVoid.Missions
{
    /// <summary>
    /// One session plays more than one mission, and this is what tells them apart.
    /// <para>
    /// Until the hub existed a session was a mission: the run id came from the slot bootstrap
    /// once and the seed was picked once in <c>Awake</c>. A hub the player comes back to breaks
    /// both. [확정] docs/04 section 5 forbids 같은 runId 중복 시작 and makes runId the key a
    /// death receipt is written under, so a second mission reusing the first one's id would have
    /// its first death refused as one already settled - no clone would ever be charged again.
    /// And [확정] docs/00: 사망하면 다시 시작한 의뢰의 맵과 적은 새로 생성, which a reused seed
    /// would rebuild identically.
    /// </para>
    /// <para>
    /// <b>The ordinal is which mission of this session it is</b>, counting from 1. Both answers
    /// are functions of it, so a retry of the same mission asks for the same id and gets the one
    /// already allocated instead of burning another.
    /// </para>
    /// Pure C#: no Unity types, no state.
    /// </summary>
    public static class MissionSeries
    {
        /// <summary>The first mission of a session.</summary>
        public const int FirstOrdinal = 1;

        /// <summary>
        /// An odd constant with a well-spread bit pattern, as <see cref="AreaSeeds"/> uses for
        /// the same job.
        /// </summary>
        private const uint Mix = 0x9E3779B9u;

        /// <summary>
        /// The nonce mission <paramref name="ordinal"/> allocates its run id under.
        /// <para>
        /// Derived rather than fresh per attempt, because that is the whole point of a nonce
        /// here: docs/04 section 5's retry policy answers a repeated command with the result it
        /// already produced, so a mission whose acknowledgement was lost must ask again under
        /// the same nonce. A <c>Guid</c> per attempt would take a new run id every time the
        /// player walked into the terminal.
        /// </para>
        /// </summary>
        public static string NonceFor(string sessionNonce, int ordinal)
        {
            if (string.IsNullOrWhiteSpace(sessionNonce))
                throw new ArgumentException("A session needs a nonce to derive from.", nameof(sessionNonce));
            RequireOrdinal(ordinal);

            return string.Concat(
                sessionNonce, ":mission-", ordinal.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The seed mission <paramref name="ordinal"/> plays.
        /// <para>
        /// <b>The first mission plays the session's seed unchanged.</b> A designer who types a
        /// seed into the scene to reproduce a report expects to get that map, and a derivation
        /// that applied to the first mission too would mean the number they typed appears
        /// nowhere in what they see. Later missions are mixed from it - not added to, because an
        /// xorshift started one apart produces visibly similar early values and the second
        /// mission would feel like the first.
        /// </para>
        /// </summary>
        public static uint SeedFor(uint sessionSeed, int ordinal)
        {
            RequireOrdinal(ordinal);
            if (ordinal == FirstOrdinal)
                return sessionSeed;

            unchecked
            {
                uint mixed = sessionSeed ^ (uint)(ordinal * (int)Mix);
                mixed ^= mixed << 13;
                mixed ^= mixed >> 17;
                mixed ^= mixed << 5;

                // 0 is a legal seed everywhere downstream, but it is also what an unset field
                // holds, so a run that logged 'seed 0' could not be told from one that was never
                // seeded.
                return mixed == 0u ? Mix : mixed;
            }
        }

        private static void RequireOrdinal(int ordinal)
        {
            if (ordinal < FirstOrdinal)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ordinal), ordinal, $"Missions are counted from {FirstOrdinal}.");
            }
        }
    }
}
