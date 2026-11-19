using NeonVoid.Combat.Status;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Which control effects are currently denying a cast.
    /// <para>
    /// docs/02 section 5 gives three different denials and they are NOT interchangeable:
    /// 정지 (Stun) blocks every combat action, 제한 (Silence) blocks active skills, and
    /// 속박 (Root) blocks movement and movement skills only. Flattening them into one
    /// "cannot act" would wrongly stop a rooted player casting a stationary skill, which is
    /// a playable difference, not a detail.
    /// </para>
    /// Keeping the mapping in one tested place means a call site cannot get it half right.
    /// </summary>
    public readonly struct SkillGate
    {
        public readonly bool Stunned;
        public readonly bool Silenced;
        public readonly bool Rooted;

        public SkillGate(bool stunned, bool silenced, bool rooted)
        {
            Stunned = stunned;
            Silenced = silenced;
            Rooted = rooted;
        }

        /// <summary>Nothing is denying a cast.</summary>
        public static SkillGate Open => new SkillGate(false, false, false);

        /// <summary>Reads the three relevant control effects off an actor's status container.</summary>
        public static SkillGate FromStatus(StatusContainer status)
        {
            if (status == null)
                return Open;

            return new SkillGate(
                stunned: status.Has(StatusEffectId.Stun),
                silenced: status.Has(StatusEffectId.Silence),
                rooted: status.Has(StatusEffectId.Root));
        }

        /// <summary>
        /// The rejection this gate produces for a skill, or <see cref="SkillCastRejection.None"/>.
        /// Checked from the broadest denial to the narrowest so the player is told the most
        /// important reason first.
        /// </summary>
        public SkillCastRejection Evaluate(bool isMovementSkill)
        {
            if (Stunned)
                return SkillCastRejection.Stunned;
            if (Silenced)
                return SkillCastRejection.Silenced;
            if (Rooted && isMovementSkill)
                return SkillCastRejection.Rooted;
            return SkillCastRejection.None;
        }
    }
}
