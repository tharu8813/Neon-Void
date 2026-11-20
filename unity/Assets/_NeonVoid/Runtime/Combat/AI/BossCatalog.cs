namespace NeonVoid.Combat.AI
{
    /// <summary>
    /// The bosses, as data, in the shape <see cref="EnemyArchetypeCatalog"/> and
    /// <see cref="Plugins.PluginCatalog"/> already use.
    /// <para>
    /// Only the CPU's 코어 워처 exists. docs/01 section 2 names the other four - 렌더 옵저버,
    /// 메모리 리치, 스트림 인터셉터, 섹터 콜로서스 - and each needs its own patterns and its own
    /// region gimmick; they are absent rather than the CPU's patterns in another colour, which is
    /// the thing CLAUDE.md forbids by name.
    /// </para>
    /// <para>
    /// <b>One of docs/01's three CPU patterns is not here.</b> The document lists (1) 붉은 부채꼴
    /// 코어 열기, (2) 직선 펄스 회로, (3) 소환과 보호막 전환. The third cannot be built honestly
    /// yet: 보호막 is D-004 in docs/08, whose safe handling is to keep shields unconnected until
    /// multi-shield absorption and expiry are decided, and 소환 needs the 소환체 XP 중복 제한 rule
    /// that sits behind an approved reward commit - a summoner without it is how one enemy
    /// becomes an XP fountain. So the third pattern is 코어 펄스, built from the CPU's own
    /// 코어펄스 danger, and the contract's 최소 3개 형태가 다른 패턴 is met with three shapes that
    /// are real.
    /// </para>
    /// All values [초안]: D-015 owns the final scenario and docs/01 marks the boss concepts
    /// themselves as drafts.
    /// </summary>
    public static class BossCatalog
    {
        /// <summary>[초안] ticks between 주기적 과열 pulses. 540 is nine seconds.</summary>
        public const int DraftCoreWatcherOverheatIntervalTicks = 540;

        /// <summary>
        /// CPU 코어 워처. docs/01 section 2: 오버클럭 패턴, 적색·흑연, 규칙적인 팬/클럭.
        /// </summary>
        public static BossPatternCycle CoreWatcher() =>
            new BossPatternCycle(CoreWatcherPatterns(), CoreWatcherOverheat(), DraftCoreWatcherOverheatIntervalTicks);

        /// <summary>The 코어 워처's three patterns, in the order it cycles them.</summary>
        public static BossPattern[] CoreWatcherPatterns() => new[]
        {
            CoreOpening(),
            PulseCircuit(),
            CorePulse(),
        };

        /// <summary>
        /// (1) 붉은 부채꼴 코어 열기. The longest warning of the three, because it is the widest
        /// thing the boss does and the player has to get around it rather than just step aside.
        /// </summary>
        public static BossPattern CoreOpening() => new BossPattern
        {
            Name = "붉은 부채꼴 코어 열기",
            Shape = BossPatternShape.Fan,
            TelegraphTicks = 72,                // [초안] 1.2s
            RecoveryTicks = 54,                 // [초안] 0.9s
            RangeUnits = 9f,                    // [초안]
            HalfAngleDegrees = 55f,             // [초안]
            Damage = new DamageRequest
            {
                Flat = 18f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,                // A boss pattern that could crit would make an
                                                // avoidable hit sometimes unsurvivable.
                IsArea = true,
            },
        };

        /// <summary>
        /// (2) 직선 펄스 회로. The shortest warning and the narrowest shape: a step to either side
        /// clears it, so it can afford to be fast.
        /// </summary>
        public static BossPattern PulseCircuit() => new BossPattern
        {
            Name = "직선 펄스 회로",
            Shape = BossPatternShape.Line,
            TelegraphTicks = 54,                // [초안] 0.9s
            RecoveryTicks = 42,                 // [초안] 0.7s
            RangeUnits = 18f,                   // [초안]
            HalfWidthUnits = 1.3f,              // [초안]
            Damage = new DamageRequest
            {
                Flat = 22f,                     // [초안] more than the fan: harder to be caught by
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// (3) 코어 펄스 - a shockwave with a safe ring beyond it. Built from the CPU's own
        /// 코어펄스 danger in docs/01 section 2; see the type's note on why it is not 소환과
        /// 보호막 전환.
        /// </summary>
        public static BossPattern CorePulse() => new BossPattern
        {
            Name = "코어 펄스",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 60,                // [초안] 1.0s
            RecoveryTicks = 66,                 // [초안] 1.1s - the longest, it leaves the boss open
            RangeUnits = 7f,                    // [초안] outside this is safe
            InnerRadiusUnits = 0f,              // a solid disc: this one is "get away"
            Damage = new DamageRequest
            {
                Flat = 20f,                     // [초안]
                Type = DamageType.Physical,
                CanCrit = false,
                IsArea = true,
            },
        };

        /// <summary>
        /// 지역 기믹: 주기적 과열. docs/01 section 2 gives the CPU 주기적 과열/코어펄스 as its
        /// 기본 위험, and this is the half that is the <i>area's</i> rather than the boss's.
        /// <para>
        /// Its safe spot is the middle, next to the boss, which is the point: the arena's hazard
        /// pushes the player toward the thing that is trying to kill them, and the boss's own
        /// 코어 펄스 pushes them away. The two together are a position problem rather than two
        /// dodges.
        /// </para>
        /// </summary>
        public static BossPattern CoreWatcherOverheat() => new BossPattern
        {
            Name = "주기적 과열",
            Shape = BossPatternShape.Ring,
            TelegraphTicks = 90,                // [초안] 1.5s - the full ceiling; it covers the room
            RecoveryTicks = 1,                  // The area does not recover; the field just passes.
            RangeUnits = 40f,                   // [초안] past the walls of any drafted room
            InnerRadiusUnits = 4.5f,            // [초안] the safe middle
            Damage = new DamageRequest
            {
                Flat = 14f,                     // [초안] survivable; it is a position tax, not a kill
                Type = DamageType.Magic,
                CanCrit = false,
                IsArea = true,
            },
        };

        // Which hardware area gets which boss is deliberately NOT decided here. docs/06's
        // ownership makes 월드 the Missions domain and Combat must not reach into it - the same
        // one-way rule that keeps Combat from referencing Plugins. The area builder picks, and
        // what it finds missing it reports rather than quietly substituting the 코어 워처: a GPU
        // fight that was secretly the CPU's would be the copy-paste CLAUDE.md forbids, and it
        // would pass every test.
    }
}
