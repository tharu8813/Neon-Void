using System;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Allocating a run id, replacing a destroyed vaccine, and getting a slot from "whatever is
    /// on disk" to "a run may begin".
    /// <para>
    /// The first test here is the one that mattered: connecting the save to the scene would have
    /// shipped a bug where the second launch onwards charged no clones at all.
    /// </para>
    /// </summary>
    public sealed class SlotLifecycleTests
    {
        private const string Root = "/tmp/neonvoid-lifecycle-tests";

        private FakeSaveFileSystem _files;
        private SaveSlotLayout _layout;
        private SaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _files = new FakeSaveFileSystem();
            _layout = SaveSlotLayout.Under(Root);
            _store = new SaveStore(_files, _layout);
        }

        private SaveStore Prepared()
        {
            _store.Open();
            _store.CreateSlot("vaccine-1", "create:vaccine-1");
            return _store;
        }

        /// <summary>A slot with one run begun, so a death keyed on run 1 belongs to a run.</summary>
        private SaveStore PreparedWithRun()
        {
            Prepared();
            _store.Commit(new AllocateRunIdCommand("session-1"));
            return _store;
        }

        private VaccineSaveState Vaccine => _store.Snapshot.vaccine;

        private SaveCommitResult DieIn(long runId, int sequence) =>
            _store.Commit(new RegisterDeathCommand(DeathId.For(runId, 1, sequence), DeathSettlement.None));

        // --- the bug that connecting the save would have shipped ---------------------------

        [Test]
        public void TwoRunsSharingAnIdMakeTheSecondDeathLookLikeARetry()
        {
            // The prototype's run id was a serialized 1, so every launch's first death produced
            // death:run1-actor1-death1. The dedupe is correct - that IS the same key - which is
            // what makes the bug invisible: the save reports success, the banner shows a death,
            // and no clone is ever spent again.
            PreparedWithRun();

            Assert.AreEqual(SaveCommitVerdict.Committed, DieIn(1L, 1).Verdict);
            Assert.AreEqual(2, Vaccine.copiesRemaining);

            SaveCommitResult secondLaunch = DieIn(1L, 1);

            Assert.AreEqual(
                SaveCommitVerdict.AlreadyApplied, secondLaunch.Verdict,
                "Two runs with the same id are indistinguishable from one run retrying.");
            Assert.AreEqual(2, Vaccine.copiesRemaining);
            Assert.IsTrue(
                secondLaunch.Acknowledged,
                "And it reports success, which is why nothing would have looked wrong.");
        }

        [Test]
        public void RunIdsFromPermanentStateChargeAClonePerRun()
        {
            // The fix: each run takes its id from the slot, so each run's first death is its own
            // death. docs/04 section 5: runId is a 중복 방지용 불변 키.
            SaveStore store = Prepared();

            for (int launch = 1; launch <= 3; ++launch)
            {
                SaveCommitResult allocated = store.Commit(new AllocateRunIdCommand($"session-{launch}"));
                long runId = AllocateRunIdCommand.RunIdIn(allocated.Result);

                Assert.AreEqual(launch, runId, "The run id did not advance.");
                Assert.AreEqual(
                    SaveCommitVerdict.Committed, DieIn(runId, 1).Verdict,
                    $"launch {launch} was deduped against an earlier one");
            }

            Assert.AreEqual(0, Vaccine.copiesRemaining);
            Assert.AreEqual(3, Vaccine.deathCount);
        }

        [Test]
        public void ADeathFromARunThisSlotNeverBeganIsRefused()
        {
            // The guard that turns the collision above into something visible. A run id nobody
            // allocated is one another run may also hold, so this death's receipt id may
            // already exist - and then the save would answer AlreadyApplied, report success and
            // quietly charge no clone for the rest of the game. docs/04 section 5's
            // precondition for RegisterDeath is 런 활성, and this is as much of it as permanent
            // state can check.
            Prepared();   // a slot, but no run begun

            SaveCommitResult result = DieIn(1L, 1);

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("never begun", result.Detail);
            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies, Vaccine.copiesRemaining,
                "A death from a run that never began spent a clone.");
        }

        [Test]
        public void ADeathFromARunLaterThanAnyBegunIsRefused()
        {
            PreparedWithRun();   // run 1

            Assert.AreEqual(SaveCommitVerdict.Committed, DieIn(1L, 1).Verdict);
            Assert.AreEqual(SaveCommitVerdict.Rejected, DieIn(2L, 1).Verdict);
        }

        // --- allocating a run id -----------------------------------------------------------

        [Test]
        public void TheFirstRunIsRunOne()
        {
            SaveStore store = Prepared();

            SaveCommitResult result = store.Commit(new AllocateRunIdCommand("session-a"));

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual(1L, AllocateRunIdCommand.RunIdIn(result.Result));
            Assert.AreEqual(1, store.Snapshot.runsStarted);
        }

        [Test]
        public void RetryingUnderTheSameNonceReturnsTheIdAlreadyTakenRatherThanBurningAnother()
        {
            SaveStore store = Prepared();
            SaveCommitResult first = store.Commit(new AllocateRunIdCommand("session-a"));

            SaveCommitResult retry = store.Commit(new AllocateRunIdCommand("session-a"));

            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, retry.Verdict);
            Assert.AreEqual(
                AllocateRunIdCommand.RunIdIn(first.Result), AllocateRunIdCommand.RunIdIn(retry.Result));
            Assert.AreEqual(1, store.Snapshot.runsStarted, "A retry took a second id.");
        }

        [Test]
        public void ADestroyedVaccineCannotBeginARun()
        {
            SaveStore store = PreparedWithRun();
            for (int sequence = 1; sequence <= 4; ++sequence)
                DieIn(1L, sequence);

            SaveCommitResult result = store.Commit(new AllocateRunIdCommand("session-a"));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("destroyed", result.Detail);
        }

        [Test]
        public void ARunNeedsANonceToBeRetriedUnder()
        {
            Assert.Throws<ArgumentException>(() => new AllocateRunIdCommand(" "));
        }

        // --- the vaccine after 최종 파기 ---------------------------------------------------

        [Test]
        public void ALivingVaccineIsNeverReplaced()
        {
            // Replacing one would destroy it without a death, and 최종 파기 is supposed to be
            // the only route there.
            SaveStore store = Prepared();

            SaveCommitResult result = store.Commit(new StartNextVaccineCommand("vaccine-2", "next:1"));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("still alive", result.Detail);
            Assert.AreEqual("vaccine-1", Vaccine.id);
        }

        [Test]
        public void TheSuccessorIsTheNextGenerationWithAFullSetOfClones()
        {
            SaveStore store = PreparedWithRun();
            for (int sequence = 1; sequence <= 4; ++sequence)
                DieIn(1L, sequence);

            SaveCommitResult result = store.Commit(new StartNextVaccineCommand("vaccine-2", "next:after-g1"));

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual("vaccine-2", Vaccine.id);
            Assert.AreEqual(2, Vaccine.generation);
            Assert.IsTrue(Vaccine.alive);
            Assert.AreEqual(VaccineSaveState.DesignStartingCopies, Vaccine.copiesRemaining);
            Assert.AreEqual(0, Vaccine.deathCount);
            Assert.AreEqual(1, Vaccine.permanentLevel);
            Assert.AreEqual(0L, Vaccine.permanentXp);
            Assert.IsEmpty(Vaccine.archivePluginInstanceIds);
            Assert.IsEmpty(Vaccine.carriedPluginInstanceIds);
        }

        [Test]
        public void TheSuccessorKeepsTheReceiptLogAndTheRunCounter()
        {
            // Both for the same reason: an id the destroyed vaccine's deaths were keyed on must
            // never be handed out again, or the new vaccine's first death is answered from the
            // dead one's receipt and costs nothing.
            SaveStore store = Prepared();
            store.Commit(new AllocateRunIdCommand("session-a"));
            for (int sequence = 1; sequence <= 4; ++sequence)
                DieIn(1L, sequence);
            int receiptsBefore = store.Snapshot.receipts.Count;

            store.Commit(new StartNextVaccineCommand("vaccine-2", "next:after-g1"));

            Assert.Greater(store.Snapshot.receipts.Count, receiptsBefore);
            Assert.AreEqual(1, store.Snapshot.runsStarted, "최종 파기 reset the run counter.");

            SaveCommitResult nextRun = store.Commit(new AllocateRunIdCommand("session-b"));
            Assert.AreEqual(
                2L, AllocateRunIdCommand.RunIdIn(nextRun.Result),
                "The successor was handed a run id the dead vaccine had already used.");
        }

        [Test]
        public void ANewGenerationChargesItsOwnClonesRatherThanBeingDedupedAgainstTheDeadOne()
        {
            SaveStore store = PreparedWithRun();
            for (int sequence = 1; sequence <= 4; ++sequence)
                DieIn(1L, sequence);
            store.Commit(new StartNextVaccineCommand("vaccine-2", "next:after-g1"));

            long runId = AllocateRunIdCommand.RunIdIn(
                store.Commit(new AllocateRunIdCommand("session-b")).Result);
            SaveCommitResult death = DieIn(runId, 1);

            Assert.AreEqual(SaveCommitVerdict.Committed, death.Verdict, death.Detail);
            Assert.AreEqual(2, Vaccine.copiesRemaining);
        }

        [Test]
        public void OnlyOneSuccessorIsCreatedHoweverManyTimesItIsAskedFor()
        {
            SaveStore store = PreparedWithRun();
            for (int sequence = 1; sequence <= 4; ++sequence)
                DieIn(1L, sequence);

            for (int i = 0; i < 5; ++i)
                store.Commit(new StartNextVaccineCommand("vaccine-2", "next:after-g1"));

            Assert.AreEqual(2, Vaccine.generation, "A retry created another generation.");
        }

        // --- preparing a slot for play -----------------------------------------------------

        [Test]
        public void AnEmptySlotIsCreatedAndGivenRunOne()
        {
            SlotBootstrapResult result = VaccineSlotBootstrap.Prepare(
                _store, "session-a", () => "vaccine-new", mayStartNextVaccine: true);

            Assert.AreEqual(SlotBootstrapVerdict.CreatedSlot, result.Verdict, result.Detail);
            Assert.AreEqual(1L, result.RunId);
            Assert.AreEqual("vaccine-new", result.VaccineId);
            Assert.AreEqual(VaccineSaveState.DesignStartingCopies, result.CopiesRemaining);
            Assert.IsTrue(result.CanCommit);
        }

        [Test]
        public void AnExistingSlotIsLoadedAndGivenTheNextRunId()
        {
            VaccineSlotBootstrap.Prepare(_store, "session-a", () => "vaccine-new", true);

            var relaunched = new SaveStore(_files, _layout);
            SlotBootstrapResult result = VaccineSlotBootstrap.Prepare(
                relaunched, "session-b", () => "should-not-be-called", true);

            Assert.AreEqual(SlotBootstrapVerdict.Ready, result.Verdict, result.Detail);
            Assert.AreEqual(2L, result.RunId);
            Assert.AreEqual("vaccine-new", result.VaccineId);
        }

        [Test]
        public void PreparingTwiceInOneSessionDoesNotTakeTwoRunIds()
        {
            SlotBootstrapResult first = VaccineSlotBootstrap.Prepare(
                _store, "session-a", () => "vaccine-new", true);

            SlotBootstrapResult again = VaccineSlotBootstrap.Prepare(
                _store, "session-a", () => "vaccine-new", true);

            Assert.AreEqual(first.RunId, again.RunId);
            Assert.AreEqual(1, _store.Snapshot.runsStarted);
        }

        [Test]
        public void ADestroyedVaccineWaitsWhenStartingASuccessorIsNotPermitted()
        {
            // docs/04 ends 최종 파기 at 백신 선택 화면. With no such screen, waiting and saying
            // so is the honest state - and nothing may be committed while it waits.
            VaccineSlotBootstrap.Prepare(_store, "session-a", () => "vaccine-1", true);
            for (int sequence = 1; sequence <= 4; ++sequence)
                DieIn(1L, sequence);

            SlotBootstrapResult result = VaccineSlotBootstrap.Prepare(
                _store, "session-b", () => "vaccine-2", mayStartNextVaccine: false);

            Assert.AreEqual(SlotBootstrapVerdict.AwaitingNextVaccine, result.Verdict);
            Assert.AreEqual(0L, result.RunId);
            Assert.IsFalse(result.CanCommit);
            Assert.IsFalse(Vaccine.alive, "Waiting changed the save.");
        }

        [Test]
        public void ADestroyedVaccineIsReplacedWhenThatIsPermitted()
        {
            VaccineSlotBootstrap.Prepare(_store, "session-a", () => "vaccine-1", true);
            for (int sequence = 1; sequence <= 4; ++sequence)
                DieIn(1L, sequence);

            SlotBootstrapResult result = VaccineSlotBootstrap.Prepare(
                _store, "session-b", () => "vaccine-2", mayStartNextVaccine: true);

            Assert.AreEqual(SlotBootstrapVerdict.StartedNextVaccine, result.Verdict, result.Detail);
            Assert.AreEqual(2, result.Generation);
            Assert.AreEqual(VaccineSaveState.DesignStartingCopies, result.CopiesRemaining);
            Assert.IsTrue(result.CanCommit);
            Assert.Greater(result.RunId, 1L, "The successor reused a run id.");
        }

        [Test]
        public void AHaltedStoreCommitsNothingAndSaysWhy()
        {
            VaccineSlotBootstrap.Prepare(_store, "session-a", () => "vaccine-1", true);
            _files.Delete(_layout.BackupPath);
            _files.BreakChecksum(_layout.SnapshotPath);

            var relaunched = new SaveStore(_files, _layout);
            SlotBootstrapResult result = VaccineSlotBootstrap.Prepare(
                relaunched, "session-b", () => "vaccine-2", true);

            Assert.AreEqual(SlotBootstrapVerdict.Halted, result.Verdict);
            Assert.IsFalse(result.CanCommit);
            Assert.IsNotEmpty(result.Detail);
        }

        [Test]
        public void ASlotThatCannotBeWrittenToCommitsNothing()
        {
            // No run id means no run, because a run that invented one would recreate the
            // collision the allocation exists to prevent.
            _files.WriteDenied.Add(_layout.TempPath);

            SlotBootstrapResult result = VaccineSlotBootstrap.Prepare(
                _store, "session-a", () => "vaccine-1", true);

            Assert.AreEqual(SlotBootstrapVerdict.WriteFailed, result.Verdict);
            Assert.AreEqual(0L, result.RunId);
            Assert.IsFalse(result.CanCommit);
        }

        [Test]
        public void ARunIdAllocationThatFailsLeavesTheSlotUsableButUncommittable()
        {
            // The slot exists and loads; only the id could not be taken. Play must not commit,
            // and the save must not be damaged.
            VaccineSlotBootstrap.Prepare(_store, "session-a", () => "vaccine-1", true);

            var relaunched = new SaveStore(_files, _layout);
            relaunched.Open();
            _files.WriteDenied.Add(_layout.TempPath);
            SlotBootstrapResult result = VaccineSlotBootstrap.Prepare(
                relaunched, "session-b", () => "vaccine-2", true);

            Assert.AreEqual(SlotBootstrapVerdict.WriteFailed, result.Verdict);
            Assert.IsFalse(result.CanCommit);
            Assert.AreEqual(SaveStoreState.Ready, relaunched.State, "A failed allocation halted the save.");
            Assert.AreEqual("vaccine-1", relaunched.Snapshot.vaccine.id);
        }

        [Test]
        public void PreparingNeedsASessionNonce()
        {
            Assert.Throws<ArgumentException>(
                () => VaccineSlotBootstrap.Prepare(_store, "  ", () => "vaccine-1", true));
        }

        // --- reading a result --------------------------------------------------------------

        [Test]
        public void AFieldIsReadOutOfAResultOrReportedAbsent()
        {
            Assert.IsTrue(SaveResultFields.TryGetInt("a=1;runId=42;b=7", "runId", out int found));
            Assert.AreEqual(42, found);

            Assert.IsFalse(SaveResultFields.TryGetInt("a=1;b=7", "runId", out _));
            Assert.IsFalse(SaveResultFields.TryGetInt("runId=notanumber", "runId", out _));
            Assert.IsFalse(SaveResultFields.TryGetInt(string.Empty, "runId", out _));

            // A field whose name merely ends with the one being asked for must not match, or
            // 'id' would read 'runId'.
            Assert.IsFalse(SaveResultFields.TryGetInt("parentRunId=5", "runId", out _));
        }
    }
}
