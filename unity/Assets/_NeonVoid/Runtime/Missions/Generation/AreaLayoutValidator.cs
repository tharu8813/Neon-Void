using System;
using System.Collections.Generic;

namespace NeonVoid.Missions.Generation
{
    /// <summary>Whether a generated map may be played, and why not when it may not.</summary>
    public readonly struct AreaLayoutVerdict
    {
        public readonly bool IsValid;

        /// <summary>Which check failed and how. Empty when valid.</summary>
        public readonly string Reason;

        private AreaLayoutVerdict(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason ?? string.Empty;
        }

        public static AreaLayoutVerdict Valid => new AreaLayoutVerdict(true, string.Empty);

        public static AreaLayoutVerdict Invalid(string reason) =>
            new AreaLayoutVerdict(false, string.IsNullOrEmpty(reason) ? "refused" : reason);

        public override string ToString() => IsValid ? "valid" : $"invalid: {Reason}";
    }

    /// <summary>
    /// docs/01 section 6's 생성 시 반드시 검증, as code that runs on every generated map.
    /// <para>
    /// The document numbers six checks. Five of them are here:
    /// </para>
    /// <list type="number">
    /// <item>시작 → 목표 전부 → 보스 → 철수 지점 connected, on a path the player's collision size
    /// can take. <see cref="CheckReachability"/>, plus the cell-size bargain in
    /// <see cref="CheckPlayerFits"/> that makes a walkable cell mean "the player fits".</item>
    /// <item>스폰과 오브젝트 충돌 반경의 안전 분리, and 초반 화면 밖 즉시 피해 금지 - which is why
    /// the entrance is held away from the boss rather than merely not on top of it.
    /// <see cref="CheckSeparation"/>.</item>
    /// <item>목표·보스·포털이 벽 안에 생성되지 않음. <see cref="CheckOnFloor"/>.</item>
    /// <item><b>Not here, and not checkable here.</b> 동일 (seed, hardwareId, generatorVersion)
    /// 재현 is a property of two generator runs, so it is a test
    /// (<c>AreaGeneratorTests</c>) rather than something one map can be asked about.</item>
    /// <item>8~12분 목표를 지나치게 벗어나지 않음. <see cref="CheckSizeBudget"/>, and it is a
    /// <b>proxy</b>: it bounds the floor area and the walk to the boss, because a minute is
    /// something only play measures. Said plainly rather than dressed up as a timing check.</item>
    /// <item>목표 완료 전 보스·철수 비활성, and 완료 보상 정확히 1회. Not a property of a map -
    /// <see cref="MissionFlow"/> and <see cref="AreaBossGate"/> hold it, and their tests
    /// do.</item>
    /// </list>
    /// Pure C#: no Unity types.
    /// </summary>
    public static class AreaLayoutValidator
    {
        /// <summary>
        /// The prototype player's CharacterController radius. Not a design [확정] - C06 in
        /// docs/08 leaves the 3D unit conversion [미확정] - but the collision size the path check
        /// has to be true of today, named here so the two cannot drift apart silently.
        /// </summary>
        public const float PrototypePlayerRadiusUnits = 0.45f;

        /// <summary>[초안] cells the entrance is held away from the boss.</summary>
        public const int DraftMinStartToBossCells = 10;

        /// <summary>[초안] cells the entrance is held away from any objective device.</summary>
        public const int DraftMinStartToObjectiveCells = 4;

        /// <summary>
        /// [초안] cells the entrance is held away from any enemy. docs/01 section 6 check 2:
        /// 초반 화면 밖 즉시 피해 금지 - the player has to be able to see what is coming before it
        /// arrives.
        /// </summary>
        public const int DraftMinStartToEnemyCells = 6;

        /// <summary>[초안] cells between the boss, the 정화 콘솔 and the 철수 패드.</summary>
        public const int DraftMinBossRoomSpacingCells = 2;

        /// <summary>[초안] the smallest floor, in cells, a nominal-size area may have.</summary>
        public const int DraftMinWalkableCellsAtNominalSize = 150;

        /// <summary>[초안] the largest floor, in cells, a nominal-size area may have.</summary>
        public const int DraftMaxWalkableCellsAtNominalSize = 900;

        /// <summary>
        /// [초안] the longest walk from the entrance to the boss, as a percentage of the grid's
        /// width plus its height.
        /// <para>
        /// Scale-free rather than an absolute number of cells, because the grid grows with
        /// 맵 크기 축소·확대 and a flat ceiling would be generous for a small area and impossible
        /// for a large one. What it catches is a snaking spanning tree that walks the player
        /// across the map several times over.
        /// </para>
        /// </summary>
        public const int DraftMaxStartToBossWalkPercentOfGridSpan = 200;

        /// <summary>Runs every check this map can be asked about, in the document's order.</summary>
        public static AreaLayoutVerdict Validate(AreaLayout layout)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));

            AreaLayoutVerdict verdict = CheckPlayerFits(layout);
            if (!verdict.IsValid)
                return verdict;

            verdict = CheckObjectiveCount(layout);
            if (!verdict.IsValid)
                return verdict;

            verdict = CheckOnFloor(layout);
            if (!verdict.IsValid)
                return verdict;

            verdict = CheckReachability(layout);
            if (!verdict.IsValid)
                return verdict;

            verdict = CheckSeparation(layout);
            if (!verdict.IsValid)
                return verdict;

            return CheckSizeBudget(layout);
        }

        /// <summary>
        /// Check 1's premise: a cell is only a place the player can stand if the player fits in
        /// one. Without this the whole 4-neighbour path argument is about an abstract grid rather
        /// than about the player.
        /// </summary>
        public static AreaLayoutVerdict CheckPlayerFits(AreaLayout layout)
        {
            float needed = PrototypePlayerRadiusUnits * 2f;
            return layout.CellSizeUnits + 1e-4f >= needed
                ? AreaLayoutVerdict.Valid
                : AreaLayoutVerdict.Invalid(
                    $"a cell is {layout.CellSizeUnits}u and the player is {needed}u across, so a walkable " +
                    "cell is not a cell the player fits in and the path check would mean nothing");
        }

        /// <summary>The map holds the objectives the area will ask the player for.</summary>
        public static AreaLayoutVerdict CheckObjectiveCount(AreaLayout layout)
        {
            if (layout.Objectives.Count == layout.Spec.ObjectiveCount)
                return AreaLayoutVerdict.Valid;

            // An area whose flow requires three objectives and whose map holds two can never be
            // cleared, and nothing would say so until somebody played it.
            return AreaLayoutVerdict.Invalid(
                $"the spec asks for {layout.Spec.ObjectiveCount} objectives and the map placed " +
                $"{layout.Objectives.Count}");
        }

        /// <summary>Check 3: nothing is generated inside a wall.</summary>
        public static AreaLayoutVerdict CheckOnFloor(AreaLayout layout)
        {
            foreach (AreaCell cell in layout.AllSpawns)
            {
                if (!layout.Grid.InBounds(cell))
                    return AreaLayoutVerdict.Invalid($"{cell} is outside the grid");
                if (!layout.Grid.IsWalkable(cell))
                    return AreaLayoutVerdict.Invalid($"{cell} is inside a wall");
            }

            return AreaLayoutVerdict.Valid;
        }

        /// <summary>
        /// Check 1: 시작 위치 → 필수 목표 전부 → 보스 위치 → 정화 후 철수 지점.
        /// <para>
        /// One distance field from the entrance answers all of it. The document's wording is a
        /// chain, but a chain through a connected floor is the same statement as "every one of
        /// them is reachable from the entrance" - and reading it that way also catches a boss
        /// reachable only through a wall the objectives are behind.
        /// </para>
        /// </summary>
        public static AreaLayoutVerdict CheckReachability(AreaLayout layout)
        {
            int[] field = layout.Grid.DistanceField(layout.Start);

            foreach (AreaCell cell in layout.AllSpawns)
            {
                if (layout.Grid.DistanceAt(field, cell) == AreaGrid.Unreachable)
                    return AreaLayoutVerdict.Invalid($"{cell} cannot be walked to from the entrance {layout.Start}");
            }

            return AreaLayoutVerdict.Valid;
        }

        /// <summary>
        /// Check 2: nothing shares a cell, and the entrance is not in anything's face.
        /// <para>
        /// Measured in walking steps rather than straight-line distance, because a boss four
        /// cells away through a wall is not four cells away. 초반 화면 밖 즉시 피해 금지 is about
        /// what can reach the player on entry, and what can reach them is what can walk to them.
        /// </para>
        /// </summary>
        public static AreaLayoutVerdict CheckSeparation(AreaLayout layout)
        {
            var seen = new HashSet<AreaCell>();
            foreach (AreaCell cell in layout.AllSpawns)
            {
                if (!seen.Add(cell))
                    return AreaLayoutVerdict.Invalid($"two things were placed on {cell}");
            }

            int[] field = layout.Grid.DistanceField(layout.Start);

            int toBoss = layout.Grid.DistanceAt(field, layout.Boss);
            if (toBoss < DraftMinStartToBossCells)
            {
                return AreaLayoutVerdict.Invalid(
                    $"the boss is {toBoss} steps from the entrance, closer than the drafted " +
                    $"{DraftMinStartToBossCells}");
            }

            for (int i = 0; i < layout.Objectives.Count; ++i)
            {
                int toObjective = layout.Grid.DistanceAt(field, layout.Objectives[i]);
                if (toObjective < DraftMinStartToObjectiveCells)
                {
                    return AreaLayoutVerdict.Invalid(
                        $"objective {i} is {toObjective} steps from the entrance, closer than the drafted " +
                        $"{DraftMinStartToObjectiveCells}");
                }
            }

            for (int i = 0; i < layout.Enemies.Count; ++i)
            {
                AreaEnemySpawn enemy = layout.Enemies[i];
                int toEnemy = layout.Grid.DistanceAt(field, enemy.Cell);
                if (toEnemy < DraftMinStartToEnemyCells)
                {
                    return AreaLayoutVerdict.Invalid(
                        $"{enemy} is {toEnemy} steps from the entrance, closer than the drafted " +
                        $"{DraftMinStartToEnemyCells} (초반 화면 밖 즉시 피해 금지)");
                }
            }

            // The three things in the boss's room must not overlap each other's reach: a 정화
            // 콘솔 and a 철수 패드 one cell apart would both be under the player at once, and the
            // pad would arm during the purification.
            int bossToConsole = layout.Boss.ManhattanTo(layout.Purification);
            int bossToPad = layout.Boss.ManhattanTo(layout.Extraction);
            int consoleToPad = layout.Purification.ManhattanTo(layout.Extraction);
            int closest = Math.Min(bossToConsole, Math.Min(bossToPad, consoleToPad));
            if (closest < DraftMinBossRoomSpacingCells)
            {
                return AreaLayoutVerdict.Invalid(
                    $"the boss, the console and the pad are {closest} cells apart at their closest, under the " +
                    $"drafted {DraftMinBossRoomSpacingCells}");
            }

            return AreaLayoutVerdict.Valid;
        }

        /// <summary>
        /// Check 5, as a proxy for a number only play can produce. See the type's note.
        /// </summary>
        public static AreaLayoutVerdict CheckSizeBudget(AreaLayout layout)
        {
            int percent = layout.Spec.SizeModifierPercent;
            int minCells = Scale(DraftMinWalkableCellsAtNominalSize, percent);
            int maxCells = Scale(DraftMaxWalkableCellsAtNominalSize, percent);

            if (layout.Grid.WalkableCount < minCells || layout.Grid.WalkableCount > maxCells)
            {
                return AreaLayoutVerdict.Invalid(
                    $"the floor is {layout.Grid.WalkableCount} cells, outside the drafted {minCells}~{maxCells} " +
                    $"for a {percent}% area");
            }

            int toBoss = layout.Grid.DistanceAt(layout.Grid.DistanceField(layout.Start), layout.Boss);
            int span = layout.Grid.Width + layout.Grid.Height;
            int maxWalk = Scale(span, DraftMaxStartToBossWalkPercentOfGridSpan);
            if (toBoss > maxWalk)
            {
                return AreaLayoutVerdict.Invalid(
                    $"the walk to the boss is {toBoss} cells, past the drafted {maxWalk} for a {span}-cell grid span");
            }

            return AreaLayoutVerdict.Valid;
        }

        /// <summary>A nominal figure at this area's size. Integer, rounded half up.</summary>
        public static int Scale(int nominal, int sizeModifierPercent) =>
            ((nominal * sizeModifierPercent) + 50) / 100;
    }
}
