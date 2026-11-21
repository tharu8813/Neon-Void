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

        private static AreaLayout LayoutFor(uint seed, int objectives = 3) =>
            LayoutFor(HardwareId.Cpu, seed, objectives);

        private static AreaLayout LayoutFor(HardwareId hardware, uint seed, int objectives = 3)
        {
            var spec = new AreaSpec(hardware, seed, objectives);
            Assert.IsTrue(AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure), failure);
            return layout;
        }

        /// <summary>Builds the area the arena's mission will report to, with short dwells.</summary>
        private AreaBuildResult BuildArea(uint seed = 3u, int objectives = 3) =>
            BuildArea(AreaConditions.Clear(HardwareId.Cpu, HardwareDifficulty.Normal), seed, objectives);

        /// <summary>The same area under the mission's conditions for it.</summary>
        private AreaBuildResult BuildArea(in AreaConditions conditions, uint seed = 3u, int objectives = 3)
        {
            _builderObject = new GameObject("Area World Builder");
            var builder = _builderObject.AddComponent<AreaWorldBuilder>();
            builder.WorldOrigin = AreaOrigin;

            _area = builder.Build(
                LayoutFor(conditions.Hardware, seed, objectives), _arena.Mission,
                _arena.PlayerObject.transform, conditions);
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

        /// <summary>
        /// The first objective of this area that is destroyed rather than stood on. A test about
        /// the player's fire reaching a device has to aim at one that is a device.
        /// </summary>
        private static int IndexOfFirstDestroyObjective(AreaBuildResult area)
        {
            for (int i = 0; i < area.Devices.Length; ++i)
            {
                if (area.Devices[i] != null && area.Devices[i].Kind == AreaObjectiveKind.Destroy)
                    return i;
            }

            Assert.Fail($"{area.Layout.Spec.Hardware} has no Destroy objective to shoot at.");
            return -1;
        }

        private static readonly string[] NoLearns = new string[0];

        /// <summary>
        /// Refills the health pool to something a room full of enemies cannot empty. Configure is
        /// the only revive this project has, and it is what the combat tests use.
        /// </summary>
        [UnityTest]
        public IEnumerator AnEliteKillIsPaidAndCountedAsAnElite()
        {
            // GDD v1 section 5.4 pays 정예 처치 32 XP against 일반 처치 8, and MissionFlow keeps
            // the two counts apart. Only ever covered by pure tests until now - and the room
            // the area actually builds holds both kinds, so the difference is reachable by
            // whoever walks into it.
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

            int elites = 0;
            CombatActor elite = null;
            for (int i = 0; i < gate.HeldCount; ++i)
            {
                AreaRoomOccupant occupant = gate.OccupantAt(i);
                if (!occupant.IsElite)
                    continue;

                ++elites;
                if (elite == null && occupant.Actor.IsAlive)
                    elite = occupant.Actor;
            }

            Assert.Greater(elites, 0, $"{gate.name} holds no 정예, so this test asserts nothing");
            Assert.IsNotNull(elite, "no live 정예 was found in the opened room");

            long xpBefore = Flow.Run.MissionXp;
            PrototypeArena.Kill(elite);
            yield return _arena.Tick(1);

            Assert.AreEqual(
                xpBefore + MissionXpBudget.XpForEliteKill(0), Flow.Run.MissionXp,
                "a 정예 kill did not pay 정예 XP");
            Assert.AreEqual(1, Flow.ElitesKilledInMission, "the 정예 was not counted as one");
            Assert.AreEqual(
                0, Flow.EnemiesKilledInMission,
                "a 정예 kill was counted as an ordinary one, so the two budgets are the same number");
        }

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

            // Two ticks and an explicit sync: a few hundred colliders were created this frame and
            // the physics scene picks transform changes up lazily. Asking it about geometry it has
            // not ingested yet is how this test flaked.
            yield return _arena.Tick(2);
            Physics.SyncTransforms();

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

            // 소목표: whatever CPU's objectives are - [확정] docs/01 only fixes that there are
            // 2~3 of them, and AreaObjectives decides their shapes per hardware.
            for (int i = 0; i < area.Devices.Length; ++i)
                Assert.IsFalse(area.Devices[i].IsCompleted, $"objective {i} began completed");

            _arena.MakePlayerUnkillable();
            yield return _arena.ClearObjectives(area);
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

            int shootable = IndexOfFirstDestroyObjective(area);
            CombatActor device = area.Devices[shootable].Device;
            StandOn(area.Layout.Objectives[shootable]);
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
            // An ORDINARY one, chosen in the room's own order.
            //
            // This test is about 일반 적 처치 8 XP, and MissionFlow counts a 정예 kill under
            // ElitesKilledInMission instead - so killing a 정예 here leaves EnemiesKilledInMission
            // at zero with nothing refused and nothing logged, which reads exactly like the kill
            // having been dropped. The room this seed opens holds 16 enemies and two of them are
            // 정예, and the search used to be over FindObjectsByType(FindObjectsSortMode.None),
            // whose order is not defined and shifts with whatever else is in the scene. That is
            // what made this test fail in full runs and pass on its own.
            CombatActor victim = null;
            for (int i = 0; i < gate.HeldCount && victim == null; ++i)
            {
                AreaRoomOccupant occupant = gate.OccupantAt(i);
                if (!occupant.IsElite && occupant.Actor != null && occupant.Actor.IsAlive)
                    victim = occupant.Actor;
            }

            Assert.IsNotNull(victim, "no live ordinary enemy was found in the opened room");
            Assert.IsTrue(
                victim.gameObject.activeInHierarchy, "the room is open, so its enemies are awake");
            Assert.IsTrue(gate.Contains(victim.transform.position), "an occupant is standing outside its room");
            string victimName = victim.name;
            PrototypeArena.Kill(victim);
            yield return _arena.Tick(1);

            Assert.IsFalse(victim.IsAlive, $"{victimName} survived a lethal hit");
            Assert.AreEqual(
                aliveBefore - 1, gate.AliveCount,
                $"{gate.name} does not hold {victimName}, so its death was nobody's");
            Assert.AreEqual(
                killsBefore + 1, Flow.EnemiesKilledInMission,
                $"the kill was not counted. {victimName} in {gate.name}, " +
                $"정예 {Flow.ElitesKilledInMission}, phase {Flow.Phase}/{Flow.AreaPhase}, " +
                $"refusal '{Flow.LastRefusal}'");
            Assert.AreEqual(
                xpBefore + MissionXpBudget.XpForNormalKill(0), Flow.Run.MissionXp,
                "the kill did not pay its 의뢰 XP");

            // The two counts are what tell an ordinary kill from a 정예 one, and a victim that
            // drifted back to being a 정예 would otherwise fail this test as if the reporting
            // had broken.
            Assert.AreEqual(0, Flow.ElitesKilledInMission, "an ordinary enemy was counted as a 정예");
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

            _arena.MakePlayerUnkillable();
            yield return _arena.ClearObjectives(area);
            PrototypeArena.Kill(area.BossGate.Boss);
            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase);

            // Standing at the entrance, which the layout keeps a long walk from the boss's room.
            StandOn(area.Layout.Start);
            yield return _arena.Tick(DwellTicks * 10);

            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase, "it purified from across the map");
            Assert.AreEqual(0, Flow.PurifiedAreas.Count);
        }
    
        // --- 변이발생 -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AMutatedAreaHasExactlyTheMutantsTheEnvironmentPromised()
        {
            // [확정] docs/01 section 5: 변이발생 - 일부 개체 능력 상승. AreaMutationTests holds
            // the rules; what only a built area can show is that the enemies standing in the
            // room are the ones those rules chose. EnemyAgent writes its archetype's stats onto
            // the actor again in its own Start, so a boost applied to the actor rather than the
            // archetype would be undone on the frame the room opens - and every pure test would
            // still pass.
            ConnectSave();
            _arena = PrototypeArena.Build();

            const int Level = AreaConditions.MaxMutationLevel;
            AreaBuildResult area = BuildArea(
                new AreaConditions(HardwareId.Cpu, HardwareDifficulty.Normal, EnvironmentKind.Mutation, Level));

            yield return _arena.Tick(1);

            int normals = 0;
            for (int i = 0; i < area.Layout.Enemies.Count; ++i)
            {
                if (!area.Layout.Enemies[i].IsElite)
                    ++normals;
            }

            Assert.AreEqual(
                AreaMutation.MutatedCount(normals, Level), area.MutatedEnemies,
                "the builder did not mutate the share the level asks for");
            Assert.Greater(area.MutatedEnemies, 0);

            int markedMutant = 0;
            int elitesSeen = 0;
            for (int g = 0; g < area.RoomGates.Length; ++g)
            {
                AreaRoomGate gate = area.RoomGates[g];
                gate.Open();
                yield return _arena.Tick(1);

                for (int i = 0; i < gate.HeldCount; ++i)
                {
                    AreaRoomOccupant occupant = gate.OccupantAt(i);
                    var agent = occupant.Actor.GetComponent<NeonVoid.Combat.AI.EnemyAgent>();
                    Assert.IsNotNull(agent, "an area enemy has no brain");

                    bool isMutant = agent.Archetype.DocumentId.Contains("mutated");
                    if (occupant.IsElite)
                    {
                        ++elitesSeen;
                        Assert.IsFalse(isMutant, "a 정예 was mutated on top of being a 정예");
                        continue;
                    }

                    if (!isMutant)
                        continue;

                    ++markedMutant;

                    // The stats the agent is running are the stats on the actor: the boost
                    // survived Start rather than being written and overwritten.
                    Assert.AreEqual(
                        agent.Archetype.MaxHealth, occupant.Actor.Health.Max, 0.05f,
                        "the mutant's archetype and its actor disagree");
                }
            }

            Assert.AreEqual(area.MutatedEnemies, markedMutant, "the mutants in the room are not the ones counted");
            Assert.Greater(elitesSeen, 0, "the area generated no 정예, so this says nothing about them");
        }

        [UnityTest]
        public IEnumerator AMutatedKillPaysTheSameMissionXpAsAnyOther()
        {
            // GDD v1 section 5.4 fixes the area's 의뢰 XP budget by cause. An environment that
            // paid more would make 변이발생 the farming environment and double some areas'
            // budgets without anything saying so.
            ConnectSave();
            _arena = PrototypeArena.Build();

            AreaBuildResult area = BuildArea(
                new AreaConditions(
                    HardwareId.Cpu, HardwareDifficulty.Normal, EnvironmentKind.Mutation,
                    AreaConditions.MaxMutationLevel));

            // The area entered the flow itself when it was built.
            Assert.AreEqual(MissionFlowPhase.InArea, Flow.Phase, Flow.LastRefusal);

            AreaRoomGate gate = area.RoomGates[0];
            gate.Open();
            yield return _arena.Tick(1);

            // A normal, not a 정예: an elite legitimately pays four times as much, so killing
            // whichever occupant came first would be a coin toss over what this asserts.
            CombatActor normal = null;
            for (int i = 0; i < gate.HeldCount && normal == null; ++i)
            {
                if (!gate.OccupantAt(i).IsElite)
                    normal = gate.OccupantAt(i).Actor;
            }

            Assert.IsNotNull(normal, "the first room holds only 정예");

            long before = Flow.Run.MissionXp;
            PrototypeArena.Kill(normal);
            yield return _arena.Tick(2);

            Assert.AreEqual(
                MissionXpBudget.XpForNormalKill(0), Flow.Run.MissionXp - before,
                "a kill in a mutated area paid a different amount");
        }
    
        // --- 구역별 소목표 -------------------------------------------------------------------

        /// <summary>The area's chain nodes, in chain order.</summary>
        private static AreaObjectiveDevice[] ChainNodesOf(AreaBuildResult area)
        {
            var nodes = new System.Collections.Generic.List<AreaObjectiveDevice>();
            for (int i = 0; i < area.Devices.Length; ++i)
            {
                if (area.Devices[i] != null && area.Devices[i].Kind == AreaObjectiveKind.Sequence)
                    nodes.Add(area.Devices[i]);
            }

            nodes.Sort((a, b) => a.ChainIndex.CompareTo(b.ChainIndex));
            return nodes.ToArray();
        }

        /// <summary>Puts the player on a station, through the motion simulation.</summary>
        private void StandAt(Transform station)
        {
            Vector3 at = station.position;
            _arena.Driver.TeleportTo(new Vector2(at.x, at.z));
        }

        /// <summary>
        /// Somewhere no station can reach, whatever the seed placed where. A cell of the map
        /// would do for most seeds and then quietly stop proving anything on the one that put an
        /// objective next to the entrance.
        /// </summary>
        private void StandFarAway() =>
            _arena.Driver.TeleportTo(new Vector2(AreaOrigin.x + 5000f, AreaOrigin.z + 5000f));

        /// <summary>The station of this area's farthest objective from the entrance.</summary>
        private AreaObjectiveDevice FarthestStation(AreaBuildResult area)
        {
            Vector3 start = WorldOf(area.Layout.Start);
            AreaObjectiveDevice farthest = null;
            float best = -1f;

            for (int i = 0; i < area.Devices.Length; ++i)
            {
                AreaObjectiveDevice candidate = area.Devices[i];
                if (candidate == null || candidate.Station == null)
                    continue;

                float distance = (candidate.Station.position - start).sqrMagnitude;
                if (distance <= best)
                    continue;

                best = distance;
                farthest = candidate;
            }

            Assert.IsNotNull(farthest, $"{area.Layout.Spec.Hardware} placed no station");
            return farthest;
        }

        [UnityTest]
        public IEnumerator EveryHardwareAreaCanActuallyBeCleared()
        {
            // [확정] docs/01 section 1: 2~3개 소목표 → 보스. The objective shapes are per hardware
            // now, so this is the test that each hardware's own table produces an area a player
            // can finish - the one failure mode a per-hardware table introduces.
            foreach (HardwareId hardware in HardwareIds.All)
            {
                ConnectSave();
                _arena = PrototypeArena.Build();
                _arena.MakePlayerUnkillable();

                AreaBuildResult area = BuildArea(
                    AreaConditions.Clear(hardware, HardwareDifficulty.Normal), seed: 11u);
                yield return _arena.Tick(1);

                Assert.AreEqual(AreaProgressPhase.Objectives, Flow.AreaPhase, $"{hardware}: {Flow.LastRefusal}");

                yield return _arena.ClearObjectives(area);

                Assert.AreEqual(
                    AreaProgressPhase.Boss, Flow.AreaPhase,
                    $"{hardware}: its objectives could not be completed. {Flow.LastRefusal}");
                Assert.AreEqual(
                    0, area.Runtime.ObjectivesOutstanding, $"{hardware} left an objective standing");

                // Object.Destroy only takes effect at the end of the frame, and the next
                // iteration's runtime looks the SimulationRunner up by type - so without a frame
                // in between it would find the one that is on its way out.
                TearDown();
                yield return null;
                SetUp();
            }
        }

        [UnityTest]
        public IEnumerator EveryHardwareAsksForSomethingDifferent()
        {
            // CLAUDE.md: the five hardware are not one copy-pasted map. The table is held by
            // AreaObjectiveTests; what only a built area shows is that the builder gives each
            // area its hardware's objectives rather than CPU's, which is what it used to do.
            var seen = new System.Collections.Generic.List<string>();

            foreach (HardwareId hardware in HardwareIds.All)
            {
                ConnectSave();
                _arena = PrototypeArena.Build();

                AreaBuildResult area = BuildArea(
                    AreaConditions.Clear(hardware, HardwareDifficulty.Normal), seed: 11u);
                yield return _arena.Tick(1);

                var names = new System.Text.StringBuilder();
                for (int i = 0; i < area.Devices.Length; ++i)
                {
                    names.Append(area.Devices[i].Kind).Append(':').Append(area.Devices[i].ObjectiveName);
                    names.Append('|');

                    bool isDestroy = area.Devices[i].Kind == AreaObjectiveKind.Destroy;
                    Assert.AreEqual(
                        isDestroy, area.Devices[i].Device != null,
                        $"{hardware}: {area.Devices[i].ObjectiveName} has the wrong kind of body");
                    Assert.AreEqual(
                        isDestroy, area.Devices[i].Station == null,
                        $"{hardware}: {area.Devices[i].ObjectiveName} has the wrong kind of place");
                }

                string signature = names.ToString();
                Assert.IsFalse(seen.Contains(signature), $"{hardware} built the same objectives as another area");
                seen.Add(signature);

                TearDown();
                yield return null;
                SetUp();
            }
        }

        [UnityTest]
        public IEnumerator AStationIsCompletedByStandingOnItAndNotFromAcrossTheMap()
        {
            // The same claim DwellGate makes, made of an objective: a Hold that counted ticks
            // without caring where the player was would complete itself while they fought
            // somewhere else.
            ConnectSave();
            _arena = PrototypeArena.Build();
            _arena.MakePlayerUnkillable();

            // HDD's three objectives are all Hold.
            AreaBuildResult area = BuildArea(
                AreaConditions.Clear(HardwareId.Hdd, HardwareDifficulty.Normal), seed: 11u);
            yield return _arena.Tick(1);

            AreaObjectiveDevice hold = FarthestStation(area);
            Assert.AreEqual(AreaObjectiveKind.Hold, hold.Kind);
            Assert.IsNull(hold.Device, "a station the player could shoot down would make the area unclearable");

            StandFarAway();
            yield return _arena.Tick(hold.Dwell.RequiredTicks * 2);

            Assert.IsFalse(hold.IsCompleted, "it completed from the entrance");
            Assert.AreEqual(0, hold.Dwell.TicksHeld, "the dwell filled from across the map");

            StandAt(hold.Station);
            yield return _arena.Tick(hold.Dwell.RequiredTicks + 1);

            Assert.IsTrue(hold.IsCompleted, "standing on it did nothing");
            Assert.AreEqual(1, Flow.ObjectivesCompletedInArea, "the flow was not told");
        }

        [UnityTest]
        public IEnumerator AChainWalkedOutOfOrderResetsAndTellsTheFlowNothing()
        {
            // [확정] docs/01 section 6.6: 완료 보상은 반복 호출에도 정확히 1회. A chain node that
            // reported on its own would have to un-report when the chain broke, so a half-walked
            // chain must have told the flow nothing at all.
            ConnectSave();
            _arena = PrototypeArena.Build();
            _arena.MakePlayerUnkillable();

            // GPU's first two objectives are a chain, with no clock.
            AreaBuildResult area = BuildArea(
                AreaConditions.Clear(HardwareId.Gpu, HardwareDifficulty.Normal), seed: 11u);
            yield return _arena.Tick(1);

            AreaObjectiveDevice[] nodes = ChainNodesOf(area);
            Assert.AreEqual(2, nodes.Length, "GPU's chain is not two nodes");
            Assert.IsNotNull(area.Runtime.ObjectiveChain, "the area built no chain");
            Assert.IsFalse(area.Runtime.ObjectiveChain.HasWindow, "GPU's chain has a clock");

            // The second node first.
            StandAt(nodes[1].Station);
            yield return _arena.Tick(nodes[1].Dwell.RequiredTicks + 1);

            Assert.AreEqual(0, area.Runtime.ObjectiveChain.Lit, "an out-of-order node lit the chain");
            Assert.IsFalse(nodes[1].IsCompleted, "a node completed on its own");
            Assert.AreEqual(0, Flow.ObjectivesCompletedInArea, "the flow counted half a chain");

            // And now in order. The first node lights but reports nothing.
            StandAt(nodes[0].Station);
            yield return _arena.Tick(nodes[0].Dwell.RequiredTicks + 1);

            Assert.IsTrue(nodes[0].IsLit, "the first node did not light");
            Assert.IsFalse(nodes[0].IsCompleted, "a node reported before the chain landed");
            Assert.AreEqual(0, Flow.ObjectivesCompletedInArea, "the flow counted a lit node");

            StandAt(nodes[1].Station);
            yield return _arena.Tick(nodes[1].Dwell.RequiredTicks + 1);

            Assert.IsTrue(area.Runtime.ObjectiveChain.IsComplete, "the chain never landed");
            Assert.AreEqual(
                nodes.Length, Flow.ObjectivesCompletedInArea,
                "the chain landed but its nodes were not counted");

            for (int i = 0; i < nodes.Length; ++i)
                Assert.IsTrue(nodes[i].IsCompleted, $"chain node {i} was not completed");
        }

        [UnityTest]
        public IEnumerator SsdsChainRunsOutIfTheWalkTakesTooLong()
        {
            // [초안] docs/01 section 2: SSD is 레일을 짧은 시간에 연결, and it is the only area with
            // a clock. The expiry has to leave the flow untouched for the same reason an
            // out-of-order node does.
            ConnectSave();
            _arena = PrototypeArena.Build();
            _arena.MakePlayerUnkillable();

            AreaBuildResult area = BuildArea(
                AreaConditions.Clear(HardwareId.Ssd, HardwareDifficulty.Normal), seed: 11u);
            yield return _arena.Tick(1);

            AreaObjectiveChain chain = area.Runtime.ObjectiveChain;
            Assert.IsNotNull(chain, "SSD built no chain");
            Assert.IsTrue(chain.HasWindow, "SSD's chain has no clock");

            AreaObjectiveDevice[] nodes = ChainNodesOf(area);
            StandAt(nodes[0].Station);
            yield return _arena.Tick(nodes[0].Dwell.RequiredTicks + 1);

            Assert.AreEqual(1, chain.Lit, "the first rail never lit");

            // Walk away and wait it out.
            StandFarAway();
            yield return _arena.Tick(chain.WindowTicks + 2);

            Assert.AreEqual(0, chain.Lit, "the window never ran out");
            Assert.AreEqual(0, Flow.ObjectivesCompletedInArea, "an expired chain paid the flow");
            Assert.IsFalse(nodes[0].IsCompleted, "an expired node stayed complete");

            // And it can still be cleared afterwards, which is the point of a reset rather than a failure.
            yield return _arena.ClearObjectives(area);
            Assert.AreEqual(AreaProgressPhase.Boss, Flow.AreaPhase, Flow.LastRefusal);
        }
    }
}
