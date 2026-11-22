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
    /// Which reading of docs/03's two 전설 shot cards is in force when they meet each other, the
    /// extra-shot cards, or 관통.
    /// <para>
    /// <b>[미확정] D-005</b> (docs/08): 레이저+부메랑+다중탄 복합 발사체 정의 — 동시 보유 의미와
    /// 주인공 체감 승인 대기. docs/03's own exception columns say it twice: L01's reads
    /// <i>L02·R06·관통과 동시 장착 정책 미확정</i> and L02's reads <i>L01과 동시 적용 시 여러
    /// 발사체 정의</i>. Each card on its own is fully specified and is implemented; it is only
    /// their combination that nobody has decided.
    /// </para>
    /// <para>
    /// <b>Every reading is implemented and one constant selects it.</b> The cards and the
    /// attacker read these constants and nothing else, so approving a reading is a one-line
    /// change here with no new code - the shape this project already uses for
    /// <see cref="EmergencyRecoveryPolicy"/>, ShieldPolicy and LifestealPolicy. Guessing in the
    /// catalogue instead would bury the undecided rule in data that looks settled.
    /// </para>
    /// </summary>
    public static class ShotConversionPolicy
    {
        /// <summary>The readings of carrying L01 and L02 at the same time.</summary>
        public enum BothCarried
        {
            /// <summary>
            /// The volley fires both: a beam and the 부메랑s, each at its own card's effect
            /// fraction. 여러 발사체, which is what docs/03 asks to have defined.
            /// </summary>
            BothForms = 0,

            /// <summary>L01 converts the shot and L02 then has no shot left to replace.</summary>
            LaserReplacesIt = 1,

            /// <summary>L02 replaces the shot and L01 has no 탄환 left to convert.</summary>
            BoomerangReplacesIt = 2,
        }

        /// <summary>
        /// [미확정] the reading in force. <see cref="BothCarried.BothForms"/>, because it is the
        /// only one of the three under which both 전설 cards still do something: the other two
        /// make the card the player picked up second - or first - worth nothing at all, and a
        /// dead 전설 is worse than a strong one.
        /// <para>
        /// <b>Stated risk.</b> This is also the strongest reading. Two 전설 cards together then
        /// give a beam and a 부메랑 volley per attack, and the 부메랑's hold (귀환 전 추가 기본공격
        /// 불가) sets the rate of both, so the pair's real damage depends on the 부메랑 round
        /// trip more than on 공격속도. That is a balance question for the 기획자, not something
        /// a constant should answer quietly, which is why this stays [미확정] until D-005.
        /// </para>
        /// </summary>
        public const BothCarried Draft = BothCarried.BothForms;

        /// <summary>
        /// [미확정] whether L01 also converts the shots other cards add - R06 보조 탄환's extra
        /// 탄환, and anything later that adds one.
        /// <para>
        /// True: 탄환을 레이저로 변환 reads as every 탄환 the attack fires, and an extra 탄환 is a
        /// 탄환. The two fractions compound, so R06's 80% shot fired as a 70% beam carries 56%
        /// of the effect damage - each card cut only what it added.
        /// </para>
        /// </summary>
        public const bool DraftLaserConvertsExtraShots = true;

        /// <summary>
        /// [미확정] whether L01's beam ignores 관통 and damages everything it reaches.
        /// <para>
        /// False: the beam respects the attacker's 관통 budget exactly as a 탄환 does. A laser
        /// that pierced without limit would make the 관통 stat meaningless on the only build
        /// that most wants it, and would turn one card into unbounded multi-target damage in a
        /// corridor of enemies - a much bigger card than 적중 효과 효율 70% suggests.
        /// </para>
        /// </summary>
        public const bool DraftLaserPiercesEverything = false;

        /// <summary>Whether L01's conversion applies at all, given what else is carried.</summary>
        public static bool LaserApplies(bool carriesLaser, bool carriesBoomerang)
        {
            if (!carriesLaser)
                return false;

            if (!carriesBoomerang)
                return true;

            return Draft != BothCarried.BoomerangReplacesIt;
        }

        /// <summary>Whether L02's conversion applies at all, given what else is carried.</summary>
        public static bool BoomerangApplies(bool carriesLaser, bool carriesBoomerang)
        {
            if (!carriesBoomerang)
                return false;

            if (!carriesLaser)
                return true;

            return Draft != BothCarried.LaserReplacesIt;
        }

        /// <summary>
        /// What the 기획자 is being asked to decide, in one line, for a log or a report. Written
        /// here so the question and the constant above can never describe different things.
        /// </summary>
        public static string UndecidedSummary =>
            $"[미확정] D-005 L01+L02 동시 보유: 현재 임시 규칙은 {Draft}" +
            $" / L01+R06 추가탄 변환 {(DraftLaserConvertsExtraShots ? "적용" : "미적용")}" +
            $" / L01+관통 {(DraftLaserPiercesEverything ? "무제한" : "관통 상한 준수")}";
    }
}
