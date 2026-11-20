using System;
using System.Collections.Generic;
using NeonVoid.Combat.AI;
using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 절차 맵 생성 against docs/01 section 6's 생성 시 반드시 검증.
    /// <para>
    /// The document's six checks split into three kinds, and all three are here. One map can be
    /// asked about reachability, placement and separation, so those are driven over a sweep of
    /// seeds rather than one. Reproducibility is a property of two runs, so it is a test and
    /// cannot be a validator. And the checks themselves are driven from hand-built broken maps -
    /// a validator nothing has ever seen fail is a validator nobody knows works.
    /// </para>
    /// </summary>
    public sealed class AreaGenerationTests
    {
        /// <summary>
        /// Enough seeds to be a sweep rather than an anecdote. docs/01 section 5 drafts
        /// 시드 100개 for the connectivity check; this doubles it and still runs in well under a
        /// second, because the generator touches no Unity object.
        /// </summary>
        private const int SweptSeeds = 200;

        private static AreaSpec Spec(
            uint seed, int objectives = 3, int sizePercent = AreaSpec.NominalSizeModifierPercent) =>
            new AreaSpec(HardwareId.Cpu, seed, objectives, sizePercent);

        private static AreaLayout Generate(in AreaSpec spec)
        {
            Assert.IsTrue(AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure), failure);
            return layout;
        }

        // --- the sweep ---------------------------------------------------------------------

        [Test]
        public void EverySweptSeedBuildsAMapThatPassesEveryCheck()
        {
            var attemptsUsed = new Dictionary<int, int>();

            for (uint seed = 1; seed <= SweptSeeds; ++seed)
            {
                AreaSpec spec = Spec(seed);
                Assert.IsTrue(
                    AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure),
                    $"seed {seed}: {failure}");

                AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);
                Assert.IsTrue(verdict.IsValid, $"seed {seed}: {verdict.Reason}");

                attemptsUsed.TryGetValue(layout.AttemptsUsed, out int count);
                attemptsUsed[layout.AttemptsUsed] = count + 1;
            }

            // Not an assertion about luck - a guard against the generator quietly needing the
            // fallback path for most seeds, which would mean the placement rules and the
            // validation thresholds disagree with each other.
            attemptsUsed.TryGetValue(1, out int firstTry);
            Assert.Greater(
                firstTry, SweptSeeds * 9 / 10,
                $"too many seeds needed a replacement: {Describe(attemptsUsed)}");
        }

        [Test]
        public void EverySweptSeedLeavesEverythingWalkableAndReachable()
        {
            // The same claim the validator makes, asserted independently of it. A validator and
            // the only test of its subject agreeing proves less than it looks like.
            for (uint seed = 1; seed <= SweptSeeds; ++seed)
            {
                AreaLayout layout = Generate(Spec(seed));

                foreach (AreaCell cell in layout.AllSpawns)
                {
                    Assert.IsTrue(layout.Grid.IsWalkable(cell), $"seed {seed}: {cell} is inside a wall");
                    Assert.IsTrue(
                        layout.Grid.PathExists(layout.Start, cell),
                        $"seed {seed}: {cell} cannot be walked to from {layout.Start}");
                }

                // And back again: a one-way floor would be a bug the forward walk cannot see.
                Assert.IsTrue(layout.Grid.PathExists(layout.Boss, layout.Start), $"seed {seed}: no way back");
            }
        }

        [Test]
        public void EverySweptSeedPlacesTheObjectivesTheSpecAskedFor()
        {
            // The area runtime takes its objective count from the devices placed, so a map that
            // placed the wrong number would be an area the flow and the room disagree about.
            for (uint seed = 1; seed <= SweptSeeds; ++seed)
            {
                for (int objectives = AreaSpec.DraftMinObjectives; objectives <= AreaSpec.DraftMaxObjectives; ++objectives)
                {
                    AreaLayout layout = Generate(Spec(seed, objectives));

                    Assert.AreEqual(objectives, layout.Objectives.Count, $"seed {seed}, {objectives} objectives");
                    Assert.AreEqual(objectives, layout.Spec.ObjectiveCount);
                }
            }
        }

        [Test]
        public void EverySweptSeedWorksAtEveryDraftedMapSize()
        {
            // 맵 크기 축소·확대 is an environment, so the whole band has to generate - not just the
            // nominal size everything was tuned at.
            int[] sizes =
            {
                AreaSpec.DraftMinSizeModifierPercent, 80, AreaSpec.NominalSizeModifierPercent, 130,
                AreaSpec.DraftMaxSizeModifierPercent,
            };

            for (uint seed = 1; seed <= 60; ++seed)
            {
                for (int i = 0; i < sizes.Length; ++i)
                {
                    AreaSpec spec = Spec(seed, 3, sizes[i]);
                    Assert.IsTrue(
                        AreaGenerator.TryGenerate(spec, out AreaLayout layout, out string failure),
                        $"seed {seed} at {sizes[i]}%: {failure}");
                    Assert.IsTrue(AreaLayoutValidator.Validate(layout).IsValid);
                }
            }
        }

        [Test]
        public void ALargerAreaHasMoreFloorThanASmallerOne()
        {
            // The size modifier has to do something. A modifier that is read and then ignored is
            // the quiet failure here, because every map would still validate.
            int small = 0;
            int large = 0;

            for (uint seed = 1; seed <= 40; ++seed)
            {
                small += Generate(Spec(seed, 3, AreaSpec.DraftMinSizeModifierPercent)).Grid.WalkableCount;
                large += Generate(Spec(seed, 3, AreaSpec.DraftMaxSizeModifierPercent)).Grid.WalkableCount;
            }

            Assert.Greater(large, small, "a 160% area is not bigger than a 60% one");
        }

        // --- 적 배치 -----------------------------------------------------------------------

        [Test]
        public void EverySweptSeedPlacesTheEnemyBudgetItWasGiven()
        {
            // GDD v1 section 5.4 budgets 48 일반 and 3 정예 per area. A map quietly short of them
            // is an area whose XP table cannot be reached, and nothing else would say so.
            for (uint seed = 1; seed <= SweptSeeds; ++seed)
            {
                AreaSpec spec = Spec(seed);
                AreaLayout layout = Generate(spec);

                Assert.AreEqual(
                    AreaGenerator.NormalEnemiesFor(spec) + AreaGenerator.ElitesFor(spec),
                    layout.Enemies.Count, $"seed {seed}: wrong number of enemies");
                Assert.AreEqual(AreaGenerator.ElitesFor(spec), layout.EliteCount, $"seed {seed}: wrong elites");
            }
        }

        [Test]
        public void TheEnemyBudgetIsTheDocumentsCountAtNominalSize()
        {
            AreaSpec spec = Spec(1u);

            Assert.AreEqual(48, AreaGenerator.NormalEnemiesFor(spec));
            Assert.AreEqual(3, AreaGenerator.ElitesFor(spec));
            Assert.Less(
                AreaGenerator.NormalEnemiesFor(spec.AtSize(AreaSpec.DraftMinSizeModifierPercent)), 48,
                "a 60% area holds as many enemies as a full one");
        }

        [Test]
        public void NoEnemyStandsInTheEntranceRoomOrTheBossRoom()
        {
            // The entrance is 초반 화면 밖 즉시 피해 금지, and a dozen chasers in the boss's room
            // would make docs/01 section 3's telegraphs unreadable.
            for (uint seed = 1; seed <= 60; ++seed)
            {
                AreaLayout layout = Generate(Spec(seed));
                AreaRoom entrance = layout.Rooms[0];

                int bossRoom = -1;
                for (int i = 0; i < layout.Rooms.Count; ++i)
                {
                    if (layout.Rooms[i].Contains(layout.Boss))
                        bossRoom = i;
                }

                Assert.GreaterOrEqual(bossRoom, 0, $"seed {seed}: the boss is in no room");

                for (int i = 0; i < layout.Enemies.Count; ++i)
                {
                    AreaEnemySpawn enemy = layout.Enemies[i];
                    Assert.AreNotEqual(0, enemy.RoomIndex, $"seed {seed}: {enemy} is in the entrance");
                    Assert.AreNotEqual(bossRoom, enemy.RoomIndex, $"seed {seed}: {enemy} is in the boss's room");
                    Assert.IsFalse(entrance.Contains(enemy.Cell), $"seed {seed}: {enemy} stands in the entrance");
                    Assert.IsTrue(
                        layout.Rooms[enemy.RoomIndex].Contains(enemy.Cell),
                        $"seed {seed}: {enemy} is not in the room it claims");
                }
            }
        }

        [Test]
        public void NoEnemyStandsCloseEnoughToHitThePlayerOnArrival()
        {
            // docs/01 section 6 check 2: 초반 화면 밖 즉시 피해 금지, measured in walking steps.
            for (uint seed = 1; seed <= 60; ++seed)
            {
                AreaLayout layout = Generate(Spec(seed));
                int[] field = layout.Grid.DistanceField(layout.Start);

                for (int i = 0; i < layout.Enemies.Count; ++i)
                {
                    int steps = layout.Grid.DistanceAt(field, layout.Enemies[i].Cell);
                    Assert.GreaterOrEqual(
                        steps, AreaLayoutValidator.DraftMinStartToEnemyCells,
                        $"seed {seed}: {layout.Enemies[i]} is {steps} steps from the entrance");
                }
            }
        }

        [Test]
        public void NoTwoEnemiesShareACellWithEachOtherOrWithAnythingElse()
        {
            for (uint seed = 1; seed <= 60; ++seed)
            {
                AreaLayout layout = Generate(Spec(seed));
                var seen = new HashSet<AreaCell>();

                foreach (AreaCell cell in layout.AllSpawns)
                    Assert.IsTrue(seen.Add(cell), $"seed {seed}: two things stand on {cell}");
            }
        }

        [Test]
        public void EachHardwareDrawsFromItsOwnArchetypes()
        {
            // CLAUDE.md: 5개 하드웨어를 한 개의 복붙 맵으로 취급하지 말 것. The same enemies in a
            // different colour is exactly what that forbids, so the mixes must differ.
            var mixes = new HashSet<string>();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
                mixes.Add(string.Join(",", AreaGenerator.DraftArchetypeMixFor(HardwareIds.All[i])));

            Assert.AreEqual(HardwareIds.All.Length, mixes.Count, "two hardware areas draw from the same mix");
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaGenerator.DraftArchetypeMixFor(HardwareId.None));
        }

        [Test]
        public void EveryPlacedEnemyComesFromItsHardwaresMix()
        {
            for (int h = 0; h < HardwareIds.All.Length; ++h)
            {
                HardwareId hardware = HardwareIds.All[h];
                var allowed = new HashSet<EnemyArchetypeId>(AreaGenerator.DraftArchetypeMixFor(hardware));

                for (uint seed = 1; seed <= 20; ++seed)
                {
                    var spec = new AreaSpec(hardware, seed, 3);
                    AreaLayout layout = Generate(spec);

                    for (int i = 0; i < layout.Enemies.Count; ++i)
                    {
                        Assert.IsTrue(
                            allowed.Contains(layout.Enemies[i].Archetype),
                            $"{hardware} seed {seed}: {layout.Enemies[i]} is not in its mix");
                    }
                }
            }
        }

        [Test]
        public void TheCpuDrawsMostlyChasersAsItsDocumentSays()
        {
            // docs/01 section 2, CPU 적 성향: 다수 접근형·지원형.
            var counts = new Dictionary<EnemyArchetypeId, int>();
            for (uint seed = 1; seed <= 30; ++seed)
            {
                AreaLayout layout = Generate(Spec(seed));
                for (int i = 0; i < layout.Enemies.Count; ++i)
                {
                    counts.TryGetValue(layout.Enemies[i].Archetype, out int count);
                    counts[layout.Enemies[i].Archetype] = count + 1;
                }
            }

            counts.TryGetValue(EnemyArchetypeId.Chaser, out int chasers);
            counts.TryGetValue(EnemyArchetypeId.Support, out int supports);

            Assert.Greater(chasers, supports * 2, "the CPU is not 다수 접근형");
            Assert.Greater(supports, 0, "the CPU has no 지원형 at all");
            Assert.IsFalse(counts.ContainsKey(EnemyArchetypeId.Ranged), "a ranged enemy appeared in the CPU");
        }

        // --- check 4: reproducibility ------------------------------------------------------

        [Test]
        public void TheSameKeyBuildsTheSameMapDownToEveryCell()
        {
            // [확정-shaped requirement] docs/01 section 6 check 4: 동일 (seed, hardwareId,
            // generatorVersion)에서 게임플레이 지형·목표 위치 재현.
            for (uint seed = 1; seed <= 40; ++seed)
            {
                AreaSpec spec = Spec(seed);
                AreaLayout first = Generate(spec);
                AreaLayout second = Generate(spec);

                Assert.AreEqual(first.EffectiveSeed, second.EffectiveSeed, $"seed {seed}: a different seed won");
                Assert.AreEqual(first.AttemptsUsed, second.AttemptsUsed);
                Assert.AreEqual(first.Start, second.Start);
                Assert.AreEqual(first.Boss, second.Boss);
                Assert.AreEqual(first.Purification, second.Purification);
                Assert.AreEqual(first.Extraction, second.Extraction);
                Assert.AreEqual(first.Objectives, second.Objectives);
                Assert.AreEqual(first.Enemies.Count, second.Enemies.Count);
                for (int i = 0; i < first.Enemies.Count; ++i)
                {
                    Assert.AreEqual(first.Enemies[i].Cell, second.Enemies[i].Cell, $"seed {seed}: enemy {i} moved");
                    Assert.AreEqual(first.Enemies[i].Archetype, second.Enemies[i].Archetype);
                    Assert.AreEqual(first.Enemies[i].IsElite, second.Enemies[i].IsElite);
                    Assert.AreEqual(first.Enemies[i].RoomIndex, second.Enemies[i].RoomIndex);
                }

                Assert.AreEqual(first.Grid.Width, second.Grid.Width);
                Assert.AreEqual(first.Grid.WalkableCount, second.Grid.WalkableCount);

                for (int y = 0; y < first.Grid.Height; ++y)
                {
                    for (int x = 0; x < first.Grid.Width; ++x)
                    {
                        var cell = new AreaCell(x, y);
                        Assert.AreEqual(
                            first.Grid.IsWalkable(cell), second.Grid.IsWalkable(cell),
                            $"seed {seed}: terrain differs at {cell}");
                    }
                }
            }
        }

        [Test]
        public void DifferentSeedsBuildDifferentMaps()
        {
            // The other half of determinism. A generator that ignored its seed would pass the
            // reproducibility test perfectly.
            var seenBossCells = new HashSet<AreaCell>();
            var seenFloorSizes = new HashSet<int>();

            for (uint seed = 1; seed <= 40; ++seed)
            {
                AreaLayout layout = Generate(Spec(seed));
                seenBossCells.Add(layout.Boss);
                seenFloorSizes.Add(layout.Grid.WalkableCount);
            }

            Assert.Greater(seenBossCells.Count, 20, "forty seeds produced too few distinct boss positions");
            Assert.Greater(seenFloorSizes.Count, 10, "forty seeds produced too few distinct floor sizes");
        }

        [Test]
        public void TheGenerationKeySeparatesEveryThingItNames()
        {
            var keys = new HashSet<string>
            {
                Spec(1).GenerationKey,
                Spec(2).GenerationKey,
                Spec(1, 2).GenerationKey,
                Spec(1, 3, 120).GenerationKey,
                new AreaSpec(HardwareId.Gpu, 1, 3).GenerationKey,
                new AreaSpec(HardwareId.Cpu, 1, 3, 100, generatorVersion: 2).GenerationKey,
            };

            Assert.AreEqual(6, keys.Count, "two different specs share a generation key");
        }

        // --- check 5: the recorded replacement seed ----------------------------------------

        [Test]
        public void AReplacementSeedIsDerivedAndRecordedRatherThanRandom()
        {
            // docs/01 section 6 check 5: 기록된 대체 시드로 재생성. Derived, so the whole sequence a
            // spec tries is reproducible - a map that needed three attempts has to be rebuildable
            // from its key alone.
            uint first = AreaGenerator.FallbackSeed(12345u, 2);
            uint again = AreaGenerator.FallbackSeed(12345u, 2);
            uint third = AreaGenerator.FallbackSeed(12345u, 3);

            Assert.AreEqual(first, again, "the replacement seed is not reproducible");
            Assert.AreNotEqual(first, third, "two attempts share a replacement seed");
            Assert.AreNotEqual(0u, first, "a zero seed would collide with another attempt's");
            Assert.AreNotEqual(12345u, first);
        }

        [Test]
        public void TheFirstAttemptHasNoReplacementSeed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaGenerator.FallbackSeed(1u, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AreaGenerator.FallbackSeed(1u, 0));
        }

        [Test]
        public void TheReceiptCarriesTheVersionTheKeyAndTheSeedThatActuallyBuiltIt()
        {
            AreaLayout layout = Generate(Spec(7u));
            string receipt = layout.ReceiptLine();

            StringAssert.Contains($"gen=v{AreaSpec.CurrentGeneratorVersion}", receipt);
            StringAssert.Contains(layout.Spec.GenerationKey, receipt);
            StringAssert.Contains($"effectiveSeed={layout.EffectiveSeed}", receipt);
            StringAssert.Contains($"attempts={layout.AttemptsUsed}", receipt);
        }

        // --- the spec ----------------------------------------------------------------------

        [Test]
        public void ASpecThatCouldNotMakeAPlayableAreaIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AreaSpec(HardwareId.None, 1u, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AreaSpec(HardwareId.Cpu, 1u, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AreaSpec(HardwareId.Cpu, 1u, AreaSpec.MaxObjectives + 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaSpec(HardwareId.Cpu, 1u, 3, AreaSpec.DraftMinSizeModifierPercent - 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaSpec(HardwareId.Cpu, 1u, 3, AreaSpec.DraftMaxSizeModifierPercent + 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaSpec(HardwareId.Cpu, 1u, 3, 100, generatorVersion: 0));
        }

        [Test]
        public void ASeedOfZeroStillBuildsAMap()
        {
            // A caller may legitimately hand over 0, and an xorshift started at 0 stays there.
            AreaLayout layout = Generate(Spec(0u));

            Assert.IsTrue(AreaLayoutValidator.Validate(layout).IsValid);
            Assert.Greater(layout.Grid.WalkableCount, 0);
        }

        [Test]
        public void TheRoomCountAlwaysLeavesARoomForEveryThingThatNeedsOne()
        {
            // The entrance, the boss and one room per objective. A count that did not leave room
            // for them would put a device in the entrance or in the boss's lap.
            for (int objectives = 1; objectives <= AreaSpec.MaxObjectives; ++objectives)
            {
                AreaSpec small = Spec(1u, objectives, AreaSpec.DraftMinSizeModifierPercent);
                Assert.GreaterOrEqual(
                    AreaGenerator.RoomCountFor(small), objectives + 2,
                    $"{objectives} objectives at the smallest size");
            }
        }

        // --- the grid ----------------------------------------------------------------------

        [Test]
        public void TheWalkIsNeverDiagonal()
        {
            // A diagonal step between two blocked cells cuts the corner between them, which a
            // collider cannot do - so allowing one would make the path check pass on maps the
            // player cannot cross.
            var grid = new AreaGrid(3, 3);
            grid.Carve(new AreaCell(0, 0));
            grid.Carve(new AreaCell(1, 1));

            Assert.IsFalse(grid.PathExists(new AreaCell(0, 0), new AreaCell(1, 1)));

            grid.Carve(new AreaCell(1, 0));
            Assert.IsTrue(grid.PathExists(new AreaCell(0, 0), new AreaCell(1, 1)));
        }

        [Test]
        public void ACorridorJoinsTwoRoomsWhicheverLegIsCarvedFirst()
        {
            for (int pass = 0; pass < 2; ++pass)
            {
                var grid = new AreaGrid(16, 16);
                var left = new AreaRoom(1, 1, 5, 5);
                var right = new AreaRoom(9, 9, 5, 5);
                grid.CarveRoom(left);
                grid.CarveRoom(right);

                Assert.IsFalse(grid.PathExists(left.Centre, right.Centre), "the rooms were already joined");

                grid.CarveCorridor(left.Centre, right.Centre, horizontalFirst: pass == 0);

                Assert.IsTrue(grid.PathExists(left.Centre, right.Centre), $"pass {pass} left them apart");
            }
        }

        [Test]
        public void TheFloorIsCountedOnceHoweverOftenItIsCarved()
        {
            var grid = new AreaGrid(8, 8);
            var room = new AreaRoom(1, 1, 3, 3);
            grid.CarveRoom(room);
            Assert.AreEqual(9, grid.WalkableCount);

            grid.CarveRoom(room);
            Assert.AreEqual(9, grid.WalkableCount, "carving the same room twice counted it twice");
        }

        [Test]
        public void RoomsWithAWallBetweenThemAreNotTooClose()
        {
            var room = new AreaRoom(0, 0, 5, 5);

            Assert.IsTrue(room.TooCloseTo(new AreaRoom(5, 0, 5, 5), margin: 1), "touching rooms would merge");
            Assert.IsFalse(room.TooCloseTo(new AreaRoom(6, 0, 5, 5), margin: 1));
            Assert.IsTrue(room.TooCloseTo(new AreaRoom(2, 2, 5, 5), margin: 1), "overlapping rooms were allowed");
        }

        [Test]
        public void AnUnreachableCellSaysSoRatherThanLookingClose()
        {
            var grid = new AreaGrid(8, 8);
            grid.Carve(new AreaCell(0, 0));
            grid.Carve(new AreaCell(7, 7));

            int[] field = grid.DistanceField(new AreaCell(0, 0));

            Assert.AreEqual(0, grid.DistanceAt(field, new AreaCell(0, 0)));
            Assert.AreEqual(AreaGrid.Unreachable, grid.DistanceAt(field, new AreaCell(7, 7)));
            Assert.AreEqual(AreaGrid.Unreachable, grid.DistanceAt(field, new AreaCell(3, 3)), "a wall read as reachable");
        }

        [Test]
        public void ADistanceFieldFromAnotherGridIsRefused()
        {
            var small = new AreaGrid(4, 4);
            var large = new AreaGrid(8, 8);
            small.Carve(new AreaCell(0, 0));

            int[] field = small.DistanceField(new AreaCell(0, 0));

            Assert.Throws<ArgumentException>(() => large.DistanceAt(field, new AreaCell(0, 0)));
        }

        // --- the validator, driven from broken maps ----------------------------------------

        /// <summary>
        /// Two rooms joined by a corridor, with everything placed legally. Each test below breaks
        /// exactly one thing about it.
        /// </summary>
        private static AreaLayout HandBuilt(
            float cellSize = AreaGenerator.DraftCellSizeUnits,
            int objectiveCount = 1,
            AreaCell? startOverride = null,
            AreaCell? objectiveOverride = null,
            AreaCell? bossOverride = null)
        {
            // Rooms big enough to clear the drafted floor band: the band is tuned to what the
            // generator builds, and a fixture small enough to trip it would fail every test
            // below for the wrong reason.
            var grid = new AreaGrid(44, 14);
            var entrance = new AreaRoom(1, 1, 8, 8);
            var middle = new AreaRoom(16, 1, 8, 8);
            var far = new AreaRoom(31, 1, 9, 9);
            grid.CarveRoom(entrance);
            grid.CarveRoom(middle);
            grid.CarveRoom(far);
            grid.CarveCorridor(entrance.Centre, middle.Centre, horizontalFirst: true);
            grid.CarveCorridor(middle.Centre, far.Centre, horizontalFirst: true);

            var spec = new AreaSpec(HardwareId.Cpu, 1u, objectiveCount);
            return new AreaLayout(
                spec,
                spec.Seed,
                attemptsUsed: 1,
                cellSizeUnits: cellSize,
                grid: grid,
                rooms: new List<AreaRoom> { entrance, middle, far },
                start: startOverride ?? entrance.Centre,
                objectives: new List<AreaCell> { objectiveOverride ?? middle.Centre },
                boss: bossOverride ?? far.Centre,
                purification: new AreaCell(far.MinX, far.MinY),
                extraction: new AreaCell(far.MaxX, far.MaxY));
        }

        [Test]
        public void TheHandBuiltReferenceMapPasses()
        {
            // Without this, every test below could be passing for the wrong reason.
            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(HandBuilt());

            Assert.IsTrue(verdict.IsValid, verdict.Reason);
        }

        [Test]
        public void ASpawnInsideAWallIsRefused()
        {
            // docs/01 section 6 check 3.
            AreaLayout layout = HandBuilt(bossOverride: new AreaCell(0, 11));

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("inside a wall", verdict.Reason);
        }

        [Test]
        public void ASpawnOutsideTheGridIsRefused()
        {
            AreaLayout layout = HandBuilt(bossOverride: new AreaCell(400, 400));

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("outside the grid", verdict.Reason);
        }

        [Test]
        public void AnIslandNobodyCanWalkToIsRefused()
        {
            // docs/01 section 6 check 1. The island is walkable, so only the walk catches it.
            var grid = new AreaGrid(20, 10);
            var entrance = new AreaRoom(1, 1, 6, 6);
            var island = new AreaRoom(12, 1, 6, 6);
            grid.CarveRoom(entrance);
            grid.CarveRoom(island);

            var spec = new AreaSpec(HardwareId.Cpu, 1u, 1);
            var layout = new AreaLayout(
                spec, spec.Seed, 1, AreaGenerator.DraftCellSizeUnits, grid,
                new List<AreaRoom> { entrance, island },
                entrance.Centre,
                new List<AreaCell> { new AreaCell(entrance.MinX, entrance.MinY) },
                island.Centre,
                new AreaCell(island.MinX, island.MinY),
                new AreaCell(island.MaxX, island.MaxY));

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("cannot be walked to", verdict.Reason);
        }

        [Test]
        public void TwoThingsOnOneCellAreRefused()
        {
            // docs/01 section 6 check 2: 안전 분리.
            AreaLayout layout = HandBuilt(objectiveOverride: new AreaCell(31, 1));

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("two things were placed", verdict.Reason);
        }

        [Test]
        public void ABossInTheEntrancesFaceIsRefused()
        {
            // 초반 화면 밖 즉시 피해 금지: measured in walking steps, because a boss four cells
            // away through a wall is not four cells away.
            AreaLayout layout = HandBuilt(bossOverride: new AreaCell(2, 2));

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("closer than the drafted", verdict.Reason);
        }

        [Test]
        public void ACellTooSmallForThePlayerIsRefused()
        {
            // The premise of the whole path argument: a walkable cell has to be a cell the player
            // fits inside, or the check is about an abstract grid instead of about the player.
            AreaLayout layout = HandBuilt(cellSize: AreaLayoutValidator.PrototypePlayerRadiusUnits);

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("the player fits", verdict.Reason);
        }

        [Test]
        public void AMapHoldingFewerObjectivesThanTheSpecAskedForIsRefused()
        {
            AreaLayout layout = HandBuilt(objectiveCount: 2);

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("asks for 2 objectives", verdict.Reason);
        }

        [Test]
        public void AFloorOutsideTheDraftedBandIsRefused()
        {
            // docs/01 section 6 check 5, as the proxy it is: a minute is something only play
            // measures, so what is bounded is the floor.
            var grid = new AreaGrid(60, 60);
            var entrance = new AreaRoom(1, 1, 25, 25);
            var far = new AreaRoom(30, 30, 25, 25);
            grid.CarveRoom(entrance);
            grid.CarveRoom(far);
            grid.CarveCorridor(entrance.Centre, far.Centre, horizontalFirst: true);

            var spec = new AreaSpec(HardwareId.Cpu, 1u, 1);
            var layout = new AreaLayout(
                spec, spec.Seed, 1, AreaGenerator.DraftCellSizeUnits, grid,
                new List<AreaRoom> { entrance, far },
                entrance.Centre,
                new List<AreaCell> { new AreaCell(entrance.MinX, entrance.MinY) },
                far.Centre,
                new AreaCell(far.MinX, far.MinY),
                new AreaCell(far.MaxX, far.MaxY));

            AreaLayoutVerdict verdict = AreaLayoutValidator.Validate(layout);

            Assert.IsFalse(verdict.IsValid);
            StringAssert.Contains("outside the drafted", verdict.Reason);
        }

        [Test]
        public void ALayoutWithoutAGridCannotBeBuilt()
        {
            var spec = new AreaSpec(HardwareId.Cpu, 1u, 1);

            Assert.Throws<ArgumentNullException>(() => new AreaLayout(
                spec, 1u, 1, 2f, null, new List<AreaRoom>(), default, new List<AreaCell>(), default, default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AreaLayout(
                spec, 1u, 0, 2f, new AreaGrid(4, 4), new List<AreaRoom>(), default, new List<AreaCell>(),
                default, default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AreaLayout(
                spec, 1u, 1, 0f, new AreaGrid(4, 4), new List<AreaRoom>(), default, new List<AreaCell>(),
                default, default, default));
        }

        // --- world positions ---------------------------------------------------------------

        [Test]
        public void ACellsWorldPositionIsItsCentreOnTheFloor()
        {
            // [확정] docs/01 section 1: 세계 좌표 XZ, 주요 이동은 평면. The generator never picks a
            // height, and a spawn on a cell edge would be a spawn half inside a wall.
            AreaLayout layout = Generate(Spec(1u));
            float cell = layout.CellSizeUnits;

            UnityEngine.Vector3 origin = layout.WorldPositionOf(new AreaCell(0, 0));
            UnityEngine.Vector3 next = layout.WorldPositionOf(new AreaCell(1, 2));

            Assert.AreEqual(cell * 0.5f, origin.x, 1e-4f);
            Assert.AreEqual(0f, origin.y, 1e-4f);
            Assert.AreEqual(cell * 0.5f, origin.z, 1e-4f);
            Assert.AreEqual(cell * 1.5f, next.x, 1e-4f);
            Assert.AreEqual(cell * 2.5f, next.z, 1e-4f);
        }

        [Test]
        public void TheCellIsBigEnoughForThePlayerToStandIn()
        {
            Assert.GreaterOrEqual(
                AreaGenerator.DraftCellSizeUnits, AreaLayoutValidator.PrototypePlayerRadiusUnits * 2f,
                "the drafted cell is narrower than the player");
        }

        private static string Describe(Dictionary<int, int> attemptsUsed)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<int, int> pair in attemptsUsed)
                parts.Add($"{pair.Value} seed(s) took {pair.Key} attempt(s)");

            return string.Join(", ", parts.ToArray());
        }
    }
}
