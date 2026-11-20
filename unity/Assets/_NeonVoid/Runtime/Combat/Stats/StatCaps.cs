using System;

namespace NeonVoid.Combat.Stats
{
    /// <summary>
    /// The 캡·검증 step of docs/02 section 2's pipeline.
    /// <para>
    /// docs/02 is explicit that an attack speed cap exists and that <b>its value is not
    /// decided</b>. So the mechanism is built and the number is marked: a draft cap that can be
    /// retuned is honest, while leaving the cap out entirely would quietly make "there is a cap"
    /// false, and inventing a confident number would make it look settled.
    /// </para>
    /// <para>
    /// Resistances are allowed to go negative - that is what 방어 감소 does, and the mitigation
    /// formula has a branch for it. Percentages that cannot meaningfully exceed their range are
    /// clamped instead.
    /// </para>
    /// </summary>
    [Serializable]
    public struct StatCaps
    {
        /// <summary>[미확정] docs/02: the cap exists, the value does not.</summary>
        public float MaxCritChancePercent;

        /// <summary>[미확정] percentage penetration above 100 would heal the target.</summary>
        public float MaxPenetrationPercent;

        /// <summary>[초안] tenacity of 100 would make every control effect last one tick.</summary>
        public float MaxTenacityPercent;

        /// <summary>[초안] no stated bound; this one only keeps the number finite.</summary>
        public float MaxSkillHaste;

        public static StatCaps Draft() => new StatCaps
        {
            MaxCritChancePercent = 100f,
            MaxPenetrationPercent = 100f,
            MaxTenacityPercent = 90f,
            MaxSkillHaste = 500f,
        };

        public void Validate()
        {
            if (MaxCritChancePercent <= 0f || MaxPenetrationPercent <= 0f
                || MaxTenacityPercent <= 0f || MaxSkillHaste <= 0f)
                throw new InvalidOperationException("Every cap must be above zero; a cap of 0 disables the stat.");
        }
    }
}
