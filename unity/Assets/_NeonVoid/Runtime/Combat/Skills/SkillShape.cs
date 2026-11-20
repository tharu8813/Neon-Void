using System;
using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// The geometry and timing of a skill's effect, separate from its cast cost.
    /// <para>
    /// Kept as a plain struct rather than fields on the ScriptableObject so the validation can
    /// be tested without creating an asset. Only the fields its own kind uses are checked; the
    /// rest are ignored rather than forced to zero, because an author switching a skill's kind
    /// while tuning should not lose the numbers they already typed.
    /// </para>
    /// </summary>
    [Serializable]
    public struct SkillShape
    {
        public SkillEffectKind Kind;

        [Tooltip("[초안] cone or field radius in units.")]
        public float RadiusUnits;

        [Tooltip("[초안] half of the cone's opening angle in degrees.")]
        public float ConeHalfAngleDegrees;

        [Tooltip("[초안] how far a teleport may move the caster.")]
        public float TeleportMaxDistanceUnits;

        [Tooltip("[초안] field lifetime and damage interval, in ticks.")]
        public FieldSchedule Field;

        /// <summary>
        /// Rejects a shape that cannot do what its kind says.
        /// <para>
        /// The movement-skill check is the one that matters beyond typos. docs/02 section 5
        /// makes 속박 (Root) deny <b>movement skills specifically</b>, and the loadout reads
        /// that from <see cref="SkillDefinition.IsMovementSkill"/>. A teleport whose author
        /// forgot the flag would be castable while rooted, which silently removes the counter
        /// to a blink. Nothing about the geometry would look wrong, so the data has to refuse
        /// it instead.
        /// </para>
        /// </summary>
        public void Validate(bool isMovementSkill)
        {
            switch (Kind)
            {
                case SkillEffectKind.None:
                    return;

                case SkillEffectKind.ConeDamage:
                    RequirePositive(RadiusUnits, nameof(RadiusUnits));
                    if (ConeHalfAngleDegrees <= 0f || ConeHalfAngleDegrees > 180f)
                        throw new InvalidOperationException(
                            $"{nameof(ConeHalfAngleDegrees)} must be above 0 and at most 180; 180 is a full circle.");
                    return;

                case SkillEffectKind.PersistentField:
                    RequirePositive(RadiusUnits, nameof(RadiusUnits));
                    Field.Validate();
                    return;

                case SkillEffectKind.Teleport:
                    RequirePositive(TeleportMaxDistanceUnits, nameof(TeleportMaxDistanceUnits));
                    if (!isMovementSkill)
                        throw new InvalidOperationException(
                            "A Teleport skill must be marked IsMovementSkill, or 속박 (Root) would not deny it (docs/02 section 5).");
                    return;

                default:
                    throw new InvalidOperationException($"Unknown skill effect kind {Kind}.");
            }
        }

        private static void RequirePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new InvalidOperationException($"{name} must be a finite value above zero.");
        }
    }
}
