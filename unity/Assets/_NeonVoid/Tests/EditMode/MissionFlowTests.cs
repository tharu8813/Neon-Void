using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Missions;
using NeonVoid.Progression;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The mission loop docs/01 fixes as [확정]: 허브 → 메인보드 → 하드웨어 구역 → 목표 → 보스 →
    /// 정화 → 철수 → 허브.
    /// <para>
    /// Two things are under test that are easy to separate and should not be. One is the
    /// geography - which transitions exist and which are refused. The other is the <b>ordering
    /// of the permanent ones</b>: 정화 and 철수 must not advance until the save says it took the
    /// change (docs/04 section 5 step 7), so a gate that refuses is driven here deliberately and
    /// the flow is checked for having stayed exactly where it was.
    /// </para>
    /// </summary>
    public sealed class MissionFlowTests
    {
        /// <summary>
        /// A gate that records what it was asked and can be told to refuse. The real one is a
        /// <see cref="Progression.Save.SaveMissionCommitGate"/> over a store; what matters here
        /// is what the flow does with each answer.
        /// </summary>
        private sealed class RecordingGate : IMissionCommitGate
        {
            public bool RefusePurification;
            public bool RefuseExtraction;

            public readonly List<string> PurifiedKeys = new List<string>();
            public int ExtractionsAsked;
            public int LastExtractionAreas;
            public int LastExtractionObjectives;
            public int LastExtractionLearned;

            public MissionCommitResult CommitPurification(long runId, string areaSaveKey)
            {
                if (RefusePurification)
                    return MissionCommitResult.Refused($"the test refused {areaSaveKey}");

                PurifiedKeys.Add(areaSaveKey);
                return MissionCommitResult.Ok($"committed {areaSaveKey} in run {runId}");
            }

            public MissionCommitResult CommitExtraction(
                long runId, int purifiedAreas, int objectivesCompleted, IReadOnlyList<string> learnedInstanceIds)
            {
                ExtractionsAsked++;
                LastExtractionAreas = purifiedAreas;
                LastExtractionObjectives = objectivesCompleted;
                LastExtractionLearned = learnedInstanceIds?.Count ?? 0;

                return RefuseExtraction
                    ? MissionCommitResult.Refused("the test refused the extraction")
                    : MissionCommitResult.Ok($"committed run {runId}");
            }
        }

        private const int Objectives = 3;

        private RecordingGate _gate;
        private MissionRunState _run;
        private MissionFlow _flow;

        [SetUp]
        public void SetUp()
        {
            _gate = new RecordingGate();
            _run = new MissionRunState();
            _flow = new MissionFlow(_run, _gate);
        }

        private static readonly string[] NoLearns = new string[0];

        private bool Begin(long runId = 1L) =>
            _flow.TryBeginMission(new RunIdentity(runId, 1234u), onTick: 0L);

        /// <summary>Objectives, boss, 정화 - everything an area needs before it is done.</summary>
        private void ClearArea(HardwareId area, int objectives = Objectives)
        {
            Assert.IsTrue(_flow.TryEnterArea(area, objectives), _flow.LastRefusal);
            for (int i = 0; i < objectives; ++i)
                Assert.IsTrue(_flow.TryCompleteObjective(), _flow.LastRefusal);

            Assert.IsTrue(_flow.TryDefeatBoss(), _flow.LastRefusal);
            Assert.IsTrue(_flow.TryPurify(), _flow.LastRefusal);
        }

        // --- the loop ----------------------------------------------------------------------

        [Test]
        public void AFreshFlowIsInTheHubWithNoMission()
        {
            Assert.AreEqual(MissionFlowPhase.Hub, _flow.Phase);
            Assert.AreEqual(AreaProgressPhase.None, _flow.AreaPhase);
            Assert.AreEqual(HardwareId.None, _flow.CurrentArea);
            Assert.AreEqual(0, _flow.PurifiedAreas.Count);
            Assert.IsFalse(_flow.CanExtract);
            Assert.IsFalse(_run.IsRunning);
        }

        [Test]
        public void AMissionRunsFromTheHubThroughOneAreaAndBackToTheHub()
        {
            // 임무 종료는 1개 이상 정화 뒤 철수 성공 ([확정] docs/01 section 1).
            Assert.IsTrue(Begin(), _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Motherboard, _flow.Phase);

            ClearArea(HardwareId.Cpu);
            Assert.AreEqual(AreaProgressPhase.Purified, _flow.AreaPhase);
            Assert.IsTrue(_flow.CanExtract);

            Assert.IsTrue(_flow.TryReturnToMotherboard(), _flow.LastRefusal);
            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Extracted, _flow.Phase);
            Assert.AreEqual(MissionPhase.Completed, _run.Phase);

            Assert.IsTrue(_flow.TryReturnToHub(), _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Hub, _flow.Phase);
            Assert.AreEqual(HardwareId.None, _flow.CurrentArea);
        }

        [Test]
        public void TheNextMissionStartsCleanAfterASuccessfulOne()
        {
            // The reason MissionPhase.Completed had to exist: a successful extraction used to
            // leave the run InProgress, and the next TryBegin was refused as beginning on top of
            // a running mission.
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryExtract(NoLearns);
            _flow.TryReturnToHub();

            Assert.IsTrue(Begin(runId: 2L), _flow.LastRefusal);

            Assert.AreEqual(MissionFlowPhase.Motherboard, _flow.Phase);
            Assert.AreEqual(0, _flow.PurifiedAreas.Count, "the new mission inherited purifications");
            Assert.AreEqual(0, _flow.ObjectivesCompletedInMission);
            Assert.AreEqual(1, _run.MissionLevel, "[확정] 새 의뢰 Lv.1");
            Assert.AreEqual(2L, _run.Identity.RunId);
        }

        [Test]
        public void AMissionCanOnlyBeginFromTheHub()
        {
            Begin();

            Assert.IsFalse(_flow.TryBeginMission(new RunIdentity(2L, 9u), 0L));
            StringAssert.Contains("from the hub", _flow.LastRefusal);
            Assert.AreEqual(1L, _run.Identity.RunId);
        }

        [Test]
        public void AnAreaIsEnteredFromTheMotherboardAndNowhereElse()
        {
            Assert.IsFalse(_flow.TryEnterArea(HardwareId.Cpu, Objectives), "entered an area from the hub");
            StringAssert.Contains("from the motherboard", _flow.LastRefusal);

            Begin();
            Assert.IsTrue(_flow.TryEnterArea(HardwareId.Cpu, Objectives));
            Assert.IsFalse(_flow.TryEnterArea(HardwareId.Gpu, Objectives), "entered an area from inside one");
        }

        [Test]
        public void TheHubIsReachedOnlyWhenAMissionEnds()
        {
            Begin();
            Assert.IsFalse(_flow.TryReturnToHub(), "walked out of a running mission");
            StringAssert.Contains("when a mission ends", _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Motherboard, _flow.Phase);
        }

        [Test]
        public void SomethingThatIsNotOneOfTheFiveAreasIsRefused()
        {
            Begin();

            Assert.IsFalse(_flow.TryEnterArea(HardwareId.None, Objectives));
            StringAssert.Contains($"{DesignConstants.HardwareCount} hardware areas", _flow.LastRefusal);
            Assert.IsFalse(_flow.TryEnterArea((HardwareId)42, Objectives));
        }

        // --- 목표와 보스: docs/01 section 6.6 ----------------------------------------------

        [Test]
        public void TheBossStaysShutWhileAnyObjectiveIsOutstanding()
        {
            // [확정] docs/01 section 6.6: 목표가 파괴/완료되기 전에 보스·철수가 활성화되지 않음.
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            _flow.TryCompleteObjective();
            _flow.TryCompleteObjective();

            Assert.IsFalse(_flow.TryDefeatBoss(), "the boss opened with an objective outstanding");
            StringAssert.Contains("1 objective(s) outstanding", _flow.LastRefusal);
            Assert.AreEqual(AreaProgressPhase.Objectives, _flow.AreaPhase);
        }

        [Test]
        public void TheLastObjectiveOpensTheBoss()
        {
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();

            Assert.AreEqual(AreaProgressPhase.Boss, _flow.AreaPhase);
            Assert.AreEqual(Objectives, _flow.ObjectivesCompletedInArea);
            Assert.IsTrue(_flow.TryDefeatBoss(), _flow.LastRefusal);
            Assert.AreEqual(AreaProgressPhase.Purification, _flow.AreaPhase);
        }

        [Test]
        public void AnExtraObjectiveAfterTheyAreAllDoneIsRefused()
        {
            // 완료 보상은 반복 호출에도 정확히 1회 (docs/01 section 6.6). An extra one here would
            // inflate the objective count the extraction settlement is computed from.
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();

            Assert.IsFalse(_flow.TryCompleteObjective());
            Assert.AreEqual(Objectives, _flow.ObjectivesCompletedInMission);
        }

        [Test]
        public void PurificationStaysShutUntilTheBossIsDown()
        {
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);

            Assert.IsFalse(_flow.TryPurify(), "purified with the objectives outstanding");
            StringAssert.Contains("boss is still standing", _flow.LastRefusal);

            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();

            Assert.IsFalse(_flow.TryPurify(), "purified with the boss alive");
            Assert.AreEqual(0, _gate.PurifiedKeys.Count, "a refused purification still reached the save");
        }

        [Test]
        public void AnAreaWithNoObjectivesIsRefused()
        {
            // With none, the boss and the extraction would be open on entry, which is the state
            // section 6.6 forbids.
            Begin();

            Assert.IsFalse(_flow.TryEnterArea(HardwareId.Cpu, 0));
            StringAssert.Contains("open on entry", _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Motherboard, _flow.Phase);
        }

        [Test]
        public void TheDraftObjectiveRangeIsTheOneTheDocumentStates()
        {
            // [초안] docs/01 section 1: 2~3개 소목표.
            Assert.AreEqual(2, HardwareIds.DraftMinObjectivesPerArea);
            Assert.AreEqual(3, HardwareIds.DraftMaxObjectivesPerArea);

            Begin();
            Assert.IsTrue(_flow.TryEnterArea(HardwareId.Cpu, HardwareIds.DraftMinObjectivesPerArea));
        }

        // --- 정화 and its commit -----------------------------------------------------------

        [Test]
        public void APurificationIsCommittedBeforeItIsCounted()
        {
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();
            _flow.TryDefeatBoss();

            Assert.IsTrue(_flow.TryPurify(), _flow.LastRefusal);

            Assert.AreEqual(new[] { "cpu" }, _gate.PurifiedKeys, "the area reached the save under the wrong key");
            Assert.AreEqual(1, _flow.PurifiedAreas.Count);
            Assert.AreEqual(1, _run.PurificationsCompleted);
            Assert.AreEqual(AreaProgressPhase.Purified, _flow.AreaPhase);
        }

        [Test]
        public void ARefusedPurificationLeavesTheAreaExactlyWhereItWas()
        {
            // docs/04 section 7: 저장 실패 → 보상 미확정·상태 유지. The boss stays down, so the
            // player retries the 정화 rather than the fight.
            _gate.RefusePurification = true;
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();
            _flow.TryDefeatBoss();
            long xpEarnedSoFar = _run.MissionXp;

            Assert.IsFalse(_flow.TryPurify());

            Assert.AreEqual(AreaProgressPhase.Purification, _flow.AreaPhase, "the area advanced anyway");
            Assert.AreEqual(0, _flow.PurifiedAreas.Count);
            Assert.AreEqual(0, _run.PurificationsCompleted);
            Assert.AreEqual(
                xpEarnedSoFar, _run.MissionXp,
                "a refused purification moved the 의뢰 XP the objectives and the boss had earned");
            Assert.IsFalse(_flow.CanExtract);
            StringAssert.Contains("was not committed", _flow.LastRefusal);

            // And the retry works, with nothing having been counted twice.
            _gate.RefusePurification = false;
            Assert.IsTrue(_flow.TryPurify(), _flow.LastRefusal);
            Assert.AreEqual(1, _run.PurificationsCompleted);
        }

        [Test]
        public void ThePurificationItselfPaysNoMissionXp()
        {
            // GDD v1 section 5.4 gives an area a table of 지급 원인 - kills, objectives, the boss -
            // that adds up to its budget, and has no line for the purification. A lump on top
            // would double every area.
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            for (int i = 0; i < Objectives; ++i)
                _flow.TryCompleteObjective();
            _flow.TryDefeatBoss();

            long before = _run.MissionXp;
            Assert.IsTrue(_flow.TryPurify(), _flow.LastRefusal);

            Assert.AreEqual(before, _run.MissionXp, "the purification paid a lump of its own");
            Assert.Greater(before, 0L, "the objectives and the boss paid nothing either");
        }

        [Test]
        public void ObjectivesAndTheBossPayTheDocumentsFigures()
        {
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);

            _flow.TryCompleteObjective();
            Assert.AreEqual(MissionXpBudget.XpForObjective(0), _run.MissionXp);

            _flow.TryCompleteObjective();
            _flow.TryCompleteObjective();
            Assert.AreEqual(MissionXpBudget.XpForObjective(0) * 3L, _run.MissionXp);

            _flow.TryDefeatBoss();
            Assert.AreEqual(
                (MissionXpBudget.XpForObjective(0) * 3L) + MissionXpBudget.XpForBoss(0), _run.MissionXp);
        }

        [Test]
        public void TheSecondAreaPaysMoreForTheSameWork()
        {
            // 구역 XP = round(720 × (1 + 0.18 × 정화 수)), so every cause scales with it. Reading
            // the figure after the purification instead of before would pay the first area at the
            // second's rate.
            Begin();
            ClearArea(HardwareId.Cpu);
            long first = _run.MissionXp;

            _flow.TryReturnToMotherboard();
            ClearArea(HardwareId.Gpu);
            long second = _run.MissionXp - first;

            Assert.Greater(second, first, "the second area paid no more than the first");
            Assert.Greater(_run.MissionLevel, 1, "의뢰 레벨 did not move on all that XP");
        }

        [Test]
        public void AKillPaysItsOwnFigureAndAnElitePaysMore()
        {
            // GDD v1 section 5.4: 일반 적 처치 8, 정예 적 처치 32.
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);

            Assert.IsTrue(_flow.NotifyEnemyKilled(elite: false), _flow.LastRefusal);
            Assert.AreEqual(MissionXpBudget.XpForNormalKill(0), _run.MissionXp);
            Assert.AreEqual(1, _flow.EnemiesKilledInMission);

            Assert.IsTrue(_flow.NotifyEnemyKilled(elite: true), _flow.LastRefusal);
            Assert.AreEqual(
                MissionXpBudget.XpForNormalKill(0) + MissionXpBudget.XpForEliteKill(0), _run.MissionXp);
            Assert.AreEqual(1, _flow.ElitesKilledInMission);
            Assert.Greater(MissionXpBudget.XpForEliteKill(0), MissionXpBudget.XpForNormalKill(0));
        }

        [Test]
        public void AKillOutsideAnAreaPaysNothing()
        {
            // Not refusing the kill - it happened - but refusing to pay for it. A mission that
            // banked XP from the motherboard or after a death would be banking it from nowhere.
            Begin();
            Assert.IsFalse(_flow.NotifyEnemyKilled(elite: false), "a kill on the motherboard paid");
            Assert.AreEqual(0L, _run.MissionXp);

            _flow.TryEnterArea(HardwareId.Cpu, Objectives);
            _flow.TryFail();

            Assert.IsFalse(_flow.NotifyEnemyKilled(elite: false), "a kill after a death paid");
            Assert.AreEqual(0, _flow.EnemiesKilledInMission);
        }

        [Test]
        public void ANewMissionForgetsItsKills()
        {
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.NotifyEnemyKilled(elite: false);
            _flow.NotifyEnemyKilled(elite: true);
            _flow.TryFail();
            _flow.TryReturnToHub();

            Assert.IsTrue(Begin(runId: 2L), _flow.LastRefusal);

            Assert.AreEqual(0, _flow.EnemiesKilledInMission);
            Assert.AreEqual(0, _flow.ElitesKilledInMission);
            Assert.AreEqual(0L, _run.MissionXp);
        }

        [Test]
        public void PurifyingTwiceInOneAreaIsRefused()
        {
            Begin();
            ClearArea(HardwareId.Cpu);

            Assert.IsFalse(_flow.TryPurify());
            StringAssert.Contains("already purified", _flow.LastRefusal);
            Assert.AreEqual(1, _gate.PurifiedKeys.Count, "the save was asked twice");
        }

        // --- 재방문 and 입장 순서 ----------------------------------------------------------

        [Test]
        public void APurifiedAreaCannotBeEnteredAgain()
        {
            // 이미 정화한 하드웨어의 완료 상태는 런 동안 유지 + 중복 정화 금지 (docs/01 section 1).
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryReturnToMotherboard();

            Assert.IsFalse(_flow.TryEnterArea(HardwareId.Cpu, Objectives));
            StringAssert.Contains("already purified", _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Motherboard, _flow.Phase);
        }

        [Test]
        public void AnAreaThatIsNotPurifiedCannotBeLeft()
        {
            // D-010 is open about 재방문, and the instruction with it is 반복 보상 지급·중복 정화
            // 금지. So the restrictive reading is taken and the refusal names the decision.
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);

            Assert.IsFalse(_flow.TryReturnToMotherboard());
            StringAssert.Contains("D-010", _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.InArea, _flow.Phase);
        }

        [Test]
        public void AreasMayBeTakenInAnyOrder()
        {
            // [확정] docs/01 section 1: 입장 순서는 자유.
            Begin();
            ClearArea(HardwareId.Hdd);
            _flow.TryReturnToMotherboard();
            ClearArea(HardwareId.Ram);

            Assert.AreEqual(new[] { "hdd", "ram" }, _gate.PurifiedKeys);
        }

        [Test]
        public void EveryAreaCanBePurifiedInOneMission()
        {
            Begin();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                ClearArea(HardwareIds.All[i]);
                _flow.TryReturnToMotherboard();
            }

            Assert.AreEqual(DesignConstants.HardwareCount, _flow.PurifiedAreas.Count);
            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);
            Assert.AreEqual(
                DesignConstants.HardwareCount, _gate.LastExtractionAreas,
                "the full-clear bonus is computed from this count");
            Assert.AreEqual(
                DesignConstants.HardwareCount * Objectives, _gate.LastExtractionObjectives);
        }

        // --- 철수 --------------------------------------------------------------------------

        [Test]
        public void AnExtractionWithNoPurificationIsRefused()
        {
            // [확정] docs/04 section 7: 정화 0개 철수 → 성공 불가, 금지: 보상 지급.
            Begin();

            Assert.IsFalse(_flow.TryExtract(NoLearns));
            StringAssert.Contains("최소 1개 정화", _flow.LastRefusal);
            Assert.AreEqual(0, _gate.ExtractionsAsked, "the save was asked to pay for nothing");
            Assert.AreEqual(MissionPhase.InProgress, _run.Phase);
        }

        [Test]
        public void OnePurifiedAreaIsEnoughAndFiveAreNotRequired()
        {
            // docs/04 section 7: 정화 1개 철수 → 성공 가능, 금지: 5개 완주 강요.
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryReturnToMotherboard();

            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);
            Assert.AreEqual(1, _gate.LastExtractionAreas);
        }

        [Test]
        public void AnExtractionCanHappenInsideAPurifiedArea()
        {
            // docs/01 section 1 offers 철수 또는 메인보드 귀환 once an area is purified, and
            // section 6 puts an extractionSpawn inside the area.
            Begin();
            ClearArea(HardwareId.Cpu);

            Assert.AreEqual(MissionFlowPhase.InArea, _flow.Phase);
            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Extracted, _flow.Phase);
            Assert.AreEqual(HardwareId.None, _flow.CurrentArea, "the area was left behind");
        }

        [Test]
        public void AnExtractionCannotHappenInsideAnAreaThatIsNotPurified()
        {
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryReturnToMotherboard();
            _flow.TryEnterArea(HardwareId.Gpu, Objectives);

            Assert.IsFalse(_flow.TryExtract(NoLearns), "extracted mid-area");
            StringAssert.Contains("must be purified", _flow.LastRefusal);
            Assert.AreEqual(0, _gate.ExtractionsAsked);
        }

        [Test]
        public void AtMostTwoPluginsMayBeLearned()
        {
            // [확정] R-015, docs/04 section 3.1: 일반·레어 플러그인을 성공 철수 후 0~2개 새로 학습.
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryReturnToMotherboard();

            Assert.IsFalse(_flow.TryExtract(new[] { "a", "b", "c" }));
            StringAssert.Contains($"최대 {DesignConstants.LearnablePluginsPerSuccessfulRun}", _flow.LastRefusal);
            Assert.AreEqual(0, _gate.ExtractionsAsked);

            Assert.IsTrue(_flow.TryExtract(new[] { "a", "b" }), _flow.LastRefusal);
            Assert.AreEqual(2, _gate.LastExtractionLearned);
        }

        [Test]
        public void ARefusedExtractionLeavesTheMissionRunning()
        {
            // docs/04 section 7: 저장 실패 → 상태 유지. 금지: UI 먼저 '보상 획득 완료'.
            _gate.RefuseExtraction = true;
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryReturnToMotherboard();

            Assert.IsFalse(_flow.TryExtract(NoLearns));

            Assert.AreEqual(MissionFlowPhase.Motherboard, _flow.Phase, "the flow said Extracted anyway");
            Assert.AreEqual(MissionPhase.InProgress, _run.Phase);
            StringAssert.Contains("was not committed", _flow.LastRefusal);

            // The retry succeeds, and the mission is only over then.
            _gate.RefuseExtraction = false;
            Assert.IsTrue(_flow.TryExtract(NoLearns), _flow.LastRefusal);
            Assert.AreEqual(MissionPhase.Completed, _run.Phase);
        }

        [Test]
        public void AnExtractedMissionCannotExtractAgain()
        {
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryExtract(NoLearns);

            Assert.IsFalse(_flow.TryExtract(NoLearns));
            Assert.AreEqual(1, _gate.ExtractionsAsked, "the save was asked to pay twice");
        }

        [Test]
        public void AnExtractedMissionTakesNoMoreProgress()
        {
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryExtract(NoLearns);

            Assert.IsFalse(_flow.TryEnterArea(HardwareId.Gpu, Objectives));
            Assert.IsFalse(_flow.TryCompleteObjective());
            Assert.IsFalse(_flow.TryPurify());
            Assert.IsFalse(_run.TryAwardMissionXp(100L), "a finished mission still levelled up");
        }

        // --- 사망 --------------------------------------------------------------------------

        [Test]
        public void ADeathFailsTheMissionFromInsideAnArea()
        {
            // [확정] docs/00: 사망은 해당 의뢰 실패.
            Begin();
            _flow.TryEnterArea(HardwareId.Cpu, Objectives);

            Assert.IsTrue(_flow.TryFail(), _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Failed, _flow.Phase);
            Assert.AreEqual(MissionPhase.Failed, _run.Phase);
            Assert.AreEqual(HardwareId.None, _flow.CurrentArea);
        }

        [Test]
        public void ADeathFailsTheMissionFromTheMotherboardToo()
        {
            Begin();

            Assert.IsTrue(_flow.TryFail(), _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Failed, _flow.Phase);
        }

        [Test]
        public void ThereIsNothingToFailInTheHub()
        {
            Assert.IsFalse(_flow.TryFail());
            StringAssert.Contains("no running mission", _flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.Hub, _flow.Phase);
        }

        [Test]
        public void AFailedMissionNeitherPurifiesNorExtracts()
        {
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryFail();

            Assert.IsFalse(_flow.TryExtract(NoLearns), "a failed mission extracted");
            Assert.IsFalse(_flow.TryPurify());
            Assert.IsFalse(_flow.TryEnterArea(HardwareId.Gpu, Objectives));
            Assert.AreEqual(1, _gate.PurifiedKeys.Count);
            Assert.AreEqual(0, _gate.ExtractionsAsked);
        }

        [Test]
        public void AFailedMissionGoesBackToTheHubAndTheNextOneIsNew()
        {
            // [확정] docs/04 section 2: 허브에서 재시도. 사망한 의뢰는 실패하고 다음 의뢰는 새 시드.
            Begin();
            ClearArea(HardwareId.Cpu);
            _flow.TryFail();

            Assert.IsTrue(_flow.TryReturnToHub(), _flow.LastRefusal);
            Assert.IsTrue(Begin(runId: 2L), _flow.LastRefusal);

            Assert.AreEqual(0, _flow.PurifiedAreas.Count, "the retry kept the failed mission's progress");
            Assert.AreEqual(1, _run.MissionLevel);
            Assert.AreEqual(0, _run.PurificationsCompleted);
        }

        [Test]
        public void TheFlowCannotBeBuiltWithoutARunOrAGate()
        {
            Assert.Throws<System.ArgumentNullException>(() => new MissionFlow(null, _gate));
            Assert.Throws<System.ArgumentNullException>(() => new MissionFlow(_run, null));
        }

        // --- the prototype gate ------------------------------------------------------------

        [Test]
        public void ThePrototypeGatePlaysTheAreaLoopAndRefusesTheExtraction()
        {
            // Everything an extraction produces is permanent. A prototype gate that let one
            // through would teach the UI to show a reward nothing had written - docs/04 section
            // 7's 금지: UI 먼저 '보상 획득 완료'.
            var flow = new MissionFlow(new MissionRunState(), PrototypeMissionCommits.Instance);
            flow.TryBeginMission(new RunIdentity(1L, 1u), 0L);
            flow.TryEnterArea(HardwareId.Cpu, Objectives);
            for (int i = 0; i < Objectives; ++i)
                flow.TryCompleteObjective();
            flow.TryDefeatBoss();

            Assert.IsTrue(flow.TryPurify(), flow.LastRefusal);
            StringAssert.Contains("nothing was written", flow.LastCommitDetail);

            Assert.IsFalse(flow.TryExtract(NoLearns), "the prototype gate committed an extraction");
            StringAssert.Contains("no save is connected", flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.InArea, flow.Phase);
            Assert.AreEqual(MissionPhase.InProgress, flow.Run.Phase);
        }

        // --- the save keys -----------------------------------------------------------------

        [Test]
        public void TheHardwareListIsTheFiveTheDesignFixes()
        {
            Assert.IsTrue(HardwareIds.CountMatchesDesign, "the list and DesignConstants disagree");
            Assert.AreEqual(DesignConstants.HardwareCount, HardwareIds.All.Length);
        }

        [Test]
        public void TheSaveKeysAreTheseExactStrings()
        {
            // A purification lives in permanent state as cleanse:{runId}:{areaKey}. Renaming an
            // enum member must not change a key a player's save already holds, so the strings
            // are pinned here rather than derived from the enum.
            Assert.AreEqual("cpu", HardwareIds.SaveKeyFor(HardwareId.Cpu));
            Assert.AreEqual("gpu", HardwareIds.SaveKeyFor(HardwareId.Gpu));
            Assert.AreEqual("ram", HardwareIds.SaveKeyFor(HardwareId.Ram));
            Assert.AreEqual("ssd", HardwareIds.SaveKeyFor(HardwareId.Ssd));
            Assert.AreEqual("hdd", HardwareIds.SaveKeyFor(HardwareId.Hdd));
        }

        [Test]
        public void EveryAreaHasItsOwnKeyAndNothingElseHasOne()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
                Assert.IsTrue(seen.Add(HardwareIds.SaveKeyFor(HardwareIds.All[i])), "two areas share a save key");

            Assert.Throws<System.ArgumentOutOfRangeException>(() => HardwareIds.SaveKeyFor(HardwareId.None));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => HardwareIds.SaveKeyFor((HardwareId)42));
            Assert.IsFalse(HardwareIds.IsArea(HardwareId.None));
            Assert.IsFalse(HardwareIds.IsArea((HardwareId)42));
        }
    }
}
