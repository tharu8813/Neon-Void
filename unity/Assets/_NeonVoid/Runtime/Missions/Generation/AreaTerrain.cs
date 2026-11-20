using System;
using System.Collections.Generic;

namespace NeonVoid.Missions.Generation
{
    /// <summary>
    /// Turns a grid of cells into a handful of rectangles, so a room can be built out of boxes
    /// instead of one object per cell.
    /// <para>
    /// A nominal area is around 1,200 cells, most of them wall. A cube each would be over a
    /// thousand GameObjects and a thousand colliders for a room with four corridors in it, which
    /// costs frame time for nothing: the shape is the same either way. Merging first is the
    /// difference between a scene that profiles and one that does not.
    /// </para>
    /// <para>
    /// <b>Greedy, not optimal.</b> Runs are taken along X and then stacked where the next row's
    /// run has exactly the same ends. A smaller set of rectangles usually exists; finding it is a
    /// covering problem, and the cheap version already removes the order of magnitude that
    /// mattered.
    /// </para>
    /// Pure C#: no Unity types.
    /// </summary>
    public static class AreaTerrain
    {
        /// <summary>The walkable floor, as rectangles.</summary>
        public static List<AreaRoom> FloorSlabs(AreaGrid grid)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            return Merge(grid, grid.IsWalkable);
        }

        /// <summary>
        /// The wall, as rectangles: the cells that are not floor but touch some.
        /// <para>
        /// <b>Eight neighbours, not four.</b> A wall cell touching the floor only diagonally is
        /// still a corner the player can see past and shoot through, and leaving it out would
        /// open a one-cell hole at every inside corner. Walls further in than that are never seen
        /// and are not built.
        /// </para>
        /// </summary>
        public static List<AreaRoom> WallSlabs(AreaGrid grid)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            return Merge(grid, cell => !grid.IsWalkable(cell) && TouchesFloor(grid, cell));
        }

        /// <summary>Whether any of the eight neighbours is walkable.</summary>
        public static bool TouchesFloor(AreaGrid grid, AreaCell cell)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    if (grid.IsWalkable(new AreaCell(cell.X + dx, cell.Y + dy)))
                        return true;
                }
            }

            return false;
        }

        private static List<AreaRoom> Merge(AreaGrid grid, Func<AreaCell, bool> include)
        {
            var finished = new List<AreaRoom>();
            var open = new List<AreaRoom>();

            for (int y = 0; y < grid.Height; ++y)
            {
                var continuing = new List<AreaRoom>();

                int x = 0;
                while (x < grid.Width)
                {
                    if (!include(new AreaCell(x, y)))
                    {
                        ++x;
                        continue;
                    }

                    int runStart = x;
                    while (x < grid.Width && include(new AreaCell(x, y)))
                        ++x;

                    int runWidth = x - runStart;
                    int extended = -1;
                    for (int i = 0; i < open.Count; ++i)
                    {
                        // Stacked only on an exact match: a run one cell wider would make a
                        // rectangle that covers a cell the predicate excluded.
                        if (open[i].MinX == runStart && open[i].Width == runWidth)
                        {
                            extended = i;
                            break;
                        }
                    }

                    if (extended >= 0)
                    {
                        AreaRoom slab = open[extended];
                        continuing.Add(new AreaRoom(slab.MinX, slab.MinY, slab.Width, slab.Height + 1));
                        open.RemoveAt(extended);
                    }
                    else
                    {
                        continuing.Add(new AreaRoom(runStart, y, runWidth, 1));
                    }
                }

                // Whatever was not continued by this row is as tall as it will get.
                finished.AddRange(open);
                open = continuing;
            }

            finished.AddRange(open);
            return finished;
        }
    }
}
