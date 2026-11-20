using System;

namespace NeonVoid.Progression
{
    /// <summary>
    /// What a vaccine's growth looks like after XP was granted. All three permanent fields
    /// together, because a level gained is also points gained and writing one without the other
    /// is the bug this type exists to make impossible.
    /// </summary>
    public readonly struct VaccineGrowth
    {
        public readonly int Level;

        /// <summary>Cumulative approved XP. Only ever rises, except at 최종 파기.</summary>
        public readonly long TotalXp;

        public readonly int UnusedStatPoints;

        /// <summary>Levels this grant produced, for the receipt and for the level-up screen.</summary>
        public readonly int LevelsGained;

        /// <summary>Points this grant produced from those levels, not counting any settled separately.</summary>
        public readonly int PointsGained;

        public VaccineGrowth(int level, long totalXp, int unusedStatPoints, int levelsGained, int pointsGained)
        {
            Level = level;
            TotalXp = totalXp;
            UnusedStatPoints = unusedStatPoints;
            LevelsGained = levelsGained;
            PointsGained = pointsGained;
        }

        public override string ToString() =>
            $"Lv.{Level} xp={TotalXp} points={UnusedStatPoints} (+{LevelsGained}Lv +{PointsGained}p)";
    }

    /// <summary>
    /// The permanent half of docs/04 section 1: a vaccine's XP curve and what a level is worth
    /// in 영구 포인트.
    /// <para>
    /// <b>Why permanent XP had a level that never moved.</b> The save document has carried
    /// <c>permanentLevel</c>, <c>permanentXp</c> and <c>unusedStatPoints</c> since it was
    /// written, and the death settlement added to the XP - but nothing ever turned XP into a
    /// level, so the level sat at 1 for the life of a vaccine and 백신 레벨 1당 영구 포인트 1
    /// awarded nothing. The number went up and the thing it was for did not, which is the
    /// failure shape this project keeps finding one layer at a time. This is the missing step,
    /// and it runs <i>inside</i> the save transaction so a committed XP total and the level read
    /// off it can never disagree.
    /// </para>
    /// <para>
    /// <b>The level is a floor, not a formula.</b> <see cref="Grant"/> advances the level as far
    /// as the new total allows and never below the level already approved. That is what lets the
    /// [초안] curve be retuned: a dearer curve leaves existing vaccines above it (nothing is
    /// taken back - [확정] docs/04 section 2) and a cheaper one lets them catch up on their next
    /// grant. It is also why <see cref="Save.VaccineSaveState.Validate"/> does not re-derive the
    /// level from the XP and refuse a mismatch: every live save would fail to load the day the
    /// curve was tuned, and a save that will not load is worse than a level a tuning pass left
    /// generous.
    /// </para>
    /// Pure C#: no Unity types, no floating point, no storage.
    /// </summary>
    public readonly struct VaccineLevelPolicy
    {
        public readonly LevelCurve Levels;

        /// <summary>영구 포인트 per level gained.</summary>
        public readonly int PointsPerLevel;

        public VaccineLevelPolicy(LevelCurve levels, int pointsPerLevel)
        {
            if (!levels.IsValid)
                throw new ArgumentException("A level policy needs a constructed curve.", nameof(levels));
            if (pointsPerLevel < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pointsPerLevel), pointsPerLevel, "A level cannot cost points.");
            }

            Levels = levels;
            PointsPerLevel = pointsPerLevel;
        }

        /// <summary>The [초안] policy from docs/04 section 1. See <see cref="ProgressionCurves"/>.</summary>
        public static VaccineLevelPolicy Draft =>
            new VaccineLevelPolicy(
                ProgressionCurves.DraftVaccineLevels, ProgressionCurves.DraftPointsPerVaccineLevel);

        public bool IsValid => Levels.IsValid && PointsPerLevel >= 0;

        /// <summary>
        /// Adds XP and levels up as far as the total now reaches, awarding points for each level
        /// gained.
        /// <para>
        /// A grant of 0 is not always a no-op, and that is intentional: it is the path by which a
        /// vaccine whose level is behind a retuned curve catches up. What it can never do is
        /// lower a level, lower a total or lower a point count.
        /// </para>
        /// </summary>
        public VaccineGrowth Grant(int level, long totalXp, int unusedStatPoints, long gainedXp)
        {
            if (!IsValid)
                throw new InvalidOperationException("This policy was defaulted rather than constructed.");
            if (level < 1)
                throw new ArgumentOutOfRangeException(nameof(level), level, "Levels start at 1.");
            if (totalXp < 0L)
                throw new ArgumentOutOfRangeException(nameof(totalXp), totalXp, "XP totals are never negative.");
            if (unusedStatPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(unusedStatPoints), unusedStatPoints, "Points are never negative.");
            if (gainedXp < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gainedXp), gainedXp,
                    "XP is never taken away ([확정] docs/04 section 2); a loss would have to be a different command.");
            }

            long nextTotal = totalXp + gainedXp;
            int nextLevel = Levels.AdvanceFrom(level, nextTotal);
            int levelsGained = nextLevel - level;
            int pointsGained = levelsGained * PointsPerLevel;

            return new VaccineGrowth(
                nextLevel, nextTotal, unusedStatPoints + pointsGained, levelsGained, pointsGained);
        }

        public override string ToString() => $"{Levels} @ {PointsPerLevel}p/level";
    }
}
