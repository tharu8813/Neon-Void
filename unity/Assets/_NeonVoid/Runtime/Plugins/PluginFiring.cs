using System;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// What a plugin does to the basic attack itself, rather than to a stat or on a hit.
    /// <para>
    /// docs/03 has two of these in the original catalogue and they are the same shape: R06
    /// 보조 탄환 adds 발사 탄환 1개, with 추가탄 적중 효과 피해 80%, and G13 4중 캐시 fires
    /// 탄환 4발 추가 연사 after 적중 효과 4회, with those shots at 70%. Both numbers are in the
    /// document, so both are reproduced exactly.
    /// </para>
    /// <para>
    /// A third kind of contribution rather than a stretched <see cref="PluginPassive"/>: a
    /// passive is a term in the stat snapshot, and an extra projectile is not a stat. Writing it
    /// as one would mean inventing a "projectile count" stat that only two cards ever read and
    /// that the cap and validation pipeline would have to carry for everyone.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PluginFiring
    {
        [Tooltip("Projectiles added to every volley. docs/03 R06 보조 탄환 adds 1.")]
        public int ExtraShotsPerVolley;

        [Tooltip("[원안 수치] fraction of effect damage those extra shots deal. R06: 0.80.")]
        public float ExtraShotOnHitScale;

        [Tooltip(
            "[초안] degrees per tick the extra shots steer towards a target. docs/03 R07 " +
            "자동 추적탄 is 유도탄 발사 and gives no number. 0 for every other card.")]
        public float ExtraShotHomingTurnDegreesPerTick;

        [Tooltip("Resolved hit effects that earn a burst. docs/03 G13 4중 캐시 counts 4.")]
        public int HitEffectsPerBurst;

        [Tooltip("Projectiles in that burst. G13 fires 4.")]
        public int BurstShots;

        [Tooltip("[원안 수치] fraction of effect damage the burst's shots deal. G13: 0.70.")]
        public float BurstOnHitScale;

        [Header("Main shot")]
        [Tooltip(
            "What this card turns the basic attack into. docs/03 L01 레이저 코어 converts the " +
            "탄환 to a 레이저; L02 부메랑 프로토콜 replaces it with a 부메랑. None for every " +
            "other card.")]
        public PluginShotConversion ShotConversion;

        [Tooltip("[원안 수치] fraction of effect damage the converted shot deals. L01: 0.70.")]
        public float MainShotOnHitScale;

        [Tooltip(
            "How many of the converted form one volley fires. L02's 부메랑 수 - docs/03 says it " +
            "grows with the card's level, so the number lives here as data rather than in code.")]
        public int MainShotCount;

        [Tooltip("[초안] L02's 범위 피해 reach in units. docs/03 gives no number.")]
        public float MainShotAreaRadiusUnits;

        [Tooltip("[초안] ticks a 부메랑 flies out before 귀환 begins.")]
        public int MainShotOutboundTicks;

        [Tooltip("[초안] how far L01's beam reaches in units. docs/03 gives no range.")]
        public float MainShotRangeUnits;

        public bool AddsExtraShots => ExtraShotsPerVolley > 0;

        public bool FiresBursts => HitEffectsPerBurst > 0 && BurstShots > 0;

        /// <summary>Whether this card changes what the basic attack IS, not what it adds.</summary>
        public bool ConvertsMainShot => ShotConversion != PluginShotConversion.None;

        public bool IsEmpty => !AddsExtraShots && !FiresBursts && !ConvertsMainShot;

        public static PluginFiring None => default;

        public void Validate(string proposalId)
        {
            if (ExtraShotsPerVolley < 0 || BurstShots < 0 || HitEffectsPerBurst < 0)
                throw new InvalidOperationException($"{proposalId}: a negative shot count is not a thing.");

            if (AddsExtraShots)
                RequireFraction(ExtraShotOnHitScale, $"{proposalId}: {nameof(ExtraShotOnHitScale)}");

            if (float.IsNaN(ExtraShotHomingTurnDegreesPerTick)
                || float.IsInfinity(ExtraShotHomingTurnDegreesPerTick)
                || ExtraShotHomingTurnDegreesPerTick < 0f)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: {nameof(ExtraShotHomingTurnDegreesPerTick)} must be zero or a " +
                    "positive turn rate.");
            }

            // A turn rate on a card that adds no shots is a rate nothing ever reads: the card
            // would read as 유도탄 발사 and fire nothing to guide.
            if (ExtraShotHomingTurnDegreesPerTick > 0f && !AddsExtraShots)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: declares a homing turn rate but adds no shots to guide.");
            }

            // A burst counter with no shots, or shots with no counter, is a card that looks like
            // it does something and never will. Either half alone is the silent-no-op shape
            // this project keeps finding, so the data refuses it.
            if (HitEffectsPerBurst > 0 && BurstShots == 0)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: counts hit effects towards a burst of no shots, so nothing would ever happen.");
            }

            if (BurstShots > 0 && HitEffectsPerBurst == 0)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: declares burst shots with nothing to earn them, so the burst would never fire.");
            }

            if (FiresBursts)
                RequireFraction(BurstOnHitScale, $"{proposalId}: {nameof(BurstOnHitScale)}");

            ValidateMainShot(proposalId);
        }

        /// <summary>
        /// A conversion must carry the numbers its form needs, and only those.
        /// <para>
        /// Each half of a half-declared conversion is the silent no-op shape this project keeps
        /// finding: a 부메랑 with no reach damages nobody, a beam with no range reaches nothing,
        /// and either would read in play as a 전설 card that does not work.
        /// </para>
        /// </summary>
        private void ValidateMainShot(string proposalId)
        {
            if (!Enum.IsDefined(typeof(PluginShotConversion), ShotConversion))
                throw new InvalidOperationException($"{proposalId}: unknown shot conversion.");

            if (!ConvertsMainShot)
            {
                if (MainShotCount != 0 || MainShotAreaRadiusUnits != 0f
                    || MainShotOutboundTicks != 0 || MainShotRangeUnits != 0f
                    || MainShotOnHitScale != 0f)
                {
                    throw new InvalidOperationException(
                        $"{proposalId}: carries main-shot numbers but converts nothing, so nothing " +
                        "would ever read them.");
                }

                return;
            }

            RequireFraction(MainShotOnHitScale, $"{proposalId}: {nameof(MainShotOnHitScale)}");

            if (MainShotCount < 1)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: a converted shot fired zero times is a basic attack that does nothing.");
            }

            if (ShotConversion == PluginShotConversion.Laser)
            {
                if (MainShotRangeUnits <= 0f || float.IsNaN(MainShotRangeUnits)
                    || float.IsInfinity(MainShotRangeUnits))
                {
                    throw new InvalidOperationException(
                        $"{proposalId}: a 레이저 needs a range above zero, or the beam reaches nothing.");
                }

                if (MainShotAreaRadiusUnits != 0f || MainShotOutboundTicks != 0)
                {
                    throw new InvalidOperationException(
                        $"{proposalId}: a 레이저 declares 부메랑 numbers that nothing reads.");
                }
            }

            if (ShotConversion == PluginShotConversion.Boomerang)
            {
                if (MainShotAreaRadiusUnits <= 0f || float.IsNaN(MainShotAreaRadiusUnits)
                    || float.IsInfinity(MainShotAreaRadiusUnits))
                {
                    throw new InvalidOperationException(
                        $"{proposalId}: 부메랑 범위 피해 needs a reach above zero, or it damages nobody.");
                }

                if (MainShotOutboundTicks < 1)
                {
                    throw new InvalidOperationException(
                        $"{proposalId}: a 부메랑 that turns back before it leaves never travels.");
                }

                if (MainShotRangeUnits != 0f)
                {
                    throw new InvalidOperationException(
                        $"{proposalId}: a 부메랑 declares a beam range that nothing reads.");
                }
            }
        }

        /// <summary>
        /// The fraction must be above zero and no more than 1. Zero would be a shot whose
        /// effects do nothing - indistinguishable in play from a broken card - and above 1 would
        /// make an extra shot stronger than the attack it was added to, which is not what
        /// 적중 효과 피해 70%/80% means.
        /// </summary>
        private static void RequireFraction(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f || value > 1f)
                throw new InvalidOperationException($"{name} of {value} is not a fraction in (0, 1].");
        }
    }
}
