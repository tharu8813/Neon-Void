using NeonVoid.Combat.Stats;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.Projectiles
{
    /// <summary>
    /// Fires the basic attack on a fixed tick interval.
    /// <para>
    /// Replaces PrototypeAutoShooter, which counted down with Time.deltaTime and allocated a
    /// fresh primitive per shot. The interval is now whole ticks, so the same number of shots
    /// happen per second on every machine rather than drifting with the frame rate.
    /// </para>
    /// <para>
    /// The projectile prefab is a plain GameObject the shooter configures; damage comes from
    /// the attacker's stats through the normal pipeline, so a stat change is visible in play
    /// without touching this class.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    public sealed class AutoAttacker : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Rate")]
        [Tooltip("[초안] attacks per second. docs/02 section 2 starting value is 2.")]
        [SerializeField, Min(0.1f)] private float attacksPerSecond = 2f;

        [Header("Projectile")]
        [Tooltip("[초안] units per second.")]
        [SerializeField, Min(0.1f)] private float projectileSpeed = 15f;

        [Tooltip("[초안] ticks before the shot despawns. 180 = 3 seconds.")]
        [SerializeField, Min(1)] private int projectileLifetimeTicks = 180;

        [Tooltip("Distinct enemies one shot may damage. 1 is no pierce.")]
        [SerializeField, Min(1)] private int maxTargetsPerShot = 1;

        [Tooltip("[초안] AD ratio of the basic attack.")]
        [SerializeField] private float adRatio = 1f;

        [SerializeField] private Material projectileMaterial;

        private CombatActor _actor;
        private SimulationRunner _runner;
        private int _ticksUntilNextShot;
        private StatCaps _caps = StatCaps.Draft();

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects;

        /// <summary>
        /// Attack interval in whole ticks, after 발사속도 증가/감소 and 기능고장.
        /// <para>
        /// Until this read the actor's statuses, every one of those effects was tracked with a
        /// duration and a magnitude that nothing looked at, so the attack rate never moved.
        /// </para>
        /// </summary>
        public int IntervalTicks => AttackRateMath.IntervalTicksFor(
            attacksPerSecond,
            _actor != null ? StatusStatContribution.AttackSpeedMultiplier(_actor.Status) : 1f,
            _caps.MaxAttacksPerSecond);

        /// <summary>Projectile speed after 탄환속도 증가.</summary>
        public float EffectiveProjectileSpeed => projectileSpeed
            * (_actor != null ? StatusStatContribution.ProjectileSpeedMultiplier(_actor.Status) : 1f);

        /// <summary>Distinct enemies one shot may damage, after 관통.</summary>
        public int EffectiveMaxTargets => maxTargetsPerShot
            + (_actor != null ? StatusStatContribution.ExtraPierceTargets(_actor.Status) : 0);

        private void Awake() => _actor = GetComponent<CombatActor>();

        private void OnEnable()
        {
            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner; this actor will never attack.", this);
                return;
            }
            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (_actor == null || !_actor.IsAlive)
                return;

            // 정지 (Stun) denies every combat action, firing included.
            if (_actor.Status.Has(Status.StatusEffectId.Stun))
                return;

            if (_ticksUntilNextShot > 0)
            {
                _ticksUntilNextShot--;
                return;
            }

            Vector3 direction = transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
                return;

            // 기능고장: the shot may simply fail. The cooldown is spent either way - a weapon
            // that only consumed its interval on success would malfunction for free.
            float failureChance = StatusStatContribution.ShotFailureChancePercent(_actor.Status);
            if (failureChance > 0f && _actor.RollChance(failureChance))
            {
                _ticksUntilNextShot = IntervalTicks;
                return;
            }

            Fire(direction.normalized);
            _ticksUntilNextShot = IntervalTicks;
        }

        private void Fire(Vector3 direction)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "NV Shot";
            orb.transform.position = transform.position + direction * 1.0f;
            orb.transform.localScale = Vector3.one * 0.26f;

            // The visual must not take part in physics: the projectile does its own sweep, and
            // a collider here would also push the player around.
            Object.Destroy(orb.GetComponent<Collider>());

            if (projectileMaterial != null)
            {
                var renderer = orb.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.sharedMaterial = projectileMaterial;
            }

            var request = new DamageRequest
            {
                AdRatio = adRatio,
                Type = DamageType.Physical,
                CanCrit = true,
            };

            orb.AddComponent<CombatProjectile>().Launch(
                _actor, request, direction,
                EffectiveProjectileSpeed, projectileLifetimeTicks, EffectiveMaxTargets);
        }
    }
}
