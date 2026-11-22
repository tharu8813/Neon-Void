using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NeonVoid.Combat;
using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The loop closed on itself: 허브 → 의뢰 시작 → 메인보드 → 구역 → 정화 → 철수 → 허브 →
    /// 다시 의뢰 시작.
    /// <para>
    /// Everything under this has its own tests. What only this can show is that a session is
    /// more than one mission - that the player comes back, that the next mission is a different
    /// run with a different map, and that permanent growth from the first one is still there for
    /// the second. Until the hub existed the mission began in <c>Awake</c> and nothing ever
    /// returned, so the extraction the save committed was the last thing that happened.
    /// </para>
    /// </summary>
    public sealed class HubPlayModeTests
    {
        private const int DwellTicks = 2;
        private const float PortalRadius = 3f;
        private const float RingRadius = 9f;
        private const float HubReach = 3f;
        private const uint SessionSeed = 20_261_121u;

        private static readonly Vector3 HubAt = new Vector3(-900f, 0f, -900f);
        private static readonly Vector3 BoardAt = new Vector3(-400f, 0f, -400f);
        private static readonly string[] NoLearns = new string[0];

        private string _root;
        private PrototypeArena _arena;
        private GameObject _saveObject;
        private GameObject _boardObject;
        private GameObject _growthObject;
        private GameObject _hubObject;
        private GameObject _hudObject;
        private GameObject _rewardObject;
        private MotherboardDirector _motherboard;
        private HubRuntime _hub;
        private VaccineGrowthDirector _growth;
        private SaveStore _store;

        [SetUp]
        public void SetUp()
        {
            // docs/04 section 4: a test that reached the player's save directory is a failed
            // test. Everything here goes into a directory made for this one test.
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-playhub-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudObject != null)
                UnityEngine.Object.Destroy(_hudObject);
            _hudObject = null;

            if (_rewardObject != null)
                UnityEngine.Object.Destroy(_rewardObject);
            _rewardObject = null;

            if (_hubObject != null)
                UnityEngine.Object.Destroy(_hubObject);
            if (_boardObject != null)
                UnityEngine.Object.Destroy(_boardObject);
            if (_growthObject != null)
                UnityEngine.Object.Destroy(_growthObject);
            if (_saveObject != null)
                UnityEngine.Object.Destroy(_saveObject);

            _arena?.Dispose();
            _arena = null;
            _motherboard = null;
            _hub = null;
            _growth = null;
            _store = null;

            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private MissionFlow Flow => _arena.Mission.Flow;

        private void ConnectSave()
        {
            _store = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));

            _saveObject = new GameObject("Vaccine Save Director");
            _saveObject.SetActive(false);
            VaccineSaveDirector director = _saveObject.AddComponent<VaccineSaveDirector>();
            typeof(VaccineSaveDirector)
                .GetField("writeToRealSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, true);
            director.UseStore(_store, sessionNonce: "playmode-hub");
            _saveObject.SetActive(true);

            Assert.IsTrue(director.CanCommit, director.Result.Detail);
        }

        /// <summary>
        /// A hub, a motherboard far from it, and an arena whose mission has not begun. Built in
        /// that order because the save gate is resolved in the mission director's Awake, and the
        /// hub needs the mission and the player to already exist.
        /// </summary>
        private IEnumerator Hub(bool connectSave = true)
        {
            if (connectSave)
                ConnectSave();

            _arena = PrototypeArena.BuildWaitingInTheHub(SessionSeed);

            // A mission is 8~12 minutes of combat in one test; the point here is the loop.
            CombatStats stats = PrototypeArena.PlayerStats();
            stats.MaxHealth = 1_000_000f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, stats);

            _boardObject = new GameObject("Motherboard");
            _boardObject.SetActive(false);
            _boardObject.transform.position = BoardAt;
            _motherboard = _boardObject.AddComponent<MotherboardDirector>();
            _motherboard.Configure(_arena.Mission, null, _arena.PlayerObject.transform);
            _boardObject.SetActive(true);
            _motherboard.ConfigurePortals(RingRadius, PortalRadius, DwellTicks);

            // 백신 성장, added after the player's stats are set so the block it captures as
            // "uninvested" is the one this test configured.
            var growthObject = new GameObject("Vaccine Growth Director");
            growthObject.SetActive(false);
            _growth = growthObject.AddComponent<VaccineGrowthDirector>();
            _growth.Configure(
                _arena.Player,
                _saveObject != null ? _saveObject.GetComponent<VaccineSaveDirector>() : null);
            growthObject.SetActive(true);
            _growthObject = growthObject;

            _hubObject = new GameObject("Defender Hub");
            _hubObject.SetActive(false);
            _hubObject.transform.position = HubAt;
            _hub = _hubObject.AddComponent<HubRuntime>();
            _hub.Configure(_arena.Mission, _motherboard, _arena.PlayerObject.transform);
            _hubObject.SetActive(true);
            _hub.ConfigureGeometry(RingRadius, HubReach);

            StandOn(HubAt);
            yield return _arena.Tick(1);
        }

        private void StandOn(Vector3 destination)
        {
            Transform player = _arena.PlayerObject.transform;
            float y = destination.y + AreaWorldBuilder.PlayerStandingLiftUnits;
            player.position = new Vector3(player.position.x, y, player.position.z);
            _arena.Driver.TeleportTo(new Vector2(destination.x, destination.z));
        }

        private static Vector3 Flat(Vector3 at) => new Vector3(at.x, 0f, at.z);

        private Vector3 PlayerFlat => Flat(_arena.PlayerObject.transform.position);

        /// <summary>Objectives, boss and 정화, from inside whatever area is standing.</summary>
        private IEnumerator ClearTheStandingArea()
        {
            AreaBuildResult area = _motherboard.CurrentArea;
            Assert.IsNotNull(area, "no area is standing");

            // Whatever this hardware's objectives are: the player chose the area, so the
            // shapes are not known here.
            _arena.MakePlayerUnkillable();
            yield return _arena.ClearObjectives(area);
            Assert.AreEqual(AreaProgressPhase.Boss, Flow.AreaPhase, Flow.LastRefusal);

            PrototypeArena.Kill(area.BossGate.Boss);
            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase);

            area.Runtime.ConfigureDwell(PortalRadius, DwellTicks, PortalRadius, DwellTicks);
            StandOn(_motherboard.CurrentArea.PurificationConsole.position);
            yield return _arena.Tick(DwellTicks + 1);

            Assert.AreEqual(AreaProgressPhase.Purified, Flow.AreaPhase, Flow.LastRefusal);
        }

        /// <summary>Walks into a portal and into the area behind it.</summary>
        private IEnumerator EnterArea(HardwareId hardware)
        {
            StandOn(_motherboard.Portal(hardware).position);
            yield return _arena.Tick(DwellTicks + 2);
            Assert.IsNotNull(_motherboard.CurrentArea, $"{hardware}'s portal never opened");
        }

        /// <summary>
        /// The same hub, with a save director that is deliberately not writing anything.
        /// <para>
        /// This is a supported way to play - the scene says so on the component - and it is how
        /// anyone measuring the 8~12분 target will run it, because an exploratory death must not
        /// cost a clone that [확정] docs/04 section 2 says can never be recovered.
        /// </para>
        /// </summary>
        private IEnumerator HubWithoutPermanentWrites()
        {
            _saveObject = new GameObject("Vaccine Save Director");
            _saveObject.SetActive(false);
            VaccineSaveDirector director = _saveObject.AddComponent<VaccineSaveDirector>();
            typeof(VaccineSaveDirector)
                .GetField("writeToRealSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, false);
            _saveObject.SetActive(true);
            Assert.IsFalse(director.CanCommit);

            _arena = PrototypeArena.BuildWaitingInTheHub(SessionSeed);

            // Before the hub, which resolves it in its own Awake.
            _growthObject = new GameObject("Vaccine Growth Director");
            _growthObject.SetActive(false);
            _growth = _growthObject.AddComponent<VaccineGrowthDirector>();
            _growth.Configure(_arena.Player, director);
            _growthObject.SetActive(true);

            _hubObject = new GameObject("Defender Hub");
            _hubObject.SetActive(false);
            _hubObject.transform.position = HubAt;
            _hub = _hubObject.AddComponent<HubRuntime>();
            _hub.Configure(_arena.Mission, null, _arena.PlayerObject.transform);
            _hubObject.SetActive(true);
            _hub.ConfigureGeometry(RingRadius, HubReach);

            StandOn(HubAt);
            yield return _arena.Tick(1);
        }

        [UnityTest]
        public IEnumerator AMissionStillBeginsWhenPermanentWritesAreSwitchedOff()
        {
            // Found by playing the generated scene: with writes off the save director took no
            // session nonce, and the first mission threw on the key it would have retried a run
            // id allocation under. The one mode that exists so a run can be walked without
            // consequences was the one mode that crashed.
            yield return HubWithoutPermanentWrites();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("motherboard"));
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission), "the mission did not begin");
            yield return _arena.Tick(1);

            Assert.AreEqual(MissionFlowPhase.Motherboard, Flow.Phase);
            Assert.Greater(Flow.Run.Identity.RunId, 0L, "a mission began with no run id at all");

            // And nothing permanent is claimed: the prototype gate refuses 철수, whose whole
            // product is permanent. docs/04 section 7: 저장 실패 → 보상 미확정.
            Assert.IsTrue(Flow.TryEnterArea(HardwareId.Cpu, 2));
            Assert.IsTrue(Flow.TryCompleteObjective());
            Assert.IsTrue(Flow.TryCompleteObjective());
            Assert.IsTrue(Flow.TryDefeatBoss());
            Assert.IsTrue(Flow.TryPurify(), Flow.LastRefusal);
            Assert.IsFalse(Flow.TryExtract(NoLearns), "an uncommitted session settled an extraction");
        }

        [UnityTest]
        public IEnumerator TwoMissionsWithoutASaveStillGetDifferentRunIds()
        {
            // Nothing is committed under a fallback id, so the only thing it has to do is
            // differ - a repeated one would make the in-memory flow look like a 중복 시작.
            yield return HubWithoutPermanentWrites();

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            long first = Flow.Run.Identity.RunId;

            Assert.IsTrue(Flow.TryEnterArea(HardwareId.Cpu, 1));
            Assert.IsTrue(Flow.TryCompleteObjective());
            Assert.IsTrue(Flow.TryDefeatBoss());
            Assert.IsTrue(Flow.TryPurify());
            Assert.IsTrue(Flow.TryFail());
            yield return _arena.Tick(2);

            Assert.AreEqual(MissionFlowPhase.Hub, Flow.Phase);
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            Assert.AreNotEqual(first, Flow.Run.Identity.RunId);
        }

        // --- standing in the hub ------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheHubStartsNoMissionAndPlacesAllFourTerminals()
        {
            yield return Hub();

            // [확정] docs/00: 허브에서 의뢰 시작. A mission begun on Awake is one the player
            // never asked for.
            Assert.AreEqual(MissionFlowPhase.Hub, Flow.Phase);
            Assert.AreEqual(0, _arena.Mission.MissionsBegun, "a mission began without the terminal");
            Assert.IsFalse(Flow.Run.IsRunning);
            Assert.IsNull(_motherboard.CurrentArea);

            // All four, including the three with nothing behind them: the set is [확정] and a
            // hub with three posts in it would look finished.
            for (int i = 0; i < HubTerminals.All.Length; ++i)
            {
                HubTerminalKind kind = HubTerminals.All[i];
                Assert.IsNotNull(_hub.Terminal(kind), $"{kind} was not placed");
            }
        }

        [UnityTest]
        public IEnumerator AnUnconnectedTerminalRefusesAndSaysWhy()
        {
            // 보관 DB, the last one still waiting: 플러그인 획득 이후. 스킬 장착 used to be
            // here and was connected by [확정] D-009 (2026-11-22).
            yield return Hub();
            Assert.IsFalse(
                HubTerminals.IsConnected(HubTerminalKind.Database),
                "보관 DB was connected; this test needs a terminal that is not");

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("보관 DB"));
            Assert.IsFalse(_hub.TryUseTerminal(HubTerminalKind.Database));
            Assert.AreEqual(MissionFlowPhase.Hub, Flow.Phase, "an unfinished terminal started something");
        }

        [UnityTest]
        public IEnumerator InteractingOutOfReachOfEveryTerminalDoesNothing()
        {
            // The terminal is a place as well as a button. 상호작용 pressed in the middle of the
            // hub should not start a mission from across the room.
            yield return Hub();

            _hub.RequestInteract();
            yield return _arena.Tick(2);

            Assert.AreEqual(MissionFlowPhase.Hub, Flow.Phase);
            Assert.AreEqual(0, _arena.Mission.MissionsBegun);
        }

        [UnityTest]
        public IEnumerator WalkingToTheMissionTerminalAndInteractingBeginsTheMission()
        {
            yield return Hub();

            StandOn(_hub.Terminal(HubTerminalKind.Mission).position);
            yield return _arena.Tick(1);
            Assert.IsTrue(_hub.IsInReach(HubTerminalKind.Mission), "the terminal is out of reach of itself");

            _hub.RequestInteract();
            yield return _arena.Tick(2);

            // 의뢰 시작 → 메인보드 도착 ([확정] docs/00 section 1).
            Assert.AreEqual(MissionFlowPhase.Motherboard, Flow.Phase);
            Assert.AreEqual(1, _arena.Mission.MissionsBegun);
            Assert.IsTrue(Flow.Run.IsRunning);
            Assert.Less(
                Vector3.Distance(PlayerFlat, BoardAt), 5f,
                "the mission began but the player is not on the motherboard");

            // The first mission plays the seed that was typed in, so a report can be reproduced.
            Assert.AreEqual(SessionSeed, Flow.Run.Identity.Seed);
            Assert.Greater(Flow.Run.Identity.RunId, 0L, "the mission began without a run id");
        }

        [UnityTest]
        public IEnumerator TheMissionTerminalDoesNothingWhileAMissionIsRunning()
        {
            yield return Hub();
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);

            long first = Flow.Run.Identity.RunId;
            Assert.IsFalse(_hub.TryUseTerminal(HubTerminalKind.Mission), "a second mission began on top of one");
            Assert.AreEqual(first, Flow.Run.Identity.RunId);
            Assert.AreEqual(1, _arena.Mission.MissionsBegun);
        }

        // --- 백신 성장 ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SpendingAPointRaisesTheStatAndSurvivesAReload()
        {
            // [확정] GDD v1 section 4.3: 레벨업마다 영구 포인트를 지급하고 허브에서 투자한다.
            // Until the terminal existed every level-up added to a number that did nothing.
            yield return Hub();
            _store.Snapshot.vaccine.unusedStatPoints = 6;

            float before = _arena.Player.Stats.MaxHealth;
            Assert.IsTrue(_growth.TryInvest(InvestableStat.MaxHealth, 4), _growth.LastRefusal);
            yield return _arena.Tick(1);

            Assert.AreEqual(
                before + (StatInvestments.DraftMaxHealthPerStep * 4f), _arena.Player.Stats.MaxHealth, 0.01f,
                "the investment never reached the player");
            Assert.AreEqual(2, _growth.UnusedStatPoints);

            // Read back from a second store, so this is what is on disk and not what the one
            // that wrote it remembers.
            var reopened = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            reopened.Open();
            Assert.AreEqual(SaveStoreState.Ready, reopened.State, reopened.HaltReason);
            Assert.AreEqual(4, VaccineInvestments.PointsSpent(reopened.Snapshot.vaccine));
        }

        [UnityTest]
        public IEnumerator RedistributingIsFreeAndPutsThePlayerBackWhereTheyStarted()
        {
            // [확정] 재분배 비용 없이 언제든 초기화·재투자.
            yield return Hub();
            _store.Snapshot.vaccine.unusedStatPoints = 9;

            float before = _arena.Player.Stats.MaxHealth;
            Assert.IsTrue(_growth.TryInvest(InvestableStat.MaxHealth, 5), _growth.LastRefusal);
            Assert.IsTrue(_growth.TryInvest(InvestableStat.Tenacity, 2), _growth.LastRefusal);
            yield return _arena.Tick(1);
            Assert.AreNotEqual(before, _arena.Player.Stats.MaxHealth);

            Assert.IsTrue(_growth.TryRedistribute(), _growth.LastRefusal);
            yield return _arena.Tick(1);

            Assert.AreEqual(before, _arena.Player.Stats.MaxHealth, 0.01f, "the stat did not come back down");
            Assert.AreEqual(9, _growth.UnusedStatPoints, "재분배가 무료가 아니다");
            Assert.AreEqual(0, _growth.PointsSpent);
        }

        [UnityTest]
        public IEnumerator AStatWithNoFieldToLandOnIsRefusedBeforeAnythingIsSpent()
        {
            yield return Hub();
            _store.Snapshot.vaccine.unusedStatPoints = 10;

            Assert.IsFalse(_growth.TryInvest(InvestableStat.MoveSpeed), "a point was spent on nothing");
            Assert.AreEqual(10, _growth.UnusedStatPoints);
            Assert.IsNotEmpty(_growth.LastRefusal);
        }

        [UnityTest]
        public IEnumerator TheGrowthTerminalIsReachableFromTheHubAndNotFromAMission()
        {
            yield return Hub();

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Growth), "the growth terminal did nothing");

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);
            Assert.IsFalse(
                _hub.TryUseTerminal(HubTerminalKind.Growth),
                "the hub's terminal answered from the motherboard");
        }

        // --- and back again ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnExtractionEndsInTheHubWithTheGrowthItEarned()
        {
            yield return Hub();
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);

            yield return EnterArea(HardwareId.Cpu);
            yield return ClearTheStandingArea();

            Assert.IsTrue(Flow.TryExtract(NoLearns), Flow.LastRefusal);
            yield return _arena.Tick(3);

            // 철수 정산 → 허브 복귀, and the player is standing in it.
            Assert.AreEqual(MissionFlowPhase.Hub, Flow.Phase);
            Assert.AreEqual(MissionPhase.Completed, Flow.Run.Phase);
            Assert.IsNull(_motherboard.CurrentArea, "the area is still standing");
            Assert.Less(
                Vector3.Distance(PlayerFlat, HubAt), 5f, "the player did not come back to the hub");

            // And what the extraction banked is in permanent state, read back from a second
            // store rather than from the one that wrote it.
            var reopened = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            reopened.Open();
            Assert.Greater(reopened.Snapshot.vaccine.permanentXp, 0L, "the extraction banked nothing");
        }

        [UnityTest]
        public IEnumerator TheNextMissionIsANewRunOnANewMapAndKeepsWhatWasEarned()
        {
            // The whole point of a hub. [확정] docs/04 section 5 forbids 같은 runId 중복 시작 -
            // a second mission on the first one's id would have its first death refused as a
            // death already settled, and no clone would ever be charged again.
            yield return Hub();
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);

            long firstRun = Flow.Run.Identity.RunId;
            uint firstSeed = Flow.Run.Identity.Seed;

            yield return EnterArea(HardwareId.Cpu);
            uint firstCpuSeed = _motherboard.CurrentArea.Layout.Spec.Seed;
            yield return ClearTheStandingArea();
            Assert.IsTrue(Flow.TryExtract(NoLearns), Flow.LastRefusal);
            yield return _arena.Tick(3);

            long bankedXp = _store.Snapshot.vaccine.permanentXp;
            Assert.Greater(bankedXp, 0L);

            // Second mission, from the same terminal.
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission), "the hub would not start a second mission");
            yield return _arena.Tick(1);

            Assert.AreEqual(2, _arena.Mission.MissionsBegun);
            Assert.AreEqual(MissionFlowPhase.Motherboard, Flow.Phase);
            Assert.AreNotEqual(firstRun, Flow.Run.Identity.RunId, "the second mission reused the first run id");
            Assert.AreNotEqual(firstSeed, Flow.Run.Identity.Seed, "the second mission replays the first map");

            // 새 의뢰 Lv.1: the mission level is temporary and starts over.
            Assert.AreEqual(MissionRunState.DesignStartingMissionLevel, Flow.Run.MissionLevel);
            Assert.AreEqual(0L, Flow.Run.MissionXp);
            Assert.AreEqual(0, Flow.PurifiedAreas.Count, "the first mission's purification carried over");

            // CPU is a legal choice again - 정화 is per mission - and it is a different map.
            yield return EnterArea(HardwareId.Cpu);
            Assert.AreNotEqual(
                firstCpuSeed, _motherboard.CurrentArea.Layout.Spec.Seed,
                "[확정] 다시 시작한 의뢰의 맵과 적은 새로 생성");

            // And the permanent growth the first mission banked is still there.
            Assert.AreEqual(bankedXp, _store.Snapshot.vaccine.permanentXp, "approved permanent XP was taken back");
        }

        [UnityTest]
        public IEnumerator ADeathEndsInTheHubAndTheMissionCanBeRetried()
        {
            // [확정] docs/04 section 2: 1~3번째 사망은 잔여 복제를 차감하고 허브에서 재시도.
            yield return Hub();
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);

            long firstRun = Flow.Run.Identity.RunId;
            yield return EnterArea(HardwareId.Cpu);
            GameObject areaRoot = _motherboard.CurrentArea.Root;

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(4);

            Assert.AreEqual(MissionFlowPhase.Hub, Flow.Phase);
            Assert.AreEqual(MissionPhase.Failed, Flow.Run.Phase);
            Assert.IsTrue(areaRoot == null, "the failed mission's area is still in the scene");
            Assert.Less(Vector3.Distance(PlayerFlat, HubAt), 5f, "the player did not come back to the hub");

            // One clone spent, and the retry is a new run.
            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies - 1, _store.Snapshot.vaccine.copiesRemaining,
                "the death did not cost a clone");

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission), "the hub would not allow a retry");
            yield return _arena.Tick(1);
            Assert.AreNotEqual(firstRun, Flow.Run.Identity.RunId, "the retry reused the failed run's id");
        }

        [UnityTest]
        public IEnumerator EachMissionGetsItsOwnBoard()
        {
            // The board is the run's, not the scene's. Taken in Awake it would have been the
            // board of the absent run - 쉬움 보장 included - and every mission would inherit it.
            yield return Hub();
            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(2);

            MissionBoardPlan first = _motherboard.Board;
            Assert.GreaterOrEqual(first.EasyCount, 1, "[확정] 최소 하나 Easy");

            yield return EnterArea(HardwareId.Cpu);
            yield return ClearTheStandingArea();
            Assert.IsTrue(Flow.TryExtract(NoLearns), Flow.LastRefusal);
            yield return _arena.Tick(3);

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(2);

            Assert.GreaterOrEqual(_motherboard.Board.EasyCount, 1, "[확정] 최소 하나 Easy, every mission");
            Assert.AreEqual(
                NeonVoid.Core.DesignConstants.HardwareCount, _motherboard.Board.Count,
                "the second mission's board is not a whole board");
        }
    
        // --- 세션의 두 번째 사망 ---------------------------------------------------------

        /// <summary>
        /// Puts the player back on their feet by hand.
        /// <para>
        /// Only for the test where nothing can commit: the game revives the vaccine after the
        /// write that spends the clone, so a death that never commits leaves it down - which is
        /// correct, and is the state that test is about.
        /// </para>
        /// </summary>
        private void PutThePlayerBackOnTheirFeet()
        {
            CombatStats stats = PrototypeArena.PlayerStats();
            stats.MaxHealth = 1_000_000f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, stats);

            // ActorDeathReaction parks the body: controller off, colliders off. A revive has to
            // undo that, and listing it here is also the clearest statement of what the missing
            // one will have to do.
            var controller = _arena.PlayerObject.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = true;

            Collider[] colliders = _arena.PlayerObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
                colliders[i].enabled = true;

            Assert.IsTrue(_arena.Player.IsAlive, "the player is still down");
        }

        [UnityTest]
        public IEnumerator ASecondDeathInTheSameSessionCostsASecondClone()
        {
            // [확정] docs/04 section 2: 1~3번째 사망은 잔여 복제를 차감. The ledger holds the one
            // death that ends a run, and a session is a series of runs now - so a ledger that
            // was never cleared answered the second mission's death with RejectedRunAlreadyEnded
            // and charged nothing for it.
            yield return Hub();

            for (int mission = 1; mission <= 2; ++mission)
            {
                Assert.IsTrue(
                    _hub.TryUseTerminal(HubTerminalKind.Mission), $"mission {mission} would not begin");
                yield return _arena.Tick(1);
                yield return EnterArea(HardwareId.Cpu);

                PrototypeArena.Kill(_arena.Player);
                yield return _arena.Tick(4);

                Assert.AreEqual(MissionPhase.Failed, Flow.Run.Phase, $"mission {mission} did not fail");
                Assert.AreEqual(
                    VaccineSaveState.DesignStartingCopies - mission, _store.Snapshot.vaccine.copiesRemaining,
                    $"death {mission} did not cost a clone");
                Assert.AreEqual(
                    mission, _store.Snapshot.vaccine.deathCount, $"death {mission} was not recorded");

                // [확정] docs/04 section 2: 허브에서 재시도. The retry is the vaccine standing up
                // again, and it happens on the commit that spent the clone - not on the return
                // to the hub, which has not been paid for yet.
                Assert.IsTrue(
                    _arena.Player.IsAlive,
                    $"after death {mission} the vaccine was not revived, so the retry begins with a corpse");
                Assert.AreEqual(
                    _arena.Player.Health.Max, _arena.Player.Health.Current,
                    $"after death {mission} the vaccine stood up on partial health");
            }
        }

        [UnityTest]
        public IEnumerator TheFourthDeathLeavesTheVaccineDown()
        {
            // [확정] docs/04 section 2: 4번째 사망은 영구 파기. Whether this death was the fourth
            // is in permanent state and nowhere else, so the revive is decided by the
            // acknowledgement rather than by anything the scene can see.
            yield return Hub();

            int deaths = 0;
            while (_store.Snapshot.vaccine.alive)
            {
                ++deaths;
                Assert.IsTrue(
                    _hub.TryUseTerminal(HubTerminalKind.Mission), $"mission {deaths} would not begin");
                yield return _arena.Tick(1);
                yield return EnterArea(HardwareId.Cpu);

                PrototypeArena.Kill(_arena.Player);
                yield return _arena.Tick(4);

                Assert.Less(deaths, 10, "the vaccine never ran out of clones");
            }

            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies + 1, deaths,
                "the vaccine was destroyed on a different death than the fourth");
            Assert.AreEqual(0, _store.Snapshot.vaccine.copiesRemaining);
            Assert.IsFalse(
                _arena.Player.IsAlive, "a destroyed vaccine was put back on its feet");
        }

        [UnityTest]
        public IEnumerator NoMissionBeginsWhileADeathIsStillUnsettled()
        {
            // [확정] CLAUDE.md: 보상/사망/철수는 저장 커밋 승인 후 UI 및 게임 상태 확정. With no
            // save connected the death stays in AwaitingCommit forever, which is exactly the
            // state a failed or slow write produces - and the hub used to hand out the next
            // mission on top of it.
            yield return Hub(connectSave: false);

            RunDeathDirector deaths = _arena.DeathDirector;
            Assert.IsFalse(deaths.HasCommitService, "this test needs a death nothing can commit");

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission), "the first mission would not begin");
            yield return _arena.Tick(1);
            yield return EnterArea(HardwareId.Cpu);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(4);

            Assert.AreEqual(RunDeathPhase.AwaitingCommit, deaths.Ledger.Phase);
            Assert.AreEqual(MissionFlowPhase.Hub, Flow.Phase, "the failed mission did not end in the hub");

            PutThePlayerBackOnTheirFeet();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("still AwaitingCommit"));
            Assert.IsFalse(
                _hub.TryUseTerminal(HubTerminalKind.Mission),
                "a new mission began on top of a death nobody had paid for");
        }
    
        // --- what the HUD says you are standing next to -----------------------------------

        [UnityTest]
        public IEnumerator ThePromptNamesTheTerminalInReachAndSaysWhenOneIsNotReady()
        {
            // The prompt is the only thing in the build that tells a player what a pillar is.
            // Asserted on the controller's own model rather than the panel, which
            // HudPlayModeTests covers - here the question is whether it reads the hub right.
            yield return Hub();

            // Left inactive on purpose: this test is about what the controller READS, and an
            // active one would also try to draw, which needs the real document. The panel half
            // is HudPlayModeTests, which loads it.
            var hudObject = new GameObject("HUD Model");
            hudObject.SetActive(false);
            hudObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var hud = hudObject.AddComponent<NeonVoid.UI.HudController>();
            hud.Configure(_arena.Player, _motherboard, null, _hub);

            try
            {
                StandOn(HubAt);
                yield return _arena.Tick(1);
                Assert.IsFalse(
                    hud.GatherPrompt().HasSubject,
                    "the middle of the hub offered something to use");

                StandOn(_hub.Terminal(HubTerminalKind.Mission).position);
                yield return _arena.Tick(1);

                NeonVoid.UI.HudPrompt mission = hud.GatherPrompt();
                Assert.AreEqual(HubTerminals.NameOf(HubTerminalKind.Mission), mission.Subject);
                Assert.IsTrue(mission.Available, "the 의뢰 터미널 works and the prompt said otherwise");

                // 스킬 장착 works now ([확정] D-009), so the prompt must say so.
                StandOn(_hub.Terminal(HubTerminalKind.Skills).position);
                yield return _arena.Tick(1);

                NeonVoid.UI.HudPrompt skills = hud.GatherPrompt();
                Assert.AreEqual(HubTerminals.NameOf(HubTerminalKind.Skills), skills.Subject);
                Assert.IsTrue(skills.Available, "스킬 장착 is connected and the prompt said otherwise");

                // 보관 DB is the one still waiting, and it says what for.
                StandOn(_hub.Terminal(HubTerminalKind.Database).position);
                yield return _arena.Tick(1);

                NeonVoid.UI.HudPrompt database = hud.GatherPrompt();
                Assert.AreEqual(HubTerminals.NameOf(HubTerminalKind.Database), database.Subject);
                Assert.IsFalse(database.Available, "an unfinished terminal offered itself as usable");
                Assert.AreEqual(HubTerminals.PendingLabel(HubTerminalKind.Database), database.Detail);
            }
            finally
            {
                UnityEngine.Object.Destroy(hudObject);
            }
        }
    
        // --- 백신 성장 터미널 화면 ----------------------------------------------------------

        /// <summary>A HUD with no panel, so what it would draw can be read.</summary>
        private NeonVoid.UI.HudController AddHud()
        {
            _hudObject = new GameObject("HUD Model");
            _hudObject.SetActive(false);
            _hudObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var hud = _hudObject.AddComponent<NeonVoid.UI.HudController>();
            hud.Configure(_arena.Player, _motherboard, null, _hub);
            return hud;
        }

        [UnityTest]
        public IEnumerator TheGrowthTerminalScreenOpensAtTheTerminalAndNowhereElse()
        {
            // docs/00 section 6 makes the hub 실제 3D 공간, so this is what a terminal shows the
            // player standing at it, not a menu that can be opened anywhere.
            yield return Hub();
            NeonVoid.UI.HudController hud = AddHud();

            Assert.IsFalse(
                hud.GatherGrowth().Shown,
                "the terminal's screen was up in the middle of the hub");

            StandOn(_hub.Terminal(HubTerminalKind.Growth).position);
            yield return _arena.Tick(1);

            NeonVoid.UI.GrowthSheet sheet = hud.GatherGrowth();
            Assert.IsTrue(sheet.Shown, "standing at the terminal showed nothing");
            Assert.AreEqual(StatInvestments.All.Length, sheet.Rows.Count);
            Assert.IsTrue(sheet.CanInvest, "the save is connected in this fixture");

            // With no points earned yet nothing is offered, which is its own correct answer.
            Assert.AreEqual(0, sheet.UnusedPoints);
            for (int i = 0; i < sheet.Rows.Count; ++i)
                Assert.IsFalse(sheet.Rows[i].Affordable, $"{sheet.Rows[i].Name} offered a free point");

            // And it closes again when the player walks away.
            StandOn(HubAt);
            yield return _arena.Tick(1);
            Assert.IsFalse(hud.GatherGrowth().Shown);
        }

        [UnityTest]
        public IEnumerator ThePreviewOnTheTerminalIsWhatThePurchaseActuallyProduces()
        {
            // [확정] docs/05 section 2 asks for 구매 전후 값 and forbids 저장 전 값 확정 표시.
            // The test the screen has to pass is that the 구매 후 값 it showed is the number the
            // player ends up with - read back from the file, not from the panel.
            yield return Hub();
            _store.Snapshot.vaccine.unusedStatPoints = 6;
            NeonVoid.UI.HudController hud = AddHud();

            StandOn(_hub.Terminal(HubTerminalKind.Growth).position);
            yield return _arena.Tick(1);

            NeonVoid.UI.GrowthSheet before = hud.GatherGrowth();
            NeonVoid.UI.GrowthSheetRow row = default;
            for (int i = 0; i < before.Rows.Count; ++i)
            {
                if (before.Rows[i].Stat == InvestableStat.PhysicalArmor)
                    row = before.Rows[i];
            }

            Assert.IsTrue(row.Affordable, "방어력 could not be bought in this fixture");
            Assert.IsNotEmpty(row.CurrentReduction, "방어력 showed no 실제 방어감소율");
            string promised = row.Preview;
            string promisedReduction = row.PreviewReduction;

            Assert.IsTrue(
                _growth.TryInvest(InvestableStat.PhysicalArmor), _growth.LastRefusal);
            yield return _arena.Tick(1);

            NeonVoid.UI.GrowthSheet after = hud.GatherGrowth();
            for (int i = 0; i < after.Rows.Count; ++i)
            {
                if (after.Rows[i].Stat != InvestableStat.PhysicalArmor)
                    continue;

                Assert.AreEqual(
                    promised, after.Rows[i].Current,
                    "the terminal promised a value the purchase did not produce");
                Assert.AreEqual(
                    promisedReduction, after.Rows[i].CurrentReduction,
                    "the 실제 방어감소율 the terminal promised is not what was bought");
                Assert.AreEqual(1, after.Rows[i].Steps);
            }

            // And the player actually carries it, which is the point of the purchase.
            Assert.AreEqual(
                StatInvestments.ValueOf(_arena.Player.Stats, InvestableStat.PhysicalArmor),
                StatInvestments.ValueOf(
                    StatInvestments.Apply(_growth.Uninvested, _growth.Allocation),
                    InvestableStat.PhysicalArmor),
                0.001f);
        }

        [UnityTest]
        public IEnumerator WithNoPermanentWritesTheTerminalOffersNothingAndSaysWhy()
        {
            // The default since D-020. A terminal whose buttons silently did nothing would read
            // as broken; the actual reason is that the save is not committing.
            yield return HubWithoutPermanentWrites();
            NeonVoid.UI.HudController hud = AddHud();

            StandOn(_hub.Terminal(HubTerminalKind.Growth).position);
            yield return _arena.Tick(1);

            NeonVoid.UI.GrowthSheet sheet = hud.GatherGrowth();
            Assert.IsTrue(sheet.Shown, "the terminal showed nothing at all");
            Assert.IsFalse(sheet.CanInvest);
            Assert.IsNotEmpty(sheet.Unavailable);

            for (int i = 0; i < sheet.Rows.Count; ++i)
                Assert.IsFalse(sheet.Rows[i].Affordable, $"{sheet.Rows[i].Name} offered a purchase");
        }
    
        // --- 보상 화면 -------------------------------------------------------------------------

        /// <summary>A reward director over this test's mission, built after the flow exists.</summary>
        private NeonVoid.Plugins.RewardDirector Rewards()
        {
            _rewardObject = new GameObject("Reward Director");
            _rewardObject.SetActive(false);
            var rewards = _rewardObject.AddComponent<NeonVoid.Plugins.RewardDirector>();
            rewards.Configure(_arena.Mission, _motherboard, _arena.Procs);
            _rewardObject.SetActive(true);
            return rewards;
        }

        [UnityTest]
        public IEnumerator PurifyingAnAreaOpensARewardScreen()
        {
            // [확정] docs/01 ties a 보상 화면 to a 정화, and MissionFlow raises it after the
            // purification is committed - never for one that was not written.
            yield return Hub();
            NeonVoid.Plugins.RewardDirector rewards = Rewards();
            yield return _arena.Tick(1);

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission), "the mission did not begin");
            yield return _arena.Tick(1);

            Assert.IsFalse(rewards.Session.IsOpen, "a screen was open before anything was purified");

            yield return EnterArea(HardwareId.Cpu);
            yield return ClearTheStandingArea();
            yield return _arena.Tick(1);

            Assert.IsTrue(rewards.Session.IsOpen, "정화를 끝냈는데 보상 화면이 안 열렸다");
            Assert.AreEqual(
                NeonVoid.Core.DesignConstants.RewardCardSlots, rewards.Session.Screen.Cards.Count);
            Assert.AreEqual(
                NeonVoid.Core.DesignConstants.InitialRewardRerolls, rewards.Session.RerollsLeft);
        }

        [UnityTest]
        public IEnumerator TakingACardPutsItOnThePlayerForTheRestOfTheMission()
        {
            // [확정] docs/04 section 1: 임시 플러그인 is 의뢰 상태. It reaches the loadout and
            // nothing is written to the save until 철수.
            yield return Hub();
            NeonVoid.Plugins.RewardDirector rewards = Rewards();
            yield return _arena.Tick(1);

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);
            yield return EnterArea(HardwareId.Cpu);
            yield return ClearTheStandingArea();
            yield return _arena.Tick(1);

            int before = _arena.Procs.Loadout.Count;
            int revisionBefore = _store != null ? _store.Snapshot.snapshotRevision : 0;

            string taken = rewards.Session.Screen.Cards[0].Definition.ProposalId;
            NeonVoid.Plugins.RewardResult result = rewards.Session.Pick(0);

            Assert.IsTrue(result.Ok, result.Detail);
            Assert.IsFalse(rewards.Session.IsOpen);
            Assert.AreEqual(before + 1, _arena.Procs.Loadout.Count);
            Assert.IsTrue(_arena.Procs.Loadout.Carries(taken));

            if (_store != null)
            {
                Assert.AreEqual(
                    revisionBefore, _store.Snapshot.snapshotRevision,
                    "카드 하나 받는 것이 영구 저장을 건드렸다");
            }

            yield return _arena.Tick(1);
        }

        [UnityTest]
        public IEnumerator TheHudDrawsWhateverTheScreenOffers()
        {
            yield return Hub();
            NeonVoid.Plugins.RewardDirector rewards = Rewards();
            yield return _arena.Tick(1);

            _hudObject = new GameObject("HUD Model");
            _hudObject.SetActive(false);
            _hudObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var hud = _hudObject.AddComponent<NeonVoid.UI.HudController>();
            hud.Configure(_arena.Player, _motherboard, null, _hub, rewardDirector: rewards);

            Assert.IsFalse(hud.RewardOpen);

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);
            yield return EnterArea(HardwareId.Cpu);
            yield return ClearTheStandingArea();
            yield return _arena.Tick(1);

            Assert.IsTrue(hud.RewardOpen, "화면이 열렸는데 HUD가 모른다");

            NeonVoid.Plugins.RewardResult taken = hud.TakeReward(0);
            Assert.IsTrue(taken.Ok, taken.Detail);
            Assert.IsFalse(hud.RewardOpen, "카드를 받았는데 화면이 안 닫혔다");
        }

        [UnityTest]
        public IEnumerator AMissionThatEndsTakesItsRewardScreenWithIt()
        {
            // A card picked from a screen that outlived its mission is a reward for a run that
            // is over.
            yield return Hub();
            NeonVoid.Plugins.RewardDirector rewards = Rewards();
            yield return _arena.Tick(1);

            Assert.IsTrue(_hub.TryUseTerminal(HubTerminalKind.Mission));
            yield return _arena.Tick(1);
            yield return EnterArea(HardwareId.Cpu);
            yield return ClearTheStandingArea();
            yield return _arena.Tick(1);

            Assert.IsTrue(rewards.Session.IsOpen);

            Assert.IsTrue(Flow.TryFail(), Flow.LastRefusal);
            yield return _arena.Tick(1);

            Assert.IsFalse(rewards.Session.IsOpen, "의뢰가 끝났는데 보상 화면이 남아 있다");
        }
    }
}
