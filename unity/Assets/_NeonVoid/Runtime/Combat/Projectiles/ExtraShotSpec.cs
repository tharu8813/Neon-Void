using System;

namespace NeonVoid.Combat.Projectiles
{
    /// <summary>
    /// Extra shots something has granted the basic attack, and how much of their effects land.
    /// <para>
    /// It lives in Combat rather than in Plugins because of the layering rule the proc system
    /// already follows: Combat must not reference Plugins. A plugin that adds projectiles
    /// therefore cannot reach into the attacker - the contribution is pushed in, exactly as
    /// passive stat modifiers are, and <see cref="AutoAttacker"/> never learns that plugins
    /// exist.
    /// </para>
    /// <para>
    /// <b>Count and scale travel together.</b> docs/03 gives R06 보조 탄환 one extra shot at
    /// 적중 효과 피해 80% and G13 4중 캐시 four at 70%, so two cards granting shots do not share
    /// a fraction. Summing the counts into one number would force one scale onto all of them and
    /// quietly make whichever card was applied second wrong.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct ExtraShotSpec
    {
        /// <summary>How many additional projectiles. Never negative.</summary>
        public readonly int Count;

        /// <summary>Fraction of their damage the effects these shots trigger deal.</summary>
        public readonly float OnHitEffectScale;

        public ExtraShotSpec(int count, float onHitEffectScale)
        {
            Count = count < 0 ? 0 : count;
            OnHitEffectScale =
                float.IsNaN(onHitEffectScale) || float.IsInfinity(onHitEffectScale) || onHitEffectScale < 0f
                    ? 1f
                    : onHitEffectScale;
        }

        public bool IsEmpty => Count <= 0;

        public override string ToString() => $"{Count} shot(s) at {OnHitEffectScale:0.##}x effects";
    }
}
