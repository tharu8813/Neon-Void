using System;
using NeonVoid.Combat.Status;

namespace NeonVoid.Combat.AI
{
    /// <summary>How an archetype delivers its attack.</summary>
    public enum EnemyAttackMode
    {
        /// <summary>Reaches the target and touches it. `enemy.chaser`.</summary>
        Contact = 0,

        /// <summary>Fires a projectile. `enemy.ranged`.</summary>
        Projectile = 1,
    }

    /// <summary>The archetypes that exist. Ids match docs/01 section 4.</summary>
    public enum EnemyArchetypeId
    {
        Chaser = 0,
        Ranged = 1,
        Charger = 2,
        Support = 3,
    }

    /// <summary>
    /// One enemy archetype as data: how it thinks, how fast it moves, how it hits and how much
    /// of it there is.
    /// <para>
    /// [확정] CLAUDE.md forbids treating the five hardware zones as one copy-pasted map, and
    /// docs/01 section 4 lists six archetypes with different tactical roles. Keeping the whole
    /// description in one struct means adding the next archetype is adding data, and means an
    /// enemy's numbers cannot drift apart from the behaviour they were tuned against.
    /// </para>
    /// <para>
    /// All values [초안]. Only the two implemented archetypes are here; charger, support, elite
    /// and the bosses are absent rather than stubbed, so nothing reads as finished that is not.
    /// </para>
    /// </summary>
    [Serializable]
    public struct EnemyArchetype
    {
        /// <summary>The id from docs/01, e.g. `enemy.ranged`, for traceability.</summary>
        public string DocumentId;

        public EnemyBrainConfig Brain;

        public float MoveSpeedUnitsPerSecond;

        public EnemyAttackMode AttackMode;

        /// <summary>AD ratio of its attack.</summary>
        public float AdRatio;

        /// <summary>Projectile speed, for a firing archetype.</summary>
        public float ProjectileSpeedUnitsPerSecond;

        /// <summary>Ticks before its projectile despawns.</summary>
        public int ProjectileLifetimeTicks;

        /// <summary>The buff a support hands its allies, or None.</summary>
        public StatusEffectId SupportStatus;

        /// <summary>[초안] strength of that buff.</summary>
        public float SupportMagnitude;

        /// <summary>[초안] how far its help reaches.</summary>
        public float SupportRadiusUnits;

        public float MaxHealth;
        public float AttackDamage;
        public float PhysicalArmor;
        public float MagicResistance;

        /// <summary>The stat block an actor of this archetype spawns with.</summary>
        public CombatStats Stats()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.MaxHealth = MaxHealth;
            stats.AttackDamage = AttackDamage;
            stats.PhysicalArmor = PhysicalArmor;
            stats.MagicResistance = MagicResistance;
            return stats;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(DocumentId))
                throw new InvalidOperationException("An archetype needs its docs/01 id for traceability.");

            Brain.Validate();

            if (MoveSpeedUnitsPerSecond < 0f || float.IsNaN(MoveSpeedUnitsPerSecond))
                throw new InvalidOperationException($"{DocumentId}: movement speed must be finite and not negative.");

            if (MaxHealth <= 0f)
                throw new InvalidOperationException($"{DocumentId}: an enemy with no health is dead on arrival.");

            if (AttackMode == EnemyAttackMode.Projectile)
            {
                if (ProjectileSpeedUnitsPerSecond <= 0f)
                    throw new InvalidOperationException(
                        $"{DocumentId}: a firing archetype needs a projectile speed above zero, or its shots never arrive.");
                if (ProjectileLifetimeTicks < 1)
                    throw new InvalidOperationException(
                        $"{DocumentId}: a projectile that expires before its first tick can never hit.");

                // A shot that dies before it can cross its own attack range is a weapon that
                // cannot reach what the brain decided to shoot at.
                float reach = ProjectileSpeedUnitsPerSecond
                              * (ProjectileLifetimeTicks / (float)Core.DesignConstants.SimulationTicksPerSecond);
                if (reach < Brain.AttackRange)
                    throw new InvalidOperationException(
                        $"{DocumentId}: its projectile travels {reach:0.#} units before expiring but it fires at up to {Brain.AttackRange:0.#}.");
            }

            if (Brain.UsesSupport)
            {
                if (SupportStatus == StatusEffectId.None)
                    throw new InvalidOperationException(
                        $"{DocumentId}: a support must name the buff it hands out, or killing it first changes nothing.");
                if (SupportRadiusUnits <= 0f)
                    throw new InvalidOperationException(
                        $"{DocumentId}: its help needs a radius above zero to reach anybody.");

                // Its help has to outlive the gap between rounds, or the buff lapses and the
                // group is unbuffed for most of the fight - which makes 우선 처치 pointless
                // because the support was barely doing anything to begin with.
                StatusEffectDefinition definition = StatusCatalog.Draft()[SupportStatus];
                if (definition.BaseDurationTicks < Brain.SupportIntervalTicks)
                    throw new InvalidOperationException(
                        $"{DocumentId}: {SupportStatus} lasts {definition.BaseDurationTicks} ticks but is reapplied every " +
                        $"{Brain.SupportIntervalTicks}, so its allies spend most of the fight unbuffed.");
            }

            if (!Brain.RequiresLineOfSight && AttackMode == EnemyAttackMode.Projectile)
                throw new InvalidOperationException(
                    $"{DocumentId}: a firing archetype must require line of sight, or it shoots through walls (docs/01 section 4).");

            Stats().Validate();
        }
    }

    /// <summary>
    /// The archetype values, in one verifiable place - the same shape StatusCatalog and
    /// PluginCatalog use.
    /// </summary>
    public static class EnemyArchetypeCatalog
    {
        public static EnemyArchetype Of(EnemyArchetypeId id) => id switch
        {
            EnemyArchetypeId.Ranged => Ranged(),
            EnemyArchetypeId.Charger => Charger(),
            EnemyArchetypeId.Support => Support(),
            _ => Chaser(),
        };

        /// <summary>`enemy.chaser` - 접근·접촉. The values P2-01 was verified with.</summary>
        public static EnemyArchetype Chaser() => new EnemyArchetype
        {
            DocumentId = "enemy.chaser",
            Brain = EnemyBrainConfig.Chaser(),
            MoveSpeedUnitsPerSecond = 2.6f,
            AttackMode = EnemyAttackMode.Contact,
            AdRatio = 1f,
            MaxHealth = 40f,
            AttackDamage = 5f,
            PhysicalArmor = 10f,
            MagicResistance = 5f,
        };

        /// <summary>
        /// `enemy.ranged` - 근/원 조정·발사, with LOS. Slower and frailer than the chaser,
        /// because a thing that hits from 8 units away and is as tough as a thing that has to
        /// walk up to you is simply better.
        /// </summary>
        public static EnemyArchetype Ranged() => new EnemyArchetype
        {
            DocumentId = "enemy.ranged",
            Brain = EnemyBrainConfig.Ranged(),
            MoveSpeedUnitsPerSecond = 2.1f,
            AttackMode = EnemyAttackMode.Projectile,
            AdRatio = 1f,
            ProjectileSpeedUnitsPerSecond = 11f,
            ProjectileLifetimeTicks = 120,
            MaxHealth = 28f,
            AttackDamage = 4f,
            PhysicalArmor = 4f,
            MagicResistance = 4f,
        };

        /// <summary>
        /// `enemy.support` - 버프, answered by 우선 처치.
        /// <para>
        /// Only the buff half of docs/01's 버프/복제/소환. 복제 and 소환 are left out on purpose:
        /// the document's own memo for this archetype is 소환체 XP 중복 제한, which is reward
        /// accounting, and rewards sit behind the approved save commit this project has not got.
        /// A summoner without that rule is how one enemy becomes an XP fountain.
        /// </para>
        /// <para>
        /// Frail and nearly harmless by itself. Its danger is entirely what it does to the
        /// others, which is what makes going out of the way to kill it a decision rather than
        /// an obvious move.
        /// </para>
        /// </summary>
        public static EnemyArchetype Support() => new EnemyArchetype
        {
            DocumentId = "enemy.support",
            Brain = EnemyBrainConfig.Support(),
            MoveSpeedUnitsPerSecond = 2.4f,
            AttackMode = EnemyAttackMode.Contact,
            AdRatio = 0.5f,
            SupportStatus = StatusEffectId.Empower,     // 강화: raises its allies' damage
            SupportMagnitude = 0.3f,                    // [초안] +30% of base per stack
            SupportRadiusUnits = 9f,                    // [초안]
            MaxHealth = 24f,
            AttackDamage = 3f,
            PhysicalArmor = 2f,
            MagicResistance = 8f,
        };

        /// <summary>
        /// `enemy.charger` - 경고 뒤 돌진. It hits hard because it announces itself and can be
        /// dodged; a charge that hurt no more than a touch would be a worse chaser.
        /// </summary>
        public static EnemyArchetype Charger() => new EnemyArchetype
        {
            DocumentId = "enemy.charger",
            Brain = EnemyBrainConfig.Charger(),
            MoveSpeedUnitsPerSecond = 2.3f,
            AttackMode = EnemyAttackMode.Contact,
            AdRatio = 1.6f,
            MaxHealth = 46f,
            AttackDamage = 6f,
            PhysicalArmor = 8f,
            MagicResistance = 2f,
        };
    }
}
