namespace NeonVoid.Combat
{
    /// <summary>
    /// What delivered a hit. docs/03 asks for classification tags on effects (G08 "스킬 분류
    /// 태그 필요"), and plugin triggers are written against these: G10 and G17 fire on
    /// 탄환 적중 - a projectile - while G12 fires on 타격 시, any hit at all.
    /// </summary>
    public enum HitOrigin
    {
        Unknown = 0,

        /// <summary>A basic attack projectile.</summary>
        Projectile = 1,

        /// <summary>An active skill: its instant damage or one of its field ticks.</summary>
        Skill = 2,

        /// <summary>A contact attack, e.g. an enemy reaching the player.</summary>
        Contact = 3,
    }

    /// <summary>
    /// One landed hit, described by ids rather than by scene references.
    /// <para>
    /// Ids, because this is what plugin triggers are evaluated against, and that evaluation has
    /// to be testable without a scene. Nothing here can be followed back into the world to
    /// change it either, which is the same reason <c>EnemyIntent</c> carries no health field.
    /// </para>
    /// </summary>
    public readonly struct HitReport
    {
        public readonly int SourceActorId;
        public readonly int TargetActorId;
        public readonly HitOrigin Origin;
        public readonly DamageType DamageType;
        public readonly bool IsArea;
        public readonly bool IsPeriodic;
        public readonly bool IsCritical;
        public readonly bool Killed;
        public readonly float HealthRemoved;

        public HitReport(
            int sourceActorId,
            int targetActorId,
            HitOrigin origin,
            DamageType damageType,
            bool isArea = false,
            bool isPeriodic = false,
            bool isCritical = false,
            bool killed = false,
            float healthRemoved = 0f)
        {
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
            Origin = origin;
            DamageType = damageType;
            IsArea = isArea;
            IsPeriodic = isPeriodic;
            IsCritical = isCritical;
            Killed = killed;
            HealthRemoved = healthRemoved;
        }

        /// <summary>
        /// True when the hit actually did something. A hit absorbed by invulnerability removed
        /// no health, and letting it trigger plugins would make 무적 a free proc generator.
        /// </summary>
        public bool Landed => HealthRemoved > 0f;
    }
}
