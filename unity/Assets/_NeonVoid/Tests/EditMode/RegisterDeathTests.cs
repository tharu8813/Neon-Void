using System;
using System.Collections.Generic;
using System.Reflection;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// `RegisterDeath` against the [확정] clone table in docs/04 section 2, and the duplicate
    /// handling docs/04 section 7 asks to be proved rather than argued.
    /// </summary>
    public sealed class RegisterDeathTests
    {
        private const string Root = "/tmp/neonvoid-death-tests";

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

            // A death belongs to a run the slot began, so the run id comes from the allocator
            // here exactly as it does in the game. Hardcoding one would test a state the save
            // now refuses.
            _store.Commit(new AllocateRunIdCommand("session-1"));
        }

        private SaveCommitResult Die(int sequence, DeathSettlement settlement = default) =>
            _store.Commit(new RegisterDeathCommand(DeathId.For(1L, 1, sequence), settlement));

        /// <summary>
        /// A level policy whose arithmetic is readable here: every level costs 100 XP and pays
        /// one point. The shipping curve is [초안] (D-011), so a test that wants to say
        /// something about levelling says it against a curve it states itself.
        /// </summary>
        private static VaccineLevelPolicy FlatLevels =>
            new VaccineLevelPolicy(new LevelCurve(100L, 0L), pointsPerLevel: 1);

        private SaveCommitResult DieWithLevels(int sequence, DeathSettlement settlement, VaccineLevelPolicy levels) =>
            _store.Commit(new RegisterDeathCommand(DeathId.For(1L, 1, sequence), settlement, levels));

        private VaccineSaveState Vaccine => _store.Snapshot.vaccine;

        /// <summary>
        /// The committed receipt for one death, found by its id. Looked up rather than indexed:
        /// the receipt log holds every command, so a position is only right until another one
        /// is added - which is how this quietly started asserting about the wrong receipt.
        /// </summary>
        private string ReceiptFor(int sequence) =>
            _store.Snapshot.FindReceipt(
                RegisterDeathCommand.ReceiptIdFor(DeathId.For(1L, 1, sequence))).result;

        // --- the clone table ---------------------------------------------------------------

        [Test]
        public void ThreeDeathsSpendThreeClonesAndLeaveTheVaccinePlayable()
        {
            // [확정] docs/04 section 2, row by row: 3 → 2 → 1 → 0, 허브에서 재시도 each time.
            Vaccine.carriedPluginInstanceIds.Add("carried-1");

            for (int sequence = 1; sequence <= 3; ++sequence)
            {
                SaveCommitResult result = Die(sequence);

                Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
                Assert.AreEqual(
                    VaccineSaveState.DesignStartingCopies - sequence, Vaccine.copiesRemaining,
                    $"death {sequence} left the wrong number of clones");
                Assert.IsTrue(Vaccine.alive, $"death {sequence} destroyed the vaccine early");
            }

            Assert.AreEqual(0, Vaccine.copiesRemaining);
            Assert.AreEqual(3, Vaccine.deathCount);
        }

        [Test]
        public void NoClonesLeftIsTheLastChanceAndNotDestruction()
        {
            // docs/04 section 7 is explicit that the third death must NOT destroy the vaccine:
            // '복제 0'은 죽은 상태가 아니라 마지막 기회. Reading destruction off
            // copiesRemaining == 0 would end the run one mission early, and nothing could
            // undo it.
            Die(1);
            Die(2);
            Die(3);

            Assert.AreEqual(0, Vaccine.copiesRemaining);
            Assert.IsTrue(Vaccine.alive, "복제 0 was treated as destruction.");
            StringAssert.Contains("destroyed=false", ReceiptFor(3));
        }

        [Test]
        public void TheFourthDeathDestroysTheVaccineAndItsDatabase()
        {
            // [확정] docs/04 section 2: 해당 백신 영구/의뢰 성장·스킬·DB·소지·관련 기록 파기.
            Vaccine.archivePluginInstanceIds.Add("archived-1");
            Vaccine.carriedPluginInstanceIds.Add("carried-1");
            Vaccine.permanentXp = 5000L;
            Vaccine.unusedStatPoints = 4;
            Vaccine.permanentLevel = 7;

            Die(1);
            Die(2);
            Die(3);
            SaveCommitResult fourth = Die(4);

            Assert.AreEqual(SaveCommitVerdict.Committed, fourth.Verdict, fourth.Detail);
            Assert.IsFalse(Vaccine.alive);
            Assert.AreEqual(0, Vaccine.copiesRemaining);
            Assert.IsEmpty(Vaccine.archivePluginInstanceIds, "The archive DB survived 최종 파기.");
            Assert.IsEmpty(Vaccine.carriedPluginInstanceIds);
            Assert.AreEqual(0L, Vaccine.permanentXp);
            Assert.AreEqual(0, Vaccine.unusedStatPoints);
            Assert.AreEqual(1, Vaccine.permanentLevel);
            StringAssert.Contains("destroyed=true", fourth.Result);
        }

        [Test]
        public void ADestroyedVaccineCannotDieAgain()
        {
            Die(1);
            Die(2);
            Die(3);
            Die(4);

            SaveCommitResult fifth = Die(5);

            Assert.AreEqual(SaveCommitVerdict.Rejected, fifth.Verdict);
            StringAssert.Contains("already destroyed", fifth.Detail);
        }

        [Test]
        public void DeathsOneToThreeLoseTheCarriedPluginsAndKeepTheArchive()
        {
            // [확정] docs/04 section 3.5: 사망 시 출전 소지분은 소실. The archive is explicitly
            // kept (section 2: DB보관·기존 XP 유지) until 최종 파기.
            Vaccine.carriedPluginInstanceIds.Add("carried-1");
            Vaccine.carriedPluginInstanceIds.Add("carried-2");
            Vaccine.archivePluginInstanceIds.Add("archived-1");

            Die(1);

            Assert.IsEmpty(Vaccine.carriedPluginInstanceIds, "The plugins taken into the mission survived.");
            Assert.AreEqual(1, Vaccine.archivePluginInstanceIds.Count, "The archive was emptied by a survivable death.");
        }

        [Test]
        public void ApprovedPermanentXpIsNeverReducedByADeath()
        {
            // [확정] docs/04 section 2: 이미 승인된 영구 XP/포인트는 빼앗지 않는다.
            Vaccine.permanentXp = 1234L;
            Vaccine.unusedStatPoints = 3;

            Die(1);

            Assert.GreaterOrEqual(Vaccine.permanentXp, 1234L);
            Assert.GreaterOrEqual(Vaccine.unusedStatPoints, 3);
        }

        [Test]
        public void ASettlementIsAddedToPermanentStateOnASurvivableDeath()
        {
            Vaccine.permanentXp = 1000L;

            // 100 XP per level here, so 1300 XP has paid for 13 steps and the vaccine is level
            // 14 - up from the level 1 it was stuck at. The 2 settled points are on top of the
            // 13 the levels paid.
            DieWithLevels(1, new DeathSettlement(300L, 2, 3), FlatLevels);

            Assert.AreEqual(1300L, Vaccine.permanentXp);
            Assert.AreEqual(14, Vaccine.permanentLevel);
            Assert.AreEqual(15, Vaccine.unusedStatPoints, "13 from levels + 2 settled");
            StringAssert.Contains("settledXp=300", ReceiptFor(1));
            StringAssert.Contains("settledPoints=2", ReceiptFor(1));
            StringAssert.Contains("levelPoints=13", ReceiptFor(1));
            StringAssert.Contains("purifications=3", ReceiptFor(1));
        }

        [Test]
        public void TheSettlementIsNotAwardedByADeathThatDestroysTheVaccine()
        {
            // The same transaction erases the vaccine's permanent growth, so awarding first
            // would put a figure on the receipt that the player never held. The receipt says
            // why, so the zero is not read later as a settlement that failed.
            Die(1);
            Die(2);
            Die(3);
            SaveCommitResult fourth = Die(4, new DeathSettlement(900L, 5, 2));

            Assert.AreEqual(0L, Vaccine.permanentXp);
            StringAssert.Contains("settledXp=0", fourth.Result);
            StringAssert.Contains("settlement-moot-on-destruction", fourth.Result);
        }

        [Test]
        public void AStateWhereClonesAndDeathsDisagreeIsRefused()
        {
            // Nothing in the game can produce this; a hand-edited save can. Guessing which
            // counter to believe either grants a clone or takes one.
            SaveDocument tampered = _store.Snapshot.Clone();
            tampered.vaccine.deathCount = 2;

            var command = new RegisterDeathCommand(DeathId.For(1L, 1, 3), DeathSettlement.None);
            SaveCommandOutcome outcome = command.Apply(tampered);

            Assert.IsFalse(outcome.Accepted);
            StringAssert.Contains("inconsistent state", outcome.RejectionReason);
        }

        // --- 백신 레벨 ---------------------------------------------------------------------

        [Test]
        public void SettledXpBecomesALevelInTheSameCommit()
        {
            // The hole this closes: permanentXp was written by every settled death and
            // permanentLevel was never touched, so the level sat at 1 for the life of a vaccine
            // and 백신 레벨 1당 영구 포인트 1 (docs/04 section 1) paid nothing at all.
            DieWithLevels(1, new DeathSettlement(250L, 0, 2), FlatLevels);

            Assert.AreEqual(250L, Vaccine.permanentXp);
            Assert.AreEqual(3, Vaccine.permanentLevel, "250 XP at 100 a level is level 3");
            Assert.AreEqual(2, Vaccine.unusedStatPoints, "two levels, two points");
            StringAssert.Contains("level=3", ReceiptFor(1));
            StringAssert.Contains("levelsGained=2", ReceiptFor(1));
        }

        [Test]
        public void XpShortOfALevelIsKeptWithoutLevellingUp()
        {
            DieWithLevels(1, new DeathSettlement(99L, 0, 0), FlatLevels);

            Assert.AreEqual(99L, Vaccine.permanentXp);
            Assert.AreEqual(1, Vaccine.permanentLevel);
            Assert.AreEqual(0, Vaccine.unusedStatPoints);
        }

        [Test]
        public void TheLevelSurvivesARelaunchBecauseItWasCommittedWithTheXp()
        {
            // A level granted outside the save transaction would be a number held only in
            // memory: the XP would come back from disk and the level it bought would not.
            DieWithLevels(1, new DeathSettlement(250L, 0, 0), FlatLevels);

            var relaunched = new SaveStore(_files, _layout);
            relaunched.Open();

            Assert.AreEqual(3, relaunched.Snapshot.vaccine.permanentLevel);
            Assert.AreEqual(250L, relaunched.Snapshot.vaccine.permanentXp);
            Assert.AreEqual(2, relaunched.Snapshot.vaccine.unusedStatPoints);
        }

        [Test]
        public void AReplayedDeathDoesNotLevelUpASecondTime()
        {
            // docs/04 section 7: 저장 성공 ACK 유실 → 기존 영수증 조회. 금지: 포인트 재발급.
            // Levels are points, so a replay that levelled again would be a point reissue.
            DieWithLevels(1, new DeathSettlement(250L, 0, 0), FlatLevels);
            string first = ReceiptFor(1);

            SaveCommitResult replay = DieWithLevels(1, new DeathSettlement(250L, 0, 0), FlatLevels);

            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, replay.Verdict);
            Assert.AreEqual(3, Vaccine.permanentLevel);
            Assert.AreEqual(2, Vaccine.unusedStatPoints);
            Assert.AreEqual(first, replay.Result, "The replay answered with a different result.");
        }

        [Test]
        public void FinalDestructionTakesTheLevelAndItsPointsWithIt()
        {
            // [확정] docs/04 section 2: 4번째 사망 → 해당 백신 영구/의뢰 성장 파기.
            DieWithLevels(1, new DeathSettlement(250L, 0, 0), FlatLevels);
            Assert.AreEqual(3, Vaccine.permanentLevel);

            Die(2);
            Die(3);
            SaveCommitResult fourth = Die(4, new DeathSettlement(900L, 5, 2));

            Assert.AreEqual(1, Vaccine.permanentLevel);
            Assert.AreEqual(0, Vaccine.unusedStatPoints);
            Assert.AreEqual(0L, Vaccine.permanentXp);
            StringAssert.Contains("level=1", fourth.Result);
            StringAssert.Contains("levelsGained=0", fourth.Result);
        }

        [Test]
        public void ADeathCommandWithoutALevelPolicyIsRefused()
        {
            // A defaulted policy has a free curve, so the first settled XP would run the level
            // to the arithmetic ceiling and hand over that many points.
            Assert.Throws<ArgumentException>(
                () => new RegisterDeathCommand(DeathId.For(1L, 1, 1), DeathSettlement.None, default));
        }

        [Test]
        public void TheDefaultLevelPolicyIsTheDraftOne()
        {
            // 180 XP is exactly level 2 on the draft vaccine curve of docs/04 section 1.
            Die(1, new DeathSettlement(ProgressionCurves.DraftVaccineFirstStepXp, 0, 0));

            Assert.AreEqual(2, Vaccine.permanentLevel);
            Assert.AreEqual(ProgressionCurves.DraftPointsPerVaccineLevel, Vaccine.unusedStatPoints);
        }

        // --- duplicates --------------------------------------------------------------------

        [Test]
        public void TheSameDeathNeverChargesTwoClones()
        {
            // [확정] docs/04 section 5: 중복 deathId로 두 번 차감 금지.
            SaveCommitResult first = Die(1);
            SaveCommitResult again = Die(1);

            Assert.AreEqual(SaveCommitVerdict.Committed, first.Verdict);
            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, again.Verdict);
            Assert.AreEqual(first.Result, again.Result);
            Assert.AreEqual(2, Vaccine.copiesRemaining);
            Assert.AreEqual(1, Vaccine.deathCount);
        }

        [Test]
        public void AHundredRepeatsOfOneDeathChargeOneClone()
        {
            // docs/04 section 7's scenario, literally: 동일 사망 100회 중복 호출.
            for (int i = 0; i < 100; ++i)
                Die(1);

            Assert.AreEqual(2, Vaccine.copiesRemaining);
            Assert.AreEqual(1, Vaccine.deathCount);
        }

        [Test]
        public void ADeathRetriedAfterARelaunchIsStillTheSameDeath()
        {
            // 사망 직후 앱 강제 종료 → 마지막 확정 사망영수증으로 1회 복구. 금지: 복제 2회 차감.
            // This is why the id is composed from the run, the actor and a counter instead of
            // being a fresh Guid: a Guid minted at the moment of death does not survive to the
            // next launch, so the retry would arrive as a death nobody had heard of.
            Die(1);

            var relaunched = new SaveStore(_files, _layout);
            relaunched.Open();
            SaveCommitResult retry = relaunched.Commit(
                new RegisterDeathCommand(DeathId.For(1L, 1, 1), DeathSettlement.None));

            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, retry.Verdict);
            Assert.AreEqual(2, relaunched.Snapshot.vaccine.copiesRemaining, "A relaunch charged a second clone.");
        }

        [Test]
        public void AFailedWriteChargesNoCloneAndTheRetryChargesExactlyOne()
        {
            // docs/04 section 7: 저장 실패 → 상태 유지. Then the retry must apply once, not
            // twice and not never.
            _files.WriteDenied.Add(_layout.TempPath);
            SaveCommitResult failed = Die(1);

            Assert.AreEqual(SaveCommitVerdict.WriteFailed, failed.Verdict);
            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies, Vaccine.copiesRemaining,
                "A failed write charged a clone.");

            _files.WriteDenied.Clear();
            SaveCommitResult retry = Die(1);

            Assert.AreEqual(SaveCommitVerdict.Committed, retry.Verdict, retry.Detail);
            Assert.AreEqual(2, Vaccine.copiesRemaining);
        }

        [Test]
        public void TwoDifferentDeathsInOneRunAreTwoDeaths()
        {
            Die(1);
            Die(2);

            Assert.AreEqual(1, Vaccine.copiesRemaining);
            Assert.AreEqual(2, Vaccine.deathCount);
        }

        [Test]
        public void ADeathNeedsAValidIdToBeDedupedBy()
        {
            Assert.Throws<ArgumentException>(
                () => new RegisterDeathCommand(DeathId.None, DeathSettlement.None));
        }

        // --- what destruction cannot reach -------------------------------------------------

        [Test]
        public void TheSaveDocumentHoldsNothingThatFinalDestructionMustProtect()
        {
            // [확정] docs/04 section 2: 최종 파기 과정에서도 화면/음량/키 설정 및 다른 백신
            // 슬롯은 보호. The protection is structural rather than a rule about what to skip:
            // this document holds one vaccine and no settings, so RegisterDeath has nothing to
            // delete them with. If a settings or multi-vaccine field is ever added here, this
            // test fails and the destruction path has to be revisited deliberately.
            var forbidden = new List<string> { "settings", "volume", "language", "keybind", "vaccines", "global" };

            foreach (FieldInfo field in typeof(SaveDocument).GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                string name = field.Name.ToLowerInvariant();
                foreach (string word in forbidden)
                {
                    Assert.IsFalse(
                        name.Contains(word),
                        $"SaveDocument.{field.Name} looks like state 최종 파기 must not be able to touch.");
                }
            }
        }

        // --- the draft settlement rate -----------------------------------------------------

        [Test]
        public void TheDraftPreservationRateMatchesTheDocumentsExample()
        {
            // [초안] docs/04 section 2: min(70%, 20% + 10% × 정화 구역 수) = 0개20%, 1개30%,
            // ... 5개70%. Tested because the worked example is in the document; still [초안],
            // and nothing in the game calls it yet.
            Assert.AreEqual(20, DeathSettlementPolicy.PreservedPercent(0));
            Assert.AreEqual(30, DeathSettlementPolicy.PreservedPercent(1));
            Assert.AreEqual(40, DeathSettlementPolicy.PreservedPercent(2));
            Assert.AreEqual(50, DeathSettlementPolicy.PreservedPercent(3));
            Assert.AreEqual(60, DeathSettlementPolicy.PreservedPercent(4));
            Assert.AreEqual(70, DeathSettlementPolicy.PreservedPercent(5));
        }

        [Test]
        public void TheDraftRateIsCappedAtSeventyPercent()
        {
            Assert.AreEqual(70, DeathSettlementPolicy.PreservedPercent(9));
            Assert.AreEqual(70, DeathSettlementPolicy.PreservedPercent(500));
        }

        [Test]
        public void TheDraftSettlementRoundsDown()
        {
            DeathSettlement settled = DeathSettlementPolicy.Draft(
                uncommittedXp: 999L, uncommittedPoints: 7, purifications: 0);

            Assert.AreEqual(199L, settled.PermanentXp, "999 * 20% is 199.8, and the player gets 199.");
            Assert.AreEqual(1, settled.StatPoints);
            Assert.AreEqual(0, settled.Purifications);
        }

        [Test]
        public void ASettlementCannotTakeXpAway()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DeathSettlement(-1L, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DeathSettlement(0L, -1, 0));
        }

        // --- the commit service ------------------------------------------------------------

        [Test]
        public void TheServiceAcknowledgesOnlyAfterTheStoreHasCommitted()
        {
            var service = new SaveDeathCommitService(_store);
            var acks = new List<DeathCommitAck>();
            service.CommitCompleted += acks.Add;

            service.RequestDeathCommit(new DeathReport(DeathId.For(1L, 1, 1), 120L));

            Assert.AreEqual(1, acks.Count);
            Assert.IsTrue(acks[0].Succeeded, acks[0].FailureReason);
            Assert.AreEqual(SaveCommitVerdict.Committed, service.LastCommit.Verdict);
            Assert.AreEqual(2, Vaccine.copiesRemaining);
            StringAssert.Contains("copiesRemaining=2", service.LastCommit.Result);
        }

        [Test]
        public void TheServiceReportsAFailureWhenNothingWasWritten()
        {
            // docs/04 section 7's forbidden result is the UI saying '보상 획득 완료' first. The
            // ack is the only thing the UI gets, so a failed write has to make it say no.
            var service = new SaveDeathCommitService(_store);
            DeathCommitAck ack = default;
            service.CommitCompleted += a => ack = a;
            _files.WriteDenied.Add(_layout.TempPath);

            service.RequestDeathCommit(new DeathReport(DeathId.For(1L, 1, 1), 120L));

            Assert.IsFalse(ack.Succeeded);
            Assert.IsNotEmpty(ack.FailureReason);
            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies, Vaccine.copiesRemaining,
                "A clone was charged for a death that was never written.");
        }

        [Test]
        public void AReplayThroughTheServiceStillSucceedsWithoutChargingAgain()
        {
            // The caller whose ack was lost asks again and must be told yes - the death IS
            // written - rather than being left to retry forever.
            var service = new SaveDeathCommitService(_store);
            var acks = new List<DeathCommitAck>();
            service.CommitCompleted += acks.Add;
            var report = new DeathReport(DeathId.For(1L, 1, 1), 120L);

            service.RequestDeathCommit(report);
            service.RequestDeathCommit(report);

            Assert.AreEqual(2, acks.Count);
            Assert.IsTrue(acks[1].Succeeded);
            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, service.LastCommit.Verdict);
            Assert.AreEqual(2, Vaccine.copiesRemaining);
        }

        [Test]
        public void TheDefaultSettlementSettlesNothingRatherThanGuessing()
        {
            // D-012 is open and no run tracks 미정산 XP, so zero is the only true figure.
            var service = new SaveDeathCommitService(_store);
            Vaccine.permanentXp = 500L;

            service.RequestDeathCommit(new DeathReport(DeathId.For(1L, 1, 1), 60L));

            Assert.AreEqual(500L, Vaccine.permanentXp);
            StringAssert.Contains("settledXp=0", service.LastCommit.Result);
        }

        [Test]
        public void ASettlementPolicyThatThrowsDoesNotSwallowTheDeath()
        {
            var service = new SaveDeathCommitService(_store, new ThrowingSettlement());
            DeathCommitAck ack = default;
            service.CommitCompleted += a => ack = a;

            service.RequestDeathCommit(new DeathReport(DeathId.For(1L, 1, 1), 60L));

            Assert.IsFalse(ack.Succeeded);
            StringAssert.Contains("settlement policy threw", ack.FailureReason);
        }

        private sealed class ThrowingSettlement : IDeathSettlementSource
        {
            public DeathSettlement SettlementFor(in DeathReport report) =>
                throw new InvalidOperationException("undecided");
        }
    }
}
