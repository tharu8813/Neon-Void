using System;
using System.Collections.Generic;
using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Merging a grid of cells into the rectangles a room is built from.
    /// <para>
    /// The property that matters is not how few rectangles come out - it is that they cover
    /// <b>exactly</b> the cells they should. A slab one cell too wide is a wall across a corridor
    /// or a floor over a pit, and neither would show up as anything but a map that cannot be
    /// finished.
    /// </para>
    /// </summary>
    public sealed class AreaTerrainTests
    {
        private static HashSet<AreaCell> CellsIn(List<AreaRoom> slabs)
        {
            var cells = new HashSet<AreaCell>();
            for (int i = 0; i < slabs.Count; ++i)
            {
                AreaRoom slab = slabs[i];
                for (int y = slab.MinY; y <= slab.MaxY; ++y)
                {
                    for (int x = slab.MinX; x <= slab.MaxX; ++x)
                    {
                        Assert.IsTrue(cells.Add(new AreaCell(x, y)), $"slab {i} covers ({x},{y}) twice over");
                    }
                }
            }

            return cells;
        }

        [Test]
        public void OneRoomBecomesOneRectangle()
        {
            var grid = new AreaGrid(10, 10);
            grid.CarveRoom(new AreaRoom(2, 3, 5, 4));

            List<AreaRoom> slabs = AreaTerrain.FloorSlabs(grid);

            Assert.AreEqual(1, slabs.Count, "a rectangle was not merged into one slab");
            Assert.AreEqual(2, slabs[0].MinX);
            Assert.AreEqual(3, slabs[0].MinY);
            Assert.AreEqual(5, slabs[0].Width);
            Assert.AreEqual(4, slabs[0].Height);
        }

        [Test]
        public void TheFloorSlabsCoverEveryWalkableCellAndNoOther()
        {
            var grid = new AreaGrid(24, 16);
            grid.CarveRoom(new AreaRoom(1, 1, 6, 5));
            grid.CarveRoom(new AreaRoom(15, 9, 7, 6));
            grid.CarveCorridor(new AreaCell(4, 3), new AreaCell(18, 12), horizontalFirst: true);

            HashSet<AreaCell> covered = CellsIn(AreaTerrain.FloorSlabs(grid));

            Assert.AreEqual(grid.WalkableCount, covered.Count, "the slabs cover the wrong number of cells");
            for (int y = 0; y < grid.Height; ++y)
            {
                for (int x = 0; x < grid.Width; ++x)
                {
                    var cell = new AreaCell(x, y);
                    Assert.AreEqual(
                        grid.IsWalkable(cell), covered.Contains(cell),
                        $"({x},{y}) is {(grid.IsWalkable(cell) ? "floor" : "wall")} and the slabs disagree");
                }
            }
        }

        [Test]
        public void TheWallSlabsCoverExactlyTheWallThatTouchesFloor()
        {
            var grid = new AreaGrid(14, 14);
            grid.CarveRoom(new AreaRoom(5, 5, 4, 4));

            HashSet<AreaCell> covered = CellsIn(AreaTerrain.WallSlabs(grid));

            for (int y = 0; y < grid.Height; ++y)
            {
                for (int x = 0; x < grid.Width; ++x)
                {
                    var cell = new AreaCell(x, y);
                    bool shouldBeWall = !grid.IsWalkable(cell) && AreaTerrain.TouchesFloor(grid, cell);
                    Assert.AreEqual(shouldBeWall, covered.Contains(cell), $"({x},{y})");
                }
            }

            // A 4x4 room has a 6x6 ring around it, less the room itself: 20 cells.
            Assert.AreEqual(20, covered.Count);
        }

        [Test]
        public void WallDeeperThanOneCellIsNotBuilt()
        {
            // It can never be seen, and a thousand colliders nobody touches is frame time for
            // nothing.
            var grid = new AreaGrid(20, 20);
            grid.CarveRoom(new AreaRoom(9, 9, 2, 2));

            HashSet<AreaCell> covered = CellsIn(AreaTerrain.WallSlabs(grid));

            Assert.IsTrue(covered.Contains(new AreaCell(8, 8)), "the diagonal corner was left open");
            Assert.IsFalse(covered.Contains(new AreaCell(7, 7)), "wall two cells deep was built");
            Assert.IsFalse(covered.Contains(new AreaCell(0, 0)));
        }

        [Test]
        public void ACellTouchingFloorOnlyDiagonallyIsStillWall()
        {
            // Eight neighbours, not four: a corner the player can see past and shoot through
            // would otherwise be a one-cell hole at every inside corner.
            var grid = new AreaGrid(5, 5);
            grid.Carve(new AreaCell(2, 2));

            Assert.IsTrue(AreaTerrain.TouchesFloor(grid, new AreaCell(1, 1)), "a diagonal neighbour was ignored");
            Assert.IsTrue(AreaTerrain.TouchesFloor(grid, new AreaCell(2, 1)));
            Assert.IsFalse(AreaTerrain.TouchesFloor(grid, new AreaCell(0, 0)));
        }

        [Test]
        public void AGridWithNoFloorHasNoSlabsAtAll()
        {
            var grid = new AreaGrid(12, 12);

            Assert.AreEqual(0, AreaTerrain.FloorSlabs(grid).Count);
            Assert.AreEqual(0, AreaTerrain.WallSlabs(grid).Count, "wall was built around nothing");
        }

        [Test]
        public void MergingTurnsAThousandCellsIntoATractableNumberOfBoxes()
        {
            // The reason this code exists. A cube per cell on a nominal area is over a thousand
            // GameObjects and a thousand colliders for a shape that is the same either way.
            Assert.IsTrue(
                AreaGenerator.TryGenerate(new AreaSpec(HardwareId.Cpu, 11u, 3), out AreaLayout layout, out string why),
                why);

            int floor = AreaTerrain.FloorSlabs(layout.Grid).Count;
            int wall = AreaTerrain.WallSlabs(layout.Grid).Count;
            int cells = layout.Grid.Width * layout.Grid.Height;

            Assert.Less(
                floor + wall, cells / 4,
                $"merging saved too little: {floor + wall} slabs for {cells} cells");
            Assert.Greater(floor, 0);
            Assert.Greater(wall, 0);
        }

        [Test]
        public void EverySweptSeedMergesIntoSlabsThatCoverItsFloorExactly()
        {
            for (uint seed = 1; seed <= 40; ++seed)
            {
                Assert.IsTrue(
                    AreaGenerator.TryGenerate(new AreaSpec(HardwareId.Cpu, seed, 3), out AreaLayout layout, out string why),
                    why);

                HashSet<AreaCell> covered = CellsIn(AreaTerrain.FloorSlabs(layout.Grid));

                Assert.AreEqual(layout.Grid.WalkableCount, covered.Count, $"seed {seed}");
                foreach (AreaCell spawn in layout.AllSpawns)
                    Assert.IsTrue(covered.Contains(spawn), $"seed {seed}: nothing to stand on at {spawn}");
            }
        }

        [Test]
        public void SlabsNeedAGrid()
        {
            Assert.Throws<ArgumentNullException>(() => AreaTerrain.FloorSlabs(null));
            Assert.Throws<ArgumentNullException>(() => AreaTerrain.WallSlabs(null));
            Assert.Throws<ArgumentNullException>(() => AreaTerrain.TouchesFloor(null, default));
        }
    }
}
