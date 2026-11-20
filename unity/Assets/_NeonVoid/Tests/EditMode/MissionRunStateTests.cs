using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The run's identity and live state. docs/04 lists runId and 시드 as part of 의뢰 상태, and
    /// docs/06 section 4 requires mission, combat and reward randomness to be separate streams.
    /// <para>
    /// Nothing here is saved; these tests are about the in-memory half that the save service
    /// will later be handed rather than invent for itself.
    /// </para>
    /// </summary>
    public sealed class MissionRunStateTests
    {
        private static readonly RandomStream[] AllStreams =
        {
            RandomStream.Mission, RandomStream.Combat, RandomStream.Reward,
        };

        // --- identity and streams ----------------------------------------------------------

        [Test]
        public void ARunWithoutAnIdIsNotAnIdentity()
        {
            Assert.IsFalse(RunIdentity.None.IsValid);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunIdentity(0L, 5u));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunIdentity(-1L, 5u));
        }

        [Test]
        public void TheSameRunAndSeedAlwaysDeriveTheSameStreamSeeds()
        {
            // Reproducing a run has to need nothing but these two numbers.
            var first = new RunIdentity(42L, 123456u);
            var second = new RunIdentity(42L, 123456u);

            for (int i = 0; i < AllStreams.Length; ++i)
                Assert.AreEqual(
                    first.SeedFor(AllStreams[i]), second.SeedFor(AllStreams[i]), $"{AllStreams[i]} drifted.");
        }

        [Test]
        public void TheThreeStreamsDoNotShareASeed()
        {
            // Sharing one would make every system's output depend on how often every other
            // system happened to ask for a number, and the seed would stop reproducing runs.
            var identity = new RunIdentity(7L, 99u);
            var seen = new HashSet<uint>();

            for (int i = 0; i < AllStreams.Length; ++i)
                Assert.IsTrue(
                    seen.Add(identity.SeedFor(AllStreams[i])),
                    $"{AllStreams[i]} collided with another stream.");
        }

        [Test]
        public void ADifferentRunRollsDifferently()
        {
            // The bug this replaces: the plugin roll used one constant seed, so every run
            // produced the same "random" stats.
            var a = new RunIdentity(1L, 1000u);
            var b = new RunIdentity(2L, 1000u);

            Assert.AreNotEqual(a.SeedFor(RandomStream.Reward), b.SeedFor(RandomStream.Reward));
        }

        [Test]
        public void ADifferentSeedRollsDifferently()
        {
            var a = new RunIdentity(1L, 1u);
            var b = new RunIdentity(1L, 2u);

            Assert.AreNotEqual(a.SeedFor(RandomStream.Reward), b.SeedFor(RandomStream.Reward));
        }

        [Test]
        public void NeighbouringRunsDoNotProduceNeighbouringStreams()
        {
            // A derivation that just added the run id would give consecutive runs almost the
            // same stream seeds, and an xorshift started one apart produces visibly similar
            // early values - so "a new run" would feel like the previous one.
            var distinct = new HashSet<uint>();
            for (long runId = 1; runId <= 50; ++runId)
                distinct.Add(new RunIdentity(runId, 7u).SeedFor(RandomStream.Reward));

            Assert.AreEqual(50, distinct.Count, "Two of fifty consecutive runs shared a reward seed.");
        }

        [Test]
        public void ASeedOfZeroIsStillUsable()
        {
            // A caller may legitimately hand over 0, and the generator guards it: xorshift
            // started at 0 stays at 0 forever.
            var identity = new RunIdentity(1L, 0u);
            var rng = new DeterministicRng(identity.SeedFor(RandomStream.Combat));

            uint first = rng.NextUInt();
            uint second = rng.NextUInt();

            Assert.AreNotEqual(0u, first);
            Assert.AreNotEqual(first, second, "The generator is stuck.");
        }

        // --- mission state -----------------------------------------------------------------

        [Test]
        public void AFreshStateIsNotRunning()
        {
            var state = new MissionRunState();

            Assert.AreEqual(MissionPhase.None, state.Phase);
            Assert.IsFalse(state.IsRunning);
            Assert.IsFalse(state.Identity.IsValid);
            Assert.AreEqual(0, state.PurificationsCompleted);
        }

        [Test]
        public void BeginningAMissionRecordsItsIdentityAndStartTick()
        {
            var state = new MissionRunState();
            var identity = new RunIdentity(9L, 33u);

            Assert.IsTrue(state.TryBegin(identity, onTick: 120L));

            Assert.AreEqual(MissionPhase.InProgress, state.Phase);
            Assert.AreEqual(9L, state.Identity.RunId);
            Assert.AreEqual(120L, state.StartedOnTick);
        }

        [Test]
        public void AMissionCannotBeginOnTopOfARunningOne()
        {
            // Two missions sharing a run identity would mint two death receipts with the same
            // key, which is the one thing the receipt exists to prevent.
            var state = new MissionRunState();
            state.TryBegin(new RunIdentity(1L, 1u), 0L);

            Assert.IsFalse(state.TryBegin(new RunIdentity(2L, 2u), 10L));
            Assert.AreEqual(1L, state.Identity.RunId);
        }

        [Test]
        public void AnInvalidIdentityIsRefused()
        {
            var state = new MissionRunState();

            Assert.IsFalse(state.TryBegin(RunIdentity.None, 0L));
            Assert.IsFalse(state.TryBegin(new RunIdentity(1L, 1u), onTick: -1L));
            Assert.AreEqual(MissionPhase.None, state.Phase);
        }

        [Test]
        public void PurificationsCountOnlyWhileAMissionIsRunning()
        {
            var state = new MissionRunState();
            Assert.IsFalse(state.TryRecordPurification(), "It counted one with no mission at all.");

            state.TryBegin(new RunIdentity(1L, 1u), 0L);
            Assert.IsTrue(state.TryRecordPurification());
            Assert.IsTrue(state.TryRecordPurification());
            Assert.AreEqual(2, state.PurificationsCompleted);
        }

        [Test]
        public void FailureIsOneWay()
        {
            // [확정] docs/00: 사망은 해당 의뢰 실패이며 실패 의뢰 전투를 그대로 재시작하지 않는다.
            // A mission that could be un-failed would let a death be walked back without the
            // save service ever hearing about it.
            var state = new MissionRunState();
            state.TryBegin(new RunIdentity(1L, 1u), 0L);

            Assert.IsTrue(state.TryFail());
            Assert.AreEqual(MissionPhase.Failed, state.Phase);

            Assert.IsFalse(state.TryFail(), "A second death failed the same mission again.");
            Assert.IsFalse(state.TryRecordPurification(), "A failed mission kept purifying.");
            Assert.IsFalse(state.IsRunning);
        }

        [Test]
        public void AFailedMissionKeepsItsIdentity()
        {
            // The death receipt is written after the mission fails and is keyed on this.
            var state = new MissionRunState();
            state.TryBegin(new RunIdentity(77L, 5u), 0L);
            state.TryFail();

            Assert.AreEqual(77L, state.Identity.RunId);
            Assert.AreEqual(
                new RunIdentity(77L, 5u).SeedFor(RandomStream.Reward), state.SeedFor(RandomStream.Reward));
        }

        [Test]
        public void ANewMissionMayBeginAfterAFailure()
        {
            // 사망 후 임무 재생성: the next mission is a new one, which needs a new identity.
            var state = new MissionRunState();
            state.TryBegin(new RunIdentity(1L, 1u), 0L);
            state.TryRecordPurification();
            state.TryFail();

            Assert.IsTrue(state.TryBegin(new RunIdentity(2L, 2u), 600L));
            Assert.AreEqual(2L, state.Identity.RunId);
            Assert.AreEqual(0, state.PurificationsCompleted, "The new mission inherited progress.");
            Assert.AreEqual(MissionPhase.InProgress, state.Phase);
        }

        [Test]
        public void WithNoMissionTheStreamSeedIsZeroRatherThanAGuess()
        {
            var state = new MissionRunState();

            Assert.AreEqual(0u, state.SeedFor(RandomStream.Mission));
        }
    }
}
