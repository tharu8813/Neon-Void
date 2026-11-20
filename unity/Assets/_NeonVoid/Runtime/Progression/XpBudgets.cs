using System;
using NeonVoid.Core;

namespace NeonVoid.Progression
{
    /// <summary>
    /// 의뢰 XP per hardware area. `구역 XP = round(720 × (1 + 0.18 × 이미 정화한 하드웨어 수))`
    /// from GDD v1 section 5.4, marked there [초안 v0.1].
    /// <para>
    /// docs/04 section 1 carries only the first area's figure (기본 720 임무 XP/구역). The
    /// scaling is in the historical GDD, which docs/08 keeps as the source whose 확정/초안 tags
    /// stand - so the scaling is a draft, not an invention, and the budget rises with the areas
    /// already cleared rather than being flat.
    /// </para>
    /// <para>
    /// <b>Integer arithmetic, rounded half up.</b> No float appears here: the save document
    /// forbids them (its checksum is verified by re-serializing), and an XP award computed in
    /// floating point would be the one number that crossed that line. The percentage is
    /// multiplied before it is divided, so the rounding happens once.
    /// </para>
    /// </summary>
    public static class MissionXpBudget
    {
        /// <summary>[초안 v0.1] 720, the first area's whole budget (GDD v1 section 5.4).</summary>
        public const long DraftFirstAreaXp = 720L;

        /// <summary>[초안 v0.1] the 0.18 of the scaling formula, as a percentage.</summary>
        public const long DraftScalingPercentPerPurification = 18L;

        // --- GDD v1 section 5.4's breakdown of the 720 -------------------------------------
        //
        // The document does not give the area a lump sum; it gives a table of 지급 원인 that adds
        // up to one. So the XP is awarded per cause - a kill, an objective, the boss - and
        // XpForArea is the budget those causes are supposed to sum to, not something handed over
        // on arrival. DraftBudgetAddsUp holds the arithmetic.

        /// <summary>[초안 v0.1] 일반 적 처치 8 XP.</summary>
        public const long DraftXpPerNormalKill = 8L;

        /// <summary>[초안 v0.1] 예상 횟수 48 - the normal enemies one area is budgeted for.</summary>
        public const int DraftNormalKillsPerArea = 48;

        /// <summary>[초안 v0.1] 정예 적 처치 32 XP.</summary>
        public const long DraftXpPerEliteKill = 32L;

        /// <summary>[초안 v0.1] 예상 횟수 3.</summary>
        public const int DraftElitesPerArea = 3;

        /// <summary>[초안 v0.1] 주요 목표 달성 40 XP.</summary>
        public const long DraftXpPerObjective = 40L;

        /// <summary>[초안 v0.1] 예상 횟수 3, which is also docs/01's 2~3개 소목표.</summary>
        public const int DraftObjectivesInBudget = 3;

        /// <summary>[초안 v0.1] 보스 처치 120 XP.</summary>
        public const long DraftXpPerBoss = 120L;

        /// <summary>
        /// The breakdown's own total, so the table and the budget can be held against each other
        /// by a test rather than by a reader adding up a document.
        /// </summary>
        public static long DraftBudgetBreakdownAtNominalSize =>
            (DraftXpPerNormalKill * DraftNormalKillsPerArea)
            + (DraftXpPerEliteKill * DraftElitesPerArea)
            + (DraftXpPerObjective * DraftObjectivesInBudget)
            + DraftXpPerBoss;

        /// <summary>의뢰 XP for one normal enemy, at this point in the mission.</summary>
        public static long XpForNormalKill(int areasAlreadyPurified) =>
            Scaled(DraftXpPerNormalKill, areasAlreadyPurified);

        /// <summary>의뢰 XP for one 정예.</summary>
        public static long XpForEliteKill(int areasAlreadyPurified) =>
            Scaled(DraftXpPerEliteKill, areasAlreadyPurified);

        /// <summary>의뢰 XP for one 주요 목표.</summary>
        public static long XpForObjective(int areasAlreadyPurified) =>
            Scaled(DraftXpPerObjective, areasAlreadyPurified);

        /// <summary>의뢰 XP for the area's boss.</summary>
        public static long XpForBoss(int areasAlreadyPurified) =>
            Scaled(DraftXpPerBoss, areasAlreadyPurified);

        /// <summary>
        /// The 의뢰 XP budget one more area is worth to a mission that has already purified
        /// <paramref name="areasAlreadyPurified"/> of them.
        /// <para>
        /// A <b>budget</b>, not an award. Nothing hands this over: it is what the area's kills,
        /// objectives and boss add up to when the player clears all of it, and a test holds the
        /// two together. An area the player half-clears is worth less, which is the point.
        /// </para>
        /// </summary>
        public static long XpForArea(int areasAlreadyPurified)
        {
            return Scaled(DraftFirstAreaXp, areasAlreadyPurified);
        }

        /// <summary>
        /// A nominal figure at this point in the mission, by the same formula the whole budget
        /// scales with. Rounded half up in integers, because the save document forbids floats and
        /// an XP award must not be the exception.
        /// </summary>
        private static long Scaled(long nominal, int areasAlreadyPurified)
        {
            if (areasAlreadyPurified < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(areasAlreadyPurified), areasAlreadyPurified, "Purified areas are counted from 0.");
            }

            // Guarded rather than clamped: more purified areas than there are hardware areas
            // means a purification was counted twice, and silently scaling the reward for it
            // would pay for the bug.
            if (areasAlreadyPurified >= DesignConstants.HardwareCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(areasAlreadyPurified), areasAlreadyPurified,
                    $"There are {DesignConstants.HardwareCount} hardware areas, so none can be the " +
                    $"{areasAlreadyPurified + 1}th to be purified.");
            }

            long percent = 100L + (DraftScalingPercentPerPurification * areasAlreadyPurified);
            return ((nominal * percent) + 50L) / 100L;
        }
    }

    /// <summary>
    /// 백신 영구 XP earned by a mission, from GDD v1 section 5.6, marked there [초안 v0.1]:
    /// 주요 목표 1개당 15XP, 하드웨어 정화 120XP, 5개 전부 정화 뒤 성공 철수 시 220XP 추가.
    /// <para>
    /// <b>This is 미정산 until an extraction commits it.</b> The GDD's own table reads the
    /// extraction total off the objectives and purifications of the whole mission, which is what
    /// makes it the unbanked total docs/04 section 2 settles a share of on death. So the figures
    /// are computed from what the mission did, and only <see cref="Save.ExtractRunCommand"/> -
    /// or, once D-012 is decided, the death settlement - turns them into permanent state.
    /// </para>
    /// <para>
    /// Separate from <see cref="MissionXpBudget"/> because they are different currencies:
    /// docs/04 section 1 makes RunXP 임시 카드 지급 and VaccineXP the permanent score, and
    /// [확정] C09 forbids mixing them.
    /// </para>
    /// </summary>
    public static class VaccineXpBudget
    {
        /// <summary>[초안 v0.1] 주요 목표 1개당 15XP.</summary>
        public const long DraftXpPerObjective = 15L;

        /// <summary>[초안 v0.1] 하드웨어 정화 120XP.</summary>
        public const long DraftXpPerPurification = 120L;

        /// <summary>[초안 v0.1] 5개 전부 정화 뒤 성공 철수 시 220XP 추가.</summary>
        public const long DraftFullClearBonusXp = 220L;

        /// <summary>
        /// [초안] the GDD's own 환산 for its published table: 구역당 목표 3개. A real mission
        /// reports the objectives it actually completed; this is only for reproducing the table.
        /// </summary>
        public const int DraftObjectivesPerArea = 3;

        /// <summary>
        /// The unbanked 백신 XP a mission has earned.
        /// </summary>
        /// <param name="purifiedAreas">정화한 하드웨어 수.</param>
        /// <param name="objectivesCompleted">주요 목표 달성 수, across the whole mission.</param>
        public static long XpEarned(int purifiedAreas, int objectivesCompleted)
        {
            RequireAreas(purifiedAreas);
            if (objectivesCompleted < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objectivesCompleted), objectivesCompleted, "Objectives are counted from 0.");
            }

            long xp = (DraftXpPerPurification * purifiedAreas) + (DraftXpPerObjective * objectivesCompleted);

            // The bonus is for 5개 전부 정화, so it is paid on the count reaching the number of
            // hardware areas there are - not on a hardcoded 5 that would quietly stop being the
            // same thing if the hardware list ever changed.
            if (purifiedAreas >= DesignConstants.HardwareCount)
                xp += DraftFullClearBonusXp;

            return xp;
        }

        /// <summary>
        /// The same total under the GDD's 환산 of 3 objectives per area, which is the row its
        /// published table shows. Used to hold that table in a test; a real mission counts its
        /// own objectives.
        /// </summary>
        public static long XpEarnedAtStandardObjectiveRate(int purifiedAreas)
        {
            RequireAreas(purifiedAreas);
            return XpEarned(purifiedAreas, purifiedAreas * DraftObjectivesPerArea);
        }

        private static void RequireAreas(int purifiedAreas)
        {
            if (purifiedAreas < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(purifiedAreas), purifiedAreas, "Purified areas are counted from 0.");
            }

            if (purifiedAreas > DesignConstants.HardwareCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(purifiedAreas), purifiedAreas,
                    $"There are only {DesignConstants.HardwareCount} hardware areas to purify.");
            }
        }
    }
}
