using System;

namespace NeonVoid.Missions
{
    /// <summary>
    /// What the 디펜더 허브 holds. [확정] docs/00 section 6: 허브에는 의뢰/백신 성장/플러그인
    /// 데이터베이스/스킬 장착 터미널이 있다 - four of them, and the hub is 실제 3D 공간 rather
    /// than a menu list.
    /// </summary>
    public enum HubTerminalKind
    {
        /// <summary>의뢰 터미널. Begins a mission and puts the player on the motherboard.</summary>
        Mission = 0,

        /// <summary>백신 성장. 영구 능력치 포인트 투자와 무료 재분배.</summary>
        Growth = 1,

        /// <summary>플러그인 데이터베이스. 보관 DB ↔ 출전 소지 이동.</summary>
        Database = 2,

        /// <summary>스킬 장착. 액티브 스킬 3개.</summary>
        Skills = 3,
    }

    /// <summary>
    /// The four terminals, and what each one is called in the game's own words.
    /// <para>
    /// A list rather than four fields, because docs/00 fixes the set as [확정] and a test can
    /// then hold that the hub places all four - including the ones whose function is not built
    /// yet. A hub that quietly shipped with three terminals would look finished.
    /// </para>
    /// Pure C#: no Unity types, no state.
    /// </summary>
    public static class HubTerminals
    {
        public static readonly HubTerminalKind[] All =
        {
            HubTerminalKind.Mission,
            HubTerminalKind.Growth,
            HubTerminalKind.Database,
            HubTerminalKind.Skills,
        };

        /// <summary>[확정] docs/00 section 6 names four.</summary>
        public const int Count = 4;

        /// <summary>The terminal's name, in the terms docs/00 and docs/05 use for it.</summary>
        public static string NameOf(HubTerminalKind kind)
        {
            switch (kind)
            {
                case HubTerminalKind.Mission: return "의뢰 터미널";
                case HubTerminalKind.Growth: return "백신 성장 터미널";
                case HubTerminalKind.Database: return "플러그인 데이터베이스";
                case HubTerminalKind.Skills: return "스킬 장착 터미널";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a hub terminal.");
            }
        }

        /// <summary>
        /// Whether standing at this terminal does anything yet.
        /// <para>
        /// <b>Stated here rather than left to look finished.</b> The hub places all four,
        /// because the set is [확정] and a space with one post in it is not the hub docs/00
        /// describes. But three of them have nothing behind them, and the honest answer when the
        /// player interacts with one is to say so - see <see cref="WhyNotConnected"/>.
        /// </para>
        /// </summary>
        public static bool IsConnected(HubTerminalKind kind)
        {
            // All four, since 2026-11-23: the 플러그인 데이터베이스 was the last one with nothing
            // behind it. This stays a method rather than becoming `true` at every call site,
            // because a terminal whose function is deferred again must be able to say so - and
            // the alternative is a screen that silently does nothing.
            Require(kind);
            return true;
        }

        /// <summary>
        /// Why a terminal does nothing yet, short enough to sit on the HUD, or empty when it
        /// works.
        /// <para>
        /// Separate from <see cref="WhyNotConnected"/>, which is written for whoever has to
        /// finish the terminal and names the decision. A prompt the player reads while walking
        /// past needs the fact and not the reasoning, and putting the long one on screen was
        /// a sixty-character paragraph in a one-line box.
        /// </para>
        /// </summary>
        public static string PendingLabel(HubTerminalKind kind)
        {
            // All four are connected since 2026-11-23. The two methods stay: a terminal
            // whose function is removed or deferred again must be able to say so, and the
            // alternative is a screen that silently does nothing.
            Require(kind);
            return string.Empty;
        }

        /// <summary>Why a terminal does nothing yet, in one line, or empty when it works.</summary>
        public static string WhyNotConnected(HubTerminalKind kind)
        {
            Require(kind);
            return string.Empty;
        }

        private static void Require(HubTerminalKind kind)
        {
            switch (kind)
            {
                case HubTerminalKind.Mission:
                case HubTerminalKind.Growth:
                case HubTerminalKind.Database:
                case HubTerminalKind.Skills:
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a hub terminal.");
            }
        }
    }
}
