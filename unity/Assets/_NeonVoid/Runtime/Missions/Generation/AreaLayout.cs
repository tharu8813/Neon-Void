using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonVoid.Missions.Generation
{
    /// <summary>
    /// One generated hardware area: where the floor is and where everything stands.
    /// <para>
    /// docs/01 section 6 calls the last stage of generation 결정론 시드 스냅샷 and says the 3D
    /// prefabs, the lighting and the walls are 통과한 그래프 위에 꾸미는 표현 단계. This is the
    /// graph that passed. Nothing here is a GameObject; a builder reads it and makes the room.
    /// </para>
    /// <para>
    /// <b><see cref="EffectiveSeed"/> may not be the seed that was asked for.</b> docs/01 section
    /// 6 check 5 allows 불가능한 시드 to be replaced - 기록된 대체 시드로 재생성하고 영수증에
    /// 생성 버전을 남김 - so the seed that actually built this map travels with it, along with how
    /// many attempts it took. <see cref="ReceiptLine"/> is what a receipt carries.
    /// </para>
    /// </summary>
    public sealed class AreaLayout
    {
        private readonly List<AreaCell> _objectives;
        private readonly List<AreaRoom> _rooms;

        public AreaLayout(
            in AreaSpec spec,
            uint effectiveSeed,
            int attemptsUsed,
            float cellSizeUnits,
            AreaGrid grid,
            List<AreaRoom> rooms,
            AreaCell start,
            List<AreaCell> objectives,
            AreaCell boss,
            AreaCell purification,
            AreaCell extraction)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (rooms == null)
                throw new ArgumentNullException(nameof(rooms));
            if (objectives == null)
                throw new ArgumentNullException(nameof(objectives));
            if (float.IsNaN(cellSizeUnits) || cellSizeUnits <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSizeUnits), cellSizeUnits, "A cell has a size.");
            if (attemptsUsed < 1)
                throw new ArgumentOutOfRangeException(nameof(attemptsUsed), attemptsUsed, "Attempts start at 1.");

            Spec = spec;
            EffectiveSeed = effectiveSeed;
            AttemptsUsed = attemptsUsed;
            CellSizeUnits = cellSizeUnits;
            Grid = grid;
            _rooms = rooms;
            Start = start;
            _objectives = objectives;
            Boss = boss;
            Purification = purification;
            Extraction = extraction;
        }

        public AreaSpec Spec { get; }

        /// <summary>The seed this map was actually built from. See the type's note.</summary>
        public uint EffectiveSeed { get; }

        /// <summary>How many seeds were tried, including this one. 1 when the first worked.</summary>
        public int AttemptsUsed { get; }

        /// <summary>
        /// World units per cell. At least the player's diameter, so a walkable cell is a cell the
        /// player fits in - which is what makes the path check a statement about the player.
        /// </summary>
        public float CellSizeUnits { get; }

        public AreaGrid Grid { get; }

        public IReadOnlyList<AreaRoom> Rooms => _rooms;

        /// <summary>Where the player enters.</summary>
        public AreaCell Start { get; }

        /// <summary>The 소목표 devices, one per room, in no particular order.</summary>
        public IReadOnlyList<AreaCell> Objectives => _objectives;

        /// <summary>The area's boss. The farthest room from the entrance.</summary>
        public AreaCell Boss { get; }

        /// <summary>
        /// The 정화 콘솔. docs/01 section 6's field list does not name one - it was written before
        /// 정화 had a place in the world - and docs/01 section 1 puts 정화 directly after the boss,
        /// so it stands in the boss's room.
        /// </summary>
        public AreaCell Purification { get; }

        /// <summary>
        /// The 철수 패드. Also in the boss's room: docs/01 section 1 offers 철수 또는 메인보드 귀환
        /// the moment an area is purified, so making the player walk the map again to leave would
        /// be padding rather than pacing.
        /// </summary>
        public AreaCell Extraction { get; }

        /// <summary>Every placed spawn, for the separation and reachability checks.</summary>
        public IEnumerable<AreaCell> AllSpawns
        {
            get
            {
                yield return Start;
                for (int i = 0; i < _objectives.Count; ++i)
                    yield return _objectives[i];

                yield return Boss;
                yield return Purification;
                yield return Extraction;
            }
        }

        /// <summary>
        /// The centre of a cell in world space, on the XZ plane. [확정] docs/01 section 1: 세계
        /// 좌표 XZ, 주요 이동은 평면 - so Y is the floor and the generator never chooses one.
        /// </summary>
        public Vector3 WorldPositionOf(AreaCell cell) =>
            new Vector3(
                (cell.X * CellSizeUnits) + (CellSizeUnits * 0.5f),
                0f,
                (cell.Y * CellSizeUnits) + (CellSizeUnits * 0.5f));

        /// <summary>
        /// What a receipt records about this map. docs/01 section 6 check 5: 영수증에 생성 버전을
        /// 남김, and the effective seed with it, because a map rebuilt from the asked-for seed
        /// would not be the map that was played.
        /// </summary>
        public string ReceiptLine() =>
            $"gen=v{Spec.GeneratorVersion};key={Spec.GenerationKey};effectiveSeed={EffectiveSeed};" +
            $"attempts={AttemptsUsed};rooms={_rooms.Count};walkable={Grid.WalkableCount}";

        public override string ToString() => ReceiptLine();
    }
}
