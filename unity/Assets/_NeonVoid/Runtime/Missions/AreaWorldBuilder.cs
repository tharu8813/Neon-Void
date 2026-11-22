using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Missions.Generation;
using UnityEngine;

namespace NeonVoid.Missions
{
    /// <summary>What was built, so a caller can drive it and a test can assert about it.</summary>
    public sealed class AreaBuildResult
    {
        public AreaBuildResult(
            GameObject root,
            AreaLayout layout,
            HardwareAreaRuntime runtime,
            AreaObjectiveDevice[] devices,
            AreaBossGate bossGate,
            Transform purificationConsole,
            Transform extractionPad,
            AreaRoomGate[] roomGates,
            int floorSlabs,
            int wallSlabs,
            int mutatedEnemies)
        {
            Root = root;
            Layout = layout;
            Runtime = runtime;
            Devices = devices;
            BossGate = bossGate;
            PurificationConsole = purificationConsole;
            ExtractionPad = extractionPad;
            RoomGates = roomGates;
            FloorSlabs = floorSlabs;
            WallSlabs = wallSlabs;
            MutatedEnemies = mutatedEnemies;
        }

        /// <summary>Everything built, under one object, so it can be torn down in one call.</summary>
        public GameObject Root { get; }

        public AreaLayout Layout { get; }

        public HardwareAreaRuntime Runtime { get; }

        public AreaObjectiveDevice[] Devices { get; }

        public AreaBossGate BossGate { get; }

        public Transform PurificationConsole { get; }

        public Transform ExtractionPad { get; }

        /// <summary>One per room holding enemies, each held shut until the player walks in.</summary>
        public AreaRoomGate[] RoomGates { get; }

        /// <summary>Boxes the floor was built from, after merging. For the performance report.</summary>
        public int FloorSlabs { get; }

        /// <summary>Boxes the wall was built from, after merging.</summary>
        public int WallSlabs { get; }

        /// <summary>
        /// How many of this area's enemies the environment mutated. 0 outside 변이발생.
        /// <para>
        /// Reported rather than left implicit because it is the only visible consequence of the
        /// board's environment right now: a Mutation area that built no mutants would look
        /// exactly like a Clear one, in the world and in every log.
        /// </para>
        /// </summary>
        public int MutatedEnemies { get; }

        /// <summary>Enemies placed, across every room.</summary>
        public int EnemyCount
        {
            get
            {
                int enemies = 0;
                for (int i = 0; i < RoomGates.Length; ++i)
                    enemies += RoomGates[i].HeldCount;

                return enemies;
            }
        }

        public override string ToString() =>
            $"{Layout.Spec.Hardware}: {Devices.Length} objectives, {EnemyCount} enemies in " +
            $"{RoomGates.Length} rooms, {FloorSlabs} floor + {WallSlabs} wall slabs";
    }

    /// <summary>
    /// Builds a playable room from an <see cref="AreaLayout"/>: floor, walls, the objective
    /// devices, the boss behind its gate, the 정화 콘솔, the 철수 패드, and the
    /// <see cref="HardwareAreaRuntime"/> that joins them to the mission.
    /// <para>
    /// This is the stage docs/01 section 6 puts after the graph: 최종 3D 프리팹/조명/벽은
    /// 통과한 그래프 위에 꾸미는 표현 단계. It closes the chain - seed → layout → room → play →
    /// flow → save - and it decides nothing about the map. If the room is wrong, the layout was
    /// wrong, and the layout has its own tests.
    /// </para>
    /// <para>
    /// <b>Prefabs where there are prefabs, primitives where there are not.</b> A device and a
    /// boss are content: what they look like, what they are worth, how the boss fights. Where a
    /// prefab is given this places it; where none is, it builds a marked
    /// <c>Prototype</c> primitive from serialized stat blocks, so the loop can be played and
    /// measured before the content exists. The stats are serialized data rather than numbers in
    /// code, per CLAUDE.md, and they are [초안] - D-015 owns what a CPU area actually contains.
    /// </para>
    /// <para>
    /// <b>No enemies, and no boss fight.</b> docs/01 section 3 asks each boss for 최소 3개의
    /// 형태가 다른 패턴 + 1개 지역 기믹 + 1개 페이즈 변화, and section 2 gives every area its own
    /// 적 성향. None of that is here: this boss can be shot and can die, which is what the flow
    /// needs to be driven end to end. The fight goes behind the gate that already holds it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaWorldBuilder : MonoBehaviour
    {
        [Header("Where")]
        [Tooltip("Added to every position, so an area can be built clear of anything else in the scene.")]
        [SerializeField] private Vector3 worldOrigin = Vector3.zero;

        [Header("Terrain [초안]")]
        [Tooltip("Build the floor and the walls. Off when something else owns the geometry.")]
        [SerializeField] private bool buildTerrain = true;

        [Tooltip("[초안] floor thickness. The top surface sits at the origin's height.")]
        [SerializeField, Min(0.05f)] private float floorThicknessUnits = 0.5f;

        [Tooltip("[초안] wall height. Tall enough to stop a shot, short enough to see over.")]
        [SerializeField, Min(0.5f)] private float wallHeightUnits = 3f;

        [Header("Contents")]
        [Tooltip("Objective device prefab. A Prototype primitive is built when empty.")]
        [SerializeField] private GameObject objectiveDevicePrefab;

        [Tooltip("Boss prefab. A Prototype primitive is built when empty.")]
        [SerializeField] private GameObject bossPrefab;

        [Tooltip("Enemy prefab. A Prototype primitive with an EnemyAgent is built when empty.")]
        [SerializeField] private GameObject enemyPrefab;

        [Tooltip(
            "[초안] a 정예's health, as a multiple of its archetype's. docs/01 calls enemy.elite " +
            "환경 변이·강인함 and neither exists, so this stands in for it.")]
        [SerializeField, Min(1f)] private float draftEliteHealthMultiplier = 2.5f;

        [Tooltip("[초안] a 정예's attack damage, as a multiple of its archetype's.")]
        [SerializeField, Min(1f)] private float draftEliteDamageMultiplier = 1.5f;

        [Tooltip("Team the devices and the boss belong to, so the player's fire can reach them.")]
        [SerializeField] private int hostileTeamId = 1;

        [Header("Prototype stats [초안] - used only without a prefab")]
        [Tooltip("[초안] what an objective device is made of. D-015 owns the real answer.")]
        [SerializeField] private CombatStats prototypeDeviceStats = PrototypeDeviceStats();

        [Tooltip("[초안] what the area boss is made of.")]
        [SerializeField] private CombatStats prototypeBossStats = PrototypeBossStats();

        [Tooltip("[초안] how wide a Prototype device or boss stands, in units.")]
        [SerializeField, Min(0.2f)] private float prototypeBodyWidthUnits = 1.6f;

        /// <summary>
        /// [초안] a device sturdy enough to need a few seconds of fire and not so sturdy that it
        /// is the area's real fight. Replaced by a prefab, and by D-015 before that.
        /// </summary>
        public static CombatStats PrototypeDeviceStats()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.MaxHealth = 240f;
            stats.AttackDamage = 0f;
            return stats;
        }

        /// <summary>[초안] a boss that takes longer than a device and does nothing else yet.</summary>
        public static CombatStats PrototypeBossStats()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.MaxHealth = 1200f;
            stats.PhysicalArmor = 10f;
            return stats;
        }

        private int _mutationLevel = AreaMutation.NoMutation;
        private bool[] _mutated;
        private int _mutatedThisBuild;

        /// <summary>Where the area's cell (0,0) begins in world space.</summary>
        public Vector3 WorldOrigin
        {
            get => worldOrigin;
            set => worldOrigin = value;
        }

        /// <summary>
        /// Builds the area and moves the player to its entrance.
        /// </summary>
        /// <param name="layout">A layout that has passed <see cref="AreaLayoutValidator"/>.</param>
        /// <param name="mission">The mission the area reports to.</param>
        /// <param name="player">
        /// Moved to the entrance. Null leaves the player where they are, which is only useful to
        /// a test that is about the geometry.
        /// </param>
        public AreaBuildResult Build(AreaLayout layout, MissionRunDirector mission, Transform player) =>
            Build(layout, mission, player, AreaConditions.Clear(layout.Spec.Hardware, HardwareDifficulty.Normal));

        /// <summary>
        /// Builds the area under the mission's conditions for it.
        /// <para>
        /// The conditions arrive here rather than in <see cref="AreaSpec"/> on purpose. docs/01
        /// section 6 check 4 makes <c>(seed, hardwareId, generatorVersion)</c> reproduce the
        /// terrain and the objective positions, so the generator never sees the environment -
        /// a Clear area and a Mutation area of the same seed are the same map with different
        /// enemies standing in it, which is what lets a player recognise a hardware they have
        /// played before.
        /// </para>
        /// <para>
        /// <b>난이도 does nothing here.</b> docs/01 section 3 lists 난이도에 따른 실제 HP/피해 배율
        /// among its [미확정] items, so Hard and Easy differ in what the gate says and in
        /// nothing else. Only 변이발생 has a [확정] effect to apply.
        /// </para>
        /// </summary>
        public AreaBuildResult Build(
            AreaLayout layout, MissionRunDirector mission, Transform player, in AreaConditions conditions)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (conditions.Hardware != layout.Spec.Hardware)
            {
                throw new ArgumentException(
                    $"those are {conditions.Hardware}'s conditions and this is a {layout.Spec.Hardware} layout.",
                    nameof(conditions));
            }

            _mutationLevel = AreaMutation.LevelOf(conditions);

            // A map that has not passed cannot be built: docs/01 section 6's checks exist so a
            // player is never handed an area they cannot finish, and building one anyway would
            // make the checks advisory.
            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);
            if (!verdict.IsValid)
                throw new ArgumentException($"that layout did not pass validation: {verdict.Reason}", nameof(layout));

            var root = new GameObject($"Area {layout.Spec.Hardware}");
            root.transform.position = Vector3.zero;

            int floorSlabs = 0;
            int wallSlabs = 0;
            if (buildTerrain)
            {
                floorSlabs = BuildFloor(layout, root.transform);
                wallSlabs = BuildWalls(layout, root.transform);
            }

            // [초안] docs/01 section 2's own objectives for this hardware, in its own 핵심 동사.
            // Every area used to build CPU's 과열 밸브, three identical destructibles, whatever
            // hardware it was - which is the copy-pasted map CLAUDE.md forbids.
            HardwareId hardware = layout.Spec.Hardware;
            AreaObjectiveSpec[] objectiveSpecs = AreaObjectives.For(hardware, layout.Objectives.Count);
            var devices = new AreaObjectiveDevice[layout.Objectives.Count];
            for (int i = 0; i < devices.Length; ++i)
            {
                AreaObjectiveSpec spec = objectiveSpecs[i];
                AreaCell cell = layout.Objectives[i];

                if (spec.Kind == AreaObjectiveKind.Destroy)
                {
                    CombatActor actor = SpawnBody(
                        objectiveDevicePrefab, prototypeDeviceStats, PrimitiveType.Cube,
                        spec.Name, layout, cell, root.transform);

                    devices[i] = AddAsleep<AreaObjectiveDevice>(
                        $"Objective {i + 1}", root.transform, d => d.Configure(actor, spec.Name));
                    continue;
                }

                Transform at = SpawnStation(spec.Name, layout, cell, root.transform);
                int ticks = spec.Kind == AreaObjectiveKind.Hold
                    ? AreaObjectives.HoldTicksFor(hardware)
                    : AreaObjectives.ChainNodeTicksFor(hardware);

                devices[i] = AddAsleep<AreaObjectiveDevice>(
                    $"Objective {i + 1}", root.transform,
                    d => d.ConfigureStation(spec, at, AreaObjectives.DraftHoldRadiusUnits, ticks));
            }

            CombatActor bossActor = SpawnBody(
                bossPrefab, prototypeBossStats, PrimitiveType.Capsule,
                BossNameFor(layout.Spec.Hardware), layout, layout.Boss, root.transform);

            GiveTheBossItsPatterns(layout.Spec.Hardware, bossActor, player);

            // The gate goes on its own object: it deactivates what it holds, and a gate that
            // deactivated itself could not be told to open again.
            AreaBossGate bossGate = AddAsleep<AreaBossGate>(
                "Boss Gate", root.transform,
                g => g.Configure(bossActor, bossActor.gameObject, BossNameFor(layout.Spec.Hardware)));

            Transform console = Marker("정화 콘솔", layout, layout.Purification, root.transform);
            Transform pad = Marker("철수 패드", layout, layout.Extraction, root.transform);

            AreaRoomGate[] roomGates = BuildEnemies(layout, root.transform);
            AreaHazard[] hazards = BuildHazards(layout, root.transform);
            int mutated = _mutatedThisBuild;
            _mutationLevel = AreaMutation.NoMutation;

            if (player != null)
                PlaceAtEntrance(player, PositionOf(layout, layout.Start));

            // Copied out of the `in` parameter: a lambda cannot capture one, and the runtime
            // needs its 환경 before it wakes.
            AreaConditions areaConditions = conditions;

            HardwareAreaRuntime runtime = AddAsleep<HardwareAreaRuntime>(
                "Area Runtime", root.transform,
                r =>
                {
                    r.Configure(layout.Spec.Hardware, mission, player, devices, bossGate, console, pad, roomGates);

                    // Before it wakes: 터널감염 lands when the player enters, and an area that
                    // learned its 환경 a tick later would infect them a tick into the fight.
                    r.UseConditions(areaConditions);
                });

            Debug.Log(
                $"Built {layout.Spec.Hardware} from {layout.ReceiptLine()}: " +
                $"{floorSlabs} floor slab(s), {wallSlabs} wall slab(s), " +
                $"{devices.Length} objective(s) [{DescribeObjectives(devices)}], " +
                $"{layout.Enemies.Count} enemy/enemies in {roomGates.Length} gated room(s), " +
                $"{conditions.GateLine()}" +
                (mutated > 0 ? $", {mutated} 변이체" : string.Empty),
                root);

            return new AreaBuildResult(
                root, layout, runtime, devices, bossGate, console, pad, roomGates, floorSlabs, wallSlabs,
                mutated);
        }

        /// <summary>
        /// The area's 환경 상호작용, one per cell the layout set aside.
        /// <para>
        /// [확정] docs/01 section 2 gives every hardware a different one, and CLAUDE.md names
        /// 환경 상호작용 among the things that must differ: 5개 하드웨어를 한 개의 복붙 맵으로
        /// 취급하지 말 것. The shape comes from <see cref="AreaHazards"/>; the cells came from
        /// the generator, which kept them clear of the entrance, the objectives and the spawns.
        /// </para>
        /// <para>
        /// A hazard that moves is handed every hazard cell as its stations, so RAM's 재배열 has
        /// somewhere to go. The others ignore all but their own.
        /// </para>
        /// </summary>
        private AreaHazard[] BuildHazards(AreaLayout layout, Transform parent)
        {
            AreaHazardShape shape = AreaHazards.For(layout.Spec.Hardware);
            IReadOnlyList<AreaCell> cells = layout.Hazards;
            if (shape.IsNone || cells.Count == 0)
                return new AreaHazard[0];

            var stations = new Vector3[cells.Count];
            for (int i = 0; i < cells.Count; ++i)
                stations[i] = PositionOf(layout, cells[i]);

            var built = new AreaHazard[cells.Count];
            for (int i = 0; i < cells.Count; ++i)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = $"Hazard {AreaHazards.NameOf(shape.Kind)} {i}";
                marker.transform.SetParent(parent, worldPositionStays: true);
                marker.transform.position = stations[i];

                // The telegraph has to be readable 이펙트 품질 최저에서도, so the hazard is a
                // flat disc on the floor at its own reach - the shape IS the warning, rather
                // than a particle system drawn over one.
                marker.transform.localScale =
                    new Vector3(shape.RadiusUnits * 2f, 0.02f, shape.RadiusUnits * 2f);
                UnityEngine.Object.Destroy(marker.GetComponent<Collider>());

                var hazard = marker.AddComponent<AreaHazard>();

                // Seeded per hazard from the area's own seed, so the same map plays the same
                // way twice - including which of them is warning when the player walks in.
                hazard.Configure(shape, stations, i, layout.EffectiveSeed + (uint)(i * 2654435761u));
                built[i] = hazard;
            }

            return built;
        }

        /// <summary>
        /// 보스 컨셉 from docs/01 section 2. The name is content; the fight is
        /// <see cref="GiveTheBossItsPatterns"/>'s problem.
        /// </summary>
        public static string BossNameFor(HardwareId hardware)
        {
            switch (hardware)
            {
                case HardwareId.Cpu: return "코어 워처";
                case HardwareId.Gpu: return "렌더 옵저버";
                case HardwareId.Ram: return "메모리 리치";
                case HardwareId.Ssd: return "스트림 인터셉터";
                case HardwareId.Hdd: return "섹터 콜로서스";
                default: return "boss";
            }
        }

        /// <summary>
        /// Gives the area's boss its own patterns. Every hardware has a different cycle, built
        /// from its own row of docs/01 section 2 and its own 세부 전개.
        /// <para>
        /// <b>Nothing is ever substituted.</b> An area whose boss has no cycle gets a boss that
        /// can be shot and killed and a loud line saying the contract is unmet - it does not get
        /// the 코어 워처's patterns in another colour. That substitution would pass every test in
        /// this project and it is the copy-paste CLAUDE.md forbids by name.
        /// </para>
        /// <para>
        /// This choice lives in the Missions domain because docs/06 makes 월드 its own, and the
        /// boss catalogues in Combat must not reach across to ask which hardware they are
        /// looking at.
        /// </para>
        /// </summary>
        private void GiveTheBossItsPatterns(HardwareId hardware, CombatActor bossActor, Transform player)
        {
            BossPatternCycle cycle = CycleFor(hardware);
            if (cycle == null)
            {
                Debug.LogWarning(
                    $"{BossNameFor(hardware)} ({hardware}) has no patterns yet, so it stands still and can be " +
                    "killed. docs/01 section 3's contract - 3 patterns, a 지역 기믹, a phase change - is " +
                    "unmet for this area, and it is not given another boss's.", this);
                return;
            }

            var brain = bossActor.gameObject.AddComponent<AreaBossBrain>();
            brain.Configure(bossActor, player != null ? player.GetComponent<CombatActor>() : null);
            brain.UseCycle(cycle);

            // Presentation, beside the brain, the way ActorDeathReaction sits beside an actor.
            // [초안] docs/05 section 6 wants 보스 경고 to have a face of its own, and section 6
            // also says 위험 공격은 음향 없이도 피할 수 있어야 한다 - so this is a second
            // channel on a warning the telegraph already draws, never the only one.
            bossActor.gameObject.AddComponent<NeonVoid.Presentation.BossWarningSound>();
        }

        /// <summary>
        /// The cycle for one hardware, or null when nothing has been built for it. Public and
        /// static so a test can hold that the five are five different fights without building
        /// five areas.
        /// </summary>
        public static BossPatternCycle CycleFor(HardwareId hardware)
        {
            switch (hardware)
            {
                case HardwareId.Cpu: return BossCatalog.CoreWatcher();
                case HardwareId.Gpu: return AreaBossCatalog.RenderObserver();
                case HardwareId.Ram: return AreaBossCatalog.MemoryLich();
                case HardwareId.Ssd: return AreaBossCatalog.StreamInterceptor();
                case HardwareId.Hdd: return AreaBossCatalog.SectorColossus();
                default: return null;
            }
        }

        /// <summary>
        /// Places the layout's enemies and puts each room's share behind its own
        /// <see cref="AreaRoomGate"/>.
        /// </summary>
        private AreaRoomGate[] BuildEnemies(AreaLayout layout, Transform parent)
        {
            var perRoom = new Dictionary<int, List<AreaRoomOccupant>>();

            // Decided once, before anything is spawned, so the choice is a function of the
            // seed and the level rather than of the order things happened to be created in.
            var isElite = new bool[layout.Enemies.Count];
            for (int i = 0; i < layout.Enemies.Count; ++i)
                isElite[i] = layout.Enemies[i].IsElite;

            _mutated = AreaMutation.Choose(layout.EffectiveSeed, isElite, _mutationLevel);
            _mutatedThisBuild = 0;
            for (int i = 0; i < _mutated.Length; ++i)
            {
                if (_mutated[i])
                    _mutatedThisBuild++;
            }

            for (int i = 0; i < layout.Enemies.Count; ++i)
            {
                AreaEnemySpawn spawn = layout.Enemies[i];
                CombatActor actor = SpawnEnemy(spawn, layout, parent, i);

                if (!perRoom.TryGetValue(spawn.RoomIndex, out List<AreaRoomOccupant> occupants))
                {
                    occupants = new List<AreaRoomOccupant>();
                    perRoom[spawn.RoomIndex] = occupants;
                }

                occupants.Add(new AreaRoomOccupant(actor, spawn.IsElite));
            }

            var gates = new List<AreaRoomGate>(perRoom.Count);
            foreach (KeyValuePair<int, List<AreaRoomOccupant>> room in perRoom)
            {
                AreaRoom shape = layout.Rooms[room.Key];
                float cell = layout.CellSizeUnits;

                Vector3 min = PositionOf(layout, new AreaCell(shape.MinX, shape.MinY));
                Vector3 max = PositionOf(layout, new AreaCell(shape.MaxX, shape.MaxY));
                var centre = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);

                // Half a cell past the outermost cell centres, so the footprint is the room and
                // not the box joining its corner cells' middles.
                float halfX = ((shape.Width * cell) * 0.5f);
                float halfZ = ((shape.Height * cell) * 0.5f);

                List<AreaRoomOccupant> occupants = room.Value;
                gates.Add(AddAsleep<AreaRoomGate>(
                    $"Room {room.Key} Gate", parent, g => g.Configure(centre, halfX, halfZ, occupants)));
            }

            return gates.ToArray();
        }

        /// <summary>
        /// One enemy: the prefab if there is one, otherwise a Prototype primitive carrying an
        /// <see cref="EnemyAgent"/>.
        /// <para>
        /// A 정예 is the same archetype with its health and damage multiplied, and the
        /// multiplication is done to the <b>archetype</b> rather than to the actor: EnemyAgent
        /// writes the archetype's stats onto the actor again in its own Start, so boosting the
        /// actor would be silently undone on the frame the room opens.
        /// </para>
        /// </summary>
        private CombatActor SpawnEnemy(in AreaEnemySpawn spawn, AreaLayout layout, Transform parent, int index)
        {
            EnemyArchetype archetype = EnemyArchetypeCatalog.Of(spawn.Archetype);
            if (spawn.IsElite)
            {
                archetype.DocumentId = $"enemy.elite({archetype.DocumentId})";
                archetype.MaxHealth *= draftEliteHealthMultiplier;
                archetype.AttackDamage *= draftEliteDamageMultiplier;
            }

            // [확정] docs/01 section 5: 변이발생 - 일부 개체 능력 상승. Applied to the archetype
            // for the same reason the elite multiplier is: EnemyAgent writes the archetype's
            // stats onto the actor again in its own Start, so boosting the actor would be
            // silently undone on the frame the room opens.
            bool mutated = _mutated != null && index < _mutated.Length && _mutated[index];
            if (mutated)
            {
                archetype.DocumentId = $"enemy.mutated{_mutationLevel}({archetype.DocumentId})";
                archetype.MaxHealth *= AreaMutation.HealthMultiplier(_mutationLevel);
                archetype.AttackDamage *= AreaMutation.DamageMultiplier(_mutationLevel);
            }

            string name =
                $"{(spawn.IsElite ? "정예 " : string.Empty)}{(mutated ? $"변이{_mutationLevel} " : string.Empty)}" +
                $"{spawn.Archetype} {index + 1}";
            Vector3 position = PositionOf(layout, spawn.Cell);

            GameObject body;
            if (enemyPrefab != null)
            {
                body = Instantiate(enemyPrefab, position, Quaternion.identity, parent);
                body.name = name;
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = $"{name} (Prototype)";
                body.transform.SetParent(parent, worldPositionStays: false);
                body.transform.position = position + (Vector3.up * 0.7f);
                body.transform.localScale = Vector3.one * 1.4f;

                // The CharacterController replaces the primitive's collider, as the enemy has to
                // move: it is a collider too, so a shot's SphereCastAll still finds it.
                UnityEngine.Object.Destroy(body.GetComponent<SphereCollider>());
                CharacterController controller = body.AddComponent<CharacterController>();
                controller.center = Vector3.zero;
                controller.height = 1f;
                controller.radius = 0.5f;
            }

            var actor = body.GetComponentInChildren<CombatActor>();
            if (actor == null)
            {
                actor = body.AddComponent<CombatActor>();
                body.AddComponent<ActorDeathReaction>();
            }

            // The team before the agent: EnemyAgent.ApplyArchetype keeps the actor's existing
            // team and only writes the stats, so an actor still on the default team would become
            // an enemy of nobody.
            actor.Configure(hostileTeamId, archetype.Stats());

            var agent = body.GetComponentInChildren<EnemyAgent>();
            if (agent == null)
                agent = body.AddComponent<EnemyAgent>();

            agent.ApplyArchetype(archetype);
            return actor;
        }

        /// <summary>
        /// [초안] how far above the floor the player is placed. A CharacterController spawned
        /// exactly on the surface starts the frame intersecting it and is pushed somewhere.
        /// </summary>
        public const float PlayerStandingLiftUnits = 1.1f;

        /// <summary>
        /// Puts the player at the entrance, <b>through the motion simulation</b> when there is
        /// one.
        /// <para>
        /// Writing <c>transform.position</c> is not enough and
        /// <see cref="Player.PlayerSimulationDriver.TeleportTo"/> says why: the rules keep their
        /// own position ledger, and a transform moved behind their back leaves the next tick
        /// resolving facing and the dash dead zone against where the player used to be - and the
        /// CharacterController, which is not switched off for the jump, is free to put them back.
        /// It did: the player arrived at the origin instead of the entrance, which is how this
        /// was found.
        /// </para>
        /// <para>
        /// The height is set on the transform first, because TeleportTo keeps the current one -
        /// it is for a blink across the floor, not for being dropped onto it.
        /// </para>
        /// </summary>
        private static void PlaceAtEntrance(Transform player, Vector3 entrance)
        {
            Vector3 standing = entrance + (Vector3.up * PlayerStandingLiftUnits);
            var driver = player.GetComponent<Player.PlayerSimulationDriver>();

            if (driver == null)
            {
                player.position = standing;
                return;
            }

            player.position = new Vector3(player.position.x, standing.y, player.position.z);
            driver.TeleportTo(new Vector2(standing.x, standing.z));
        }

        private int BuildFloor(AreaLayout layout, Transform parent)
        {
            List<AreaRoom> slabs = AreaTerrain.FloorSlabs(layout.Grid);
            for (int i = 0; i < slabs.Count; ++i)
            {
                // The top surface at the origin's height, so the floor is where the player
                // stands rather than where they are half buried.
                Box($"Floor {i}", layout, slabs[i], floorThicknessUnits, -floorThicknessUnits * 0.5f, parent);
            }

            return slabs.Count;
        }

        private int BuildWalls(AreaLayout layout, Transform parent)
        {
            List<AreaRoom> slabs = AreaTerrain.WallSlabs(layout.Grid);
            for (int i = 0; i < slabs.Count; ++i)
                Box($"Wall {i}", layout, slabs[i], wallHeightUnits, wallHeightUnits * 0.5f, parent);

            return slabs.Count;
        }

        /// <summary>One merged rectangle as a box, centred on the cells it covers.</summary>
        private void Box(string name, AreaLayout layout, in AreaRoom slab, float height, float centreY, Transform parent)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, worldPositionStays: false);

            float cell = layout.CellSizeUnits;
            Vector3 min = PositionOf(layout, new AreaCell(slab.MinX, slab.MinY));
            Vector3 max = PositionOf(layout, new AreaCell(slab.MaxX, slab.MaxY));

            box.transform.position = new Vector3((min.x + max.x) * 0.5f, worldOrigin.y + centreY, (min.z + max.z) * 0.5f);
            box.transform.localScale = new Vector3(slab.Width * cell, height, slab.Height * cell);
        }

        /// <summary>An empty transform the dwell gates measure against.</summary>
        /// <summary>The area's objectives, for the build log: what the player is actually asked to do.</summary>
        private static string DescribeObjectives(AreaObjectiveDevice[] devices)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < devices.Length; ++i)
            {
                if (i > 0)
                    text.Append(", ");

                text.Append(devices[i] != null ? devices[i].ToString() : "missing");
            }

            return text.ToString();
        }

        /// <summary>
        /// A station for a Hold or Sequence objective: somewhere to stand, visible, and
        /// <b>inert</b>.
        /// <para>
        /// No <see cref="CombatActor"/>, so the player cannot shoot down an objective and leave
        /// the area unclearable, and no collider, so the thing they have to stand on does not
        /// push them off it or swallow their own fire. A Prototype primitive until there is art;
        /// the dwell radius is what actually decides where counts, not this plate.
        /// </para>
        /// </summary>
        private Transform SpawnStation(string name, AreaLayout layout, AreaCell cell, Transform parent)
        {
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plate.name = $"{name} (Prototype)";
            plate.transform.SetParent(parent, worldPositionStays: false);
            plate.transform.position = PositionOf(layout, cell);
            plate.transform.localScale = new Vector3(
                AreaObjectives.DraftHoldRadiusUnits * 2f, 0.05f, AreaObjectives.DraftHoldRadiusUnits * 2f);

            // Disabled rather than destroyed: Destroy takes until the end of the frame, and for
            // that frame the plate would still be solid - which is a tick where the station the
            // player is told to stand on is in their way.
            Collider collider = plate.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;

            return plate.transform;
        }

        private Transform Marker(string name, AreaLayout layout, AreaCell cell, Transform parent)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(parent, worldPositionStays: false);
            marker.transform.position = PositionOf(layout, cell);
            return marker.transform;
        }

        /// <summary>
        /// The prefab, or a Prototype primitive when there is none. Either way the result is a
        /// <see cref="CombatActor"/> standing on the given cell, hostile to the player so their
        /// fire can reach it.
        /// </summary>
        private CombatActor SpawnBody(
            GameObject prefab,
            in CombatStats prototypeStats,
            PrimitiveType prototypeShape,
            string name,
            AreaLayout layout,
            AreaCell cell,
            Transform parent)
        {
            Vector3 position = PositionOf(layout, cell);

            if (prefab != null)
            {
                GameObject spawned = Instantiate(prefab, position, Quaternion.identity, parent);
                spawned.name = name;

                var fromPrefab = spawned.GetComponentInChildren<CombatActor>();
                if (fromPrefab == null)
                {
                    // Nothing can destroy it and nothing can kill it, so the area would be
                    // unclearable. Loud, and not silently replaced with a primitive: a prefab
                    // was given and it is the wrong prefab.
                    throw new InvalidOperationException(
                        $"{prefab.name} has no CombatActor, so {name} could never be destroyed.");
                }

                return fromPrefab;
            }

            GameObject body = GameObject.CreatePrimitive(prototypeShape);
            body.name = $"{name} (Prototype)";
            body.transform.SetParent(parent, worldPositionStays: false);
            body.transform.position = position + (Vector3.up * (prototypeBodyWidthUnits * 0.5f));
            body.transform.localScale = Vector3.one * prototypeBodyWidthUnits;

            // The primitive keeps its own collider: that is what a shot's SphereCastAll finds,
            // and CombatActor is looked up from it with GetComponentInParent.
            var actor = body.AddComponent<CombatActor>();
            body.AddComponent<ActorDeathReaction>();
            actor.Configure(hostileTeamId, prototypeStats);
            return actor;
        }

        /// <summary>
        /// Adds a component to a deactivated child and then wakes it, so its Awake sees the
        /// references it was configured with instead of searching the scene for them.
        /// </summary>
        private static T AddAsleep<T>(string name, Transform parent, Action<T> configure)
            where T : Component
        {
            var host = new GameObject(name);
            host.SetActive(false);
            host.transform.SetParent(parent, worldPositionStays: false);

            T component = host.AddComponent<T>();
            configure(component);
            host.SetActive(true);
            return component;
        }

        private Vector3 PositionOf(AreaLayout layout, AreaCell cell) => worldOrigin + layout.WorldPositionOf(cell);
    }
}
