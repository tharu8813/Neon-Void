using System;
using NeonVoid.Combat.Status;
using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// A status effect a skill puts on whatever it hits, or nothing.
    /// <para>
    /// The duration is deliberately an override rather than a value: left negative, the effect
    /// uses the catalogue duration from docs/02 section 5, so a slow is as long as every other
    /// slow in the game unless a skill has a stated reason to differ.
    /// </para>
    /// <para>
    /// Nothing here shortens a control effect. The target's own tenacity does that, inside
    /// <see cref="CombatActor.ApplyStatus"/>, which is why a skill must apply status through
    /// the actor rather than by writing into its status container.
    /// </para>
    /// </summary>
    [Serializable]
    public struct SkillStatusEffect
    {
        public StatusEffectId Id;

        [Tooltip("[초안] effect strength, e.g. 0.3 for a 30% slow.")]
        public float Magnitude;

        [Tooltip("Ticks; negative means use the catalogue duration.")]
        public int DurationTicksOverride;

        /// <summary>Whether this skill applies any status at all.</summary>
        public bool Applies => Id != StatusEffectId.None;

        public static SkillStatusEffect None => new SkillStatusEffect
        {
            Id = StatusEffectId.None,
            Magnitude = 0f,
            DurationTicksOverride = -1,
        };

        public void Validate()
        {
            if (!Applies)
                return;

            if (float.IsNaN(Magnitude) || float.IsInfinity(Magnitude))
                throw new InvalidOperationException($"{nameof(Magnitude)} must be finite.");
            if (DurationTicksOverride == 0)
                throw new InvalidOperationException(
                    $"{nameof(DurationTicksOverride)} of 0 would apply an effect that expires before it does anything; use a negative value for the catalogue duration.");
        }
    }
}
