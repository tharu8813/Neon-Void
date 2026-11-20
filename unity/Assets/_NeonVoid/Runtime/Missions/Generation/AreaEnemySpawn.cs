using NeonVoid.Combat.AI;

namespace NeonVoid.Missions.Generation
{
    /// <summary>
    /// One enemy the generator placed: where, which archetype, and whether it is a 정예.
    /// <para>
    /// docs/01 section 6 check 2 is about 플레이어/적 스폰과 함정 및 각 오브젝트 충돌 반경의 안전
    /// 분리, so enemy positions are part of the layout rather than something the room-building
    /// stage improvises. A spawn chosen while instantiating could not be validated, and could not
    /// be reproduced from the seed either.
    /// </para>
    /// <para>
    /// <b>Elite is a [초안] stand-in.</b> GDD v1 section 5.4's XP budget counts 정예 적 처치 3 per
    /// area, so something has to be worth 32 XP - but docs/01 section 4 describes
    /// <c>enemy.elite</c> as 환경 변이·강인함 with 중첩 버프·마나 디버프, and neither the
    /// environment system nor those mechanics exist. So an elite here is an ordinary archetype
    /// with drafted stat multipliers: tougher, worth more, and <b>not</b> what the document means
    /// by 정예. <see cref="AreaWorldBuilder"/> holds the multipliers and says the same thing.
    /// </para>
    /// </summary>
    public readonly struct AreaEnemySpawn
    {
        public readonly AreaCell Cell;

        public readonly EnemyArchetypeId Archetype;

        /// <summary>Whether this one counts as a 정예 for the XP budget. See the type's note.</summary>
        public readonly bool IsElite;

        /// <summary>Which room it stands in, so the room's gate can hold it.</summary>
        public readonly int RoomIndex;

        public AreaEnemySpawn(AreaCell cell, EnemyArchetypeId archetype, bool isElite, int roomIndex)
        {
            Cell = cell;
            Archetype = archetype;
            IsElite = isElite;
            RoomIndex = roomIndex;
        }

        public override string ToString() =>
            $"{Archetype}{(IsElite ? " (elite)" : string.Empty)} at {Cell} in room {RoomIndex}";
    }
}
