using System;

namespace NeonVoid.Progression
{
    /// <summary>
    /// A linear-step level curve: the cost of the first level, plus a fixed amount added to
    /// every step after it.
    /// <para>
    /// docs/04 section 1 writes both of this game's curves in exactly that shape -
    /// 임무 레벨 100+30(L-1) XP and 백신 레벨 180+90(L-1) XP - so the shape is the type and the
    /// numbers stay data. The two levels must never be mixed into one variable ([확정] C09 in
    /// docs/08), and giving them one curve *type* rather than one curve *instance* is what makes
    /// that structural instead of a rule somebody has to remember.
    /// </para>
    /// <para>
    /// <b>Totals, not remainders.</b> A level is read off the player's cumulative XP, and XP is
    /// never spent or deducted - [확정] docs/04 section 2: 이미 승인된 영구 XP/포인트는 빼앗지
    /// 않는다. So <see cref="XpToAdvanceFrom"/> is a step width and
    /// <see cref="CumulativeXpForLevel"/> is the threshold a total has to reach; nothing here
    /// subtracts.
    /// </para>
    /// Pure C#: no Unity types, no floating point.
    /// </summary>
    public readonly struct LevelCurve
    {
        /// <summary>
        /// A ceiling on the level this arithmetic will walk to. <b>Not a design cap</b> - docs
        /// state none, and D-011 in docs/08 still owes the XP economy its numbers. It is here so
        /// that <see cref="CumulativeXpForLevel"/>'s quadratic term cannot be handed a level
        /// large enough to overflow, and so a corrupt level in a save cannot spin a loop.
        /// </summary>
        public const int MaxSupportedLevel = 100_000;

        /// <summary>XP needed to go from level 1 to level 2.</summary>
        public readonly long FirstStepXp;

        /// <summary>Added to each following step, so step L costs First + Growth × (L-1).</summary>
        public readonly long StepGrowthXp;

        public LevelCurve(long firstStepXp, long stepGrowthXp)
        {
            // A first step of zero would make every level free and the "walk while the total
            // reaches the next threshold" loop below never terminate. The termination of that
            // loop rests on this check, so it is an argument check and not a Validate() a
            // caller might skip.
            if (firstStepXp < 1L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firstStepXp), firstStepXp, "A level has to cost at least 1 XP.");
            }

            if (stepGrowthXp < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stepGrowthXp), stepGrowthXp,
                    "A negative growth would make later levels cheaper and eventually free.");
            }

            FirstStepXp = firstStepXp;
            StepGrowthXp = stepGrowthXp;
        }

        /// <summary>Whether this curve was constructed rather than defaulted.</summary>
        public bool IsValid => FirstStepXp >= 1L && StepGrowthXp >= 0L;

        /// <summary>XP the step from <paramref name="level"/> to the next one costs.</summary>
        public long XpToAdvanceFrom(int level)
        {
            RequireLevel(level);
            return FirstStepXp + (StepGrowthXp * (level - 1));
        }

        /// <summary>
        /// Cumulative XP a total must reach to be at <paramref name="level"/>. Level 1 is 0:
        /// everybody starts there, and docs/04 section 1 makes level 1 the start of both curves.
        /// </summary>
        public long CumulativeXpForLevel(int level)
        {
            RequireLevel(level);

            long steps = level - 1;
            // Sum of First + Growth×0, First + Growth×1, ... over `steps` terms.
            return (steps * FirstStepXp) + (StepGrowthXp * steps * (steps - 1) / 2L);
        }

        /// <summary>
        /// The highest level <paramref name="totalXp"/> reaches, never going below
        /// <paramref name="fromLevel"/>.
        /// <para>
        /// The floor matters: an already-approved level is never taken away, so a curve that was
        /// retuned to be more expensive leaves a player above it rather than demoting them.
        /// </para>
        /// </summary>
        public int AdvanceFrom(int fromLevel, long totalXp)
        {
            RequireLevel(fromLevel);
            if (totalXp < 0L)
                throw new ArgumentOutOfRangeException(nameof(totalXp), totalXp, "XP totals are never negative.");

            int reached = fromLevel;
            while (reached < MaxSupportedLevel && totalXp >= CumulativeXpForLevel(reached + 1))
                ++reached;

            return reached;
        }

        /// <summary>The level a fresh total reaches, starting from level 1.</summary>
        public int LevelFor(long totalXp) => AdvanceFrom(1, totalXp);

        /// <summary>
        /// How far into <paramref name="level"/> a total is, for a progress display. Clamped at
        /// 0, because a level above what the total pays for is a legitimate state (see
        /// <see cref="AdvanceFrom"/>) and a negative progress bar is not.
        /// </summary>
        public long XpIntoLevel(int level, long totalXp)
        {
            RequireLevel(level);
            if (totalXp < 0L)
                throw new ArgumentOutOfRangeException(nameof(totalXp), totalXp, "XP totals are never negative.");

            long into = totalXp - CumulativeXpForLevel(level);
            return into < 0L ? 0L : into;
        }

        private static void RequireLevel(int level)
        {
            if (level < 1)
                throw new ArgumentOutOfRangeException(nameof(level), level, "Levels start at 1.");
            if (level > MaxSupportedLevel)
                throw new ArgumentOutOfRangeException(
                    nameof(level), level, $"This arithmetic is only defined up to level {MaxSupportedLevel}.");
        }

        public override string ToString() => $"curve(+{FirstStepXp} then +{StepGrowthXp}/level)";
    }
}
