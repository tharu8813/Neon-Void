using System;

namespace NeonVoid.Missions
{
    /// <summary>
    /// [확정] docs/01 section 5's 환경 종류, all six of them and no others.
    /// <para>
    /// The document gives the list as approved content, so the enum is the list. A seventh kind
    /// invented as a tuning convenience would be a new [확정] rule nobody approved, and
    /// <c>MissionBoardTests</c> holds the count.
    /// </para>
    /// </summary>
    public enum EnvironmentKind
    {
        /// <summary>이상없음 - nothing wrong with this area.</summary>
        Clear = 0,

        /// <summary>
        /// 변이발생 Lv.1~5 - 일부 개체 능력 상승, and 일반·레어 플러그인 1~2개 획득 for the
        /// player. Carries a level; see <see cref="AreaConditions.MutationLevel"/>.
        /// </summary>
        Mutation = 1,

        /// <summary>스택오버플로우.</summary>
        StackOverflow = 2,

        /// <summary>
        /// 터널감염 - 현재 학습되지 않은 소지 플러그인 1~2개 비활성 또는 부정 효과. [확정], and
        /// the document says in the same breath that 학습 출전분을 예외로 하는 상태 태그가
        /// 필요하다 - a tag the plugin loadout does not have yet, which is why selecting this
        /// environment does not yet do anything to a loadout.
        /// </summary>
        TunnelInfection = 3,

        /// <summary>확산.</summary>
        Spread = 4,

        /// <summary>초기상태.</summary>
        InitialState = 5,
    }

    /// <summary>
    /// How hard one hardware area is. [확정] docs/01 section 5 requires at least one Easy per
    /// mission; the <i>number</i> of tiers is not approved - 난이도 3·4·5단계 여부 is listed
    /// [미확정] - so there are three and no more until it is.
    /// </summary>
    public enum HardwareDifficulty
    {
        /// <summary>쉬움. [확정] at least one area per mission is this.</summary>
        Easy = 0,

        Normal = 1,

        Hard = 2,
    }

    /// <summary>
    /// What one hardware area offers this mission: how hard it is and what is wrong with it.
    /// <para>
    /// <b>One environment, not a set.</b> docs/01 section 5 lists 환경 상호배타/중첩 as [미확정],
    /// so stacking two is a rule nobody has approved - and the restrictive reading is the one that
    /// cannot surprise a player with two penalties at once. The field is singular on purpose, so
    /// the day stacking is decided it is a visible change rather than a list quietly filling up.
    /// </para>
    /// </summary>
    public readonly struct AreaConditions
    {
        public readonly HardwareId Hardware;

        public readonly HardwareDifficulty Difficulty;

        public readonly EnvironmentKind Environment;

        /// <summary>
        /// 변이 단계 1~5 for <see cref="EnvironmentKind.Mutation"/>, 0 for every other kind.
        /// <para>
        /// Rolled from the mission seed alone. docs/01 says 높은 변이 단계의 비율은 반복 의뢰와
        /// 함께 커질 수 있음 and leaves the exact relationship [미확정], so the mission count is
        /// deliberately not an input: a curve invented here would be that undecided rule, shipped.
        /// </para>
        /// </summary>
        public readonly int MutationLevel;

        /// <summary>[확정] docs/01 section 5: 변이발생 Lv.1~5.</summary>
        public const int MinMutationLevel = 1;

        /// <summary>[확정] the top of 변이발생 Lv.1~5.</summary>
        public const int MaxMutationLevel = 5;

        public AreaConditions(
            HardwareId hardware, HardwareDifficulty difficulty, EnvironmentKind environment, int mutationLevel)
        {
            if (!HardwareIds.IsArea(hardware))
                throw new ArgumentOutOfRangeException(nameof(hardware), hardware, "Not a hardware area.");
            if (!Enum.IsDefined(typeof(HardwareDifficulty), difficulty))
                throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown difficulty.");
            if (!Enum.IsDefined(typeof(EnvironmentKind), environment))
                throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unknown environment.");

            if (environment == EnvironmentKind.Mutation)
            {
                if (mutationLevel < MinMutationLevel || mutationLevel > MaxMutationLevel)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(mutationLevel), mutationLevel,
                        $"[확정] 변이발생 is Lv.{MinMutationLevel}~{MaxMutationLevel}.");
                }
            }
            else if (mutationLevel != 0)
            {
                // A level on a non-mutation would be a number a gate might display and nothing
                // means.
                throw new ArgumentOutOfRangeException(
                    nameof(mutationLevel), mutationLevel, $"only {nameof(EnvironmentKind.Mutation)} has a level.");
            }

            Hardware = hardware;
            Difficulty = difficulty;
            Environment = environment;
            MutationLevel = mutationLevel;
        }

        /// <summary>An area with nothing wrong with it, at a given difficulty.</summary>
        public static AreaConditions Clear(HardwareId hardware, HardwareDifficulty difficulty) =>
            new AreaConditions(hardware, difficulty, EnvironmentKind.Clear, 0);

        /// <summary>The same area at another difficulty, for the 쉬움 보장 correction.</summary>
        public AreaConditions At(HardwareDifficulty difficulty) =>
            new AreaConditions(Hardware, difficulty, Environment, MutationLevel);

        /// <summary>
        /// What the hardware gate shows. [초안] docs/01 section 5 asks for 환경 명칭/영향/대상/
        /// 복구 가능 여부/구역 난이도; this is the name and the difficulty, which are the two that
        /// exist. The impact, the target and whether it can be undone are the environment's
        /// effects, and no environment has one yet.
        /// </summary>
        public string GateLine() =>
            Environment == EnvironmentKind.Mutation
                ? $"{Hardware} · {Difficulty} · {Environment} Lv.{MutationLevel}"
                : $"{Hardware} · {Difficulty} · {Environment}";

        public override string ToString() => GateLine();
    }
}
