namespace NeonVoid.Combat.Status
{
    /// <summary>
    /// [확정] The status catalogue from docs/02 section 5: eight buffs and nine debuffs.
    /// Ids are explicit so saved data and plugin definitions cannot be shifted by inserting a
    /// new entry in the middle later.
    /// </summary>
    public enum StatusEffectId
    {
        None = 0,

        // --- Buffs ---------------------------------------------------------------------
        /// <summary>강화 - attack damage up.</summary>
        Empower = 1,

        /// <summary>이동속도 증가 - movement speed up.</summary>
        Haste = 2,

        /// <summary>탄환속도 증가 - projectile speed up.</summary>
        ProjectileSpeedUp = 3,

        /// <summary>발사속도 증가 - attacks per second up.</summary>
        AttackSpeedUp = 4,

        /// <summary>스킬 가속 - ability haste.</summary>
        SkillHaste = 5,

        /// <summary>재생 - health regeneration over time.</summary>
        Regeneration = 6,

        /// <summary>무적 - damage is nullified.</summary>
        Invulnerable = 7,

        /// <summary>관통 - projectiles pierce; one projectile still may not hit one enemy twice.</summary>
        Pierce = 8,

        // --- Debuffs -------------------------------------------------------------------
        /// <summary>나약함 - attack damage down. Not a control effect.</summary>
        Weaken = 20,

        /// <summary>구속 - movement speed down. Control.</summary>
        Slow = 21,

        /// <summary>발사속도 감소 - attack interval up. Not a control effect.</summary>
        AttackSpeedDown = 22,

        /// <summary>기능고장 - attack speed down plus a chance the shot fails. Control.</summary>
        Malfunction = 23,

        /// <summary>제한 - active skills unusable. Control.</summary>
        Silence = 24,

        /// <summary>속박 - movement and movement skills unusable. Control.</summary>
        Root = 25,

        /// <summary>정지 - no combat actions, attacks or interaction. Control.</summary>
        Stun = 26,

        /// <summary>무결성 공격 - damage over time. Not a control effect.</summary>
        IntegrityAttack = 27,

        /// <summary>방화벽 차단 - armor and damage reduction weakened. Not a control effect.</summary>
        FirewallBreach = 28,

        /// <summary>
        /// 해체 - unbounded magic damage over time, for docs/03 R04 해체 프로토콜.
        /// <para>
        /// <b>Its own effect rather than 무결성 공격.</b> docs/02's stack-type list names the
        /// policy <c>UnboundedAggregated</c>(해체) and section 6 calls out 중첩이 무제한인 해체
        /// against thousands of enemies as a specific performance risk, so the document already
        /// treats 해체 as a state of its own. 무결성 공격 is the generic DoT and uses
        /// IndependentStacks - one timer per application - which is the opposite of the single
        /// aggregate state docs/03 section 7 requires here. Changing its policy to suit one
        /// card would quietly change every other DoT in the game.
        /// </para>
        /// </summary>
        Dismantle = 29,
    }
}
