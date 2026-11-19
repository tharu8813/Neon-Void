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
