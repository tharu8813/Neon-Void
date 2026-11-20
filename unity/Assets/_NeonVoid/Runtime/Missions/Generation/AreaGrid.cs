using System;
using System.Collections.Generic;

namespace NeonVoid.Missions.Generation
{
    /// <summary>One square of the area grid.</summary>
    public readonly struct AreaCell : IEquatable<AreaCell>
    {
        public readonly int X;
        public readonly int Y;

        public AreaCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Steps on a 4-neighbour grid. See <see cref="AreaGrid.PathExists"/>.</summary>
        public int ManhattanTo(AreaCell other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public bool Equals(AreaCell other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is AreaCell other && Equals(other);

        public override int GetHashCode() => (X * 397) ^ Y;

        public override string ToString() => $"({X},{Y})";
    }

    /// <summary>An axis-aligned room, in cells.</summary>
    public readonly struct AreaRoom
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int Width;
        public readonly int Height;

        public AreaRoom(int minX, int minY, int width, int height)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width), "A room is at least one cell.");

            MinX = minX;
            MinY = minY;
            Width = width;
            Height = height;
        }

        public int MaxX => MinX + Width - 1;

        public int MaxY => MinY + Height - 1;

        public int CellCount => Width * Height;

        /// <summary>The middle cell, biased down-left on an even side.</summary>
        public AreaCell Centre => new AreaCell(MinX + ((Width - 1) / 2), MinY + ((Height - 1) / 2));

        public bool Contains(AreaCell cell) =>
            cell.X >= MinX && cell.X <= MaxX && cell.Y >= MinY && cell.Y <= MaxY;

        /// <summary>
        /// Whether two rooms are closer than <paramref name="margin"/> cells apart. A margin of 1
        /// keeps a wall between them, so two rooms never merge into one odd shape and every
        /// connection is a corridor the path check can see.
        /// </summary>
        public bool TooCloseTo(in AreaRoom other, int margin) =>
            MinX - margin <= other.MaxX && other.MinX - margin <= MaxX
            && MinY - margin <= other.MaxY && other.MinY - margin <= MaxY;

        public override string ToString() => $"[{MinX},{MinY} {Width}x{Height}]";
    }

    /// <summary>
    /// The walkable footprint of an area, as cells.
    /// <para>
    /// <b>Why cells and not metres.</b> docs/01 section 6 check 1 asks for a connected path based
    /// on 플레이어 충돌 크기. A grid whose cell is at least as wide as the player makes that
    /// checkable rather than approximated: a walkable cell is a cell the player fits inside, so a
    /// 4-neighbour path through walkable cells is a path the player's collider can take.
    /// <see cref="AreaLayoutValidator"/> holds the cell-size side of that bargain.
    /// </para>
    /// <para>
    /// <b>4-neighbour, never diagonal.</b> A diagonal step between two blocked cells cuts the
    /// corner between them, which a collider cannot do. Allowing diagonals would make the path
    /// check pass on maps the player cannot actually cross.
    /// </para>
    /// Pure C#: no Unity types.
    /// </summary>
    public sealed class AreaGrid
    {
        /// <summary>Returned by <see cref="DistanceField"/> for a cell the start cannot reach.</summary>
        public const int Unreachable = -1;

        private readonly bool[] _walkable;

        public AreaGrid(int width, int height)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width), "A grid has at least one cell.");

            Width = width;
            Height = height;
            _walkable = new bool[width * height];
        }

        public int Width { get; }

        public int Height { get; }

        public int WalkableCount { get; private set; }

        public bool InBounds(AreaCell cell) =>
            cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

        public bool IsWalkable(AreaCell cell) => InBounds(cell) && _walkable[Index(cell)];

        /// <summary>Opens one cell. Carving an already-open cell is not counted twice.</summary>
        public void Carve(AreaCell cell)
        {
            if (!InBounds(cell))
                throw new ArgumentOutOfRangeException(nameof(cell), cell, "A cell outside the grid cannot be carved.");

            int index = Index(cell);
            if (_walkable[index])
                return;

            _walkable[index] = true;
            WalkableCount++;
        }

        /// <summary>Opens a whole room. The room must be inside the grid.</summary>
        public void CarveRoom(in AreaRoom room)
        {
            for (int y = room.MinY; y <= room.MaxY; ++y)
            {
                for (int x = room.MinX; x <= room.MaxX; ++x)
                    Carve(new AreaCell(x, y));
            }
        }

        /// <summary>
        /// Opens an L-shaped corridor one cell wide between two cells.
        /// </summary>
        /// <param name="horizontalFirst">
        /// Which leg is carved first. Both reach the same place, so the choice is the generator's
        /// to make from its seed - it is the difference between two maps, not a correctness
        /// question.
        /// </param>
        public void CarveCorridor(AreaCell from, AreaCell to, bool horizontalFirst)
        {
            if (horizontalFirst)
            {
                CarveRun(from.Y, from.X, to.X, horizontal: true);
                CarveRun(to.X, from.Y, to.Y, horizontal: false);
            }
            else
            {
                CarveRun(from.X, from.Y, to.Y, horizontal: false);
                CarveRun(to.Y, from.X, to.X, horizontal: true);
            }
        }

        /// <summary>
        /// Steps from <paramref name="start"/> to every walkable cell, or
        /// <see cref="Unreachable"/>. The field rather than a yes/no, because the generator reads
        /// distances off it to decide which room is farthest and the validator reads the same
        /// field to answer every reachability question at once.
        /// </summary>
        public int[] DistanceField(AreaCell start)
        {
            var distances = new int[Width * Height];
            for (int i = 0; i < distances.Length; ++i)
                distances[i] = Unreachable;

            if (!IsWalkable(start))
                return distances;

            var queue = new Queue<AreaCell>();
            distances[Index(start)] = 0;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                AreaCell cell = queue.Dequeue();
                int next = distances[Index(cell)] + 1;

                Visit(new AreaCell(cell.X + 1, cell.Y), next, distances, queue);
                Visit(new AreaCell(cell.X - 1, cell.Y), next, distances, queue);
                Visit(new AreaCell(cell.X, cell.Y + 1), next, distances, queue);
                Visit(new AreaCell(cell.X, cell.Y - 1), next, distances, queue);
            }

            return distances;
        }

        /// <summary>Steps from a distance field, or <see cref="Unreachable"/>.</summary>
        public int DistanceAt(int[] field, AreaCell cell)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (field.Length != Width * Height)
                throw new ArgumentException("That distance field belongs to another grid.", nameof(field));

            return InBounds(cell) ? field[Index(cell)] : Unreachable;
        }

        /// <summary>Whether the player could walk from one cell to the other.</summary>
        public bool PathExists(AreaCell from, AreaCell to) =>
            DistanceAt(DistanceField(from), to) != Unreachable;

        private void Visit(AreaCell cell, int distance, int[] distances, Queue<AreaCell> queue)
        {
            if (!IsWalkable(cell))
                return;

            int index = Index(cell);
            if (distances[index] != Unreachable)
                return;

            distances[index] = distance;
            queue.Enqueue(cell);
        }

        private void CarveRun(int fixedCoordinate, int from, int to, bool horizontal)
        {
            int step = from <= to ? 1 : -1;
            for (int moving = from; moving != to + step; moving += step)
            {
                Carve(horizontal
                    ? new AreaCell(moving, fixedCoordinate)
                    : new AreaCell(fixedCoordinate, moving));
            }
        }

        private int Index(AreaCell cell) => (cell.Y * Width) + cell.X;
    }
}
