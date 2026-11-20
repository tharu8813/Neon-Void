using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NeonVoid.Combat;
using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
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
        private GameObject _hubObject;
        private MotherboardDirector _motherboard;
        private HubRuntime _hub;
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
            if (_hubObject != null)
                UnityEngine.Object.Destroy(_hubObject);
            if (_boardObject != null)
                UnityEngine.Object.Destroy(_boardObject);
            if (_saveObject != null)
                UnityEngine.Object.Destroy(_saveObject);

            _arena?.Dispose();
            _arena = null;
            _motherboard = null;
            _hub = null;
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
        private IEnumerator Hub()
        {
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

            for (int i = 0; i < area.Devices.Length; ++i)
                PrototypeArena.Kill(area.Devices[i].Device);

            yield return _arena.Tick(1);
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

            _hubObject = new GameObject("Defender Hub");
            _hubObject.SetActive(false);
            _hubObject.transform.position = HubAt;
            _hub = _hubObject.AddComponent<HubRuntime>();
            _hub.Configure(_arena.Mission, null, _arena.PlayerObject.transform);
            _hubObject.SetActive(true);

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
            yield return Hub();

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("백신 성장 터미널"));
            Assert.IsFalse(_hub.TryUseTerminal(HubTerminalKind.Growth));
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
    }
}
