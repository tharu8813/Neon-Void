namespace NeonVoid.Combat.AI
{
    /// <summary>
    /// The four bosses that are not the CPU's, built from docs/01 section 2's 보스 컨셉 column
    /// and each area's 세부 전개 - 렌더 옵저버, 메모리 리치, 스트림 인터셉터, 섹터 콜로서스.
    /// <para>
    /// <b>Why these exist at all.</b> CLAUDE.md: 5개 하드웨어를 한 개의 복붙 맵으로 취급하지 말
    /// 것. Four areas sharing the 코어 워처's cycle in another colour is exactly that, and it
    /// would pass every test in this project - which is why
    /// <see cref="NeonVoid.Missions.AreaWorldBuilder"/> refuses to substitute rather than
    /// quietly reusing one.
    /// </para>
    /// <para>
    /// <b>Everything here is [초안].</b> docs/01's own header says 적·보스·목표·비율·맵 구조는
    /// 모두 [초안], and D-015 owns the final scenarios. The document's mitigation for D-015 is
    /// to build from its explicit drafts and test them, which is what this is - not an approved
    /// design.
    /// </para>
    /// <para>
    /// <b>What is deliberately not built, per boss.</b> Each one is named where it belongs, so
    /// a missing idea is a recorded gap rather than something nobody noticed:
    /// </para>
    /// <list type="bullet">
    /// <item>GPU's 가짜 잔상 - a decoy telegraph. docs/01 requires the fake to be identifiable
    /// from the real one, and there is no cue system to identify it with; a decoy that reads as
    /// real is the 이펙트만 반짝이고 판정 없는 것 the same section forbids, with the sign
    /// flipped.</item>
    /// <item>RAM's 이전 플레이어 공격 방향을 재생하는 반격 - the boss needs a record of where the
    /// player has been attacking from, which nothing keeps.</item>
    /// <item>SSD's 직선 워프 예고 후 이동 - the boss relocating partway through a pattern. The
    /// cycle aims once when the telegraph goes up and never again (otherwise the attack is
    /// undodgeable), and a boss that moves mid-pattern needs that rule written differently.</item>
    /// <item>Strong CC anywhere. docs/01's RAM section is explicit: 시간 정지나 입력 차단 같은
    /// 강한 CC는 강인함·저항/회피창 검증 없이는 넣지 않는다, and a pattern carries a
    /// <see cref="DamageRequest"/> and nothing else.</item>
    /// <item>난이도에 따른 HP/피해 배율, which docs/01 section 3 lists as [미확정].</item>
    /// </list>
    /// </summary>
    public static class AreaBossCatalog
    {
        // [초안] 지역 기믹 intervals, one per area, chosen to match each area's rhythm rather
        // than copied: the SSD's pulse is the quickest because 빠른 이동 is its identity, and
        // the HDD's is the slowest because 장기전 is.
        public const int DraftRenderObserverSweepIntervalTicks = 480;   // 8s
        public const int DraftMemoryLichRegenIntervalTicks = 420;       // 7s
        public const int DraftStreamInterceptorLoopIntervalTicks = 360; // 6s
        public const int DraftSectorColossusDebrisIntervalTicks = 600;  // 10s

        // --- GPU 렌더 옵저버 -----------------------------------------------------------------

        /// <summary>
        /// GPU 렌더 옵저버. docs/01 section 2: 스캔+홀로그램, 자홍·청보라, 선명한 전조음; the
        /// area's verbs are 회피·조준·공간 읽기 and its danger is 레이저 스윕·정확한 장판.
        /// </summary>
        public static BossPatternCycle RenderObserver() =>
            new BossPatternCycle(
                RenderObserverPatterns(), LaserSweep(), DraftRenderObserverSweepIntervalTicks);

        public static BossPattern[] RenderObserverPatterns() => new[]
        {
            SequentialBeam(),
            RotatingSafeFan(),
            PrecisionZone(),
        };

        /// <summary>
        /// (1) 세 방향 순차 광선, as one beam. The cycle fires one pattern at a time, so three
        /// beams under a single telegraph is not something it can express; three beams in a row
        /// is what the rotation produces, each aimed where the player was when it went up.
        /// </summary>
        public static BossPattern SequentialBeam() => new BossPattern
        {
            Name = "순차 광선",
            Shape = BossPatternShape.Line,
            TelegraphTicks = 60,                // [초안] 1.0s
            RecoveryTicks = 36,                 // [초안] 0.6s - the shortest, it is the fast one
            RangeUnits = 22f,                   // [초안] it crosses the room
            HalfWidthUnits = 1.1f,              // [초안] narrower than the CPU's; stepping aside clears it
            Damage = new DamageRequest
            {
                Flat = 21f,                     // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// (2) 회전 팬형 안전 영역. The wedge is the safe place and the rest of the disc is the
        /// attack, which is why this one gets the full 1.5s: the player has to move <i>into</i>
        /// something rather than away from it, and that is a longer trip.
        /// </summary>
        public static BossPattern RotatingSafeFan() => new BossPattern
        {
            Name = "회전 팬형 안전 영역",
            Shape = BossPatternShape.Fan,
            SafeInsideShape = true,
            TelegraphTicks = 90,                // [초안] 1.5s, the ceiling
            RecoveryTicks = 60,                 // [초안] 1.0s
            RangeUnits = 16f,                   // [초안] the dangerous disc
            HalfAngleDegrees = 50f,             // [초안] the safe wedge
            Damage = new DamageRequest
            {
                Flat = 24f,                     // [초안] the heaviest of the three
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// (3) 정밀 장판, from the area's own 정확한 장판 danger - the same move docs/01 makes
        /// for the CPU, where the third listed pattern could not be built. Small and quick:
        /// 조준 is the verb, so it rewards reading where it lands.
        /// </summary>
        public static BossPattern PrecisionZone() => new BossPattern
        {
            Name = "정밀 장판",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 48,                // [초안] 0.8s, the floor - it is small
            RecoveryTicks = 48,                 // [초안] 0.8s
            RangeUnits = 5f,                    // [초안] a tight disc
            InnerRadiusUnits = 0f,
            Damage = new DamageRequest
            {
                Flat = 19f,                     // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>지역 기믹: 레이저 스윕, docs/01 section 2's GPU 기본 위험.</summary>
        public static BossPattern LaserSweep() => new BossPattern
        {
            Name = "레이저 스윕",
            Shape = BossPatternShape.Line,
            TelegraphTicks = 90,                // [초안] 1.5s
            RecoveryTicks = 1,                  // the area does not recover; the sweep passes
            RangeUnits = 40f,                   // [초안] past the walls of any drafted room
            HalfWidthUnits = 2.2f,              // [초안] wide enough to matter, narrow enough to leave
            Damage = new DamageRequest
            {
                Flat = 16f,                     // [초안] a position tax, not a kill
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        // --- RAM 메모리 리치 -----------------------------------------------------------------

        /// <summary>
        /// RAM 메모리 리치. docs/01 section 2: 공격 복제·잔상, 녹색·보라, 에코/글리치; the area's
        /// verbs are 진위 구분·적응 and its danger is 환영/재배열/재생성.
        /// </summary>
        public static BossPatternCycle MemoryLich() =>
            new BossPatternCycle(MemoryLichPatterns(), MemoryRegrowth(), DraftMemoryLichRegenIntervalTicks);

        public static BossPattern[] MemoryLichPatterns() => new[]
        {
            AfterimageSpread(),
            ReorderedLattice(),
            CacheCollapse(),
        };

        /// <summary>(1) 잔상 전개 - the wide one, from 잔상.</summary>
        public static BossPattern AfterimageSpread() => new BossPattern
        {
            Name = "잔상 전개",
            Shape = BossPatternShape.Fan,
            TelegraphTicks = 66,                // [초안] 1.1s
            RecoveryTicks = 54,                 // [초안] 0.9s
            RangeUnits = 10f,                   // [초안]
            HalfAngleDegrees = 70f,             // [초안] wider than the CPU's fan
            Damage = new DamageRequest
            {
                Flat = 19f,                     // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>(2) 재배열 격자, from the area's 재배열 danger.</summary>
        public static BossPattern ReorderedLattice() => new BossPattern
        {
            Name = "재배열 격자",
            Shape = BossPatternShape.Line,
            TelegraphTicks = 54,                // [초안] 0.9s
            RecoveryTicks = 42,                 // [초안] 0.7s
            RangeUnits = 20f,                   // [초안]
            HalfWidthUnits = 1.6f,              // [초안]
            Damage = new DamageRequest
            {
                Flat = 21f,                     // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// (3) 캐시 붕괴 - safe close in, dangerous further out. The opposite of the CPU's
        /// 코어 펄스 on purpose: 진위 구분 means the answer is to commit to the real thing rather
        /// than to back away from everything.
        /// </summary>
        public static BossPattern CacheCollapse() => new BossPattern
        {
            Name = "캐시 붕괴",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 72,                // [초안] 1.2s
            RecoveryTicks = 60,                 // [초안] 1.0s
            RangeUnits = 12f,                   // [초안] outer edge
            InnerRadiusUnits = 3.5f,            // [초안] the safe middle, next to the boss
            Damage = new DamageRequest
            {
                Flat = 20f,                     // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// 지역 기믹: 메모리 재생성, docs/01 section 2's RAM 재생성 danger. A room-wide pulse
        /// with a safe middle, like the CPU's 주기적 과열 - the shape a 기믹 has to take when the
        /// player can be anywhere.
        /// </summary>
        public static BossPattern MemoryRegrowth() => new BossPattern
        {
            Name = "메모리 재생성",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 84,                // [초안] 1.4s
            RecoveryTicks = 1,
            RangeUnits = 34f,                   // [초안]
            InnerRadiusUnits = 5f,              // [초안] the safe middle
            Damage = new DamageRequest
            {
                Flat = 13f,                     // [초안]
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        // --- SSD 스트림 인터셉터 --------------------------------------------------------------

        /// <summary>
        /// SSD 스트림 인터셉터. docs/01 section 2: 돌진+궤도, 청색·백색, 짧은 디지털 비프; the
        /// area's verbs are 빠른 이동·구간 연결 and its danger is 레일 돌진·짧은 순환 장판.
        /// </summary>
        public static BossPatternCycle StreamInterceptor() =>
            new BossPatternCycle(
                StreamInterceptorPatterns(), ShortLoopField(), DraftStreamInterceptorLoopIntervalTicks);

        public static BossPattern[] StreamInterceptorPatterns() => new[]
        {
            RailCharge(),
            NodeWave(),
            RailRebuild(),
        };

        /// <summary>
        /// (1) 레일 돌진. The fastest telegraph in the project, sitting exactly on docs/01's
        /// floor: 빠른 이동 is the area's identity, and the floor is where that stops being
        /// 불가피 피격.
        /// </summary>
        public static BossPattern RailCharge() => new BossPattern
        {
            Name = "레일 돌진",
            Shape = BossPatternShape.Line,
            TelegraphTicks = 48,                // [초안] 0.8s, the floor
            RecoveryTicks = 36,                 // [초안] 0.6s
            RangeUnits = 24f,                   // [초안] the longest line of any boss
            HalfWidthUnits = 1.5f,              // [초안]
            Damage = new DamageRequest
            {
                Flat = 23f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>(2) 노드 파형 - 따라오는 노드 파형, as a pulse around the boss.</summary>
        public static BossPattern NodeWave() => new BossPattern
        {
            Name = "노드 파형",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 54,                // [초안] 0.9s
            RecoveryTicks = 42,                 // [초안] 0.7s
            RangeUnits = 8f,                    // [초안]
            InnerRadiusUnits = 0f,
            Damage = new DamageRequest
            {
                Flat = 18f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// (3) 레일 재구성 - 안전 공간 축소. A narrow safe wedge, which is the 구간 연결 verb as
        /// an attack: the rail is where you have to be.
        /// </summary>
        public static BossPattern RailRebuild() => new BossPattern
        {
            Name = "레일 재구성",
            Shape = BossPatternShape.Fan,
            SafeInsideShape = true,
            TelegraphTicks = 84,                // [초안] 1.4s - moving into it takes longer
            RecoveryTicks = 54,                 // [초안] 0.9s
            RangeUnits = 18f,                   // [초안] the dangerous disc
            HalfAngleDegrees = 40f,             // [초안] narrower than the GPU's wedge
            Damage = new DamageRequest
            {
                Flat = 22f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>지역 기믹: 짧은 순환 장판, docs/01 section 2's SSD 기본 위험.</summary>
        public static BossPattern ShortLoopField() => new BossPattern
        {
            Name = "짧은 순환 장판",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 60,                // [초안] 1.0s - the quickest gimmick
            RecoveryTicks = 1,
            RangeUnits = 26f,                   // [초안]
            InnerRadiusUnits = 6f,              // [초안] the safe middle
            Damage = new DamageRequest
            {
                Flat = 13f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        // --- HDD 섹터 콜로서스 ---------------------------------------------------------------

        /// <summary>
        /// HDD 섹터 콜로서스. docs/01 section 2: 회전/압박, 황동·주황, 기계 마찰/저음; the area's
        /// verbs are 위치 관리·장기전 and its danger is 회전 구획·낙하 파편.
        /// </summary>
        public static BossPatternCycle SectorColossus() =>
            new BossPatternCycle(
                SectorColossusPatterns(), DebrisFall(), DraftSectorColossusDebrisIntervalTicks);

        public static BossPattern[] SectorColossusPatterns() => new[]
        {
            OuterRotation(),
            HeadAlignment(),
            CompressionSweep(),
        };

        /// <summary>
        /// (1) 외곽 회전 구역. The outside is the attack and the middle is safe, which is the
        /// 위치 관리 verb: the answer is to be somewhere specific rather than to keep moving.
        /// </summary>
        public static BossPattern OuterRotation() => new BossPattern
        {
            Name = "외곽 회전 구역",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 90,                // [초안] 1.5s - it is a long walk inward
            RecoveryTicks = 72,                 // [초안] 1.2s
            RangeUnits = 26f,                   // [초안]
            InnerRadiusUnits = 7f,              // [초안] the safe middle, and it is generous
            Damage = new DamageRequest
            {
                Flat = 24f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>(2) 헤드 정렬 - the read head sweeping a line across the platter.</summary>
        public static BossPattern HeadAlignment() => new BossPattern
        {
            Name = "헤드 정렬",
            Shape = BossPatternShape.Line,
            TelegraphTicks = 78,                // [초안] 1.3s
            RecoveryTicks = 60,                 // [초안] 1.0s
            RangeUnits = 26f,                   // [초안]
            HalfWidthUnits = 2.2f,              // [초안] the widest line; it is a heavy machine
            Damage = new DamageRequest
            {
                Flat = 25f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>(3) 압박 회전 - 압박, as the close, wide fan a heavy boss makes.</summary>
        public static BossPattern CompressionSweep() => new BossPattern
        {
            Name = "압박 회전",
            Shape = BossPatternShape.Fan,
            TelegraphTicks = 84,                // [초안] 1.4s
            RecoveryTicks = 78,                 // [초안] 1.3s - the longest opening in the project
            RangeUnits = 12f,                   // [초안]
            HalfAngleDegrees = 80f,             // [초안]
            Damage = new DamageRequest
            {
                Flat = 27f,                     // [초안] the heaviest single hit; 장기전 with big swings
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// 지역 기믹: 파편 낙하. docs/01 asks for 피할 수 있는 파편 낙하, so it has a safe middle
        /// rather than covering the room - the same reason the CPU's 주기적 과열 has one.
        /// </summary>
        public static BossPattern DebrisFall() => new BossPattern
        {
            Name = "파편 낙하",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 90,                // [초안] 1.5s
            RecoveryTicks = 1,
            RangeUnits = 34f,                   // [초안]
            InnerRadiusUnits = 5f,              // [초안] the safe middle
            Damage = new DamageRequest
            {
                Flat = 15f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };
    }
}
