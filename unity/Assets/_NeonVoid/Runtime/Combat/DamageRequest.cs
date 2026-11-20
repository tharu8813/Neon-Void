using System;

namespace NeonVoid.Combat
{
    /// <summary>
    /// One attack's damage definition: <c>flat + adScale*AD + apScale*AP</c>, as docs/02
    /// section 3 requires each attack to declare.
    /// <para>
    /// <see cref="IsArea"/> and <see cref="IsPeriodic"/> are carried on the request rather than
    /// inferred later, because omnivamp efficiency depends on them and guessing from the effect
    /// that happened to spawn the hit is exactly how the 0.33 rule gets applied twice.
    /// </para>
    /// </summary>
    [Serializable]
    public struct DamageRequest
    {
        /// <summary>Damage that does not scale with any stat.</summary>
        public float Flat;

        /// <summary>Multiplier on the attacker's AttackDamage.</summary>
        public float AdRatio;

        /// <summary>Multiplier on the attacker's AbilityPower.</summary>
        public float ApRatio;

        public DamageType Type;

        /// <summary>[확정] Whether this attack is allowed to crit, declared per attack.</summary>
        public bool CanCrit;

        /// <summary>True when the hit is part of an area effect.</summary>
        public bool IsArea;

        /// <summary>True when the hit is a periodic (damage over time) tick.</summary>
        public bool IsPeriodic;

        /// <summary>A plain single-target physical hit scaling fully with AD.</summary>
        public static DamageRequest BasicAttack(float adRatio = 1f) => new DamageRequest
        {
            Flat = 0f,
            AdRatio = adRatio,
            ApRatio = 0f,
            Type = DamageType.Physical,
            CanCrit = true,
            IsArea = false,
            IsPeriodic = false,
        };

        /// <summary>
        /// The same attack at a fraction of its size. docs/03 states this number three times -
        /// R06 보조 탄환's extra shot deals 적중 효과 피해 80%, G13 4중 캐시's burst 70%, L01
        /// 레이저 코어's conversion 70% - so it is one operation rather than three places that
        /// each remember to multiply.
        /// <para>
        /// Every term scales, flat included. Scaling only the stat ratios would make a plugin
        /// whose damage happens to be flat immune to the reduction, which is a balance hole that
        /// depends on how a card was authored rather than on what it does.
        /// </para>
        /// <para>
        /// <see cref="CanCrit"/>, <see cref="IsArea"/> and <see cref="IsPeriodic"/> are
        /// deliberately untouched: a smaller hit is the same KIND of hit, and changing those
        /// would change which omnivamp efficiency applies and whether the attack may crit.
        /// </para>
        /// </summary>
        public DamageRequest Scaled(float factor)
        {
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor < 0f)
                throw new InvalidOperationException($"A damage scale of {factor} is not a fraction.");

            DamageRequest scaled = this;
            scaled.Flat *= factor;
            scaled.AdRatio *= factor;
            scaled.ApRatio *= factor;
            return scaled;
        }

        public void Validate()
        {
            if (float.IsNaN(Flat) || float.IsInfinity(Flat))
                throw new InvalidOperationException($"{nameof(Flat)} must be finite.");
            if (float.IsNaN(AdRatio) || float.IsInfinity(AdRatio))
                throw new InvalidOperationException($"{nameof(AdRatio)} must be finite.");
            if (float.IsNaN(ApRatio) || float.IsInfinity(ApRatio))
                throw new InvalidOperationException($"{nameof(ApRatio)} must be finite.");
            if (!Enum.IsDefined(typeof(DamageType), Type))
                throw new InvalidOperationException($"{nameof(Type)} is not a known damage type.");
        }
    }
}
