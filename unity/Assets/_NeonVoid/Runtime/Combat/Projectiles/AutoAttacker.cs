using System.Collections.Generic;
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

        [Header("Extra shots")]
        [Tooltip(
            "[초안] degrees between the extra shots a plugin grants. docs/03 does not say where " +
            "R06 보조 탄환's additional projectile goes, only that there is one.")]
        [SerializeField, Min(0f)] private float extraShotSpreadDegrees = 7f;

        [Tooltip("[초안] ticks between the shots of a burst. 연사 is rapid, not simultaneous.")]
        [SerializeField, Min(1)] private int burstShotGapTicks = 3;

        private CombatActor _actor;
        private SimulationRunner _runner;
        private int _ticksUntilNextShot;
        private StatCaps _caps = StatCaps.Draft();

        /// <summary>
        /// What the carried plugins add to the shape of the basic attack, pushed in by whatever
        /// grants them - the same direction as <see cref="SetExtraShots"/>, so Combat still
        /// knows nothing about Plugins.
        /// </summary>
        private float _passiveAttackSpeedFraction;
        private float _passiveProjectileSpeedFraction;
        private float _passiveProjectileSizeFraction;

        /// <summary>Extra projectiles per volley, pushed in by whatever grants them.</summary>
        private readonly List<ExtraShotSpec> _extraShots = new List<ExtraShotSpec>();

        /// <summary>
        /// One entry per queued burst shot, holding that shot's own effect fraction.
        /// <para>
        /// A queue rather than a count and a scale, because two bursts can overlap - G13 fires
        /// every fourth hit effect, and four shots at three ticks apart take longer than that
        /// can take to happen again. Collapsing them would give one burst's shots the other's
        /// fraction.
        /// </para>
        /// </summary>
        private readonly Queue<float> _pendingBurstShots = new Queue<float>();

        private int _ticksUntilNextBurstShot;

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
            StatusAttackSpeedMultiplier + _passiveAttackSpeedFraction,
            _caps.MaxAttacksPerSecond);

        /// <summary>
        /// Projectile speed after 탄환속도 증가 and G06 스로틀 증폭.
        /// <para>
        /// The passive fraction is <b>added to</b> the status multiplier rather than compounded
        /// with it, for the reason AttackRateMath gives for its own single multiplier: a
        /// fraction is of the base, so the result does not depend on which contribution arrived
        /// first.
        /// </para>
        /// </summary>
        public float EffectiveProjectileSpeed =>
            projectileSpeed * (StatusProjectileSpeedMultiplier + _passiveProjectileSpeedFraction);

        /// <summary>
        /// The collision radius this attacker's shots fly with, after G03 확장 탄두.
        /// docs/03's 유효 충돌크기 - what the sweep tests with, not what the body looks like.
        /// </summary>
        public float EffectiveProjectileRadius =>
            CombatProjectile.BaseRadiusUnits * (1f + Mathf.Max(0f, _passiveProjectileSizeFraction));

        private float StatusAttackSpeedMultiplier =>
            _actor != null ? StatusStatContribution.AttackSpeedMultiplier(_actor.Status) : 1f;

        private float StatusProjectileSpeedMultiplier =>
            _actor != null ? StatusStatContribution.ProjectileSpeedMultiplier(_actor.Status) : 1f;

        /// <summary>
        /// Hands the attacker what the carried plugins do to the shape of its basic attack:
        /// G06 스로틀 증폭's rate and projectile speed, G03 확장 탄두's size. All [초안].
        /// <para>
        /// The attack rate still goes through <see cref="AttackRateMath"/> and therefore still
        /// meets the cap [확정] docs/02 section 3 asks for - which is the 공격속도 상한 검사
        /// docs/03 lists against G06 by name. A card that could stack past it would end at an
        /// interval of one tick, sixty attacks a second.
        /// </para>
        /// </summary>
        public void SetPassiveShotShape(
            float attackSpeedFraction, float projectileSpeedFraction, float projectileSizeFraction)
        {
            _passiveAttackSpeedFraction = Sane(attackSpeedFraction);
            _passiveProjectileSpeedFraction = Sane(projectileSpeedFraction);
            _passiveProjectileSizeFraction = Mathf.Max(0f, Sane(projectileSizeFraction));
        }

        private static float Sane(float fraction) =>
            float.IsNaN(fraction) || float.IsInfinity(fraction) ? 0f : fraction;

        /// <summary>Distinct enemies one shot may damage, after 관통.</summary>
        public int EffectiveMaxTargets => maxTargetsPerShot
            + (_actor != null ? StatusStatContribution.ExtraPierceTargets(_actor.Status) : 0);

        /// <summary>Extra projectiles this attacker currently fires per volley.</summary>
        public int ExtraShotsPerVolley
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _extraShots.Count; ++i)
                    total += _extraShots[i].Count;

                return total;
            }
        }

        /// <summary>Burst shots still waiting to be fired.</summary>
        public int PendingBurstShots => _pendingBurstShots.Count;

        /// <summary>
        /// Replaces the extra shots this attacker fires. Pushed in, because Combat must not
        /// reference Plugins; the attacker never learns what granted them.
        /// <para>
        /// Replaces rather than adds, for the same reason the stat pipeline recomputes from
        /// base: a list that accumulated would grow every time the loadout was re-pushed, and
        /// a card removed would keep firing.
        /// </para>
        /// </summary>
        public void SetExtraShots(IReadOnlyList<ExtraShotSpec> shots)
        {
            _extraShots.Clear();
            if (shots == null)
                return;

            for (int i = 0; i < shots.Count; ++i)
            {
                if (!shots[i].IsEmpty)
                    _extraShots.Add(shots[i]);
            }
        }

        /// <summary>
        /// Queues a burst of shots, fired a few ticks apart. docs/03 G13 4중 캐시 asks for
        /// 탄환 4발 추가 연사 - rapid fire, so they are queued rather than fired at once.
        /// </summary>
        public void QueueBurst(int count, float onHitEffectScale)
        {
            if (count <= 0)
                return;

            for (int i = 0; i < count; ++i)
                _pendingBurstShots.Enqueue(onHitEffectScale);
        }

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

            // 정지 (Stun) denies every combat action, firing included. A queued burst is held,
            // not discarded: the shots were earned before the stun landed, and throwing them
            // away would make 정지 silently delete damage the player had already paid for.
            if (_actor.Status.Has(Status.StatusEffectId.Stun))
                return;

            FireDueBurstShot();

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

            FireVolley(direction.normalized);
            _ticksUntilNextShot = IntervalTicks;
        }

        /// <summary>
        /// The main shot plus whatever extra shots have been granted. The main shot keeps its
        /// full effect strength; each extra one carries the fraction its grantor declared.
        /// </summary>
        private void FireVolley(Vector3 direction)
        {
            Fire(direction, 1f, homingTurnDegreesPerTick: 0f);

            // Spread symmetrically around the aim so an odd number of extras does not pull the
            // volley to one side. The angle is [초안]; docs/03 only says there is an extra shot.
            int fired = 0;
            for (int i = 0; i < _extraShots.Count; ++i)
            {
                ExtraShotSpec spec = _extraShots[i];
                for (int shot = 0; shot < spec.Count; ++shot)
                {
                    fired++;
                    float side = (fired % 2 == 1) ? 1f : -1f;
                    float degrees = extraShotSpreadDegrees * Mathf.Ceil(fired / 2f) * side;
                    Fire(
                        Quaternion.Euler(0f, degrees, 0f) * direction,
                        spec.OnHitEffectScale,
                        spec.HomingTurnDegreesPerTick);
                }
            }
        }

        /// <summary>
        /// One queued burst shot, if its gap has elapsed. Fired in the aim direction, which is
        /// where the attacker is looking now rather than where it looked when the burst was
        /// earned - 연사 is a continuing volley, not a replay.
        /// </summary>
        private void FireDueBurstShot()
        {
            if (_pendingBurstShots.Count == 0)
            {
                _ticksUntilNextBurstShot = 0;
                return;
            }

            if (_ticksUntilNextBurstShot > 0)
            {
                _ticksUntilNextBurstShot--;
                return;
            }

            Vector3 direction = transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
                return;

            // A burst shot is a straight one: G13 is 추가 연사, and guiding it would hand
            // R07's behaviour to a card that does not have it.
            Fire(direction.normalized, _pendingBurstShots.Dequeue(), homingTurnDegreesPerTick: 0f);
            _ticksUntilNextBurstShot = burstShotGapTicks;
        }

        private void Fire(Vector3 direction, float onHitEffectScale, float homingTurnDegreesPerTick)
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
                EffectiveProjectileSpeed, projectileLifetimeTicks, EffectiveMaxTargets,
                onHitEffectScale, EffectiveProjectileRadius, homingTurnDegreesPerTick);
        }
    }
}
