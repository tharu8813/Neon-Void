namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// What a skill does once <see cref="SkillLoadout.TryCast"/> has allowed it.
    /// <para>
    /// The cast rules (mana, cooldown, control gate) are shared by every skill and already
    /// tested; this is only the shape of the effect that follows. Three kinds exist because
    /// docs/03 section 6 names three prototype skills worth proving, each of which exercises a
    /// rule the project had built and never used in play.
    /// </para>
    /// </summary>
    public enum SkillEffectKind
    {
        /// <summary>Nothing beyond the cast itself. Useful for a stub slot.</summary>
        None = 0,

        /// <summary>
        /// Instant damage in a cone in front of the caster. `skill.arc_pulse`: proves cursor
        /// aiming, magic damage against magic resistance, and a control effect shortened by the
        /// target's tenacity.
        /// </summary>
        ConeDamage = 1,

        /// <summary>
        /// A field that sits on the ground and damages periodically. `skill.firewall_field`:
        /// the only effect that is both area and periodic, which is what makes omnivamp
        /// efficiency 0.33 observable instead of merely unit-tested.
        /// </summary>
        PersistentField = 2,

        /// <summary>
        /// Moves the caster to a nearby position. `skill.packet_blink`: proves the Root rule,
        /// since 속박 denies movement skills specifically rather than all skills.
        /// </summary>
        Teleport = 3,

        /// <summary>
        /// Heals the caster. `skill.patch_burst`, whose verification point in docs/03 is the
        /// case where the caster is already at full health.
        /// </summary>
        SelfHeal = 4,

        /// <summary>
        /// Removes named control effects from the caster. `skill.cleanse`, whose verification
        /// point is 사용불가 디버프 경계 - the debuffs that stop it being cast in the first place.
        /// This is the only thing in the project that calls StatusContainer.Remove.
        /// </summary>
        CleanseControl = 5,

        /// <summary>
        /// Damages the nearest single enemy. `skill.lockon`: physical damage on one target plus
        /// a defence debuff, with no cone to aim.
        /// </summary>
        SingleTarget = 6,
    }
}
