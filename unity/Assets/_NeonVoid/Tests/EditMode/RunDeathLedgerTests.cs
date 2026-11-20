using System;
using NeonVoid.Progression;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/04: <c>RegisterDeath(deathId)</c> with "중복 deathId로 두 번 차감 금지", and
    /// CLAUDE.md's "보상/사망/철수/DB 이동은 저장 커밋 승인 후 UI 및 게임 상태 확정.
    /// 실패·ACK 유실·중복 시 재지급 금지".
    /// <para>
    /// Every test here is about one of the two ways this goes wrong: a death charged twice, or
    /// a result shown for a save that was never written.
    /// </para>
    /// </summary>
    public sealed class RunDeathLedgerTests
    {
        private const long Run = 7L;
        private const int PlayerActor = 3;
        private const int SecondActor = 4;

        private static DeathReport FirstDeath(long tick = 120L) =>
            new DeathReport(DeathId.For(Run, PlayerActor, 1), tick);

        // --- the id itself -----------------------------------------------------------------

        [Test]
        public void TheSameDeathProducesTheSameIdOnAReplay()
        {
            // This is the whole reason the id is composed instead of random: after a crash
            // before the ack, the relaunched run must mint the id it minted the first time, or
            // the save service cannot recognise the retry and charges a second clone.
            Assert.AreEqual(DeathId.For(Run, PlayerActor, 1), DeathId.For(Run, PlayerActor, 1));
            Assert.AreNotEqual(DeathId.For(Run, PlayerActor, 1), DeathId.For(Run, PlayerActor, 2));
            Assert.AreNotEqual(DeathId.For(Run, PlayerActor, 1), DeathId.For(Run, SecondActor, 1));
            Assert.AreNotEqual(DeathId.For(Run, PlayerActor, 1), DeathId.For(Run + 1, PlayerActor, 1));
        }

        [Test]
        public void AnIncompleteIdIsNotAUsableKey()
        {
            Assert.IsFalse(DeathId.None.IsValid);
            Assert.Throws<ArgumentOutOfRangeException>(() => DeathId.For(0L, PlayerActor, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeathId.For(Run, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => DeathId.For(Run, PlayerActor, 0));
        }

        [Test]
        public void AReportWithoutAnIdIsRefusedAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => new DeathReport(DeathId.None, 10L));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DeathReport(DeathId.For(Run, PlayerActor, 1), -1L));
        }

        // --- registration ------------------------------------------------------------------

        [Test]
        public void AFreshRunHasNoDeathAndReleasesNothing()
        {
            var ledger = new RunDeathLedger();

            Assert.AreEqual(RunDeathPhase.Alive, ledger.Phase);
            Assert.IsFalse(ledger.HasRegisteredDeath);
            Assert.AreEqual(DeathId.None, ledger.RegisteredDeathId);
            Assert.IsFalse(ledger.MayReleaseResults);
        }

        [Test]
        public void ARegisteredDeathWaitsForTheSaveService()
        {
            var ledger = new RunDeathLedger();

            Assert.AreEqual(DeathRegistrationVerdict.Accepted, ledger.TryRegisterDeath(FirstDeath()));

            Assert.AreEqual(RunDeathPhase.AwaitingCommit, ledger.Phase);
            Assert.AreEqual(DeathId.For(Run, PlayerActor, 1), ledger.RegisteredDeathId);
            Assert.AreEqual(120L, ledger.RegisteredDeath.TickIndex);
            Assert.IsFalse(ledger.MayReleaseResults, "Results were released before the save was committed.");
        }

        [Test]
        public void TheSameDeathReportedTwiceIsRecognisedRatherThanChargedAgain()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());

            // A relaunch after a lost ack re-reports the identical death. Nothing may change.
            Assert.AreEqual(DeathRegistrationVerdict.DuplicateIgnored, ledger.TryRegisterDeath(FirstDeath(999L)));

            Assert.AreEqual(RunDeathPhase.AwaitingCommit, ledger.Phase);
            Assert.AreEqual(120L, ledger.RegisteredDeath.TickIndex, "A duplicate overwrote the original report.");
        }

        [Test]
        public void ASecondDifferentDeathIsRefusedBecauseTheRunAlreadyEnded()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());

            var other = new DeathReport(DeathId.For(Run, SecondActor, 1), 130L);
            Assert.AreEqual(DeathRegistrationVerdict.RejectedRunAlreadyEnded, ledger.TryRegisterDeath(other));

            Assert.AreEqual(DeathId.For(Run, PlayerActor, 1), ledger.RegisteredDeathId,
                "A second death replaced the one that actually ended the run.");
        }

        // --- acknowledgement ---------------------------------------------------------------

        [Test]
        public void AnAnswerWithNoDeathWaitingIsRefused()
        {
            var ledger = new RunDeathLedger();

            Assert.AreEqual(
                DeathAckVerdict.RejectedNothingPending,
                ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, PlayerActor, 1))));
            Assert.IsFalse(ledger.MayReleaseResults);
        }

        [Test]
        public void AnAnswerAboutAnotherDeathCannotReleaseThisOne()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());

            Assert.AreEqual(
                DeathAckVerdict.RejectedWrongDeath,
                ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, SecondActor, 1))));

            Assert.AreEqual(RunDeathPhase.AwaitingCommit, ledger.Phase);
            Assert.IsFalse(ledger.MayReleaseResults,
                "A success for a different death released this run's results.");
        }

        [Test]
        public void OnlyACommittedSaveReleasesResults()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());

            Assert.AreEqual(DeathAckVerdict.Applied,
                ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, PlayerActor, 1))));

            Assert.AreEqual(RunDeathPhase.Committed, ledger.Phase);
            Assert.IsTrue(ledger.MayReleaseResults);
            Assert.IsEmpty(ledger.LastFailureReason);
        }

        [Test]
        public void AFailedCommitReleasesNothingAndKeepsTheReason()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());

            ledger.Acknowledge(DeathCommitAck.Failure(DeathId.For(Run, PlayerActor, 1), "disk full"));

            Assert.AreEqual(RunDeathPhase.CommitFailed, ledger.Phase);
            Assert.IsFalse(ledger.MayReleaseResults);
            Assert.AreEqual("disk full", ledger.LastFailureReason);
        }

        [Test]
        public void ASecondSuccessForACommittedDeathIsNotASecondPayout()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());
            ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, PlayerActor, 1)));

            Assert.AreEqual(DeathAckVerdict.DuplicateIgnored,
                ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, PlayerActor, 1))));
            Assert.AreEqual(RunDeathPhase.Committed, ledger.Phase);
        }

        [Test]
        public void ACommittedDeathCannotBeUndoneByALateFailure()
        {
            // A retry whose first attempt actually succeeded can still report a failure later.
            // Taking that answer would un-commit a write that happened.
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());
            ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, PlayerActor, 1)));

            ledger.Acknowledge(DeathCommitAck.Failure(DeathId.For(Run, PlayerActor, 1), "late timeout"));

            Assert.AreEqual(RunDeathPhase.Committed, ledger.Phase);
            Assert.IsTrue(ledger.MayReleaseResults);
            Assert.IsEmpty(ledger.LastFailureReason);
        }

        [Test]
        public void ReportingTheSameDeathAfterItCommittedChangesNothing()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());
            ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, PlayerActor, 1)));

            Assert.AreEqual(DeathRegistrationVerdict.DuplicateIgnored, ledger.TryRegisterDeath(FirstDeath()));
            Assert.AreEqual(RunDeathPhase.Committed, ledger.Phase);
        }

        // --- retry -------------------------------------------------------------------------

        [Test]
        public void ARetryKeepsTheDeathIdItFailedWith()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());
            ledger.Acknowledge(DeathCommitAck.Failure(DeathId.For(Run, PlayerActor, 1), "io error"));

            Assert.IsTrue(ledger.TryBeginRetry(out DeathReport retry));

            Assert.AreEqual(DeathId.For(Run, PlayerActor, 1), retry.Id,
                "The retry minted a new id, so the save service would see a second death.");
            Assert.AreEqual(120L, retry.TickIndex);
            Assert.AreEqual(RunDeathPhase.AwaitingCommit, ledger.Phase);
            Assert.IsFalse(ledger.MayReleaseResults);
        }

        [Test]
        public void ThereIsNothingToRetryUntilACommitHasFailed()
        {
            var ledger = new RunDeathLedger();
            Assert.IsFalse(ledger.TryBeginRetry(out _), "Retried while the run was still alive.");

            ledger.TryRegisterDeath(FirstDeath());
            Assert.IsFalse(ledger.TryBeginRetry(out _), "Retried while the first attempt was still open.");

            ledger.Acknowledge(DeathCommitAck.Success(DeathId.For(Run, PlayerActor, 1)));
            Assert.IsFalse(ledger.TryBeginRetry(out _), "Retried a death that was already committed.");
        }

        [Test]
        public void ARetryThatSucceedsReleasesTheResult()
        {
            var ledger = new RunDeathLedger();
            ledger.TryRegisterDeath(FirstDeath());
            ledger.Acknowledge(DeathCommitAck.Failure(DeathId.For(Run, PlayerActor, 1), "io error"));
            ledger.TryBeginRetry(out DeathReport retry);

            Assert.AreEqual(DeathAckVerdict.Applied, ledger.Acknowledge(DeathCommitAck.Success(retry.Id)));
            Assert.IsTrue(ledger.MayReleaseResults);
            Assert.IsEmpty(ledger.LastFailureReason);
        }

        // --- failure messages --------------------------------------------------------------

        [Test]
        public void AFailureAlwaysCarriesSomethingToShowOnTheRecoveryScreen()
        {
            DeathCommitAck ack = DeathCommitAck.Failure(DeathId.For(Run, PlayerActor, 1), null);
            Assert.IsNotEmpty(ack.FailureReason);
            Assert.IsFalse(ack.Succeeded);
        }
    }
}
