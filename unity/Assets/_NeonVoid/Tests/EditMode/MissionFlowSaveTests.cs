using NeonVoid.Missions;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The mission flow over a real <see cref="SaveStore"/>, which is the permanent half of P3's
    /// gate: 실제로 CPU 1개 클리어 → 철수 → 재실행 성장 확인.
    /// <para>
    /// <see cref="MissionFlowTests"/> drives the flow against a fake gate, which proves the
    /// ordering. This proves the other thing: that the loop, the save commands, the XP budget and
    /// the level curve add up to a vaccine that is measurably stronger on the next mission. The
    /// assertion is the number in permanent state, not that a command was called.
    /// </para>
    /// </summary>
    public sealed class MissionFlowSaveTests
    {
        private const string Root = "/tmp/neonvoid-mission-flow-tests";
        private const int Objectives = 3;

        private FakeSaveFileSystem _files;
        private SaveSlotLayout _layout;
        private SaveStore _store;
        private SaveMissionCommitGate _gate;
        private MissionRunState _run;
        private MissionFlow _flow;

        [SetUp]
        public void SetUp()
        {
            _files = new FakeSaveFileSystem();
            _layout = SaveSlotLayout.Under(Root);
            _store = new SaveStore(_files, _layout);
            _store.Open();
            _store.CreateSlot("vaccine-1", "create:vaccine-1");

            _gate = new SaveMissionCommitGate(_store);
            _run = new MissionRunState();
            _flow = new MissionFlow(_run, _gate);
        }

        private VaccineSaveState Vaccine => _store.Snapshot.vaccine;

        /// <summary>Begins a mission on a run id the save allocated, as the game does.</summary>
        private long BeginMission(string sessionNonce)
        {
            SaveCommitResult allocated = _store.Commit(new AllocateRunIdCommand(sessionNonce));
            Assert.IsTrue(allocated.Acknowledged, allocated.Detail);

            long runId = AllocateRunIdCommand.RunIdIn(allocated.Result);
            Assert.IsTrue(_flow.TryBeginMission(new RunIdentity(runId, 7u), 0L), _flow.LastRefusal);
            return runId;
        }

        private void ClearArea(HardwareId area)
        {
            Assert.IsTrue(_flow.TryEnterArea(area, Objectives), _flow.LastRefusal);
            for (int i = 0; i < Objectives; ++i)
                Assert.IsTrue(_flow.TryCompleteObjective(), _flow.LastRefusal);

            Assert.IsTrue(_flow.TryDefeatBoss(), _flow.LastRefusal);
            Assert.IsTrue(_flow.TryPurify(), _flow.LastRefusal);
        }

        private static readonly string[] NoLearns = new string[0];

        [Test]
        public void OneClearedAreaAndAnExtractionBanksTheDocumentsFigure()
        {
            long run = BeginMission("session-1");

            ClearArea(HardwareId.Cpu);
            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);

            // GDD v1 section 5.6: 정화 1개 = 165 백신 XP at the standard three objectives.
            Assert.AreEqual(165L, Vaccine.permanentXp);
            Assert.AreEqual(MissionPhase.Completed, _run.Phase);
            Assert.AreEqual(
                1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(run)),
                "the purification is not in the receipt log");
            Assert.IsNotNull(
                _store.Snapshot.FindReceipt(ExtractRunCommand.ReceiptIdFor(run)),
                "the extraction left no receipt");
        }

        [Test]
        public void TheSecondMissionIsPlayedByAStrongerVaccine()
        {
            // This is P3's gate as a unit test: 재실행 성장 확인. Two missions, each one CPU
            // cleared and extracted, and the permanent total and the level both move.
            BeginMission("session-1");
            ClearArea(HardwareId.Cpu);
            _flow.TryExtract(NoLearns);
            _flow.TryReturnToHub();

            Assert.AreEqual(165L, Vaccine.permanentXp, "after one mission");
            Assert.AreEqual(1, Vaccine.permanentLevel, "165 XP is short of the 180 the first level costs");

            BeginMission("session-2");
            ClearArea(HardwareId.Cpu);
            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);

            Assert.AreEqual(330L, Vaccine.permanentXp, "the second mission did not add to the first");
            Assert.AreEqual(2, Vaccine.permanentLevel, "330 XP has paid for the first level");
            Assert.AreEqual(
                ProgressionCurves.DraftPointsPerVaccineLevel, Vaccine.unusedStatPoints,
                "백신 레벨 1당 영구 포인트 1");
        }

        [Test]
        public void TheSameAreaInTwoMissionsIsTwoPurifications()
        {
            // The receipt key carries the run, so clearing CPU again next mission is real
            // progress - while clearing it twice in one mission is not.
            long first = BeginMission("session-1");
            ClearArea(HardwareId.Cpu);
            _flow.TryExtract(NoLearns);
            _flow.TryReturnToHub();

            long second = BeginMission("session-2");
            ClearArea(HardwareId.Cpu);

            Assert.AreEqual(1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(first)));
            Assert.AreEqual(1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(second)));
        }

        [Test]
        public void TwoClearedAreasBankTheTwoAreaFigure()
        {
            BeginMission("session-1");
            ClearArea(HardwareId.Cpu);
            _flow.TryReturnToMotherboard();
            ClearArea(HardwareId.Gpu);

            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);

            // GDD v1 section 5.6: 정화 2개 = 330.
            Assert.AreEqual(330L, Vaccine.permanentXp);
            Assert.AreEqual(2, Vaccine.permanentLevel);
        }

        [Test]
        public void AFullClearBanksTheBonusAndReachesTheDocumentsLevel()
        {
            // GDD v1 section 5.6: 5개 전부 정화 = 1,045 백신 XP, and 전체 정화 1회 → Lv.4.
            BeginMission("session-1");
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                ClearArea(HardwareIds.All[i]);
                if (i < HardwareIds.All.Length - 1)
                    _flow.TryReturnToMotherboard();
            }

            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);

            Assert.AreEqual(1045L, Vaccine.permanentXp);
            Assert.AreEqual(4, Vaccine.permanentLevel);
            Assert.AreEqual(3, Vaccine.unusedStatPoints);
        }

        [Test]
        public void LearnedPluginsLandInTheArchive()
        {
            BeginMission("session-1");
            ClearArea(HardwareId.Cpu);

            Assert.IsTrue(_flow.TryExtract(new[] { "learned-1", "learned-2" }), _flow.LastRefusal);

            Assert.Contains("learned-1", Vaccine.archivePluginInstanceIds);
            Assert.Contains("learned-2", Vaccine.archivePluginInstanceIds);
            Assert.AreEqual(0, Vaccine.carriedPluginInstanceIds.Count);
        }

        [Test]
        public void APurificationTheStoreCannotWriteStopsTheFlow()
        {
            // docs/04 section 7: 저장 실패 → 보상 미확정·상태 유지. The flow must not count a
            // purification the disk refused.
            BeginMission("session-1");
            Assert.IsTrue(_flow.TryEnterArea(HardwareId.Cpu, Objectives));
            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();
            _flow.TryDefeatBoss();

            _files.WriteDenied.Add(_layout.TempPath);
            Assert.IsFalse(_flow.TryPurify(), "a denied write was treated as a purification");

            Assert.AreEqual(AreaProgressPhase.Purification, _flow.AreaPhase);
            Assert.AreEqual(0, _run.PurificationsCompleted);
            Assert.IsFalse(_flow.CanExtract);

            // And once the disk comes back, the retry commits exactly one.
            _files.WriteDenied.Clear();
            Assert.IsTrue(_flow.TryPurify(), _flow.LastRefusal);
            Assert.AreEqual(1, _run.PurificationsCompleted);
            Assert.AreEqual(1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(1L)));
        }

        [Test]
        public void AnExtractionTheStoreCannotWriteLeavesTheMissionRunning()
        {
            BeginMission("session-1");
            ClearArea(HardwareId.Cpu);
            _flow.TryReturnToMotherboard();

            _files.WriteDenied.Add(_layout.TempPath);
            Assert.IsFalse(_flow.TryExtract(NoLearns));

            Assert.AreEqual(MissionFlowPhase.Motherboard, _flow.Phase);
            Assert.AreEqual(MissionPhase.InProgress, _run.Phase);
            Assert.AreEqual(0L, Vaccine.permanentXp, "a failed write paid out");

            _files.WriteDenied.Clear();
            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);
            Assert.AreEqual(165L, Vaccine.permanentXp);
        }

        [Test]
        public void AMissionOnARunTheSlotNeverBeganCannotPurify()
        {
            // The flow does not allocate run ids; if it is handed one the save never issued, the
            // save refuses it and the flow stays put rather than playing on uncommitted.
            Assert.IsTrue(_flow.TryBeginMission(new RunIdentity(99L, 7u), 0L), _flow.LastRefusal);
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();
            _flow.TryDefeatBoss();

            Assert.IsFalse(_flow.TryPurify());
            StringAssert.Contains("never begun", _flow.LastRefusal);
            Assert.AreEqual(0, _run.PurificationsCompleted);
        }

        [Test]
        public void TheGateReportsAlreadyAppliedAsCommitted()
        {
            // docs/04 section 7: 저장 성공 ACK 유실 → 기존 영수증 조회. A purification whose ack
            // was lost is the same purification, so the gate treats the store's AlreadyApplied as
            // a commit - otherwise the flow would refuse a transition that had in fact been saved.
            long run = BeginMission("session-1");
            _store.Commit(new CleanseHardwareCommand(run, HardwareIds.SaveKeyFor(HardwareId.Cpu)));

            MissionCommitResult replay = _gate.CommitPurification(run, HardwareIds.SaveKeyFor(HardwareId.Cpu));

            Assert.IsTrue(replay.Committed, replay.Detail);
            StringAssert.Contains("AlreadyApplied", replay.Detail);
        }

        [Test]
        public void TheGateTurnsARefusedCommandIntoARefusalRatherThanAThrow()
        {
            // The command constructors throw - on three learns, on a settlement with no
            // purification. An exception escaping the gate would be a state the flow cannot
            // report, so it comes back as a refusal with the message attached.
            long run = BeginMission("session-1");

            MissionCommitResult tooMany = _gate.CommitExtraction(run, 1, 3, new[] { "a", "b", "c" });
            Assert.IsFalse(tooMany.Committed);
            StringAssert.Contains("could not be built", tooMany.Detail);

            MissionCommitResult noAreas = _gate.CommitExtraction(run, 0, 0, NoLearns);
            Assert.IsFalse(noAreas.Committed);
            StringAssert.Contains("could not be built", noAreas.Detail);

            MissionCommitResult badKey = _gate.CommitPurification(run, "cpu:1");
            Assert.IsFalse(badKey.Committed);
            StringAssert.Contains("could not be built", badKey.Detail);
        }

        [Test]
        public void GrowthCachesBecomePointsWhenTheGateIsToldAboutThem()
        {
            // [초안 v0.1] GDD v1 section 5.6: 캐시 1개당 1P. Nothing rolls them yet, so the gate's
            // default is zero; this is the path that will carry them once something does.
            _gate.GrowthCachesCollected = 2;
            BeginMission("session-1");
            ClearArea(HardwareId.Cpu);

            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);

            Assert.AreEqual(2, Vaccine.unusedStatPoints, "the caches were not banked");
            Assert.AreEqual(165L, Vaccine.permanentXp, "caches are points, not XP");
        }
    }
}
