using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NeonVoid.UI;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// What the death screen is allowed to say at each stage of a death.
    /// <para>
    /// This is the screen the project's own rules are hardest on. [확정] CLAUDE.md:
    /// 보상/사망/철수/DB 이동은 저장 커밋 승인 후 UI 및 게임 상태 확정 - so every number here
    /// has to be absent until a write produced it, and the two states that look alike in a
    /// counter (복제 0, and a destroyed vaccine) have to read as the different things they are.
    /// </para>
    /// </summary>
    public sealed class DeathScreenSnapshotTests
    {
        private static DeathId AnId => DeathId.For(7L, 1, 1);

        // --- nothing before the write -----------------------------------------------------

        [Test]
        public void ALiveRunDrawsNoScreen()
        {
            Assert.IsFalse(DeathScreenSnapshot.None.Shown);
            Assert.AreEqual(DeathScreenPhase.None, DeathScreenSnapshot.None.Phase);
            Assert.IsEmpty(DeathScreenSnapshot.None.Unsettled, "a default struct handed back null");
        }

        [Test]
        public void ADeathWaitingOnTheSaveCarriesNoNumbers()
        {
            // The forbidden outcome in docs/04 section 7 is the UI saying it first. A clone
            // count here would be a guess at the result of a write that has not happened.
            DeathScreenSnapshot pending = DeathScreenSnapshot.Pending(AnId, commitConnected: true);

            Assert.IsTrue(pending.Shown);
            Assert.IsFalse(pending.NumbersKnown, "the screen charged a clone before the save did");
            Assert.AreEqual(0, pending.DeathsLeft, "a destruction countdown appeared before the write");
            Assert.IsEmpty(pending.Unsettled);
        }

        [Test]
        public void ADeathWithNoSaveConnectedSaysSoRatherThanWaitingSilently()
        {
            // The ordinary case now that permanent writes are off by default (D-020): there is
            // no service, so the ledger would sit in AwaitingCommit forever.
            DeathScreenSnapshot off = DeathScreenSnapshot.Pending(AnId, commitConnected: false);
            DeathScreenSnapshot waiting = DeathScreenSnapshot.Pending(AnId, commitConnected: true);

            Assert.AreNotEqual(
                waiting.Status, off.Status,
                "a save that will never answer reads the same as one that is about to");
            Assert.IsFalse(off.NumbersKnown);
        }

        [Test]
        public void AFailedCommitIsAFailureAndNotAResult()
        {
            DeathScreenSnapshot failed = DeathScreenSnapshot.Failed(AnId, "checksum mismatch");

            Assert.AreEqual(DeathScreenPhase.CommitFailed, failed.Phase);
            Assert.IsFalse(failed.NumbersKnown);
            StringAssert.Contains("checksum mismatch", failed.Detail);
            Assert.IsNotEmpty(failed.NextStep, "a failed commit left the player with no way out");
        }

        [Test]
        public void AFailureWithNoStatedReasonStillSaysSomething()
        {
            Assert.IsNotEmpty(DeathScreenSnapshot.Failed(AnId, string.Empty).Detail);
        }

        // --- 복제 0 is not 파기 --------------------------------------------------------------

        [Test]
        public void TheLastCloneSpentIsAMarkedLastChanceAndNotADestruction()
        {
            // [확정] docs/04 section 2: '복제 0'은 죽은 상태가 아니라 마지막 기회.
            // docs/05 section 2 names the same edge: 3번째 사망 잔여 0 ≠ 파기.
            DeathScreenSnapshot third =
                DeathScreenSnapshot.SettledDeath(AnId, clonesRemaining: 0, deathCount: 3);

            Assert.AreEqual(DeathScreenPhase.Settled, third.Phase, "복제 0 was read as 파기");
            Assert.IsTrue(third.LastChance);
            Assert.AreEqual(1, third.DeathsLeft, "the vaccine has one death left, not none");
            StringAssert.DoesNotContain("최종 파기", third.Title);
        }

        [Test]
        public void AnOrdinaryDeathNamesTheClonesLeftAndIsNotAnAlarm()
        {
            DeathScreenSnapshot first =
                DeathScreenSnapshot.SettledDeath(AnId, clonesRemaining: 2, deathCount: 1);

            Assert.IsFalse(first.LastChance);
            Assert.AreEqual(2, first.ClonesRemaining);
            StringAssert.Contains("2", first.Status);
            Assert.AreEqual(3, first.DeathsLeft);
        }

        [Test]
        public void OnlyTheFourthDeathDestroysTheVaccine()
        {
            DeathScreenSnapshot destroyed = DeathScreenSnapshot.FinalDestruction(AnId, deathCount: 4);

            Assert.AreEqual(DeathScreenPhase.Destroyed, destroyed.Phase);
            Assert.AreEqual(0, destroyed.DeathsLeft);
            Assert.IsFalse(destroyed.LastChance, "a destroyed vaccine still read as having a chance");
            StringAssert.Contains("설정", destroyed.Detail, "docs/04 section 2 preserves 설정 and 타 백신");
        }

        [Test]
        public void DestructionIsReadFromTheWriteAndNotFromTheCounterReachingZero()
        {
            // The two states that look identical in a clone counter. Only permanent state knows
            // which this is, so From takes its answer rather than inferring one.
            DeathScreenSnapshot survived = DeathScreenSnapshot.From(
                RunDeathPhase.Committed, AnId, string.Empty,
                commitConnected: true, vaccineAlive: true, clonesRemaining: 0, deathCount: 3);

            DeathScreenSnapshot gone = DeathScreenSnapshot.From(
                RunDeathPhase.Committed, AnId, string.Empty,
                commitConnected: true, vaccineAlive: false, clonesRemaining: 0, deathCount: 4);

            Assert.AreEqual(DeathScreenPhase.Settled, survived.Phase);
            Assert.AreEqual(DeathScreenPhase.Destroyed, gone.Phase);
            Assert.AreNotEqual(survived.Title, gone.Title);
        }

        [Test]
        public void TheDestructionCountMatchesTheRuleInTheSave()
        {
            // So a change to docs/04's four cannot leave this screen counting to a different one.
            DeathScreenSnapshot firstDeath =
                DeathScreenSnapshot.SettledDeath(AnId, clonesRemaining: 2, deathCount: 1);

            Assert.AreEqual(
                VaccineSaveState.DesignDeathsBeforeFinalDestruction - 1, firstDeath.DeathsLeft);
        }

        // --- what is not decided -------------------------------------------------------------

        [Test]
        public void ASettledDeathNamesWhatIsStillUndecidedRatherThanShowingItAsZero()
        {
            // docs/05 section 2 asks this screen for 이번 의뢰 손실 and 보존 XP. D-012 has not
            // fixed the settlement, so "0 XP" would read as a settled loss of everything.
            DeathScreenSnapshot settled =
                DeathScreenSnapshot.SettledDeath(AnId, clonesRemaining: 1, deathCount: 2);

            CollectionAssert.Contains(settled.Unsettled, DeathScreenSnapshot.UnsettledXp);
        }

        [Test]
        public void ThePluginLossIsStatedAsSomethingThatHappened()
        {
            // It used to sit in the undecided list as 획득 자체가 미구현 (D-006/D-007). Those
            // were approved on 2026-11-22 and a 의뢰 now starts with the 출전 목록, so a player
            // reading that line had just lost something the screen said could not be acquired.
            DeathScreenSnapshot settled =
                DeathScreenSnapshot.SettledDeath(AnId, clonesRemaining: 1, deathCount: 2);

            StringAssert.Contains(DeathScreenSnapshot.CarriedLost, settled.Detail);
            Assert.AreEqual(
                1, settled.Unsettled.Count,
                "the plugin loss is decided and should not be listed as waiting on anything");

            // [확정] docs/04 3.5: 보관 DB is not touched by a death the vaccine survives, and
            // the screen says so - it is the difference between a setback and a disaster.
            StringAssert.Contains("보관 DB", settled.Detail);
        }

        [Test]
        public void TheLastChanceReadingStillSaysWhatWasLost()
        {
            // 복제 0 changes the warning, not the loss.
            DeathScreenSnapshot last =
                DeathScreenSnapshot.SettledDeath(AnId, clonesRemaining: 0, deathCount: 3);

            Assert.IsTrue(last.LastChance);
            StringAssert.Contains(DeathScreenSnapshot.CarriedLost, last.Detail);
            StringAssert.Contains("파기가 아닙니다", last.Detail);
        }

        [Test]
        public void ADestroyedVaccineHasNothingLeftToSettle()
        {
            // [확정] docs/04 section 2 destroys this vaccine's growth, skills, DB and carried
            // items, so a "보존 XP, 미확정" line there would promise something that is gone.
            Assert.IsEmpty(DeathScreenSnapshot.FinalDestruction(AnId, deathCount: 4).Unsettled);
        }

        // --- the mapping -----------------------------------------------------------------------

        [Test]
        public void ALiveLedgerMapsToNoScreenWhateverElseIsPassed()
        {
            DeathScreenSnapshot alive = DeathScreenSnapshot.From(
                RunDeathPhase.Alive, AnId, "ignored",
                commitConnected: true, vaccineAlive: true, clonesRemaining: 3, deathCount: 0);

            Assert.IsFalse(alive.Shown);
        }

        [Test]
        public void EveryShownReadingSaysWhatHappensNext()
        {
            // A screen that states a loss and then nothing is where a player gets stuck.
            var readings = new[]
            {
                DeathScreenSnapshot.Pending(AnId, commitConnected: true),
                DeathScreenSnapshot.Pending(AnId, commitConnected: false),
                DeathScreenSnapshot.Failed(AnId, "disk full"),
                DeathScreenSnapshot.SettledDeath(AnId, 2, 1),
                DeathScreenSnapshot.SettledDeath(AnId, 0, 3),
                DeathScreenSnapshot.FinalDestruction(AnId, 4),
            };

            foreach (DeathScreenSnapshot reading in readings)
            {
                Assert.IsTrue(reading.Shown, reading.ToString());
                Assert.IsNotEmpty(reading.Title, reading.ToString());
                Assert.IsNotEmpty(reading.Status, reading.ToString());
                Assert.IsNotEmpty(reading.NextStep, reading.ToString());
                Assert.IsNotEmpty(reading.DeathIdText, reading.ToString());
            }
        }
    }
}
