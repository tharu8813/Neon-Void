using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NeonVoid.Combat;
using NeonVoid.Missions;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// A hardware area played in a running scene: devices destroyed, a boss that was held out of
    /// the fight, a 정화 콘솔 stood at, a 철수 패드 armed - and a vaccine that grew on disk.
    /// <para>
    /// <see cref="MissionFlowTests"/> proves the flow refuses what the documents forbid. This
    /// proves the <b>room agrees with it</b>, which is a different claim: a boss the flow would
    /// refuse to count is still a boss the player can shoot, and an objective device that reports
    /// twice is still two objectives. Those are properties of the world, so they are checked in
    /// one.
    /// </para>
    /// </summary>
    public sealed class MissionWorldPlayModeTests
    {
        private const int DwellTicks = 2;
        private const float DwellRadius = 3f;

        private string _root;
        private PrototypeArena _arena;
        private GameObject _saveObject;

        private HardwareAreaRuntime _area;
        private AreaObjectiveDevice[] _devices;
        private AreaBossGate _bossGate;
        private CombatActor _boss;
        private SaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-playworld-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_saveObject != null)
                UnityEngine.Object.Destroy(_saveObject);
            _arena?.Dispose();
            _arena = null;
            _area = null;
            _devices = null;
            _bossGate = null;
            _boss = null;
            _store = null;

            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private MissionFlow Flow => _arena.Mission.Flow;

        /// <summary>
        /// A prepared save, stood up <b>before</b> the arena because the mission director picks
        /// its commit gate in Awake.
        /// </summary>
        private void ConnectSave()
        {
            _store = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));

            _saveObject = new GameObject("Vaccine Save Director");
            _saveObject.SetActive(false);
            VaccineSaveDirector director = _saveObject.AddComponent<VaccineSaveDirector>();
            typeof(VaccineSaveDirector)
                .GetField("writeToRealSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, true);
            director.UseStore(_store, sessionNonce: "playmode-world");
            _saveObject.SetActive(true);

            Assert.IsTrue(director.CanCommit, director.Result.Detail);
        }

        /// <summary>
        /// Builds a CPU area around the arena's player: three destructible devices, a boss held
        /// by its gate, and a console and a pad under the player's feet so a dwell of two ticks
        /// is all these tests have to wait for.
        /// </summary>
        private void BuildArea(int objectiveCount = 3)
        {
            _devices = new AreaObjectiveDevice[objectiveCount];
            for (int i = 0; i < objectiveCount; ++i)
            {
                CombatActor actor = _arena.SpawnEnemyNear(_arena.Player, $"과열 밸브 {i + 1}", 4f + i);
                _devices[i] = AddWhileAsleep<AreaObjectiveDevice>(
                    $"Objective {i + 1}", d => d.Configure(actor, $"과열 밸브 {i + 1}"));
            }

            _boss = _arena.SpawnEnemyNear(_arena.Player, "코어 워처", 9f);
            _bossGate = AddWhileAsleep<AreaBossGate>(
                "Boss Gate", g => g.Configure(_boss, _boss.gameObject, "코어 워처"));

            Transform console = _arena.Track(new GameObject("정화 콘솔")).transform;
            Transform pad = _arena.Track(new GameObject("철수 패드")).transform;
            console.position = _arena.PlayerObject.transform.position;
            pad.position = _arena.PlayerObject.transform.position;

            _area = AddWhileAsleep<HardwareAreaRuntime>("CPU Area", a =>
            {
                a.Configure(
                    HardwareId.Cpu, _arena.Mission, _arena.PlayerObject.transform,
                    _devices, _bossGate, console, pad);
                a.ConfigureDwell(DwellRadius, DwellTicks, DwellRadius, DwellTicks);
            });
        }

        /// <summary>
        /// Adds a component to a deactivated object, configures it, then wakes it - so its Awake
        /// sees the references a scene would have serialized instead of searching the scene for
        /// them.
        /// </summary>
        private T AddWhileAsleep<T>(string objectName, Action<T> configure)
            where T : Component
        {
            GameObject host = _arena.Track(new GameObject(objectName));
            host.SetActive(false);
            T component = host.AddComponent<T>();
            configure(component);
            host.SetActive(true);
            return component;
        }

        private void DestroyEveryDevice()
        {
            for (int i = 0; i < _devices.Length; ++i)
                PrototypeArena.Kill(_devices[i].Device);
        }

        private static readonly string[] NoLearns = new string[0];

        // --- entry and the objective gate --------------------------------------------------

        [UnityTest]
        public IEnumerator EnteringAnAreaCountsTheDevicesInTheRoomAndHoldsTheBoss()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            Assert.IsTrue(_area.HasEntered, Flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.InArea, Flow.Phase);
            Assert.AreEqual(AreaProgressPhase.Objectives, Flow.AreaPhase);
            Assert.AreEqual(
                3, Flow.ObjectivesRequiredInArea,
                "the flow requires a number of objectives the room does not hold");
            Assert.AreEqual(3, _area.ObjectivesOutstanding);

            // [확정] docs/01 section 6.6, as a property of the room: the boss is not merely
            // uncounted, it is not there to be shot.
            Assert.IsFalse(_bossGate.IsOpen);
            Assert.IsFalse(_boss.gameObject.activeInHierarchy, "the boss was standing in the room");
        }

        [UnityTest]
        public IEnumerator AnAreaPlannedWithTwoDevicesAsksForTwo()
        {
            // The count comes from the devices that are actually placed, so a room and its flow
            // cannot disagree about how many objectives it has.
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea(objectiveCount: 2);
            yield return _arena.Tick(1);

            Assert.AreEqual(2, Flow.ObjectivesRequiredInArea);
            Assert.AreEqual(HardwareIds.DraftMinObjectivesPerArea, Flow.ObjectivesRequiredInArea);
        }

        [UnityTest]
        public IEnumerator DestroyingEveryDeviceLetsTheBossIn()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            PrototypeArena.Kill(_devices[0].Device);
            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Objectives, _area.Flow.AreaPhase, "one device opened the boss");
            Assert.IsFalse(_boss.gameObject.activeInHierarchy);

            PrototypeArena.Kill(_devices[1].Device);
            PrototypeArena.Kill(_devices[2].Device);
            yield return _arena.Tick(1);

            Assert.AreEqual(AreaProgressPhase.Boss, Flow.AreaPhase);
            Assert.AreEqual(3, Flow.ObjectivesCompletedInArea);
            Assert.IsTrue(_bossGate.IsOpen);
            Assert.IsTrue(_boss.gameObject.activeInHierarchy, "the boss never arrived");
        }

        [UnityTest]
        public IEnumerator ABossKilledBeforeItsPhaseIsNotCounted()
        {
            // Unreachable in play, because the object is inactive. If it ever happens, counting
            // it would hand over a purification for a fight that never took place - the flow
            // cannot see the room, so it would accept. The area becomes unclearable instead,
            // which is the safe failure, and it is loud.
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            LogAssert.Expect(LogType.Error, new Regex("died before its phase opened"));
            PrototypeArena.Kill(_boss);
            yield return _arena.Tick(1);

            Assert.IsFalse(_bossGate.IsDefeated);
            Assert.AreEqual(AreaProgressPhase.Objectives, Flow.AreaPhase);
        }

        // --- 정화 -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheConsoleDoesNothingUntilTheBossIsDown()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();

            // The player is standing on the console the whole time.
            yield return _arena.Tick(DwellTicks * 10);

            Assert.AreEqual(AreaProgressPhase.Objectives, Flow.AreaPhase);
            Assert.AreEqual(0, Flow.PurifiedAreas.Count, "the console purified with the objectives outstanding");
            Assert.AreEqual(0f, _area.PurificationDwell.Progress, 1e-6f, "the dwell accumulated while shut");
        }

        [UnityTest]
        public IEnumerator StandingAtTheConsolePurifiesTheAreaAndWritesIt()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            DestroyEveryDevice();
            yield return _arena.Tick(1);
            PrototypeArena.Kill(_boss);
            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase);

            yield return _arena.Tick(DwellTicks);

            Assert.AreEqual(AreaProgressPhase.Purified, Flow.AreaPhase, Flow.LastRefusal);
            Assert.AreEqual(1, Flow.PurifiedAreas.Count);
            Assert.AreEqual(HardwareId.Cpu, Flow.PurifiedAreas[0]);
            Assert.Greater(Flow.Run.MissionXp, 0L, "the area's 의뢰 XP was not awarded");

            long runId = Flow.Run.Identity.RunId;
            Assert.AreEqual(
                1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(runId)),
                "the purification never reached the file");
        }

        [UnityTest]
        public IEnumerator TheConsoleAsksTheSaveExactlyOnceWhileThePlayerStandsThere()
        {
            // A dwell that reported completion every tick would ask for a purification sixty
            // times a second and be answered AlreadyApplied each time - a loop that looks like
            // it works. One receipt is the proof it does not happen.
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            DestroyEveryDevice();
            yield return _arena.Tick(1);
            PrototypeArena.Kill(_boss);
            yield return _arena.Tick(1);

            long runId = Flow.Run.Identity.RunId;
            int revisionAfterEntry = _store.Snapshot.snapshotRevision;

            yield return _arena.Tick(DwellTicks * 20);

            Assert.AreEqual(1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(runId)));
            Assert.AreEqual(
                revisionAfterEntry + 1, _store.Snapshot.snapshotRevision,
                "the console wrote to the save more than once");
            Assert.AreEqual(1, Flow.Run.PurificationsCompleted);
        }

        // --- 철수 --------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePadStaysDeadUntilSomethingIsPurified()
        {
            // [확정] docs/01 section 6.6: 목표가 완료되기 전에 보스·철수가 활성화되지 않음.
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(DwellTicks * 10);

            Assert.IsFalse(_area.IsExtractionArmed, "the pad armed with nothing purified");
            Assert.AreEqual(0f, _area.ExtractionDwell.Progress, 1e-6f);
            Assert.IsFalse(Flow.CanExtract);
        }

        [UnityTest]
        public IEnumerator AnExtractionConfirmedWithoutStandingOnThePadIsRefused()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            LogAssert.Expect(LogType.Warning, new Regex("without the pad being armed"));
            Assert.IsFalse(_area.TryConfirmExtraction(NoLearns));
            Assert.AreEqual(MissionPhase.InProgress, Flow.Run.Phase);
        }

        [UnityTest]
        public IEnumerator ThePadArmsAfterThePurificationAndTheConfirmationBanksOnDisk()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            DestroyEveryDevice();
            yield return _arena.Tick(1);
            PrototypeArena.Kill(_boss);
            yield return _arena.Tick(1);
            yield return _arena.Tick(DwellTicks);
            Assert.AreEqual(AreaProgressPhase.Purified, Flow.AreaPhase, Flow.LastRefusal);

            // The pad only starts counting once there is something to extract with.
            yield return _arena.Tick(DwellTicks);
            Assert.IsTrue(_area.IsExtractionArmed, "the pad never armed");
            Assert.AreEqual(MissionPhase.InProgress, Flow.Run.Phase, "the pad extracted on its own");

            Assert.IsTrue(_area.TryConfirmExtraction(NoLearns), Flow.LastRefusal);

            Assert.AreEqual(MissionFlowPhase.Extracted, Flow.Phase);
            Assert.AreEqual(MissionPhase.Completed, Flow.Run.Phase);

            // GDD v1 section 5.6: 정화 1개 at the standard three objectives is 165 백신 XP, and a
            // second process reading the directory is what makes it permanent.
            var reopened = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            reopened.Open();
            Assert.AreEqual(165L, reopened.Snapshot.vaccine.permanentXp);
        }

        [UnityTest]
        public IEnumerator ThePadDisarmsWhenTheMissionStopsBeingExtractable()
        {
            // A death during the 1.5분 정산 window must not leave a pad standing armed, ready to
            // extract a mission that has already failed.
            ConnectSave();
            _arena = PrototypeArena.Build();
            BuildArea();
            yield return _arena.Tick(1);

            DestroyEveryDevice();
            yield return _arena.Tick(1);
            PrototypeArena.Kill(_boss);
            yield return _arena.Tick(1);
            yield return _arena.Tick(DwellTicks * 2);
            Assert.IsTrue(_area.IsExtractionArmed);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(1);

            Assert.AreEqual(MissionFlowPhase.Failed, Flow.Phase);
            Assert.IsFalse(_area.IsExtractionArmed, "the pad stayed armed after the mission failed");

            LogAssert.Expect(LogType.Warning, new Regex("without the pad being armed"));
            Assert.IsFalse(_area.TryConfirmExtraction(NoLearns));
        }
    }
}
