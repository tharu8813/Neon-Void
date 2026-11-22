using System;

namespace NeonVoid.Plugins
{
    /// <summary>The 보상 화면's difficulty input. [확정] docs/01: 5개 구역의 난이도.</summary>
    public enum RewardDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    /// <summary>
    /// [확정] D-006, approved 2026-11-22: candidate **B. 곡선**.
    /// <para>
    /// Chosen against <c>docs/D006_REWARD_SIMULATION.md</c>: it matches the most conservative
    /// candidate's drought (median 7 screens, p95 13) while making the late game 2.3x richer
    /// than the early one, which is what gives [확정] docs/01's <i>정화 1곳 후 철수 가능</i> a
    /// cost the player can feel. The other two candidates stay in the Editor simulator as the
    /// record of what was compared.
    /// </para>
    /// <para>
    /// <b>These numbers are data, not behaviour.</b> The roll is in
    /// <see cref="RewardRoller"/>; this is only what it reads, so retuning D-011 later is an
    /// edit here and nothing else.
    /// </para>
    /// </summary>
    public readonly struct RewardRollTable
    {
        /// <summary>How many grades the table covers. 신화 is the fourth and weighs nothing.</summary>
        public const int Grades = 4;

        /// <summary>
        /// [확정] docs/03 section 3 item 5: correction begins after five 보상 화면 with no
        /// 전설+, counted per screen and never per card or per reroll.
        /// </summary>
        public const int PityAfterScreens = Core.DesignConstants.HighTierPityStartsAfterMissedScreens;

        private RewardRollTable(
            float[] baseWeight,
            float[] luckPerPoint,
            float[] easyMul,
            float[] normalMul,
            float[] hardMul,
            float[] progressPerPurify,
            float pityGrowthPerScreen,
            float pityCap)
        {
            BaseWeight = baseWeight;
            LuckPerPoint = luckPerPoint;
            EasyMul = easyMul;
            NormalMul = normalMul;
            HardMul = hardMul;
            ProgressPerPurify = progressPerPurify;
            PityGrowthPerScreen = pityGrowthPerScreen;
            PityCap = pityCap;
        }

        public float[] BaseWeight { get; }
        public float[] LuckPerPoint { get; }
        public float[] EasyMul { get; }
        public float[] NormalMul { get; }
        public float[] HardMul { get; }
        public float[] ProgressPerPurify { get; }

        /// <summary>Added to the 전설+ weight multiplier per screen past the threshold.</summary>
        public float PityGrowthPerScreen { get; }

        /// <summary>The largest the pity multiplier may reach.</summary>
        public float PityCap { get; }

        /// <summary>
        /// The approved table. [확정] D-006 (2026-11-22), candidate B. 곡선.
        /// <para>
        /// 신화 weighs 0 everywhere: D-008 has not chosen any 신화 card, and a grade with no
        /// card behind it must never be rolled. It keeps its column so turning it on later is
        /// a number rather than a shape change.
        /// </para>
        /// </summary>
        public static RewardRollTable Approved() => new RewardRollTable(
            baseWeight: new[] { 80f, 18f, 2f, 0f },
            luckPerPoint: new[] { 0f, 0.012f, 0.035f, 0f },
            easyMul: new[] { 1.05f, 0.95f, 0.85f, 0f },
            normalMul: new[] { 1f, 1f, 1f, 0f },
            hardMul: new[] { 0.92f, 1.08f, 1.45f, 0f },
            progressPerPurify: new[] { -0.06f, 0.10f, 0.35f, 0f },
            pityGrowthPerScreen: 0.70f,
            pityCap: 6f);

        public float[] MulFor(RewardDifficulty difficulty)
        {
            switch (difficulty)
            {
                case RewardDifficulty.Easy: return EasyMul;
                case RewardDifficulty.Hard: return HardMul;
                default: return NormalMul;
            }
        }

        /// <summary>
        /// The 전설+ weight multiplier for a run of screens with nothing high. 1 until the
        /// threshold is passed, then growing, then capped.
        /// </summary>
        public float PityMultiplier(int screensWithoutHighTier)
        {
            int past = screensWithoutHighTier - PityAfterScreens;
            if (past <= 0)
                return 1f;

            float multiplier = 1f + (PityGrowthPerScreen * past);
            return multiplier > PityCap ? PityCap : multiplier;
        }

        /// <summary>
        /// The weight of one grade under the given conditions, before normalising. Never
        /// negative: a correction large enough to drive a weight below zero would otherwise
        /// start adding to the total again.
        /// </summary>
        public float WeightOf(
            PluginRarity rarity,
            RewardDifficulty difficulty,
            int purifiedThisMission,
            int luck,
            float pityMultiplier)
        {
            int index = (int)rarity;
            if (index < 0 || index >= Grades)
                return 0f;

            float weight = BaseWeight[index];
            if (weight <= 0f)
                return 0f;

            weight *= MulFor(difficulty)[index];
            weight *= 1f + (LuckPerPoint[index] * Math.Max(0, luck));
            weight *= 1f + (ProgressPerPurify[index] * Math.Max(0, purifiedThisMission));

            if (rarity >= PluginRarity.Legendary)
                weight *= pityMultiplier;

            return weight > 0f ? weight : 0f;
        }
    }
}
