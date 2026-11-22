using System;
using System.Collections.Generic;
using NeonVoid.Combat.AI;
using NeonVoid.Core;
using NeonVoid.Progression;

namespace NeonVoid.Missions.Generation
{
    /// <summary>
    /// Builds one hardware area: rooms, corridors, and where everything stands.
    /// <para>
    /// docs/01 section 6's 초기 구현 in order - 모듈형 방/복도 → 목표 지점·보스·철수 포인트 배치
    /// → 동선 검증 → 결정론 시드 스냅샷. The 3D prefabs, the lighting and the walls are the stage
    /// after this one, on a graph that has already passed, and none of it is here.
    /// </para>
    /// <para>
    /// <b>Connected by construction, and checked anyway.</b> The rooms are joined into a spanning
    /// tree, so there is no way for the carving to leave an island - and
    /// <see cref="AreaLayoutValidator"/> still walks the floor from the entrance to every spawn.
    /// The point of check 1 is not to doubt the corridor code; it is that the <i>placement</i>
    /// could put something in a wall, and a map that cannot be finished must never be handed to a
    /// player.
    /// </para>
    /// <para>
    /// <b>Seeds this generator cannot build.</b> docs/01 section 6 check 5 allows 불가능한 시드 to
    /// be replaced by a 기록된 대체 시드. Rooms are placed by rejection, so a seed can run out of
    /// space before every room is down; rather than relaxing the margins - which would let two
    /// rooms merge and make the path check a statement about a shape nobody designed - the whole
    /// attempt is thrown away and a derived seed is tried. The seed that won and the number of
    /// attempts travel on the layout, because a map rebuilt from the asked-for seed would not be
    /// the map that was played.
    /// </para>
    /// <para>
    /// The seed is expected to be the run's <see cref="RandomStream.Mission"/> seed. [확정]
    /// docs/06 section 4 keeps map generation, combat and rewards in separate streams so that
    /// drawing one more card cannot change what the next map looks like; this generator consumes
    /// its stream and no other.
    /// </para>
    /// Pure C#: no Unity types, no scene, no assets.
    /// </summary>
    public static class AreaGenerator
    {
        /// <summary>
        /// [초안] world units per cell. Chosen to clear the player's diameter with room to spare -
        /// <see cref="AreaLayoutValidator.CheckPlayerFits"/> is what holds the rule.
        /// </summary>
        public const float DraftCellSizeUnits = 2f;

        /// <summary>[초안] rooms in a nominal-size area.</summary>
        public const int DraftNominalRoomCount = 7;

        /// <summary>A hard ceiling on rooms, so a large 확대 cannot grow without bound.</summary>
        public const int MaxRoomCount = 12;

        /// <summary>[초안] the smallest room side, in cells.</summary>
        public const int DraftMinRoomSideCells = 5;

        /// <summary>[초안] the largest room side, in cells.</summary>
        public const int DraftMaxRoomSideCells = 8;

        /// <summary>
        /// Cells of wall kept between rooms. One is enough to stop two rooms merging, which is
        /// what keeps every connection a corridor the path check can see.
        /// </summary>
        public const int RoomMarginCells = 1;

        /// <summary>Seeds tried before the generator gives up on a spec.</summary>
        public const int MaxAttempts = 12;

        /// <summary>
        /// Normal enemies this area is budgeted for. GDD v1 section 5.4 counts 일반 적 처치
        /// 예상 횟수 48 at nominal size, scaled with the map so a 60% area is not a crowd.
        /// </summary>
        public static int NormalEnemiesFor(in AreaSpec spec) =>
            AreaLayoutValidator.Scale(
                AreaLayoutValidator.Scale(
                    MissionXpBudget.DraftNormalKillsPerArea, spec.SizeModifierPercent),
                spec.EnemyPercent);

        /// <summary>
        /// 정예 this area is budgeted for. GDD v1 section 5.4 counts 3.
        /// <para>
        /// 스택오버플로우 raises this with the rest. 적 증가 does not say 일반 적만, and an area
        /// that doubled its fodder while keeping three elites would be easier per enemy rather
        /// than harder.
        /// </para>
        /// </summary>
        public static int ElitesFor(in AreaSpec spec) =>
            AreaLayoutValidator.Scale(
                AreaLayoutValidator.Scale(
                    MissionXpBudget.DraftElitesPerArea, spec.SizeModifierPercent),
                spec.EnemyPercent);

        /// <summary>
        /// [초안] the archetypes a hardware area draws from, repeated for weight.
        /// <para>
        /// Straight from docs/01 section 2's 적 성향 column, which is the whole reason this is a
        /// per-hardware table and not one list: CLAUDE.md forbids treating the five areas as one
        /// copy-pasted map, and "the same enemies in a different colour" is exactly what that
        /// means. Only the four implemented archetypes appear - 분열, 복제, 속박 and 중장갑 are
        /// named by the document and do not exist, so they are absent rather than faked with a
        /// chaser.
        /// </para>
        /// </summary>
        public static EnemyArchetypeId[] DraftArchetypeMixFor(HardwareId hardware)
        {
            switch (hardware)
            {
                // 다수 접근형·지원형.
                case HardwareId.Cpu:
                    return new[]
                    {
                        EnemyArchetypeId.Chaser, EnemyArchetypeId.Chaser, EnemyArchetypeId.Chaser,
                        EnemyArchetypeId.Support,
                    };

                // 원거리·탄막. 분열 does not exist.
                case HardwareId.Gpu:
                    return new[]
                    {
                        EnemyArchetypeId.Ranged, EnemyArchetypeId.Ranged, EnemyArchetypeId.Ranged,
                        EnemyArchetypeId.Chaser,
                    };

                // 지원형 heavy. 복제·속박 do not exist.
                case HardwareId.Ram:
                    return new[]
                    {
                        EnemyArchetypeId.Support, EnemyArchetypeId.Support,
                        EnemyArchetypeId.Chaser, EnemyArchetypeId.Ranged,
                    };

                // 돌격·고속 원거리.
                case HardwareId.Ssd:
                    return new[]
                    {
                        EnemyArchetypeId.Charger, EnemyArchetypeId.Charger, EnemyArchetypeId.Charger,
                        EnemyArchetypeId.Ranged,
                    };

                // 지역 점유. 중장갑 does not exist, so the elite multipliers carry that weight.
                case HardwareId.Hdd:
                    return new[]
                    {
                        EnemyArchetypeId.Chaser, EnemyArchetypeId.Chaser,
                        EnemyArchetypeId.Charger, EnemyArchetypeId.Support,
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(hardware), hardware, "Not a hardware area.");
            }
        }

        /// <summary>Placements tried for one room before the attempt is abandoned.</summary>
        private const int PlacementTriesPerRoom = 64;

        /// <summary>
        /// Builds and validates an area, replacing a seed it cannot build with a recorded
        /// derived one.
        /// </summary>
        /// <param name="failure">
        /// Why every attempt failed, when none succeeded. Empty on success.
        /// </param>
        public static bool TryGenerate(in AreaSpec spec, out AreaLayout layout, out string failure)
        {
            var refusals = new List<string>(MaxAttempts);

            for (int attempt = 1; attempt <= MaxAttempts; ++attempt)
            {
                uint seed = attempt == 1 ? spec.Seed : FallbackSeed(spec.Seed, attempt);
                AreaLayout candidate = BuildOnce(spec, seed, attempt, out string buildRefusal);

                if (candidate == null)
                {
                    refusals.Add($"attempt {attempt} (seed {seed}): {buildRefusal}");
                    continue;
                }

                AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(candidate);
                if (verdict.IsValid)
                {
                    layout = candidate;
                    failure = string.Empty;
                    return true;
                }

                refusals.Add($"attempt {attempt} (seed {seed}): {verdict.Reason}");
            }

            layout = null;
            failure = $"{spec.GenerationKey} could not be built in {MaxAttempts} attempts: " +
                      string.Join(" | ", refusals.ToArray());
            return false;
        }

        /// <summary>
        /// The replacement seed for an attempt. Derived rather than random, so the whole sequence
        /// of seeds a spec tries is itself reproducible - otherwise a map that needed three
        /// attempts could never be rebuilt from its key.
        /// </summary>
        public static uint FallbackSeed(uint original, int attempt)
        {
            if (attempt < 2)
                throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "The first attempt uses the spec's own seed.");

            unchecked
            {
                uint mixed = original ^ (uint)(attempt * 0x9E3779B9u);
                mixed ^= mixed << 13;
                mixed ^= mixed >> 17;
                mixed ^= mixed << 5;

                // The generator's RNG guards a zero seed, but a zero here would also make two
                // different attempts share a seed if the mix ever landed on it twice.
                return mixed == 0u ? 0x6C078965u : mixed;
            }
        }

        /// <summary>Rooms a spec asks for: the drafted count at its size, with room for its spawns.</summary>
        public static int RoomCountFor(in AreaSpec spec)
        {
            int scaled = AreaLayoutValidator.Scale(DraftNominalRoomCount, spec.SizeModifierPercent);

            // The entrance, the boss and one room per objective, all distinct: an objective
            // sharing the boss's room would be an objective the player trips over on the way in,
            // and the entrance sharing one would put a device in their face on arrival.
            int needed = spec.ObjectiveCount + 2;
            int rooms = scaled < needed ? needed : scaled;
            return rooms > MaxRoomCount ? MaxRoomCount : rooms;
        }

        /// <summary>The square grid side that holds this many rooms with their margins.</summary>
        public static int GridSideFor(int roomCount)
        {
            if (roomCount < 1)
                throw new ArgumentOutOfRangeException(nameof(roomCount), roomCount, "An area has rooms.");

            int perSide = (int)Math.Ceiling(Math.Sqrt(roomCount));
            int side = (perSide * (DraftMaxRoomSideCells + 2)) + 4;

            // A floor small enough to hold its rooms can still be too cramped to put the boss a
            // walk away from the entrance, so there is a lower bound independent of the count.
            return side < 28 ? 28 : side;
        }

        /// <summary>
        /// One attempt at one seed. Returns null with a reason when the seed cannot be laid out;
        /// the result is not validated here.
        /// </summary>
        private static AreaLayout BuildOnce(in AreaSpec spec, uint seed, int attempt, out string refusal)
        {
            var rng = new DeterministicRng(seed);
            int roomCount = RoomCountFor(spec);
            int side = GridSideFor(roomCount);

            List<AreaRoom> rooms = PlaceRooms(rng, roomCount, side);
            if (rooms == null)
            {
                refusal = $"could not place {roomCount} rooms in a {side}x{side} grid without merging them";
                return null;
            }

            var grid = new AreaGrid(side, side);
            for (int i = 0; i < rooms.Count; ++i)
                grid.CarveRoom(rooms[i]);

            ConnectRooms(grid, rooms, rng);

            AreaCell start = rooms[0].Centre;
            int[] field = grid.DistanceField(start);

            int bossRoom = FarthestRoom(grid, rooms, field, skip: 0);
            if (bossRoom < 0)
            {
                refusal = "no room could be reached from the entrance";
                return null;
            }

            List<int> objectiveRooms = ObjectiveRooms(grid, rooms, field, spec.ObjectiveCount, bossRoom);
            if (objectiveRooms == null)
            {
                refusal = $"only {rooms.Count} rooms, too few to hold {spec.ObjectiveCount} objectives apart " +
                          "from the entrance and the boss";
                return null;
            }

            var objectives = new List<AreaCell>(objectiveRooms.Count);
            for (int i = 0; i < objectiveRooms.Count; ++i)
                objectives.Add(rooms[objectiveRooms[i]].Centre);

            AreaRoom boss = rooms[bossRoom];

            // Opposite corners of the boss's room, so the console and the pad are as far from
            // each other and from the boss as that room allows.
            var purification = new AreaCell(boss.MinX, boss.MinY);
            var extraction = new AreaCell(boss.MaxX, boss.MaxY);

            var taken = new HashSet<AreaCell> { start, boss.Centre, purification, extraction };
            for (int i = 0; i < objectives.Count; ++i)
                taken.Add(objectives[i]);

            List<AreaEnemySpawn> enemies = PlaceEnemies(spec, rng, grid, rooms, field, bossRoom, taken);
            List<AreaCell> hazards = PlaceHazards(spec, rng, grid, rooms, bossRoom, taken, enemies);

            refusal = string.Empty;
            return new AreaLayout(
                spec, seed, attempt, DraftCellSizeUnits, grid, rooms, start, objectives,
                boss.Centre, purification, extraction, enemies, hazards);
        }

        /// <summary>
        /// Scatters the area's enemies through every room except the entrance and the boss's.
        /// <para>
        /// <b>Not the entrance room.</b> docs/01 section 6 check 2 forbids 초반 화면 밖 즉시 피해,
        /// and the room the player materialises in is where that would happen. docs/01's CPU
        /// opening - 진입 0~90초: 접근형 적 - is the room <i>after</i> this one, which is where
        /// they are.
        /// </para>
        /// <para>
        /// <b>Not the boss's room either.</b> docs/01 section 3 wants a boss fight with
        /// telegraphs that can be read and dodged; a dozen chasers in the same room would make
        /// every pattern unreadable, and the area's 2분 보스 segment would be a brawl that
        /// happens to contain a boss.
        /// </para>
        /// <para>
        /// Candidates are gathered and then shuffled, rather than filled room by room: a room
        /// too small for its share would otherwise leave the area short of enemies without
        /// anything saying so.
        /// </para>
        /// </summary>
        /// <summary>
        /// Where this area's 환경 상호작용 stand.
        /// <para>
        /// [확정] docs/01 section 6 check 2: 플레이어/적 스폰과 함정 및 각 오브젝트 충돌 반경의
        /// 안전 분리, 초반 화면 밖 즉시 피해 금지. So a hazard is kept a stated distance from the
        /// entrance, every objective, the boss, the console, the pad <b>and every enemy spawn</b>
        /// - a patch on top of a sleeping enemy would kill it for the player, which is the
        /// area clearing itself.
        /// </para>
        /// <para>
        /// The entrance room is skipped outright. A hazard in the room the player wakes up in
        /// is the 초반 즉시 피해 that line forbids, whatever its distance.
        /// </para>
        /// </summary>
        private static List<AreaCell> PlaceHazards(
            in AreaSpec spec,
            DeterministicRng rng,
            AreaGrid grid,
            List<AreaRoom> rooms,
            int bossRoom,
            HashSet<AreaCell> taken,
            List<AreaEnemySpawn> enemies)
        {
            var placed = new List<AreaCell>();
            AreaHazardShape shape = AreaHazards.For(spec.Hardware);
            if (shape.IsNone)
                return placed;

            // Two distances, because the document asks for two different things. [확정] docs/01
            // section 6 check 2 is about 각 오브젝트 충돌 반경의 안전 분리 and 초반 즉시 피해 -
            // the entrance, the objectives, the boss, the console and the pad are fixed things
            // the player must be able to stand at. An enemy spawn is not fixed: the enemy walks
            // off it the moment the room opens, so it needs room rather than a moat. The first
            // version used one distance for both and CPU - whose rooms are the most crowded -
            // ended up with nowhere to put a hazard at all.
            var fixtures = new HashSet<AreaCell>(taken);
            var spawns = new HashSet<AreaCell>();
            for (int i = 0; i < enemies.Count; ++i)
                spawns.Add(enemies[i].Cell);

            // Both distances are derived from the hazard's own reach: a separation counted
            // in cells that ignores how far the patch actually reaches is not a separation.
            int fromFixtures = AreaHazards.CellsFromFixturesFor(shape, DraftCellSizeUnits);
            int apart = AreaHazards.CellsApartFor(shape, DraftCellSizeUnits);

            var candidates = new List<AreaCell>();
            for (int room = 0; room < rooms.Count; ++room)
            {
                if (room == 0 || room == bossRoom)
                    continue;

                AreaRoom area = rooms[room];
                for (int y = area.MinY; y <= area.MaxY; ++y)
                {
                    for (int x = area.MinX; x <= area.MaxX; ++x)
                    {
                        var cell = new AreaCell(x, y);
                        if (!grid.IsWalkable(cell))
                            continue;
                        if (IsWithin(cell, fixtures, fromFixtures))
                            continue;
                        if (IsWithin(cell, spawns, AreaHazards.DraftSafeCellsFromSpawns))
                            continue;

                        candidates.Add(cell);
                    }
                }
            }

            Shuffle(candidates, rng);

            // Checked against what has already been placed, not only against what was there
            // before: two patches a cell apart read as one bigger patch, and the first version
            // only filtered the candidate list once, before any hazard existed.
            for (int i = 0; i < candidates.Count && placed.Count < AreaHazards.DraftHazardsPerArea; ++i)
            {
                if (IsWithin(candidates[i], placed, apart))
                    continue;

                placed.Add(candidates[i]);
            }

            return placed;
        }

        /// <summary>Whether any of <paramref name="of"/> is within <paramref name="cells"/> of it.</summary>
        private static bool IsWithin(AreaCell cell, ICollection<AreaCell> of, int cells)
        {
            foreach (AreaCell other in of)
            {
                int dx = cell.X - other.X;
                int dy = cell.Y - other.Y;
                if (dx < 0) dx = -dx;
                if (dy < 0) dy = -dy;

                if ((dx > dy ? dx : dy) <= cells)
                    return true;
            }

            return false;
        }

        private static List<AreaEnemySpawn> PlaceEnemies(
            in AreaSpec spec,
            DeterministicRng rng,
            AreaGrid grid,
            List<AreaRoom> rooms,
            int[] distancesFromStart,
            int bossRoom,
            HashSet<AreaCell> taken)
        {
            var spawns = new List<AreaEnemySpawn>();
            var candidates = new List<AreaEnemySpawn>();

            for (int room = 0; room < rooms.Count; ++room)
            {
                if (room == 0 || room == bossRoom)
                    continue;

                AreaRoom shape = rooms[room];
                for (int y = shape.MinY; y <= shape.MaxY; ++y)
                {
                    for (int x = shape.MinX; x <= shape.MaxX; ++x)
                    {
                        var cell = new AreaCell(x, y);
                        if (taken.Contains(cell) || !grid.IsWalkable(cell))
                            continue;

                        int steps = grid.DistanceAt(distancesFromStart, cell);
                        if (steps == AreaGrid.Unreachable || steps < AreaLayoutValidator.DraftMinStartToEnemyCells)
                            continue;

                        candidates.Add(new AreaEnemySpawn(cell, EnemyArchetypeId.Chaser, false, room));
                    }
                }
            }

            Shuffle(candidates, rng);

            EnemyArchetypeId[] mix = DraftArchetypeMixFor(spec.Hardware);
            int elites = ElitesFor(spec);
            int wanted = NormalEnemiesFor(spec) + elites;
            int place = wanted < candidates.Count ? wanted : candidates.Count;

            for (int i = 0; i < place; ++i)
            {
                AreaEnemySpawn candidate = candidates[i];

                // The elites are the first drawn, and the draw was already shuffled, so they are
                // spread rather than all standing in the last room.
                bool elite = i < elites;
                EnemyArchetypeId archetype = mix[rng.NextInt(0, mix.Length)];
                spawns.Add(new AreaEnemySpawn(candidate.Cell, archetype, elite, candidate.RoomIndex));
            }

            return spawns;
        }

        /// <summary>Fisher-Yates on the run's own generator, so the scatter is reproducible.</summary>
        private static void Shuffle<T>(List<T> items, DeterministicRng rng)
        {
            for (int i = items.Count - 1; i > 0; --i)
            {
                int j = rng.NextInt(0, i + 1);
                T swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }

        private static List<AreaRoom> PlaceRooms(DeterministicRng rng, int roomCount, int side)
        {
            var rooms = new List<AreaRoom>(roomCount);

            for (int i = 0; i < roomCount; ++i)
            {
                bool placed = false;

                for (int attempt = 0; attempt < PlacementTriesPerRoom && !placed; ++attempt)
                {
                    int width = rng.NextInt(DraftMinRoomSideCells, DraftMaxRoomSideCells + 1);
                    int height = rng.NextInt(DraftMinRoomSideCells, DraftMaxRoomSideCells + 1);

                    // Two cells of border, so a corridor can always run outside a room.
                    int maxX = side - width - 1;
                    int maxY = side - height - 1;
                    if (maxX <= 1 || maxY <= 1)
                        continue;

                    var room = new AreaRoom(rng.NextInt(1, maxX), rng.NextInt(1, maxY), width, height);
                    if (Collides(rooms, room))
                        continue;

                    rooms.Add(room);
                    placed = true;
                }

                if (!placed)
                    return null;
            }

            return rooms;
        }

        private static bool Collides(List<AreaRoom> rooms, in AreaRoom candidate)
        {
            for (int i = 0; i < rooms.Count; ++i)
            {
                if (rooms[i].TooCloseTo(candidate, RoomMarginCells))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Joins every room into one tree: each new room to the nearest room already in it. A
        /// tree is the fewest corridors that leave nothing stranded, and it keeps the walk to the
        /// boss meaningful - a map full of loops would make every room about equally far away.
        /// </summary>
        private static void ConnectRooms(AreaGrid grid, List<AreaRoom> rooms, DeterministicRng rng)
        {
            for (int i = 1; i < rooms.Count; ++i)
            {
                AreaCell from = rooms[i].Centre;
                int nearest = 0;
                int nearestSteps = int.MaxValue;

                for (int j = 0; j < i; ++j)
                {
                    int steps = from.ManhattanTo(rooms[j].Centre);
                    if (steps < nearestSteps)
                    {
                        nearestSteps = steps;
                        nearest = j;
                    }
                }

                grid.CarveCorridor(from, rooms[nearest].Centre, horizontalFirst: (rng.NextUInt() & 1u) == 0u);
            }
        }

        private static int FarthestRoom(AreaGrid grid, List<AreaRoom> rooms, int[] field, int skip)
        {
            int best = -1;
            int bestSteps = -1;

            for (int i = 0; i < rooms.Count; ++i)
            {
                if (i == skip)
                    continue;

                int steps = grid.DistanceAt(field, rooms[i].Centre);
                if (steps > bestSteps)
                {
                    bestSteps = steps;
                    best = i;
                }
            }

            return bestSteps <= 0 ? -1 : best;
        }

        /// <summary>
        /// The rooms the objectives go in: the farthest ones that are neither the entrance nor
        /// the boss's.
        /// <para>
        /// Farthest rather than nearest, so the player crosses the area to clear it instead of
        /// finishing every objective in the first two rooms and walking an empty map to the boss.
        /// </para>
        /// </summary>
        private static List<int> ObjectiveRooms(
            AreaGrid grid, List<AreaRoom> rooms, int[] field, int wanted, int bossRoom)
        {
            var candidates = new List<int>(rooms.Count);
            for (int i = 0; i < rooms.Count; ++i)
            {
                if (i == 0 || i == bossRoom)
                    continue;
                if (grid.DistanceAt(field, rooms[i].Centre) == AreaGrid.Unreachable)
                    continue;

                candidates.Add(i);
            }

            if (candidates.Count < wanted)
                return null;

            candidates.Sort((a, b) =>
                grid.DistanceAt(field, rooms[b].Centre).CompareTo(grid.DistanceAt(field, rooms[a].Centre)));

            return candidates.GetRange(0, wanted);
        }
    }
}
