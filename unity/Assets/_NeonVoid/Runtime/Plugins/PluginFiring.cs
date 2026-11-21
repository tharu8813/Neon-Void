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

        public bool AddsExtraShots => ExtraShotsPerVolley > 0;

        public bool FiresBursts => HitEffectsPerBurst > 0 && BurstShots > 0;

        public bool IsEmpty => !AddsExtraShots && !FiresBursts;

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
