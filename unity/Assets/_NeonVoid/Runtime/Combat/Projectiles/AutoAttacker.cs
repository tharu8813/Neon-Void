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
        /// What form the main shot takes, pushed in the same way the extra shots are.
        /// <para>
        /// A list, and empty means an ordinary 탄환. [확정] D-005 (2026-11-23): carrying L01
        /// 레이저 코어 and L02 부메랑 프로토콜 together fires both forms, which a single mode
        /// could not express at all. The rule is applied in Plugins and arrives here resolved.
        /// </para>
        /// </summary>
        private readonly List<MainShotSpec> _mainShots = new List<MainShotSpec>();

        /// <summary>
        /// 부메랑s still out. docs/03 L02: 귀환 전 추가 기본공격 불가, so the volley is held while
        /// this is above zero. Counted rather than flagged because L02's 부메랑 수 grows with the
        /// card's level and several can be out at once.
        /// </summary>
        private int _boomerangsInFlight;

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

        /// <summary>
        /// Replaces the forms the main shot is fired in. Empty or null restores the ordinary
        /// 탄환, for the same reason <see cref="SetExtraShots"/> replaces rather than adds: a
        /// list that accumulated would keep firing a card that had been removed.
        /// </summary>
        public void SetMainShots(IReadOnlyList<MainShotSpec> shots)
        {
            _mainShots.Clear();
            if (shots == null)
                return;

            for (int i = 0; i < shots.Count; ++i)
            {
                if (shots[i].ReplacesTheProjectile)
                    _mainShots.Add(shots[i]);
            }
        }

        /// <summary>The forms this attacker's main shot currently takes. Empty is 탄환.</summary>
        public IReadOnlyList<MainShotSpec> MainShots => _mainShots;

        /// <summary>
        /// 부메랑s still in flight. docs/03 L02: while this is above zero the basic attack is
        /// held, which is 귀환 전 추가 기본공격 불가 stated as a number a test can read.
        /// </summary>
        public int BoomerangsInFlight => _boomerangsInFlight;

        /// <summary>Whether the basic attack is being held for a 귀환.</summary>
        public bool WaitingForBoomerang => _boomerangsInFlight > 0;

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

            // docs/03 L02 부메랑 프로토콜: 귀환 전 추가 기본공격 불가. Held after the interval
            // has already run down, not before, so the wait does not also cost the player their
            // cooldown - the card says they may not fire another, not that the weapon is reset.
            // A queued burst is left alone: G13's 추가 연사 was earned before the 부메랑 left,
            // and gating it here would extend L02's restriction to a card that never asked for
            // it, the same reasoning 정지 uses above for holding rather than discarding.
            if (_boomerangsInFlight > 0)
                return;

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
            // The main shot, in whatever form the carried cards resolved to. No form means the
            // ordinary 탄환, which is every attacker in the game until L01 or L02 is picked up.
            if (_mainShots.Count == 0)
            {
                Fire(direction, 1f, homingTurnDegreesPerTick: 0f);
            }
            else
            {
                for (int i = 0; i < _mainShots.Count; ++i)
                    FireMainShot(_mainShots[i], direction);
            }

            // Spread symmetrically around the aim so an odd number of extras does not pull the
            // volley to one side. The angle is [초안]; docs/03 only says there is an extra shot.
            MainShotSpec extraForm = FormForExtraShots();
            int fired = 0;
            for (int i = 0; i < _extraShots.Count; ++i)
            {
                ExtraShotSpec spec = _extraShots[i];
                for (int shot = 0; shot < spec.Count; ++shot)
                {
                    fired++;
                    float side = (fired % 2 == 1) ? 1f : -1f;
                    float degrees = extraShotSpreadDegrees * Mathf.Ceil(fired / 2f) * side;
                    Vector3 aim = Quaternion.Euler(0f, degrees, 0f) * direction;

                    if (extraForm.Mode == MainShotMode.Laser)
                    {
                        // [확정] D-005: L01 converts R06's extra 탄환 too. Decided in Plugins
                        // and arriving here as a flag. The two fractions compound: an extra shot
                        // at 80% fired as a laser at 70% carries 56%, because each card cut only
                        // what it added.
                        FireLaser(aim, extraForm, extraForm.OnHitEffectScale * spec.OnHitEffectScale);
                        continue;
                    }

                    Fire(aim, spec.OnHitEffectScale, spec.HomingTurnDegreesPerTick);
                }
            }
        }

        /// <summary>
        /// The form the shots other cards add are fired in. The first main form that says it
        /// converts them wins; with none, an extra shot stays an ordinary 탄환.
        /// </summary>
        private MainShotSpec FormForExtraShots()
        {
            for (int i = 0; i < _mainShots.Count; ++i)
            {
                if (_mainShots[i].ConvertsExtraShots)
                    return _mainShots[i];
            }

            return MainShotSpec.Ordinary;
        }

        private void FireMainShot(in MainShotSpec spec, Vector3 direction)
        {
            switch (spec.Mode)
            {
                case MainShotMode.Laser:
                    FireLaser(direction, spec, spec.OnHitEffectScale);
                    break;

                case MainShotMode.Boomerang:
                    ThrowBoomerangs(spec, direction);
                    break;

                default:
                    Fire(direction, spec.OnHitEffectScale, homingTurnDegreesPerTick: 0f);
                    break;
            }
        }

        /// <summary>
        /// L01 레이저 코어's shot. Resolved on this tick, inside the attacker's own step.
        /// <para>
        /// 관통 arrives from the spec: [확정] D-005 says the beam respects the attacker's 관통
        /// budget, and that rule is applied in Plugins rather than restated here.
        /// </para>
        /// </summary>
        private void FireLaser(Vector3 direction, in MainShotSpec spec, float onHitEffectScale)
        {
            LaserBeam.Fire(
                _actor,
                BasicAttackRequest(),
                transform.position,
                direction,
                spec.RangeUnits,
                EffectiveProjectileRadius,
                spec.PiercesEverything ? ProjectileHitLedger.UnlimitedTargets : EffectiveMaxTargets,
                onHitEffectScale,
                projectileMaterial,
                _runner);
        }

        /// <summary>
        /// L02 부메랑 프로토콜's shot, and the hold it puts on the basic attack.
        /// <para>
        /// The count is spread like the extra shots are, for the same reason: several boomerangs
        /// thrown down one line would be one boomerang as far as the player can see.
        /// </para>
        /// </summary>
        private void ThrowBoomerangs(in MainShotSpec spec, Vector3 direction)
        {
            for (int i = 0; i < spec.Count; ++i)
            {
                float side = (i % 2 == 0) ? 1f : -1f;
                float degrees = extraShotSpreadDegrees * 2f * Mathf.Floor(i / 2f + 0.5f) * side;
                Vector3 aim = Quaternion.Euler(0f, i == 0 ? 0f : degrees, 0f) * direction;

                var disc = GameObject.CreatePrimitive(PrimitiveType.Cube);
                disc.name = "NV Boomerang";
                disc.transform.position = transform.position + aim * 1.0f;
                disc.transform.localScale = new Vector3(0.5f, 0.12f, 0.5f);
                Object.Destroy(disc.GetComponent<Collider>());

                if (projectileMaterial != null)
                {
                    var renderer = disc.GetComponent<Renderer>();
                    if (renderer != null)
                        renderer.sharedMaterial = projectileMaterial;
                }

                _boomerangsInFlight++;
                disc.AddComponent<BoomerangProjectile>().Throw(
                    _actor,
                    BasicAttackRequest(),
                    aim,
                    EffectiveProjectileSpeed * BoomerangSpeedFraction,
                    spec.OutboundTicks,
                    spec.AreaRadiusUnits,
                    spec.OnHitEffectScale,
                    ReleaseBoomerang);
            }
        }

        /// <summary>
        /// [초안] a 부메랑 travels slower than a 탄환 - it is a thrown disc that has to be
        /// catchable, and at full projectile speed the round trip is over before it reads as one.
        /// </summary>
        private const float BoomerangSpeedFraction = 0.7f;

        private void ReleaseBoomerang()
        {
            if (_boomerangsInFlight > 0)
                _boomerangsInFlight--;
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

        /// <summary>
        /// The damage one basic attack asks for. Shared by every form of the shot, so L01's
        /// beam and L02's 부메랑 deal the basic attack's damage rather than a number of their
        /// own - docs/03 gives neither card a damage figure, only a shape.
        /// </summary>
        private DamageRequest BasicAttackRequest() => new DamageRequest
        {
            AdRatio = adRatio,
            Type = DamageType.Physical,
            CanCrit = true,
        };

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

            orb.AddComponent<CombatProjectile>().Launch(
                _actor, BasicAttackRequest(), direction,
                EffectiveProjectileSpeed, projectileLifetimeTicks, EffectiveMaxTargets,
                onHitEffectScale, EffectiveProjectileRadius, homingTurnDegreesPerTick);
        }
    }
}
