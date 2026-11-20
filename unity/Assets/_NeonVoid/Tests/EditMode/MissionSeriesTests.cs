using System;
using System.Collections.Generic;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Telling one mission of a session from the next.
    /// <para>
    /// The two rules under test are both [확정] and both broken by the obvious implementation.
    /// docs/04 section 5 forbids 같은 runId 중복 시작 and keys the death receipt on the run, so
    /// two missions sharing an id would have the second one's first death refused as already
    /// settled - and no clone would ever be charged again. docs/00 makes the next mission's map
    /// and enemies new, which a reused seed rebuilds identically.
    /// </para>
    /// </summary>
    public sealed class MissionSeriesTests
    {
        private const string Session = "e7c1a0f94b7d4e4f8d2b6a5c3f1e0d99";

        [Test]
        public void EveryMissionOfASessionAsksUnderItsOwnNonce()
        {
            var nonces = new HashSet<string>();
            for (int ordinal = 1; ordinal <= 200; ++ordinal)
            {
                Assert.IsTrue(
                    nonces.Add(MissionSeries.NonceFor(Session, ordinal)),
                    $"mission {ordinal} reused another mission's nonce");
            }
        }

        [Test]
        public void TheSameMissionAlwaysAsksUnderTheSameNonce()
        {
            // The retry policy: a repeated command is answered with the result it already
            // produced. A Guid per attempt would take a new run id every time the player walked
            // into the terminal.
            for (int ordinal = 1; ordinal <= 50; ++ordinal)
            {
                Assert.AreEqual(
                    MissionSeries.NonceFor(Session, ordinal),
                    MissionSeries.NonceFor(Session, ordinal),
                    $"mission {ordinal}");
            }
        }

        [Test]
        public void TwoSessionsNeverShareANonce()
        {
            Assert.AreNotEqual(
                MissionSeries.NonceFor(Session, 1),
                MissionSeries.NonceFor("a-different-session", 1));
        }

        [Test]
        public void TheFirstMissionPlaysTheSeedThatWasPicked()
        {
            // A designer who types a seed into the scene to reproduce a report expects that map.
            // A derivation applied to the first mission too would mean the number they typed
            // appears nowhere in what they see.
            Assert.AreEqual(12345u, MissionSeries.SeedFor(12345u, MissionSeries.FirstOrdinal));
            Assert.AreEqual(0u, MissionSeries.SeedFor(0u, MissionSeries.FirstOrdinal));
        }

        [Test]
        public void EveryLaterMissionPlaysASeedOfItsOwn()
        {
            var seeds = new HashSet<uint>();
            for (int ordinal = 1; ordinal <= 500; ++ordinal)
                Assert.IsTrue(seeds.Add(MissionSeries.SeedFor(777u, ordinal)), $"mission {ordinal} collided");
        }

        [Test]
        public void NeighbouringMissionsDoNotGetNeighbouringSeeds()
        {
            // An xorshift started one apart produces visibly similar early values, so the second
            // mission would feel like the first. Mixed, not added - the same reason AreaSeeds
            // mixes the hardware in.
            for (uint session = 1u; session <= 200u; ++session)
            {
                uint second = MissionSeries.SeedFor(session, 2);
                uint third = MissionSeries.SeedFor(session, 3);

                Assert.Greater(
                    Distance(second, third), 1_000u,
                    $"session {session}: missions 2 and 3 start {Distance(second, third)} apart");
            }
        }

        [Test]
        public void NoLaterMissionEverGetsASeedOfZero()
        {
            // 0 is a legal seed everywhere downstream, but it is also what an unset field holds,
            // so a run that logged 'seed 0' could not be told from one that was never seeded.
            for (uint session = 0u; session <= 3000u; ++session)
            {
                for (int ordinal = 2; ordinal <= 6; ++ordinal)
                    Assert.AreNotEqual(0u, MissionSeries.SeedFor(session, ordinal), $"session {session}");
            }
        }

        [Test]
        public void MissionsAreCountedFromOne()
        {
            Assert.AreEqual(1, MissionSeries.FirstOrdinal);
            Assert.Throws<ArgumentOutOfRangeException>(() => MissionSeries.SeedFor(1u, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MissionSeries.NonceFor(Session, -1));
            Assert.Throws<ArgumentException>(() => MissionSeries.NonceFor(" ", 1));
        }

        private static uint Distance(uint a, uint b) => a > b ? a - b : b - a;
    }

    /// <summary>
    /// What the hub holds. [확정] docs/00 section 6 names four terminals, and a hub that quietly
    /// shipped with three would look finished.
    /// </summary>
    public sealed class HubTerminalTests
    {
        [Test]
        public void TheHubHasExactlyTheFourTerminalsTheDocumentNames()
        {
            Assert.AreEqual(HubTerminals.Count, HubTerminals.All.Length);
            Assert.AreEqual(4, HubTerminals.Count, "[확정] 의뢰/백신 성장/플러그인 데이터베이스/스킬 장착");
            CollectionAssert.AllItemsAreUnique(HubTerminals.All);
            CollectionAssert.AreEquivalent((HubTerminalKind[])Enum.GetValues(typeof(HubTerminalKind)), HubTerminals.All);
        }

        [Test]
        public void EveryTerminalHasAName()
        {
            for (int i = 0; i < HubTerminals.All.Length; ++i)
                Assert.IsNotEmpty(HubTerminals.NameOf(HubTerminals.All[i]));

            Assert.Throws<ArgumentOutOfRangeException>(() => HubTerminals.NameOf((HubTerminalKind)42));
        }

        [Test]
        public void AnUnconnectedTerminalSaysWhatItIsWaitingFor()
        {
            // The honest answer when the player interacts with an unfinished terminal. A post
            // that did nothing silently is the thing this is here to prevent.
            for (int i = 0; i < HubTerminals.All.Length; ++i)
            {
                HubTerminalKind kind = HubTerminals.All[i];
                string why = HubTerminals.WhyNotConnected(kind);

                if (HubTerminals.IsConnected(kind))
                    Assert.IsEmpty(why, $"{kind} works, so it has nothing to explain");
                else
                    Assert.IsNotEmpty(why, $"{kind} does nothing and does not say why");
            }
        }

        [Test]
        public void TheMissionTerminalIsTheOneThatWorks()
        {
            Assert.IsTrue(HubTerminals.IsConnected(HubTerminalKind.Mission));
        }
    }
}
