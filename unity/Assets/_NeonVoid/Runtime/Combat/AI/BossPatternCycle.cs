using System;
using NeonVoid.Core;

namespace NeonVoid.Combat.AI
{
    /// <summary>What the boss is doing right now. docs/01 section 3's 기본 전개, in order.</summary>
    public enum BossActivity
    {
        /// <summary>입장 예고 - announcing itself before anything can hurt anybody.</summary>
        Entering = 0,

        /// <summary>The telegraph is up. The attack has not landed.</summary>
        Telegraphing = 1,

        /// <summary>회복/반격 창 - the player's turn.</summary>
        Recovering = 2,

        /// <summary>냉각 - the overclocked boss has stopped to cool. Its 공격 기회.</summary>
        Cooling = 3,

        /// <summary>Dead. Nothing more is telegraphed or resolved.</summary>
        Finished = 4,
    }

    /// <summary>The fight's phases. docs/01 asks for one 페이즈 변화; this is it.</summary>
    public enum BossPhase
    {
        /// <summary>Before the first pattern.</summary>
        Entering = 0,

        /// <summary>Normal pacing.</summary>
        Fighting = 1,

        /// <summary>오버클럭 - faster telegraphs, paid for with a 냉각 창.</summary>
        Overclocked = 2,
    }

    /// <summary>What one tick of the cycle produced.</summary>
    public readonly struct BossTickOutcome
    {
        /// <summary>The pattern that landed on this tick, or -1.</summary>
        public readonly int ResolvedPatternIndex;

        /// <summary>Whether the 지역 기믹 pulsed on this tick.</summary>
        public readonly bool ResolvedGimmick;

        /// <summary>Whether the phase changed on this tick, for an announcement.</summary>
        public readonly bool PhaseChanged;

        public BossTickOutcome(int resolvedPatternIndex, bool resolvedGimmick, bool phaseChanged)
        {
            ResolvedPatternIndex = resolvedPatternIndex;
            ResolvedGimmick = resolvedGimmick;
            PhaseChanged = phaseChanged;
        }

        public bool DidSomething => ResolvedPatternIndex >= 0 || ResolvedGimmick || PhaseChanged;
    }

    /// <summary>
    /// The boss fight's clock: which pattern is coming, how long its warning has left, and when
    /// the 지역 기믹 pulses.
    /// <para>
    /// docs/01 section 3 gives the shape - 입장 예고 → 0.8~1.5초 텔레그래프 → 공격 판정 →
    /// 회복/반격 창 → 패턴 전환 - and asks each boss for 최소 3개의 형태가 다른 패턴 + 1개 지역
    /// 기믹 + 1개 페이즈 변화. <see cref="Validate"/> refuses a boss that does not have all three,
    /// so "a stat-boosted enemy with one swing" cannot be written down as a boss.
    /// </para>
    /// <para>
    /// <b>The gimmick runs on its own clock.</b> 주기적 과열 is the CPU's 기본 위험 in docs/01
    /// section 2, not one of the boss's moves: it keeps coming while the boss telegraphs, cools or
    /// recovers. Hanging it off the pattern cycle would make it stop exactly when the player has
    /// the most room to breathe, which is backwards.
    /// </para>
    /// <para>
    /// <b>Overclock never shortens a telegraph below 0.8 seconds.</b> docs/01's floor is on the
    /// contract, not on the opening phase, so the clamp is here rather than left to whoever picks
    /// the multiplier. An overclocked fight gets faster by having less recovery and more pulses,
    /// not by becoming undodgeable.
    /// </para>
    /// Pure C#: no Unity types, no clock of its own, no RNG. One tick in, one outcome out.
    /// </summary>
    public sealed class BossPatternCycle
    {
        /// <summary>docs/01 section 3: 최소 3개의 형태가 다른 패턴.</summary>
        public const int MinPatterns = 3;

        /// <summary>[초안] health fraction at which 오버클럭 begins.</summary>
        public const float DraftOverclockAtHealthFraction = 0.5f;

        /// <summary>[초안] telegraph length while overclocked, as a percentage.</summary>
        public const int DraftOverclockTelegraphPercent = 70;

        /// <summary>[초안] recovery length while overclocked, as a percentage.</summary>
        public const int DraftOverclockRecoveryPercent = 60;

        /// <summary>[초안] patterns between 냉각 windows while overclocked.</summary>
        public const int DraftPatternsPerCooling = 3;

        /// <summary>[초안] ticks the 냉각 창 lasts. 150 is 2.5 seconds of free hits.</summary>
        public const int DraftCoolingTicks = 150;

        /// <summary>[초안] ticks of 입장 예고 before the first telegraph.</summary>
        public const int DraftEntranceTicks = 90;

        private readonly BossPattern[] _patterns;
        private readonly BossPattern _gimmick;
        private readonly int _gimmickIntervalTicks;

        private int _ticksLeftInActivity;
        private int _patternIndex;
        private int _patternsSinceCooling;
        private int _gimmickTicksUntilTelegraph;
        private int _gimmickTelegraphTicksLeft;

        public BossPatternCycle(BossPattern[] patterns, in BossPattern gimmick, int gimmickIntervalTicks)
        {
            _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
            _gimmick = gimmick;
            _gimmickIntervalTicks = gimmickIntervalTicks;

            Validate();

            Activity = BossActivity.Entering;
            Phase = BossPhase.Entering;
            _ticksLeftInActivity = DraftEntranceTicks;
            _patternIndex = 0;
            _gimmickTicksUntilTelegraph = gimmickIntervalTicks;
        }

        public BossActivity Activity { get; private set; }

        public BossPhase Phase { get; private set; }

        /// <summary>The pattern being telegraphed or just recovered from.</summary>
        public int CurrentPatternIndex => _patternIndex;

        /// <summary>The pattern being telegraphed or just recovered from.</summary>
        public BossPattern CurrentPattern => _patterns[_patternIndex];

        public int PatternCount => _patterns.Length;

        public BossPattern PatternAt(int index) => _patterns[index];

        /// <summary>The 지역 기믹's own shape.</summary>
        public BossPattern Gimmick => _gimmick;

        /// <summary>Ticks left in whatever the boss is doing.</summary>
        public int TicksLeftInActivity => _ticksLeftInActivity;

        /// <summary>
        /// Total ticks of the telegraph now showing, so a gauge can be drawn as a fraction
        /// rather than as an absolute countdown nobody can read.
        /// </summary>
        public int TelegraphTotalTicks { get; private set; }

        /// <summary>0 to 1 through the current telegraph, or 0 when none is up.</summary>
        public float TelegraphProgress =>
            Activity == BossActivity.Telegraphing && TelegraphTotalTicks > 0
                ? 1f - (_ticksLeftInActivity / (float)TelegraphTotalTicks)
                : 0f;

        /// <summary>Whether an attack is being announced right now.</summary>
        public bool IsTelegraphing => Activity == BossActivity.Telegraphing;

        /// <summary>Whether the 기믹 is being announced right now.</summary>
        public bool IsGimmickTelegraphing => _gimmickTelegraphTicksLeft > 0;

        /// <summary>Ticks until the 기믹 starts announcing itself.</summary>
        public int GimmickTicksUntilTelegraph => _gimmickTicksUntilTelegraph;

        /// <summary>
        /// Whether the boss has stopped attacking. docs/01: 대신 냉각 창으로 공격 기회 - the
        /// opportunity IS the pause, which is why no damage multiplier is involved.
        /// </summary>
        public bool IsCooling => Activity == BossActivity.Cooling;

        /// <summary>
        /// Takes the boss's health fraction and changes phase at the drafted threshold.
        /// <para>
        /// Fed in rather than read, so the cycle stays free of Unity and a test can state the
        /// fraction in one line. One-way: a boss healed back above the threshold would leave
        /// 오버클럭, which would undo a phase change the player has already been told about.
        /// </para>
        /// </summary>
        public bool ReportHealthFraction(float fraction)
        {
            if (Phase != BossPhase.Fighting || float.IsNaN(fraction))
                return false;
            if (fraction > DraftOverclockAtHealthFraction)
                return false;

            Phase = BossPhase.Overclocked;
            _patternsSinceCooling = 0;
            return true;
        }

        /// <summary>The fight is over. Nothing further telegraphs or resolves.</summary>
        public void Finish()
        {
            Activity = BossActivity.Finished;
            _ticksLeftInActivity = 0;
            TelegraphTotalTicks = 0;
            _gimmickTelegraphTicksLeft = 0;
        }

        /// <summary>
        /// Advances one logic tick. The outcome names anything that landed, so the caller resolves
        /// damage and nothing else has to inspect the state machine.
        /// </summary>
        public BossTickOutcome Tick()
        {
            if (Activity == BossActivity.Finished)
                return new BossTickOutcome(-1, false, false);

            bool gimmickResolved = TickGimmick();
            int resolvedPattern = -1;
            bool phaseChanged = false;

            if (_ticksLeftInActivity > 0)
                _ticksLeftInActivity--;

            if (_ticksLeftInActivity > 0)
                return new BossTickOutcome(-1, gimmickResolved, false);

            switch (Activity)
            {
                case BossActivity.Entering:
                    // 입장 예고 over: the fight starts, which is itself a phase change worth
                    // announcing - the boss was not fightable a tick ago.
                    Phase = BossPhase.Fighting;
                    phaseChanged = true;
                    BeginTelegraph();
                    break;

                case BossActivity.Telegraphing:
                    // 공격 판정, on the tick the warning runs out and not a tick before it.
                    resolvedPattern = _patternIndex;
                    _patternsSinceCooling++;
                    Activity = BossActivity.Recovering;
                    _ticksLeftInActivity = ScaledRecovery(_patterns[_patternIndex].RecoveryTicks);
                    TelegraphTotalTicks = 0;
                    break;

                case BossActivity.Recovering:
                    if (Phase == BossPhase.Overclocked && _patternsSinceCooling >= DraftPatternsPerCooling)
                    {
                        _patternsSinceCooling = 0;
                        Activity = BossActivity.Cooling;
                        _ticksLeftInActivity = DraftCoolingTicks;
                        break;
                    }

                    AdvancePattern();
                    BeginTelegraph();
                    break;

                case BossActivity.Cooling:
                    AdvancePattern();
                    BeginTelegraph();
                    break;
            }

            return new BossTickOutcome(resolvedPattern, gimmickResolved, phaseChanged);
        }

        /// <summary>
        /// The telegraph the current phase gives this pattern, never below docs/01's floor.
        /// </summary>
        public int EffectiveTelegraphTicks(int patternIndex)
        {
            int ticks = _patterns[patternIndex].TelegraphTicks;
            if (Phase != BossPhase.Overclocked)
                return ticks;

            int shortened = ((ticks * DraftOverclockTelegraphPercent) + 50) / 100;
            return shortened < BossPattern.MinTelegraphTicks ? BossPattern.MinTelegraphTicks : shortened;
        }

        /// <summary>
        /// Holds docs/01 section 3's contract on whatever this boss was built from.
        /// </summary>
        public void Validate()
        {
            if (_patterns.Length < MinPatterns)
            {
                throw new InvalidOperationException(
                    $"docs/01 section 3 asks each boss for at least {MinPatterns} patterns of different " +
                    $"shapes; this one has {_patterns.Length}.");
            }

            var shapesSeen = new System.Collections.Generic.HashSet<BossPatternShape>();
            for (int i = 0; i < _patterns.Length; ++i)
            {
                _patterns[i].Validate();
                shapesSeen.Add(_patterns[i].Shape);
            }

            if (shapesSeen.Count < MinPatterns)
            {
                // 형태가 다른 패턴. Three fans with different radii are one pattern tuned three
                // ways, and the document says so.
                throw new InvalidOperationException(
                    $"this boss's {_patterns.Length} patterns use only {shapesSeen.Count} distinct shapes; " +
                    "docs/01 asks for 형태가 다른 패턴.");
            }

            _gimmick.Validate();

            if (_gimmickIntervalTicks <= _gimmick.TelegraphTicks)
            {
                throw new InvalidOperationException(
                    $"the 지역 기믹 pulses every {_gimmickIntervalTicks} ticks and announces itself for " +
                    $"{_gimmick.TelegraphTicks}, so it would never be off.");
            }

            if (DraftOverclockAtHealthFraction <= 0f || DraftOverclockAtHealthFraction >= 1f)
                throw new InvalidOperationException("the overclock threshold has to be a fraction between 0 and 1.");
        }

        private bool TickGimmick()
        {
            if (_gimmickTelegraphTicksLeft > 0)
            {
                _gimmickTelegraphTicksLeft--;
                if (_gimmickTelegraphTicksLeft > 0)
                    return false;

                _gimmickTicksUntilTelegraph = ScaledGimmickInterval();
                return true;
            }

            if (_gimmickTicksUntilTelegraph > 0)
                _gimmickTicksUntilTelegraph--;

            if (_gimmickTicksUntilTelegraph == 0)
                _gimmickTelegraphTicksLeft = _gimmick.TelegraphTicks;

            return false;
        }

        /// <summary>
        /// Overclock makes the gimmick come round sooner, which is the half of 속도 증가 that does
        /// not touch a telegraph.
        /// </summary>
        private int ScaledGimmickInterval()
        {
            if (Phase != BossPhase.Overclocked)
                return _gimmickIntervalTicks;

            int shortened = ((_gimmickIntervalTicks * DraftOverclockRecoveryPercent) + 50) / 100;
            int floor = _gimmick.TelegraphTicks + DesignConstants.SimulationTicksPerSecond;
            return shortened < floor ? floor : shortened;
        }

        private int ScaledRecovery(int ticks)
        {
            if (Phase != BossPhase.Overclocked)
                return ticks < 1 ? 1 : ticks;

            int shortened = ((ticks * DraftOverclockRecoveryPercent) + 50) / 100;
            return shortened < 1 ? 1 : shortened;
        }

        private void AdvancePattern() => _patternIndex = (_patternIndex + 1) % _patterns.Length;

        private void BeginTelegraph()
        {
            Activity = BossActivity.Telegraphing;
            TelegraphTotalTicks = EffectiveTelegraphTicks(_patternIndex);
            _ticksLeftInActivity = TelegraphTotalTicks;
        }

        public override string ToString() =>
            Activity == BossActivity.Telegraphing
                ? $"{Phase}/{Activity} {CurrentPattern.Name} {TelegraphProgress:P0}"
                : $"{Phase}/{Activity} {_ticksLeftInActivity}t";
    }
}
