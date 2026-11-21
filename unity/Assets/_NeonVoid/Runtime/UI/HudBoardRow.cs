using NeonVoid.Missions;

namespace NeonVoid.UI
{
    /// <summary>
    /// One hardware on the 메인보드 panel: what it is, what is waiting there, and whether it
    /// has already been dealt with.
    /// <para>
    /// docs/01 section 1 asks the motherboard for a 현재 임무 위험도/환경 패널 by name. This is
    /// the row of it. The conditions were already rolled per mission and shown only in the
    /// debug overlay, so the choice [확정] docs/01 calls 입장 순서는 자유 was one a player made
    /// blind.
    /// </para>
    /// </summary>
    public readonly struct HudBoardRow
    {
        public readonly HardwareId Hardware;
        public readonly HardwareDifficulty Difficulty;
        public readonly EnvironmentKind Environment;
        public readonly int MutationLevel;

        /// <summary>Whether this area has already been purified on this mission.</summary>
        public readonly bool Purified;

        public HudBoardRow(in AreaConditions conditions, bool purified)
        {
            Hardware = conditions.Hardware;
            Difficulty = conditions.Difficulty;
            Environment = conditions.Environment;
            MutationLevel = conditions.MutationLevel;
            Purified = purified;
        }

        /// <summary>
        /// What is waiting there, in one line. 변이발생 carries its level because the level is
        /// the whole difference between one mutation area and another.
        /// </summary>
        public string ConditionLine =>
            Environment == EnvironmentKind.Mutation
                ? $"{Difficulty} · {Environment} Lv.{MutationLevel}"
                : $"{Difficulty} · {Environment}";

        /// <summary>
        /// Whether this row should read as somewhere still worth going. A purified area cannot
        /// be re-entered - [확정] docs/01 section 1 forbids 중복 정화 - so it is shown done
        /// rather than hidden, which would make the board look like it had fewer choices than
        /// the mission was rolled with.
        /// </summary>
        public bool IsOpen => !Purified;

        public override string ToString() =>
            $"{Hardware} {ConditionLine}{(Purified ? " (정화됨)" : string.Empty)}";
    }
}
