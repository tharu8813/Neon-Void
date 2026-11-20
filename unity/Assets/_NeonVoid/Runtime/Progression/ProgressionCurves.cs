namespace NeonVoid.Progression
{
    /// <summary>
    /// The [초안] XP numbers docs/04 section 1 states, in one place.
    /// <para>
    /// <b>Every number here is a draft.</b> docs/04 says so in the same sentence it gives them:
    /// 조정 전 승인 불필요한 밸런스 초안이지만 사용자 확정 수치가 아님. D-011 in docs/08 is the
    /// open decision that owns them, and its safe handling is to keep them as separable data.
    /// The <i>structure</i> around them is not a draft - [확정] R-011 and C09 require two
    /// separate levels, a mission that restarts at level 1, and permanent points earned from
    /// vaccine levels - so the structure is in code and these are the dials.
    /// </para>
    /// <para>
    /// They are consts rather than a ScriptableObject because nothing loads balance assets yet;
    /// every consumer takes a <see cref="LevelCurve"/> by value, so the asset can replace these
    /// defaults later without any of that code changing. The <c>Draft</c> prefix is deliberate:
    /// a reader who finds one of these in a diff should see it is unapproved without looking
    /// anything up.
    /// </para>
    /// </summary>
    public static class ProgressionCurves
    {
        /// <summary>[초안] 임무 레벨 100+30(L-1) XP (docs/04 section 1).</summary>
        public const long DraftMissionFirstStepXp = 100L;

        /// <summary>[초안] the +30 per level of the mission curve.</summary>
        public const long DraftMissionStepGrowthXp = 30L;

        /// <summary>[초안] 백신 레벨 180+90(L-1) XP (docs/04 section 1).</summary>
        public const long DraftVaccineFirstStepXp = 180L;

        /// <summary>[초안] the +90 per level of the vaccine curve.</summary>
        public const long DraftVaccineStepGrowthXp = 90L;

        /// <summary>
        /// [초안] 백신 레벨 1당 영구 포인트 1 (docs/04 section 1). The permanent points a vaccine
        /// level is worth; what they may be spent on is 영구 투자, which nothing implements yet.
        /// </summary>
        public const int DraftPointsPerVaccineLevel = 1;

        /// <summary>The draft mission (의뢰) curve. Reset to level 1 by every new mission.</summary>
        public static LevelCurve DraftMissionLevels =>
            new LevelCurve(DraftMissionFirstStepXp, DraftMissionStepGrowthXp);

        /// <summary>The draft vaccine (백신) curve. Permanent, across missions and deaths.</summary>
        public static LevelCurve DraftVaccineLevels =>
            new LevelCurve(DraftVaccineFirstStepXp, DraftVaccineStepGrowthXp);
    }
}
