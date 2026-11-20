namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Why a cast was refused. docs/02 section 9 item 5 requires mana shortage and cooldown
    /// rejection to be tested, and section 5 adds the control effects that deny casting.
    /// <para>
    /// These are distinct values rather than one "cannot cast" because the HUD has to tell the
    /// player which one it was. "Not enough mana" and "you are silenced" call for completely
    /// different reactions, and collapsing them is how a game ends up feeling unresponsive for
    /// no visible reason.
    /// </para>
    /// </summary>
    public enum SkillCastRejection
    {
        None = 0,

        /// <summary>The slot index is outside the three active slots.</summary>
        NoSuchSlot = 1,

        /// <summary>The slot holds no skill.</summary>
        EmptySlot = 2,

        /// <summary>Not enough mana.</summary>
        NotEnoughMana = 3,

        /// <summary>Still on cooldown.</summary>
        OnCooldown = 4,

        /// <summary>정지 - stunned; no combat action at all.</summary>
        Stunned = 5,

        /// <summary>제한 - silenced; active skills unusable.</summary>
        Silenced = 6,

        /// <summary>속박 - rooted; movement skills specifically are unusable.</summary>
        Rooted = 7,

        /// <summary>
        /// The caster is dead. Not a control effect: a corpse is refused before the gate is
        /// even consulted, and the HUD must not report it as a stun.
        /// </summary>
        CasterIsDead = 8,

        /// <summary>
        /// A movement skill had nowhere legal to put the caster - 벽 속 이동 금지. Refused
        /// before anything is spent, so the player keeps the mana and the cooldown.
        /// </summary>
        NoValidDestination = 9,
    }

    /// <summary>The outcome of attempting one cast.</summary>
    public readonly struct SkillCastResult
    {
        public readonly bool Succeeded;
        public readonly SkillCastRejection Rejection;
        public readonly int SkillId;

        /// <summary>Mana actually spent. Zero on a refusal.</summary>
        public readonly float ManaSpent;

        /// <summary>Cooldown the slot entered, after skill haste. Zero on a refusal.</summary>
        public readonly int CooldownTicks;

        private SkillCastResult(bool succeeded, SkillCastRejection rejection, int skillId, float manaSpent, int cooldownTicks)
        {
            Succeeded = succeeded;
            Rejection = rejection;
            SkillId = skillId;
            ManaSpent = manaSpent;
            CooldownTicks = cooldownTicks;
        }

        public static SkillCastResult Cast(int skillId, float manaSpent, int cooldownTicks) =>
            new SkillCastResult(true, SkillCastRejection.None, skillId, manaSpent, cooldownTicks);

        public static SkillCastResult Refused(SkillCastRejection rejection, int skillId = 0) =>
            new SkillCastResult(false, rejection, skillId, 0f, 0);

        public override string ToString() =>
            Succeeded ? $"cast skill={SkillId} mana={ManaSpent:0.##} cd={CooldownTicks}" : $"refused: {Rejection}";
    }
}
