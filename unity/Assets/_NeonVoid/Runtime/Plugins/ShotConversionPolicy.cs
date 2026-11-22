namespace NeonVoid.Plugins
{
    /// <summary>What a card does to the form of the basic attack.</summary>
    public enum PluginShotConversion
    {
        /// <summary>Nothing. Every card but the two 전설 shot cards.</summary>
        None = 0,

        /// <summary>L01 레이저 코어: 탄환을 레이저로 변환.</summary>
        Laser = 1,

        /// <summary>L02 부메랑 프로토콜: 부메랑 범위 피해 + 귀환.</summary>
        Boomerang = 2,
    }

    /// <summary>
    /// What docs/03's two 전설 shot cards do when they meet each other, the extra-shot cards, or
    /// 관통.
    /// <para>
    /// <b>[확정] D-005 (2026-11-23):</b> 레이저+부메랑+다중탄 복합 발사체 정의. Three rules were
    /// approved together and they are the three constants below: 동시 보유 시 <b>둘 다 발사</b>,
    /// L01은 R06의 <b>추가탄도 변환</b>, L01의 빔은 <b>관통 상한을 지킨다</b>. docs/03's own
    /// exception columns asked for exactly these - L01's read <i>L02·R06·관통과 동시 장착 정책
    /// 미확정</i> and L02's read <i>L01과 동시 적용 시 여러 발사체 정의</i>.
    /// </para>
    /// <para>
    /// <b>The readings that were not chosen are still implemented.</b> They are not what the
    /// game does, and a test asserts the approved values are what ships; they stay because a
    /// balance change to an approved rule is then one constant rather than new code - the same
    /// shape <see cref="NeonVoid.Combat.ShieldPolicy"/> keeps after D-004.
    /// </para>
    /// </summary>
    public static class ShotConversionPolicy
    {
        /// <summary>The readings of carrying L01 and L02 at the same time.</summary>
        public enum BothCarried
        {
            /// <summary>
            /// The volley fires both: a beam and the 부메랑s, each at its own card's effect
            /// fraction. 여러 발사체, which is what docs/03 asked to have defined. <b>Approved.</b>
            /// </summary>
            BothForms = 0,

            /// <summary>L01 converts the shot and L02 then has no shot left to replace.</summary>
            LaserReplacesIt = 1,

            /// <summary>L02 replaces the shot and L01 has no 탄환 left to convert.</summary>
            BoomerangReplacesIt = 2,
        }

        /// <summary>
        /// [확정] D-005 (2026-11-23): carrying both fires <b>both forms</b>.
        /// <para>
        /// It is the only one of the three readings under which both 전설 cards still do
        /// something: the other two make whichever card the player picked up second worth
        /// nothing, and a dead 전설 is worse than a strong one.
        /// </para>
        /// <para>
        /// <b>What this is known to cost.</b> It is also the strongest reading. Two 전설 cards
        /// together give a beam and a 부메랑 volley per attack, and the 부메랑's hold
        /// (귀환 전 추가 기본공격 불가) sets the rate of both, so the pair's real damage depends
        /// on the 부메랑 round trip more than on 공격속도. That was stated when the reading was
        /// approved; it is a balance figure to watch, not a surprise.
        /// </para>
        /// </summary>
        public const BothCarried Approved = BothCarried.BothForms;

        /// <summary>
        /// [확정] D-005: L01 also converts the shots other cards add - R06 보조 탄환's extra
        /// 탄환, and anything later that adds one.
        /// <para>
        /// 탄환을 레이저로 변환 reads as every 탄환 the attack fires, and an extra 탄환 is a 탄환.
        /// The two fractions compound, so R06's 80% shot fired as a 70% beam carries 56% of the
        /// effect damage - each card cuts only what it added.
        /// </para>
        /// </summary>
        public const bool ApprovedLaserConvertsExtraShots = true;

        /// <summary>
        /// [확정] D-005: L01's beam respects the attacker's 관통 budget exactly as a 탄환 does.
        /// <para>
        /// A laser that pierced without limit would make the 관통 stat meaningless on the build
        /// that most wants it, and would turn one card into unbounded multi-target damage in a
        /// corridor of enemies - a much bigger card than 적중 효과 효율 70% suggests.
        /// </para>
        /// </summary>
        public const bool ApprovedLaserPiercesEverything = false;

        /// <summary>Whether L01's conversion applies at all, given what else is carried.</summary>
        public static bool LaserApplies(bool carriesLaser, bool carriesBoomerang)
        {
            if (!carriesLaser)
                return false;

            if (!carriesBoomerang)
                return true;

            return Approved != BothCarried.BoomerangReplacesIt;
        }

        /// <summary>Whether L02's conversion applies at all, given what else is carried.</summary>
        public static bool BoomerangApplies(bool carriesLaser, bool carriesBoomerang)
        {
            if (!carriesBoomerang)
                return false;

            if (!carriesLaser)
                return true;

            return Approved != BothCarried.LaserReplacesIt;
        }

        /// <summary>
        /// The approved combination rules in one line, for a log or a report. Written here so
        /// the sentence and the constants above can never describe different things.
        /// </summary>
        public static string ApprovedSummary =>
            $"[확정] D-005 L01+L02 동시 보유: {Approved}" +
            $" / L01+R06 추가탄 변환 {(ApprovedLaserConvertsExtraShots ? "적용" : "미적용")}" +
            $" / L01+관통 {(ApprovedLaserPiercesEverything ? "무제한" : "관통 상한 준수")}";
    }
}
