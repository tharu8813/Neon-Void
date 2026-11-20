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
    /// A seed played to an extraction: seed → layout → room → play → flow → save.
    /// <para>
    /// Every link has its own tests. What only this can show is that they join - that the cells
    /// the generator chose are cells the player can stand on, that the devices it placed are
    /// devices the player's fire can destroy, and that standing where it put the 정화 콘솔 writes
    /// a purification to a file. A chain of six tested links with an untested joint is how this
    /// project's gaps have looked every time.
    /// </para>
    /// </summary>
    public sealed class AreaWorldPlayModeTests
    {
        private const int DwellTicks = 2;
        private const float DwellRadius = 3f;

        /// <summary>Clear of the arena's own ground plane, so neither floor stands in the other's way.</summary>
        private static readonly Vector3 AreaOrigin = new Vector3(300f, 0f, 300f);

        private string _root;
        private PrototypeArena _arena;
        private GameObject _saveObject;
        private GameObject _builderObject;
        private SaveStore _store;
        private AreaBuildResult _area;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-playarea-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_area != null && _area.Root != null)
                UnityEngine.Object.Destroy(_area.Root);
            if (_builderObject != null)
                UnityEngine.Object.Destroy(_builderObject);
            if (_saveObject != null)
                UnityEngine.Object.Destroy(_saveObject);

            _arena?.Dispose();
            _arena = null;
            _area = null;
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
            director.UseStore(_store, sessionNonce: "playmode-area");
            _saveObject.SetActive(true);

            Assert.IsTrue(director.CanCommit, director.Result.Detail);
        }

        private static AreaLayout LayoutFor(uint seed, int objectives = 3)
        {
            var spec = new AreaSpec(HardwareId.Cpu, seed, objectives);
            Assert.IsTrue(AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure), failure);
            return layout;
        }

        /// <summary>Builds the area the arena's mission will report to, with short dwells.</summary>
        private AreaBuildResult BuildArea(uint seed = 3u, int objectives = 3)
        {
            _builderObject = new GameObject("Area World Builder");
            var builder = _builderObject.AddComponent<AreaWorldBuilder>();
            builder.WorldOrigin = AreaOrigin;

            _area = builder.Build(LayoutFor(seed, objectives), _arena.Mission, _arena.PlayerObject.transform);
            _area.Runtime.ConfigureDwell(DwellRadius, DwellTicks, DwellRadius, DwellTicks);
            return _area;
        }

        private Vector3 WorldOf(AreaCell cell) => AreaOrigin + _area.Layout.WorldPositionOf(cell);

        /// <summary>
        /// Puts the player on a cell, through the motion simulation. Writing the transform is not
        /// enough - PlayerSimulationDriver keeps its own position ledger, and the
        /// CharacterController puts the player back where that ledger says they are.
        /// </summary>
        private void StandOn(AreaCell cell)
        {
            Vector3 destination = WorldOf(cell) + (Vector3.up * AreaWorldBuilder.PlayerStandingLiftUnits);
            Transform player = _arena.PlayerObject.transform;
            player.position = new Vector3(player.position.x, destination.y, player.position.z);
            _arena.Driver.TeleportTo(new Vector2(destination.x, destination.z));
        }

        private static readonly string[] NoLearns = new string[0];

        /// <summary>
        /// Refills the health pool to something a room full of enemies cannot empty. Configure is
        /// the only revive this project has, and it is what the combat tests use.
        /// </summary>
        private static void MakeUnkillable(CombatActor actor)
        {
            CombatStats stats = PrototypeArena.PlayerStats();
            stats.MaxHealth = 1_000_000f;
            actor.Configure(PrototypeArena.PlayerTeam, stats);
        }

        // --- the room ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ABuiltAreaEntersTheMissionWithTheDevicesItPlaced()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(1);

            Assert.IsTrue(area.Runtime.HasEntered, Flow.LastRefusal);
            Assert.AreEqual(MissionFlowPhase.InArea, Flow.Phase);
            Assert.AreEqual(AreaProgressPhase.Objectives, Flow.AreaPhase);
            Assert.AreEqual(area.Layout.Objectives.Count, Flow.ObjectivesRequiredInArea);
            Assert.AreEqual(3, area.Devices.Length);
            Assert.IsFalse(area.BossGate.Boss.gameObject.activeInHierarchy, "the boss was standing in the room");

            // The merged terrain, as a number worth watching: a cube per cell would be thousands.
            Assert.Greater(area.FloorSlabs, 0);
            Assert.Greater(area.WallSlabs, 0);
            Assert.Less(
                area.FloorSlabs + area.WallSlabs, area.Layout.Grid.Width * area.Layout.Grid.Height / 4,
                "the terrain was not merged");
        }

        [UnityTest]
        public IEnumerator ThePlayerStartsAtTheEntranceStandingOnFloor()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(2);

            Vector3 entrance = WorldOf(area.Layout.Start);
            Vector3 player = _arena.PlayerObject.transform.position;

            Assert.AreEqual(entrance.x, player.x, 0.5f, "the player did not start at the entrance");
            Assert.AreEqual(entrance.z, player.z, 0.5f);
            Assert.Greater(player.y, -0.5f, "the player fell through the floor");
        }

        [UnityTest]
        public IEnumerator EveryPlacedThingHasFloorUnderItAndTheWallsAreSolid()
        {
            // The generator says a cell is walkable; this asks the physics engine whether there
            // is anything there to walk on. The two could disagree and only the room would know.
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(1);

            foreach (AreaCell cell in area.Layout.AllSpawns)
            {
                // Every hit on the way down, not the first: a device or the boss is standing on
                // most of these cells, and the question is whether there is floor under them.
                RaycastHit[] hits = Physics.RaycastAll(WorldOf(cell) + (Vector3.up * 6f), Vector3.down, 20f);
                bool foundFloor = false;
                for (int i = 0; i < hits.Length && !foundFloor; ++i)
                    foundFloor = hits[i].point.y <= 0.05f;

                Assert.IsTrue(foundFloor, $"no floor under {cell}; {hits.Length} hit(s) on the way down");
            }

            // And a cell outside the map has no floor at all, so the walls are a boundary rather
            // than decoration on an endless plane.
            Vector3 outside = AreaOrigin + new Vector3(-20f, 6f, -20f);
            Assert.IsFalse(
                Physics.Raycast(outside, Vector3.down, 20f),
                "there is floor outside the generated area");
        }

        [UnityTest]
        public IEnumerator AnUnvalidatedLayoutIsRefusedRatherThanBuilt()
        {
            // docs/01 section 6's checks exist so a player is never handed an area they cannot
            // finish. Building one anyway would make them advisory.
            ConnectSave();
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            _builderObject = new GameObject("Area World Builder");
            var builder = _builderObject.AddComponent<AreaWorldBuilder>();

            var grid = new AreaGrid(12, 12);
            grid.CarveRoom(new AreaRoom(1, 1, 4, 4));
            var spec = new AreaSpec(HardwareId.Cpu, 1u, 1);
            var broken = new AreaLayout(
                spec, spec.Seed, 1, AreaGenerator.DraftCellSizeUnits, grid,
                new System.Collections.Generic.List<AreaRoom> { new AreaRoom(1, 1, 4, 4) },
                new AreaCell(2, 2),
                new System.Collections.Generic.List<AreaCell> { new AreaCell(3, 3) },
                new AreaCell(11, 11),
                new AreaCell(1, 1),
                new AreaCell(4, 4));

            Assert.Throws<ArgumentException>(
                () => builder.Build(broken, _arena.Mission, _arena.PlayerObject.transform));
        }

        // --- the whole chain ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASeedIsPlayedToAnExtractionAndTheVaccineGrowsOnDisk()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(1);

            long runId = Flow.Run.Identity.RunId;

            // 소목표: destroy every device the generator placed.
            for (int i = 0; i < area.Devices.Length; ++i)
            {
                Assert.IsFalse(area.Devices[i].IsCompleted);
                PrototypeArena.Kill(area.Devices[i].Device);
            }

            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Boss, Flow.AreaPhase, Flow.LastRefusal);
            Assert.IsTrue(area.BossGate.Boss.gameObject.activeInHierarchy, "the boss never arrived");

            // 보스.
            PrototypeArena.Kill(area.BossGate.Boss);
            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase);

            // 정화: walk to the console the generator placed and stand there.
            StandOn(area.Layout.Purification);
            yield return _arena.Tick(DwellTicks + 1);

            Assert.AreEqual(AreaProgressPhase.Purified, Flow.AreaPhase, Flow.LastRefusal);
            Assert.AreEqual(
                1, _store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(runId)),
                "the purification never reached the file");

            // 철수: and the pad is somewhere else, so the player has to go to it.
            Assert.IsFalse(area.Runtime.IsExtractionArmed, "the pad armed from the console");
            StandOn(area.Layout.Extraction);
            yield return _arena.Tick(DwellTicks + 1);
            Assert.IsTrue(area.Runtime.IsExtractionArmed, "the pad never armed");

            Assert.IsTrue(area.Runtime.TryConfirmExtraction(NoLearns), Flow.LastRefusal);

            Assert.AreEqual(MissionFlowPhase.Extracted, Flow.Phase);
            Assert.AreEqual(MissionPhase.Completed, Flow.Run.Phase);

            // GDD v1 section 5.6: 정화 1개 at three objectives is 165 백신 XP, read back by a
            // second store because that is what makes it permanent.
            var reopened = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            reopened.Open();
            Assert.AreEqual(165L, reopened.Snapshot.vaccine.permanentXp);
            Assert.Greater(Flow.Run.MissionXp, 0L, "no 의뢰 XP was awarded for the area");
        }

        [UnityTest]
        public IEnumerator ThePlayersOwnFireDestroysAPlacedDevice()
        {
            // The devices are Prototype primitives, and a primitive whose collider the shot's
            // SphereCastAll cannot find is a device nothing can destroy - an area that would
            // look fine until somebody tried to clear it.
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(1);

            CombatActor device = area.Devices[0].Device;
            StandOn(area.Layout.Objectives[0]);
            yield return _arena.Tick(2);

            float before = device.Health.Current;
            _arena.FireAt(device, adRatio: 40f);
            yield return _arena.Tick(30);

            Assert.Less(device.Health.Current, before, "the player's shot never reached the device");
        }

        // --- 적 배치 -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheRoomsEnemiesWaitUntilThePlayerWalksIn()
        {
            // A nominal area is budgeted for fifty-one enemies. Spawning them awake means
            // fifty-one AI agents walking at the player from the moment the area loads, which is
            // not the 5.5분 of 일반 전투 docs/01 section 7 budgets - it is one mob.
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(1);

            Assert.Greater(area.EnemyCount, 0, "the area was built with no enemies");
            Assert.Greater(area.RoomGates.Length, 1, "every enemy ended up in one room");

            for (int i = 0; i < area.RoomGates.Length; ++i)
            {
                Assert.IsFalse(area.RoomGates[i].IsOpen, $"{area.RoomGates[i].name} was open on arrival");
                Assert.IsFalse(
                    area.RoomGates[i].Contains(_arena.PlayerObject.transform.position),
                    "a gated room contains the entrance");
            }

            // Walk into one of them.
            AreaEnemySpawn first = area.Layout.Enemies[0];
            StandOn(first.Cell);
            yield return _arena.Tick(1);

            int open = 0;
            for (int i = 0; i < area.RoomGates.Length; ++i)
            {
                if (area.RoomGates[i].IsOpen)
                    ++open;
            }

            Assert.AreEqual(1, open, "walking into one room woke more than one");
        }

        [UnityTest]
        public IEnumerator AKillInAnOpenedRoomPaysItsMissionXp()
        {
            // GDD v1 section 5.4's 일반 적 처치 8 XP, through the gate, the runtime and the flow.
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(1);

            AreaEnemySpawn target = default;
            bool found = false;
            for (int i = 0; i < area.Layout.Enemies.Count && !found; ++i)
            {
                if (!area.Layout.Enemies[i].IsElite)
                {
                    target = area.Layout.Enemies[i];
                    found = true;
                }
            }

            Assert.IsTrue(found, "the area holds no ordinary enemy");

            // The player is about to stand in the middle of a woken room. Sixteen enemies hitting
            // them is correct behaviour and it is also how this test went flaky: a player who
            // dies fails the mission, and a failed mission pays for no kills. The test is about
            // the XP, so the player is taken out of the equation rather than the enemies.
            MakeUnkillable(_arena.Player);

            StandOn(target.Cell);
            yield return _arena.Tick(1);

            AreaRoomGate gate = null;
            for (int i = 0; i < area.RoomGates.Length; ++i)
            {
                if (area.RoomGates[i].IsOpen)
                    gate = area.RoomGates[i];
            }

            Assert.IsNotNull(gate, "no room woke up");
            int aliveBefore = gate.AliveCount;
            Assert.Greater(aliveBefore, 0);

            long xpBefore = Flow.Run.MissionXp;
            int killsBefore = Flow.EnemiesKilledInMission;

            // One of the room's enemies, found by where it stands rather than by reaching into
            // the gate - and killed through the normal damage pipeline, so the gate's own
            // subscription is what reports it.
            CombatActor victim = null;
            CombatActor[] everyActor = UnityEngine.Object.FindObjectsByType<CombatActor>(FindObjectsSortMode.None);
            for (int i = 0; i < everyActor.Length && victim == null; ++i)
            {
                if (everyActor[i].TeamId == 1
                    && everyActor[i].IsAlive
                    && everyActor[i].gameObject.activeInHierarchy
                    && gate.Contains(everyActor[i].transform.position)
                    && everyActor[i].GetComponent<NeonVoid.Combat.AI.EnemyAgent>() != null)
                {
                    victim = everyActor[i];
                }
            }

            Assert.IsNotNull(victim, "no live enemy was found in the opened room");
            string victimName = victim.name;
            PrototypeArena.Kill(victim);
            yield return _arena.Tick(1);

            Assert.IsFalse(victim.IsAlive, $"{victimName} survived a lethal hit");
            Assert.AreEqual(
                aliveBefore - 1, gate.AliveCount,
                $"{gate.name} does not hold {victimName}, so its death was nobody's");
            Assert.AreEqual(killsBefore + 1, Flow.EnemiesKilledInMission, "the kill was not counted");
            Assert.AreEqual(
                xpBefore + MissionXpBudget.XpForNormalKill(0), Flow.Run.MissionXp,
                "the kill did not pay its 의뢰 XP");
        }

        [UnityTest]
        public IEnumerator TheConsoleDoesNothingFromTheEntrance()
        {
            // The dwell is a place as well as a duration. A gate that only counted ticks would
            // purify the area from wherever the player happened to be standing.
            ConnectSave();
            _arena = PrototypeArena.Build();
            AreaBuildResult area = BuildArea();
            yield return _arena.Tick(1);

            for (int i = 0; i < area.Devices.Length; ++i)
                PrototypeArena.Kill(area.Devices[i].Device);
            yield return _arena.Tick(1);
            PrototypeArena.Kill(area.BossGate.Boss);
            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase);

            // Standing at the entrance, which the layout keeps a long walk from the boss's room.
            StandOn(area.Layout.Start);
            yield return _arena.Tick(DwellTicks * 10);

            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase, "it purified from across the map");
            Assert.AreEqual(0, Flow.PurifiedAreas.Count);
        }
    }
}
