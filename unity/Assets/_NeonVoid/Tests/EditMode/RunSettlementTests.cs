using System;
using NeonVoid.Core;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// `CleanseHardware` and `ExtractRun` from docs/04 section 5: 정화 and 성공 철수.
    /// <para>
    /// The thing under test that is easy to miss: neither command adds a field to the save
    /// document. A purification exists in permanent state <i>as its receipt</i>, and the
    /// extraction's 최소 1개 정화 precondition is checked by counting those receipts. So these
    /// tests are as much about the receipt log being a usable record of a run as about the two
    /// commands - and about the run state the schema still deliberately does not hold (D-017).
    /// </para>
    /// </summary>
    public sealed class RunSettlementTests
    {
        private const string Root = "/tmp/neonvoid-run-settlement-tests";

        private FakeSaveFileSystem _files;
        private SaveSlotLayout _layout;
        private SaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _files = new FakeSaveFileSystem();
            _layout = SaveSlotLayout.Under(Root);
            _store = new SaveStore(_files, _layout);
            _store.Open();
            _store.CreateSlot("vaccine-1", "create:vaccine-1");
        }

        private VaccineSaveState Vaccine => _store.Snapshot.vaccine;

        /// <summary>Begins a run the way the game does, and returns its id.</summary>
        private long BeginRun(string nonce)
        {
            SaveCommitResult result = _store.Commit(new AllocateRunIdCommand(nonce));
            Assert.IsTrue(result.Acknowledged, result.Detail);
            return AllocateRunIdCommand.RunIdIn(result.Result);
        }

        private SaveCommitResult Purify(long runId, string areaId) =>
            _store.Commit(new CleanseHardwareCommand(runId, areaId));

        /// <summary>
        /// A level policy whose arithmetic is readable in the test: 100 XP a level, one point
        /// each. The shipping curve is [초안], so tests that are about extraction say what they
        /// mean rather than depending on it.
        /// </summary>
        private static VaccineLevelPolicy FlatLevels =>
            new VaccineLevelPolicy(new LevelCurve(100L, 0L), pointsPerLevel: 1);

        private SaveCommitResult Extract(
            long runId, ExtractionSettlement settlement, params string[] learned) =>
            _store.Commit(new ExtractRunCommand(runId, settlement, learned, FlatLevels));

        private static ExtractionSettlement Settlement(
            long xp, int points, int purifiedAreas, int objectives = 0) =>
            new ExtractionSettlement(xp, points, purifiedAreas, objectives);

        // --- 정화 -------------------------------------------------------------------------

        [Test]
        public void APurificationIsRecordedAsItsReceiptAndNothingElse()
        {
            long run = BeginRun("session-1");
            int revisionBefore = _store.Snapshot.snapshotRevision;
            string archiveBefore = string.Join(",", Vaccine.archivePluginInstanceIds);

            SaveCommitResult result = Purify(run, "cpu");

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            StringAssert.Contains("area=cpu", result.Result);
            StringAssert.Contains("purified=1", result.Result);
            Assert.AreEqual(revisionBefore + 1, _store.Snapshot.snapshotRevision, "nothing was written");
            Assert.AreEqual(
                archiveBefore, string.Join(",", Vaccine.archivePluginInstanceIds),
                "a purification touched the plugin database");
            Assert.AreEqual(0L, Vaccine.permanentXp, "a purification banked permanent XP");
            Assert.AreEqual(1, Vaccine.permanentLevel);
        }

        [Test]
        public void PurifyingFiveAreasCountsFive()
        {
            long run = BeginRun("session-1");
            string[] areas = { "cpu", "gpu", "ram", "ssd", "hdd" };

            for (int i = 0; i < areas.Length; ++i)
            {
                SaveCommitResult result = Purify(run, areas[i]);
                Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
                StringAssert.Contains($"purified={i + 1}", result.Result);
            }

            Assert.AreEqual(
                5, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(run)));
        }

        [Test]
        public void TheSameAreaTwiceInOneRunIsOnePurification()
        {
            // 중복 보상 없음, and 중복 정화 금지 (docs/01 section 1). The receipt id carries the
            // area, so the store answers the second one from the first one's receipt.
            long run = BeginRun("session-1");
            Purify(run, "cpu");

            SaveCommitResult again = Purify(run, "cpu");

            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, again.Verdict);
            Assert.AreEqual(
                1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(run)),
                "the same area was counted twice");
        }

        [Test]
        public void TheSameAreaInAnotherRunIsAnotherPurification()
        {
            // 사망한 의뢰의 다음 의뢰는 새 시드·새 적: purifying CPU again in a later mission is a
            // real purification, so the run has to be part of the key.
            long first = BeginRun("session-1");
            long second = BeginRun("session-2");
            Assert.AreNotEqual(first, second, "the allocator handed out one id twice");

            Purify(first, "cpu");
            SaveCommitResult inSecond = Purify(second, "cpu");

            Assert.AreEqual(SaveCommitVerdict.Committed, inSecond.Verdict, inSecond.Detail);
            Assert.AreEqual(1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(first)));
            Assert.AreEqual(1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(second)));
        }

        [Test]
        public void APurificationFromARunThisSlotNeverBeganIsRefused()
        {
            // The same guard RegisterDeath has: an unallocated run id may belong to another run,
            // and then this receipt id is one that already exists.
            SaveCommitResult result = Purify(99L, "cpu");

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("never begun", result.Detail);
        }

        [Test]
        public void ASixthPurificationInOneRunIsRefused()
        {
            long run = BeginRun("session-1");
            string[] areas = { "cpu", "gpu", "ram", "ssd", "hdd" };
            for (int i = 0; i < areas.Length; ++i)
                Purify(run, areas[i]);

            SaveCommitResult sixth = Purify(run, "mystery-hardware");

            Assert.AreEqual(SaveCommitVerdict.Rejected, sixth.Verdict);
            StringAssert.Contains($"all {DesignConstants.HardwareCount}", sixth.Detail);
        }

        [Test]
        public void AnAreaIdCannotCarryTheReceiptSeparator()
        {
            // "cpu:1" and an area literally called "1" under run "cpu" would mint the same key,
            // and the second would be refused as already purified.
            Assert.Throws<ArgumentException>(() => new CleanseHardwareCommand(1L, "cpu:1"));
            Assert.Throws<ArgumentException>(() => new CleanseHardwareCommand(1L, " "));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CleanseHardwareCommand(0L, "cpu"));
        }

        // --- 성공 철수 --------------------------------------------------------------------

        [Test]
        public void AnExtractionWithNoCommittedPurificationIsRefused()
        {
            // [확정] docs/04 section 7: 정화 0개 철수 → 성공 불가, 보상 지급 금지.
            long run = BeginRun("session-1");

            SaveCommitResult result = Extract(run, Settlement(500L, 2, 1));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("no committed purification", result.Detail);
            Assert.AreEqual(0L, Vaccine.permanentXp, "a refused extraction paid out");
            Assert.AreEqual(0, Vaccine.unusedStatPoints);
        }

        [Test]
        public void ASettlementThatClaimsNoPurificationCannotEvenBeBuilt()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionSettlement(100L, 0, 0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionSettlement(-1L, 0, 1, 3));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ExtractionSettlement(100L, 0, DesignConstants.HardwareCount + 1, 3));
        }

        [Test]
        public void OnePurificationIsEnoughForASuccessfulExtraction()
        {
            // [확정] R-004 and docs/04 section 7: 정화 1개 철수 → 성공 가능, 금지: 5개 완주 강요.
            long run = BeginRun("session-1");
            Purify(run, "cpu");

            SaveCommitResult result = Extract(run, Settlement(250L, 2, 1, objectives: 3));

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual(250L, Vaccine.permanentXp);
            Assert.AreEqual(3, Vaccine.permanentLevel, "250 XP at 100 a level");
            Assert.AreEqual(4, Vaccine.unusedStatPoints, "2 levels + 2 banked");
            StringAssert.Contains("purified=1", result.Result);
            StringAssert.Contains("objectives=3", result.Result);
            StringAssert.Contains("bankedXp=250", result.Result);
            StringAssert.Contains("levelPoints=2", result.Result);
        }

        [Test]
        public void ASecondExtractionOfOneRunPaysNothingAgain()
        {
            // 이중 철수/보상 차단 (docs/04 section 5).
            long run = BeginRun("session-1");
            Purify(run, "cpu");
            SaveCommitResult first = Extract(run, Settlement(250L, 2, 1));

            SaveCommitResult second = Extract(run, Settlement(250L, 2, 1));

            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, second.Verdict);
            Assert.AreEqual(first.Result, second.Result, "the replay answered differently");
            Assert.AreEqual(250L, Vaccine.permanentXp, "the extraction paid twice");
            Assert.AreEqual(4, Vaccine.unusedStatPoints);
        }

        [Test]
        public void TheExtractionSurvivesARelaunch()
        {
            long run = BeginRun("session-1");
            Purify(run, "cpu");
            Extract(run, Settlement(250L, 0, 1), "learned-1");

            var relaunched = new SaveStore(_files, _layout);
            relaunched.Open();

            Assert.AreEqual(250L, relaunched.Snapshot.vaccine.permanentXp);
            Assert.AreEqual(3, relaunched.Snapshot.vaccine.permanentLevel);
            Assert.Contains("learned-1", relaunched.Snapshot.vaccine.archivePluginInstanceIds);
            Assert.AreEqual(
                SaveCommitVerdict.AlreadyApplied,
                relaunched.Commit(new ExtractRunCommand(run, Settlement(250L, 0, 1), new[] { "learned-1" }, FlatLevels))
                    .Verdict,
                "a retry after a relaunch extracted a second time");
        }

        [Test]
        public void ASettlementComputedFromMorePurificationsThanWereCommittedIsRefused()
        {
            // docs/04 section 5 step 7: the reward follows the commit, not the screen. A
            // settlement for three areas when only two were saved would pay for one that was not.
            long run = BeginRun("session-1");
            Purify(run, "cpu");
            Purify(run, "gpu");

            SaveCommitResult result = Extract(run, Settlement(495L, 0, 3));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("claims 3", result.Detail);
            StringAssert.Contains("committed 2", result.Detail);
            Assert.AreEqual(0L, Vaccine.permanentXp);
        }

        [Test]
        public void FewerClaimedPurificationsThanWereCommittedIsAlsoRefused()
        {
            // The other direction is just as wrong, and quieter: the player would be underpaid
            // for an area the save does hold.
            long run = BeginRun("session-1");
            Purify(run, "cpu");
            Purify(run, "gpu");

            SaveCommitResult result = Extract(run, Settlement(165L, 0, 1));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("committed 2", result.Detail);
        }

        // --- 학습 -------------------------------------------------------------------------

        [Test]
        public void NewlyLearnedPluginsGoIntoTheArchiveAndNotTheLoadout()
        {
            // Section 3.3 treats 학습된 플러그인 as what the 보관 DB holds, and the loadout is what
            // a death takes (section 3.5) - so a plugin just learned would be undone by one death
            // if it landed there.
            long run = BeginRun("session-1");
            Purify(run, "cpu");

            Extract(run, Settlement(0L, 0, 1), "learned-1", "learned-2");

            Assert.Contains("learned-1", Vaccine.archivePluginInstanceIds);
            Assert.Contains("learned-2", Vaccine.archivePluginInstanceIds);
            Assert.AreEqual(0, Vaccine.carriedPluginInstanceIds.Count);
        }

        [Test]
        public void TheCarriedLoadoutIsNotArchivedAutomatically()
        {
            // [확정] docs/04 section 3.5: 성공 철수에서 살아 돌아온 학습 소지분은 자동 DB 입고가
            // 아니다. 플레이어가 직접 DB로 넣어야 한다.
            long run = BeginRun("session-1");
            Vaccine.carriedPluginInstanceIds.Add("carried-1");
            Purify(run, "cpu");

            Extract(run, Settlement(0L, 0, 1), "learned-1");

            Assert.Contains("carried-1", Vaccine.carriedPluginInstanceIds, "the loadout was emptied for the player");
            Assert.IsFalse(
                Vaccine.archivePluginInstanceIds.Contains("carried-1"),
                "a carried plugin was archived automatically");
        }

        [Test]
        public void LearningNothingIsAllowed()
        {
            // 0~2개: zero is a legitimate choice, and a player who wants none must not be refused
            // their XP for it.
            long run = BeginRun("session-1");
            Purify(run, "cpu");

            SaveCommitResult result = Extract(run, Settlement(250L, 0, 1));

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            StringAssert.Contains("learned=0", result.Result);
            Assert.AreEqual(0, Vaccine.archivePluginInstanceIds.Count);
            Assert.AreEqual(250L, Vaccine.permanentXp);
        }

        [Test]
        public void MoreThanTwoNewLearnsIsRefusedAtConstruction()
        {
            // [확정] R-015, docs/04 section 3.1. Refused rather than trimmed: trimming would
            // silently pick which of the player's choices to drop.
            Assert.AreEqual(2, DesignConstants.LearnablePluginsPerSuccessfulRun);
            Assert.Throws<ArgumentException>(
                () => new ExtractRunCommand(1L, Settlement(0L, 0, 1), new[] { "a", "b", "c" }));
        }

        [Test]
        public void AnInstanceTheVaccineAlreadyOwnsCannotBeLearnedAgain()
        {
            // docs/04 section 7 forbids DB+소지 중복 잔존, and a duplicate inside the archive is
            // worse than a loss: afterwards nothing can tell which one is real.
            long run = BeginRun("session-1");
            Vaccine.archivePluginInstanceIds.Add("owned-1");
            Vaccine.carriedPluginInstanceIds.Add("carried-1");
            Purify(run, "cpu");

            SaveCommitResult inArchive = Extract(run, Settlement(250L, 0, 1), "owned-1");
            Assert.AreEqual(SaveCommitVerdict.Rejected, inArchive.Verdict);
            StringAssert.Contains("already in the archive", inArchive.Detail);

            SaveCommitResult carried = Extract(run, Settlement(250L, 0, 1), "carried-1");
            Assert.AreEqual(SaveCommitVerdict.Rejected, carried.Verdict);
            StringAssert.Contains("already carried", carried.Detail);

            Assert.AreEqual(0L, Vaccine.permanentXp, "a refused extraction still banked its XP");
            Assert.AreEqual(1, Vaccine.archivePluginInstanceIds.Count);
        }

        [Test]
        public void TheSameInstanceCannotBeLearnedTwiceInOneExtraction()
        {
            long run = BeginRun("session-1");
            Purify(run, "cpu");

            SaveCommitResult result = Extract(run, Settlement(0L, 0, 1), "learned-1", "learned-1");

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("twice", result.Detail);
            Assert.AreEqual(0, Vaccine.archivePluginInstanceIds.Count);
        }

        // --- a destroyed vaccine -----------------------------------------------------------

        [Test]
        public void ADestroyedVaccineCanNeitherPurifyNorExtract()
        {
            long run = BeginRun("session-1");
            for (int sequence = 1; sequence <= VaccineSaveState.DesignDeathsBeforeFinalDestruction; ++sequence)
                _store.Commit(new RegisterDeathCommand(DeathId.For(run, 1, sequence), DeathSettlement.None));
            Assert.IsFalse(Vaccine.alive, "the fourth death did not destroy the vaccine");

            SaveCommitResult purify = Purify(run, "cpu");
            SaveCommitResult extract = Extract(run, Settlement(250L, 0, 1));

            Assert.AreEqual(SaveCommitVerdict.Rejected, purify.Verdict);
            Assert.AreEqual(SaveCommitVerdict.Rejected, extract.Verdict);
            StringAssert.Contains("destroyed", purify.Detail);
            StringAssert.Contains("destroyed", extract.Detail);
            Assert.AreEqual(0L, Vaccine.permanentXp);
        }

        // --- the draft policy --------------------------------------------------------------

        [Test]
        public void TheDraftPolicyBuildsItsSettlementFromThePublishedBudget()
        {
            ExtractionSettlement settlement = ExtractionSettlementPolicy.Draft(
                purifiedAreas: 2, objectivesCompleted: 6, growthCachesCollected: 1);

            Assert.AreEqual(330L, settlement.PermanentXp, "GDD v1 section 5.6: two areas is 330");
            Assert.AreEqual(1, settlement.StatPoints, "캐시 1개당 1P");
            Assert.AreEqual(2, settlement.PurifiedAreas);
            Assert.AreEqual(6, settlement.ObjectivesCompleted);
        }

        [Test]
        public void AFullClearThroughTheDraftPolicyReachesLevelFourOnTheRealCurve()
        {
            // The end-to-end shape of the P3 gate: purify, extract, and the vaccine has grown by
            // an amount the design document itself predicts (전체 정화 1회 → Lv.4).
            long run = BeginRun("session-1");
            string[] areas = { "cpu", "gpu", "ram", "ssd", "hdd" };
            for (int i = 0; i < areas.Length; ++i)
                Purify(run, areas[i]);

            ExtractionSettlement settlement = ExtractionSettlementPolicy.Draft(
                purifiedAreas: 5, objectivesCompleted: 15, growthCachesCollected: 0);
            SaveCommitResult result = _store.Commit(
                new ExtractRunCommand(run, settlement, Array.Empty<string>()));

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual(1045L, Vaccine.permanentXp);
            Assert.AreEqual(4, Vaccine.permanentLevel);
            Assert.AreEqual(
                3 * ProgressionCurves.DraftPointsPerVaccineLevel, Vaccine.unusedStatPoints,
                "three levels gained, one point each");
        }

        [Test]
        public void AnExtractionWithoutALevelPolicyIsRefused()
        {
            Assert.Throws<ArgumentException>(
                () => new ExtractRunCommand(1L, Settlement(0L, 0, 1), Array.Empty<string>(), default));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ExtractRunCommand(0L, Settlement(0L, 0, 1), Array.Empty<string>()));
        }
    }
}
