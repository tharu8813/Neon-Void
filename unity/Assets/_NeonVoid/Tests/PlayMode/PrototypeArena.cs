using System.Collections;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Skills;
using NeonVoid.Core;
using NeonVoid.Missions;
using NeonVoid.Player;
using NeonVoid.Plugins;
using NeonVoid.Progression;
using UnityEngine;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// A real scene for PlayMode tests: real colliders, real CharacterControllers, the real
    /// <see cref="SimulationRunner"/>, built in code.
    /// <para>
    /// <b>Why one frame is exactly one tick here.</b> The runner advances from
    /// <c>Time.deltaTime</c>, which in a test is however long the frame happened to take -
    /// so the number of ticks per frame would vary with machine load and an assertion about
    /// "after 60 ticks" would be flaky. <c>Time.captureDeltaTime</c> pins every frame to
    /// exactly one tick's worth of time, which makes these tests deterministic for the same
    /// reason the simulation itself is.
    /// </para>
    /// <para>
    /// Built in code rather than loaded from Prototype_Bootstrap.unity: a test that loads the
    /// prototype scene would fail whenever the scene was regenerated differently, and would be
    /// testing the scene builder instead of the rules.
    /// </para>
    /// </summary>
    internal sealed class PrototypeArena
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        public SimulationRunner Runner { get; private set; }
        public MissionRunDirector Mission { get; private set; }
        public GameObject PlayerObject { get; private set; }
        public CombatActor Player { get; private set; }
        public PlayerSimulationDriver Driver { get; private set; }
        public PlayerSkillCaster Caster { get; private set; }
        public AutoAttacker Attacker { get; private set; }
        public PluginProcDirector Procs { get; private set; }
        public RunDeathDirector DeathDirector { get; private set; }
        public List<CombatActor> Enemies { get; } = new List<CombatActor>();

        /// <summary>One tick's worth of a frame, so frames and ticks are the same thing.</summary>
        public static float TickSeconds => (float)SimulationClock.TickDeltaSeconds;

        public const int PlayerTeam = 0;
        public const int EnemyTeam = 1;

        /// <summary>The same [초안] block the prototype scene gives the player.</summary>
        public static CombatStats PlayerStats()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.AbilityPower = 20f;
            stats.OmnivampPercent = 15f;
            return stats;
        }

        /// <summary>The same [초안] block the prototype scene gives a dummy.</summary>
        public static CombatStats EnemyStats()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            stats.MaxHealth = 40f;
            stats.PhysicalArmor = 10f;
            stats.MagicResistance = 5f;
            return stats;
        }

        /// <summary>Gives an enemy the chasing brain. Opt-in, so a passive dummy stays still.</summary>
        public static EnemyAgent MakeChaser(CombatActor enemy) => enemy.gameObject.AddComponent<EnemyAgent>();

        /// <summary>Gives an enemy the ranged archetype, stats included.</summary>
        public static EnemyAgent MakeRanged(CombatActor enemy)
        {
            EnemyAgent agent = enemy.gameObject.AddComponent<EnemyAgent>();
            agent.ApplyArchetype(EnemyArchetypeCatalog.Ranged());
            return agent;
        }

        /// <summary>Gives an enemy the charging archetype, stats included.</summary>
        public static EnemyAgent MakeCharger(CombatActor enemy)
        {
            EnemyAgent agent = enemy.gameObject.AddComponent<EnemyAgent>();
            agent.ApplyArchetype(EnemyArchetypeCatalog.Charger());
            return agent;
        }

        /// <summary>Gives an enemy the support archetype, stats included.</summary>
        public static EnemyAgent MakeSupport(CombatActor enemy)
        {
            EnemyAgent agent = enemy.gameObject.AddComponent<EnemyAgent>();
            agent.ApplyArchetype(EnemyArchetypeCatalog.Support());
            return agent;
        }

        /// <summary>Drops a solid wall into the arena, tracked so teardown removes it.</summary>
        public GameObject Wall(Vector3 center, Vector3 size)
        {
            GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.name = "Wall";
            wall.transform.position = center;
            wall.transform.localScale = size;
            return wall;
        }

        /// <summary>Builds a skill asset at runtime, as the prototype authoring values do.</summary>
        public static SkillAsset SkillFrom(
            string key, SkillDefinition definition, SkillShape shape, SkillStatusEffect status)
        {
            var asset = ScriptableObject.CreateInstance<SkillAsset>();
            asset.Configure(key, key, definition, shape, status);
            return asset;
        }

        public static PrototypeArena Build(params Vector3[] enemyPositions)
        {
            var arena = new PrototypeArena();
            Time.captureDeltaTime = TickSeconds;

            arena.CreateGround();

            // The runner first: every actor looks for it in OnEnable, which runs the moment the
            // component is added in play mode.
            var runnerObject = arena.Track(new GameObject("Simulation Runner"));
            arena.Runner = runnerObject.AddComponent<SimulationRunner>();

            // The run identity everything else keys off. Added before the player, so the death
            // director and the plugin director both find it.
            var missionObject = arena.Track(new GameObject("Mission Run Director"));
            arena.Mission = missionObject.AddComponent<MissionRunDirector>();

            arena.CreatePlayer();

            for (int i = 0; i < enemyPositions.Length; ++i)
                arena.Enemies.Add(arena.CreateEnemy(enemyPositions[i], $"Enemy {i}"));

            return arena;
        }

        /// <summary>Advances the simulation by exactly <paramref name="ticks"/> ticks.</summary>
        public IEnumerator Tick(int ticks)
        {
            for (int i = 0; i < ticks; ++i)
                yield return null;
        }

        public void Dispose()
        {
            Time.captureDeltaTime = 0f;

            for (int i = 0; i < _spawned.Count; ++i)
                if (_spawned[i] != null)
                    Object.Destroy(_spawned[i]);

            // Anything the simulation spawned itself: shots and fields.
            foreach (CombatProjectile shot in Object.FindObjectsByType<CombatProjectile>(FindObjectsSortMode.None))
                Object.Destroy(shot.gameObject);
            foreach (PersistentDamageField field in Object.FindObjectsByType<PersistentDamageField>(FindObjectsSortMode.None))
                Object.Destroy(field.gameObject);

            _spawned.Clear();
            Enemies.Clear();
        }

        /// <summary>Distance between two actors on the ground plane.</summary>
        public static float DistanceXZ(Component a, Component b)
        {
            Vector3 offset = a.transform.position - b.transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>Kills an actor outright, through the normal damage entry point.</summary>
        public static void Kill(CombatActor actor)
        {
            var lethal = new DamageRequest { Flat = actor.Health.Max * 10f, Type = DamageType.True };
            actor.ReceiveDamage(lethal, CombatStats.StartingVaccine());
        }

        /// <summary>Fires a shot from the player at a target, as AutoAttacker would.</summary>
        /// <param name="onHitEffectScale">
        /// 적중 효과 피해 for this shot: 1 for a normal one, 0.80 for an R06 보조 탄환 extra
        /// shot, 0.70 for a G13 4중 캐시 burst shot.
        /// </param>
        public CombatProjectile FireAt(
            CombatActor target, float adRatio = 1f, int maxTargets = 1, float onHitEffectScale = 1f)
        {
            Vector3 direction = target.transform.position - PlayerObject.transform.position;
            direction.y = 0f;

            var orb = Track(new GameObject("Test Shot"));
            orb.transform.position = PlayerObject.transform.position + direction.normalized;

            var request = new DamageRequest
            {
                AdRatio = adRatio,
                Type = DamageType.Physical,
                CanCrit = false,
            };

            CombatProjectile projectile = orb.AddComponent<CombatProjectile>();
            projectile.Launch(
                Player, request, direction.normalized, 15f, 180, maxTargets, onHitEffectScale);
            return projectile;
        }

        private void CreateGround()
        {
            GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(60f, 0.5f, 60f);
        }

        private void CreatePlayer()
        {
            PlayerObject = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            PlayerObject.name = "Player";
            PlayerObject.transform.position = new Vector3(0f, 1f, 0f);
            Object.DestroyImmediate(PlayerObject.GetComponent<CapsuleCollider>());

            CharacterController body = PlayerObject.AddComponent<CharacterController>();
            body.center = Vector3.zero;
            body.height = 2f;
            body.radius = 0.45f;

            Player = PlayerObject.AddComponent<CombatActor>();
            Driver = PlayerObject.AddComponent<PlayerSimulationDriver>();
            // Disabled by default: most tests fire deliberately through FireAt rather than on
            // a timer. Tests about the attack rate enable it.
            Attacker = PlayerObject.AddComponent<AutoAttacker>();
            Attacker.enabled = false;

            Caster = PlayerObject.AddComponent<PlayerSkillCaster>();
            Procs = PlayerObject.AddComponent<PluginProcDirector>();
            PlayerObject.AddComponent<ActorDeathReaction>();

            // On the player, so the director finds the actor whose death ends the run without
            // a serialized reference. Its Awake resolves it from the same GameObject.
            DeathDirector = PlayerObject.AddComponent<RunDeathDirector>();

            Player.Configure(PlayerTeam, PlayerStats());
        }

        /// <summary>
        /// Another dummy beside an existing one, for effects that need a second body in reach -
        /// 전위 연쇄 and 폭발 각인 both need somewhere to go.
        /// </summary>
        public CombatActor SpawnEnemyNear(CombatActor of, string name, float offsetUnits = 1.6f)
        {
            CombatActor spawned = CreateEnemy(
                of.transform.position + new Vector3(offsetUnits, 0f, 0f), name);
            Enemies.Add(spawned);
            return spawned;
        }

        private CombatActor CreateEnemy(Vector3 position, string name)
        {
            GameObject enemy = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            enemy.name = name;
            enemy.transform.position = position;
            enemy.transform.localScale = Vector3.one * 1.4f;
            Object.DestroyImmediate(enemy.GetComponent<SphereCollider>());

            CharacterController body = enemy.AddComponent<CharacterController>();
            body.center = Vector3.zero;
            body.height = 1f;
            body.radius = 0.5f;

            CombatActor actor = enemy.AddComponent<CombatActor>();
            enemy.AddComponent<ActorDeathReaction>();
            actor.Configure(EnemyTeam, EnemyStats());
            return actor;
        }

        /// <summary>
        /// Switches the plugin procs off, for a test about something else. An explosion or a
        /// chain reaching a second enemy is correct behaviour that would wreck an assertion
        /// about what one projectile touched.
        /// </summary>
        public void WithoutPlugins() => Procs.enabled = false;

        /// <summary>Registers an object the test created so teardown destroys it.</summary>
        public GameObject Track(GameObject spawned)
        {
            _spawned.Add(spawned);
            return spawned;
        }
    }
}
