using System;

namespace NeonVoid.Missions
{
    /// <summary>
    /// The 환경 상호작용 of one hardware, from docs/01 section 2's own column.
    /// <para>
    /// Five entries because the document gives five, and CLAUDE.md names 환경 상호작용 as one of
    /// the things that must differ between the areas: <i>5개 하드웨어를 한 개의 복붙 맵으로
    /// 취급하지 말 것</i>.
    /// </para>
    /// </summary>
    public enum AreaHazardKind
    {
        /// <summary>No hazard. Not a hardware's answer - the absence of one.</summary>
        None = 0,

        /// <summary>CPU 주기적 과열: a room that heats, warns, and burns on a cycle.</summary>
        Overheat = 1,

        /// <summary>GPU 정확한 장판: a telegraphed patch that goes off exactly where it was drawn.</summary>
        LaserField = 2,

        /// <summary>RAM 재배열: a patch that moves between cycles, so its place is not learnable.</summary>
        Reshuffle = 3,

        /// <summary>SSD 짧은 순환 장판: short, fast, and often.</summary>
        ShortCycle = 4,

        /// <summary>HDD 낙하 파편: slow, heavy, and worth walking around.</summary>
        FallingDebris = 5,
    }

    /// <summary>
    /// What a hazard is, in ticks and units.
    /// <para>
    /// <b>Telegraph first, always.</b> [확정] docs/01 section 2's GPU entry is explicit:
    /// <i>바닥에 예고선이 먼저 나타나고 실제 레이저 판정은 늦게 발생. 비디오 이펙트만 반짝이고
    /// 판정 없는 것 금지</i> - and the same section's 합격 line asks that the danger be readable
    /// <i>이펙트 품질 최저에서도</i>. So the warning is a phase of the hazard itself, counted in
    /// ticks, not a particle effect that happens to play first: a hazard with no telegraph
    /// cannot be built at all (<see cref="Validate"/>).
    /// </para>
    /// </summary>
    public readonly struct AreaHazardShape
    {
        public AreaHazardShape(
            AreaHazardKind kind, int telegraphTicks, int activeTicks, int restTicks,
            float radiusUnits, float damage, bool movesBetweenCycles)
        {
            Kind = kind;
            TelegraphTicks = telegraphTicks;
            ActiveTicks = activeTicks;
            RestTicks = restTicks;
            RadiusUnits = radiusUnits;
            Damage = damage;
            MovesBetweenCycles = movesBetweenCycles;
        }

        public AreaHazardKind Kind { get; }

        /// <summary>Ticks the warning is up before anything can be hurt.</summary>
        public int TelegraphTicks { get; }

        /// <summary>Ticks it is dangerous.</summary>
        public int ActiveTicks { get; }

        /// <summary>Ticks it is quiet before the warning goes up again.</summary>
        public int RestTicks { get; }

        /// <summary>Reach, in units.</summary>
        public float RadiusUnits { get; }

        /// <summary>[초안] damage per active tick, as true damage.</summary>
        public float Damage { get; }

        /// <summary>RAM's 재배열: whether it stands somewhere else next cycle.</summary>
        public bool MovesBetweenCycles { get; }

        /// <summary>The whole cycle, in ticks.</summary>
        public int CycleTicks => TelegraphTicks + ActiveTicks + RestTicks;

        public bool IsNone => Kind == AreaHazardKind.None;

        /// <summary>
        /// A hazard has to warn, has to be dangerous for a while, and has to have a reach.
        /// <para>
        /// The telegraph is checked hardest: a hazard that damaged on the tick it appeared would
        /// be the 비디오 이펙트만 반짝이고 판정 없는 것 docs/01 forbids, with the sign flipped -
        /// damage nobody could have avoided.
        /// </para>
        /// </summary>
        public void Validate()
        {
            if (IsNone)
                return;

            if (TelegraphTicks < MinTelegraphTicks)
            {
                throw new InvalidOperationException(
                    $"{Kind}: a hazard warns for at least {MinTelegraphTicks} ticks before it can hurt " +
                    "anyone ([확정] docs/01 2절: 예고선이 먼저).");
            }

            if (ActiveTicks < 1)
                throw new InvalidOperationException($"{Kind}: a hazard that is never dangerous is scenery.");

            if (RestTicks < 0)
                throw new InvalidOperationException($"{Kind}: a negative rest is not a cycle.");

            if (float.IsNaN(RadiusUnits) || RadiusUnits <= 0f)
                throw new InvalidOperationException($"{Kind}: a hazard with no reach touches nobody.");

            if (float.IsNaN(Damage) || Damage <= 0f)
                throw new InvalidOperationException($"{Kind}: a hazard that deals nothing is scenery.");
        }

        /// <summary>
        /// [초안] the shortest warning any hazard may give. A third of a second is enough to
        /// read and step out of at the drafted move speed, and short enough that a hazard still
        /// feels like a threat.
        /// </summary>
        public const int MinTelegraphTicks = 20;

        public override string ToString() =>
            IsNone
                ? "없음"
                : $"{Kind} 예고 {TelegraphTicks}t · 발동 {ActiveTicks}t · 휴지 {RestTicks}t · " +
                  $"{RadiusUnits:0.#}u · {Damage:0.#}";
    }

    /// <summary>
    /// Which hazard each hardware has, and how many of them an area gets.
    /// <para>
    /// Straight from docs/01 section 2's 환경 상호작용 column. The column is content; the ticks
    /// and the numbers under it are [초안] and live here so a balance pass is one table.
    /// </para>
    /// Pure C#: no Unity types.
    /// </summary>
    public static class AreaHazards
    {
        /// <summary>[초안] hazards an area places per room it has to spare.</summary>
        public const int DraftHazardsPerArea = 3;

        /// <summary>
        /// [초안] how far a hazard must stand from the entrance, the objectives, the boss and
        /// the pad, in cells.
        /// <para>
        /// [확정] docs/01 section 6 check 2: 플레이어/적 스폰과 함정 및 각 오브젝트 충돌 반경의
        /// 안전 분리, 초반 화면 밖 즉시 피해 금지. A hazard on the entrance would be exactly the
        /// damage-before-you-can-see-it that line forbids.
        /// </para>
        /// </summary>
        public const int DraftSafeCellsFromAnything = 2;

        /// <summary>
        /// [초안] how far a hazard must stand from an enemy's spawn cell, in cells.
        /// <para>
        /// Smaller than <see cref="DraftSafeCellsFromAnything"/> on purpose. A spawn is not a
        /// fixture - the enemy walks off it the moment the room opens - so it needs room, not a
        /// moat. With one distance for both, CPU's rooms (the most crowded of the five) had
        /// nowhere left to put a hazard at all.
        /// </para>
        /// </summary>
        public const int DraftSafeCellsFromSpawns = 1;

        /// <summary>
        /// How many cells apart two of these have to stand so their reaches do not touch.
        /// <para>
        /// <b>Derived from the radius, not a fixed number of cells.</b> The first version used a
        /// flat 2 cells and a PlayMode test caught it: CPU's 과열 reaches 3.2 units and a cell is
        /// smaller than that, so two patches two cells apart overlapped into one - the player was
        /// burned by a second hazard while standing in the first one's warning. A separation in
        /// cells that ignores the reach in units is not a separation.
        /// </para>
        /// </summary>
        public static int CellsApartFor(in AreaHazardShape shape, float cellSizeUnits)
        {
            if (shape.IsNone || cellSizeUnits <= 0f)
                return DraftSafeCellsFromAnything;

            int needed = (int)System.Math.Ceiling(shape.RadiusUnits * 2f / cellSizeUnits);
            return needed > DraftSafeCellsFromAnything ? needed : DraftSafeCellsFromAnything;
        }

        /// <summary>
        /// How far a hazard must stand from a fixture, in cells: far enough that its reach does
        /// not cover the thing the player has to stand at.
        /// </summary>
        public static int CellsFromFixturesFor(in AreaHazardShape shape, float cellSizeUnits)
        {
            if (shape.IsNone || cellSizeUnits <= 0f)
                return DraftSafeCellsFromAnything;

            int needed = (int)System.Math.Ceiling(shape.RadiusUnits / cellSizeUnits)
                         + DraftSafeCellsFromAnything;
            return needed;
        }

        /// <summary>The hazard this hardware's rooms carry.</summary>
        public static AreaHazardShape For(HardwareId hardware)
        {
            switch (hardware)
            {
                // 주기적 과열: slow, wide, and unmissable - the room itself is the threat.
                case HardwareId.Cpu:
                    return new AreaHazardShape(
                        AreaHazardKind.Overheat, 45, 60, 150, 3.2f, 6f, movesBetweenCycles: false);

                // 정확한 장판: the one docs/01 writes the telegraph rule against. Tight, brief,
                // and exactly where it was drawn.
                case HardwareId.Gpu:
                    return new AreaHazardShape(
                        AreaHazardKind.LaserField, 40, 25, 95, 2.4f, 9f, movesBetweenCycles: false);

                // 재배열: it does not stay put, so the room cannot be memorised - RAM's whole
                // 진위 구분·적응 identity.
                case HardwareId.Ram:
                    return new AreaHazardShape(
                        AreaHazardKind.Reshuffle, 35, 30, 110, 2.6f, 7f, movesBetweenCycles: true);

                // 짧은 순환 장판: fast and frequent, to be moved through rather than waited out.
                case HardwareId.Ssd:
                    return new AreaHazardShape(
                        AreaHazardKind.ShortCycle, 25, 20, 55, 2.0f, 5f, movesBetweenCycles: false);

                // 낙하 파편: heavy and slow, and worth the detour.
                case HardwareId.Hdd:
                    return new AreaHazardShape(
                        AreaHazardKind.FallingDebris, 50, 35, 130, 2.8f, 11f, movesBetweenCycles: false);

                default:
                    return default;
            }
        }

        /// <summary>What the HUD and a log call it.</summary>
        public static string NameOf(AreaHazardKind kind)
        {
            switch (kind)
            {
                case AreaHazardKind.Overheat: return "과열";
                case AreaHazardKind.LaserField: return "장판";
                case AreaHazardKind.Reshuffle: return "재배열";
                case AreaHazardKind.ShortCycle: return "순환 장판";
                case AreaHazardKind.FallingDebris: return "낙하 파편";
                default: return "없음";
            }
        }

        /// <summary>
        /// Whether the five are five different things. Held by a test, because a table that
        /// drifted into one shape with five names is exactly what CLAUDE.md forbids.
        /// </summary>
        public static bool EveryAreaHasItsOwn()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                if (!HardwareIds.IsArea(hardware))
                    continue;

                AreaHazardShape shape = For(hardware);
                if (shape.IsNone || !seen.Add(shape.ToString()))
                    return false;
            }

            return true;
        }
    }
}
