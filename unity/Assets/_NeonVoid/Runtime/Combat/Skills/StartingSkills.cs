namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// The three skills a new vaccine starts with.
    /// <para>
    /// [확정] D-009 (2026-11-22): 고정 3종으로 시작하고 허브의 스킬 장착 터미널에서 교체한다.
    /// Before this the starting set was whatever came first in an array, which is not a
    /// decision - it meant the first play differed whenever a file was added.
    /// </para>
    /// <para>
    /// <b>[초안] which three.</b> The set is chosen to cover the three things a player needs on
    /// their first run and nothing else: something to kill a group with, something to get out
    /// of the way with, and something to survive a hit they could not avoid. Every other skill
    /// deepens one of those three rather than opening a fourth.
    /// </para>
    /// </summary>
    public static class StartingSkills
    {
        /// <summary>공격 - 전방 부채꼴 광역. The only skill cursor aiming matters for.</summary>
        public const string Attack = "skill.arc_pulse";

        /// <summary>회피 - 커서 방향 점멸. Different from 대시, which follows the move vector.</summary>
        public const string Escape = "skill.packet_blink";

        /// <summary>생존 - 보호막. The answer to a telegraph that is not running from it.</summary>
        public const string Survive = "skill.data_shell";

        /// <summary>The three, in slot order: Q, E, R by default.</summary>
        public static readonly string[] DocumentKeys = { Attack, Escape, Survive };
    }
}
