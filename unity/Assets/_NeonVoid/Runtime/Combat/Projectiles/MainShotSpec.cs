using UnityEngine;

namespace NeonVoid.Combat.Projectiles
{
    /// <summary>
    /// What form the basic attack takes. docs/03's two 전설 cards replace the shot itself rather
    /// than adding to it: L01 레이저 코어 is 탄환을 레이저로 변환 and L02 부메랑 프로토콜 throws a
    /// 부메랑 that deals 범위 피해 and 귀환s.
    /// </summary>
    public enum MainShotMode
    {
        /// <summary>An ordinary travelling 탄환. What every attacker fires by default.</summary>
        Projectile = 0,

        /// <summary>L01: a hitscan beam, resolved on the tick it is fired.</summary>
        Laser = 1,

        /// <summary>L02: thrown out, damages an area, comes back.</summary>
        Boomerang = 2,
    }

    /// <summary>
    /// One form the basic attack is fired in, with the numbers that form needs.
    /// <para>
    /// <b>Why a spec pushed in rather than a flag on the attacker.</b> Combat must not reference
    /// Plugins, and the attacker must not learn which card changed its shot - the same rule
    /// <see cref="ExtraShotSpec"/> already follows. The cards and the policy that resolves them
    /// live in Plugins; what arrives here is a form and its numbers.
    /// </para>
    /// <para>
    /// <b>A list of these, not one.</b> [확정] D-005 (2026-11-23): carrying L01 and L02
    /// together fires both forms. An attacker that held a single mode could not express that at
    /// all, so it holds however many forms the loadout resolved to and fires each of them.
    /// </para>
    /// </summary>
    public readonly struct MainShotSpec
    {
        private MainShotSpec(
            MainShotMode mode,
            float onHitEffectScale,
            int count,
            float areaRadiusUnits,
            int outboundTicks,
            float rangeUnits,
            bool convertsExtraShots,
            bool piercesEverything)
        {
            Mode = mode;
            OnHitEffectScale = Fraction(onHitEffectScale);
            Count = Mathf.Max(1, count);
            AreaRadiusUnits = Positive(areaRadiusUnits);
            OutboundTicks = Mathf.Max(0, outboundTicks);
            RangeUnits = Positive(rangeUnits);
            ConvertsExtraShots = convertsExtraShots;
            PiercesEverything = piercesEverything;
        }

        /// <summary>Which form this is.</summary>
        public MainShotMode Mode { get; }

        /// <summary>
        /// Fraction of their damage the effects this shot triggers deal. [원안 수치] docs/03 L01
        /// states 적중 효과 효율 70%; an ordinary shot is 1.
        /// </summary>
        public float OnHitEffectScale { get; }

        /// <summary>
        /// How many of this form one volley fires. L02's 부메랑 수 - docs/03 says it grows with
        /// the card's level, and plugin levels are not built yet (docs/03 section 3 item 6), so
        /// the number arrives from the card's data and nothing is invented here.
        /// </summary>
        public int Count { get; }

        /// <summary>L02's 범위 피해 reach. 0 for the forms that hit one thing at a time.</summary>
        public float AreaRadiusUnits { get; }

        /// <summary>Ticks a 부메랑 travels outward before 귀환 begins.</summary>
        public int OutboundTicks { get; }

        /// <summary>How far a laser reaches. 0 for the forms that travel.</summary>
        public float RangeUnits { get; }

        /// <summary>
        /// Whether the shots other cards add - R06 보조 탄환's extra 탄환 - are fired in this
        /// form too. [확정] D-005: they are. The rule lives in Plugins, not here.
        /// </summary>
        public bool ConvertsExtraShots { get; }

        /// <summary>
        /// Whether this form ignores the attacker's 관통 budget and hits everything it reaches.
        /// [확정] D-005: it does not. Also decided in Plugins.
        /// </summary>
        public bool PiercesEverything { get; }

        /// <summary>The default: one travelling 탄환 at full effect strength.</summary>
        public static MainShotSpec Ordinary =>
            new MainShotSpec(MainShotMode.Projectile, 1f, 1, 0f, 0, 0f, false, false);

        /// <summary>L01 레이저 코어's form.</summary>
        public static MainShotSpec Laser(
            float onHitEffectScale, float rangeUnits, bool convertsExtraShots, bool piercesEverything) =>
            new MainShotSpec(
                MainShotMode.Laser, onHitEffectScale, 1, 0f, 0, rangeUnits,
                convertsExtraShots, piercesEverything);

        /// <summary>L02 부메랑 프로토콜's form.</summary>
        public static MainShotSpec Boomerang(
            int count, float areaRadiusUnits, int outboundTicks, float onHitEffectScale) =>
            new MainShotSpec(
                MainShotMode.Boomerang, onHitEffectScale, count, areaRadiusUnits, outboundTicks,
                0f, false, false);

        /// <summary>Whether this form replaces the ordinary 탄환.</summary>
        public bool ReplacesTheProjectile => Mode != MainShotMode.Projectile;

        /// <summary>Whether firing this form stops the attacker until it comes back.</summary>
        public bool HoldsTheAttackUntilItReturns => Mode == MainShotMode.Boomerang;

        private static float Fraction(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 1f : Mathf.Min(1f, value);

        private static float Positive(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;

        public override string ToString() => Mode switch
        {
            MainShotMode.Laser =>
                $"레이저 {RangeUnits:0.#}u, 효과 {OnHitEffectScale:P0}" +
                (PiercesEverything ? ", 무제한 관통" : string.Empty) +
                (ConvertsExtraShots ? ", 추가탄 변환" : string.Empty),
            MainShotMode.Boomerang =>
                $"부메랑 x{Count}, 범위 {AreaRadiusUnits:0.#}u, 왕복 {OutboundTicks}t, " +
                $"효과 {OnHitEffectScale:P0}",
            _ => "탄환",
        };
    }
}
