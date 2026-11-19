using System;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// One active skill's data. [확정] docs/02 section 7: each skill carries its own mana cost,
    /// cooldown, type and damage coefficients; none of those may be hardcoded in behaviour.
    /// </summary>
    [Serializable]
    public struct SkillDefinition
    {
        /// <summary>Stable id. 0 means an empty slot.</summary>
        public int SkillId;

        /// <summary>[초안] mana required to cast.</summary>
        public float ManaCost;

        /// <summary>[초안] cooldown in ticks before skill haste is applied.</summary>
        public int BaseCooldownTicks;

        /// <summary>
        /// Whether this skill moves the caster. [확정] docs/02 section 5: Root (속박) denies
        /// movement and movement skills, but not every skill, so the distinction has to exist
        /// in data.
        /// </summary>
        public bool IsMovementSkill;

        /// <summary>Damage this skill requests, resolved through the normal damage pipeline.</summary>
        public DamageRequest Damage;

        public bool IsEmpty => SkillId == 0;

        public static SkillDefinition Empty => default;

        public void Validate()
        {
            if (IsEmpty)
                return;
            if (BaseCooldownTicks < 1)
                throw new InvalidOperationException(
                    $"Skill {SkillId}: cooldown must be at least one tick. docs/02 section 3 requires a minimum above zero.");
            if (float.IsNaN(ManaCost) || float.IsInfinity(ManaCost) || ManaCost < 0f)
                throw new InvalidOperationException($"Skill {SkillId}: mana cost must be finite and non-negative.");
            Damage.Validate();
        }
    }
}
