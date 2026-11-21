using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.Projectiles
{
    /// <summary>
    /// A projectile that moves and resolves hits on the 60 Hz logic tick.
    /// <para>
    /// Replaces PrototypeProjectile, which moved in Update with Time.deltaTime and re-hit a
    /// target on every frame it overlapped. Both of those made damage depend on frame rate: a
    /// 144 fps machine did more damage per second than a 60 fps one, from the same shot.
    /// </para>
    /// <para>
    /// A sweep test is used rather than a trigger. At tick speeds a fast projectile moves
    /// several times its own radius per step, so a projectile that only checked where it landed
    /// would tunnel straight through thin enemies. The sweep covers the whole segment.
    /// </para>
    /// </summary>
    public sealed class CombatProjectile : MonoBehaviour, ISimulationTickReceiver
    {
        /// <summary>
        /// [초안] the collision radius of an ordinary shot. G03 확장 탄두 scales it.
        /// <para>
        /// docs/03 asks for 유효 충돌크기·VFX 크기 분리 by name, and this is the 충돌크기 half:
        /// what the sweep tests with. Nothing about the body's visible size reaches this - a
        /// bigger-looking shot that hit no wider would be a card that reads as working and is
        /// not, which is the failure shape this project has already shipped three times.
        /// </para>
        /// </summary>
        public const float BaseRadiusUnits = 0.13f;

        private float _radiusUnits = BaseRadiusUnits;

        /// <summary>
        /// [초안] how far a 유도탄 looks for something to follow. docs/03 R07 gives no number.
        /// </summary>
        public const float DraftHomingAcquireRangeUnits = 18f;

        /// <summary>Degrees this shot may turn per tick. 0 is an ordinary straight shot.</summary>
        private float _homingTurnDegreesPerTick;

        private CombatActor _homingTarget;

        private CombatActor _attacker;
        private CombatStats _attackerStats;
        private DamageRequest _damage;
        private Vector3 _direction;
        private float _speedUnitsPerSecond;
        private int _ticksRemaining;
        private ProjectileHitLedger _ledger;
        private SimulationRunner _runner;
        private bool _launched;

        /// <summary>
        /// Fraction of their damage the effects this shot triggers deal. 1 for a normal shot,
        /// 0.80 for R06 보조 탄환's extra shot, 0.70 for G13 4중 캐시's burst.
        /// <para>
        /// It belongs to the shot and travels with it, so a shot already in flight keeps the
        /// fraction it was fired with even if the loadout changes before it lands - the same
        /// reason the attacker's stats are copied here rather than referenced.
        /// </para>
        /// </summary>
        private float _onHitEffectScale = 1f;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Projectiles;

        /// <summary>
        /// Arms the projectile. The attacker's stats are <b>copied</b>, not referenced: a shot
        /// already in flight must resolve with the stats it was fired with, and the attacker
        /// may be dead by the time it lands.
        /// </summary>
        public void Launch(
            CombatActor attacker,
            in DamageRequest damage,
            Vector3 direction,
            float speedUnitsPerSecond,
            int lifetimeTicks,
            int maxTargets,
            float onHitEffectScale = 1f,
            float radiusUnits = BaseRadiusUnits,
            float homingTurnDegreesPerTick = 0f)
        {
            _homingTurnDegreesPerTick =
                float.IsNaN(homingTurnDegreesPerTick) || float.IsInfinity(homingTurnDegreesPerTick)
                    ? 0f
                    : Mathf.Max(0f, homingTurnDegreesPerTick);
            _homingTarget = null;
            _radiusUnits = radiusUnits > 0f && !float.IsNaN(radiusUnits) && !float.IsInfinity(radiusUnits)
                ? radiusUnits
                : BaseRadiusUnits;
            _onHitEffectScale = onHitEffectScale;
            _attacker = attacker;
            _attackerStats = attacker != null ? attacker.Stats : CombatStats.StartingVaccine();
            _damage = damage;
            _direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            _speedUnitsPerSecond = Mathf.Max(0f, speedUnitsPerSecond);
            _ticksRemaining = Mathf.Max(1, lifetimeTicks);
            _ledger = new ProjectileHitLedger(Mathf.Max(1, maxTargets));
            _launched = true;

            _runner = FindFirstObjectByType<SimulationRunner>();
            if (_runner != null)
                _runner.Register(this);
            else
                Debug.LogError($"{name}: no SimulationRunner; this projectile will never move.", this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        /// <summary>Whether this shot follows something. docs/03 R07 자동 추적탄.</summary>
        public bool IsHoming => _homingTurnDegreesPerTick > 0f;

        /// <summary>What it is following, or null when it has nothing to follow.</summary>
        public CombatActor HomingTarget => _homingTarget;

        /// <summary>The direction it is travelling, for a test.</summary>
        public Vector3 Direction => _direction;

        /// <summary>
        /// Turns a 유도탄 towards what it is following, by at most its turn rate.
        /// <para>
        /// <b>타깃 소실 [초안]:</b> a shot whose target dies or disappears keeps flying in the
        /// direction it was last pointing. The two alternatives both change what the card is:
        /// despawning deletes a shot the player had already earned, and re-acquiring makes it a
        /// weapon that cannot miss, which is a different and much stronger card than 유도탄 발사.
        /// It may acquire again only if it never had a target at all.
        /// </para>
        /// <para>
        /// <b>벽:</b> no special rule, and that is deliberate. The sweep below already stops a
        /// shot on geometry, so a 유도탄 flies into a wall exactly like any other shot. A
        /// missile that steered around walls would need pathing the projectile has no business
        /// owning.
        /// </para>
        /// <para>
        /// <b>관통/연쇄:</b> also no special rule. The shot keeps the ledger it was launched
        /// with, so following a target grants no extra pierce - docs/03 leaves the policy open
        /// and granting one here would decide it.
        /// </para>
        /// </summary>
        private void Steer()
        {
            if (!IsHoming)
                return;

            if (_homingTarget != null && !_homingTarget.IsAlive)
            {
                // Lost, and not replaced: see 타깃 소실 above.
                _homingTarget = null;
                _homingTurnDegreesPerTick = 0f;
                return;
            }

            if (_homingTarget == null)
            {
                _homingTarget = NearestHostile();
                if (_homingTarget == null)
                    return;
            }

            Vector3 toTarget = _homingTarget.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-6f)
                return;

            float radians = _homingTurnDegreesPerTick * Mathf.Deg2Rad;
            _direction = Vector3.RotateTowards(_direction, toTarget.normalized, radians, 0f).normalized;
        }

        /// <summary>
        /// The closest living hostile within acquisition range, ties broken by actor id.
        /// <para>
        /// The tiebreak is not decoration. Physics queries return their results in no defined
        /// order, so two enemies at the same distance would otherwise be chosen by whatever the
        /// engine happened to list first - and a shot that picked a different target on a replay
        /// of the same seed would make the whole run non-reproducible.
        /// </para>
        /// </summary>
        private CombatActor NearestHostile()
        {
            if (_attacker == null)
                return null;

            Collider[] near = Physics.OverlapSphere(
                transform.position, DraftHomingAcquireRangeUnits, ~0, QueryTriggerInteraction.Ignore);

            CombatActor best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < near.Length; ++i)
            {
                var candidate = near[i].GetComponentInParent<CombatActor>();
                if (candidate == null || !candidate.IsAlive || !_attacker.IsHostileTo(candidate))
                    continue;

                Vector3 offset = candidate.transform.position - transform.position;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;

                if (distance > bestDistance)
                    continue;

                if (distance < bestDistance || best == null || candidate.ActorId < best.ActorId)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (!_launched)
                return;

            Steer();

            float step = _speedUnitsPerSecond * (float)SimulationClock.TickDeltaSeconds;
            Vector3 origin = transform.position;

            // RaycastHitAll rather than the first hit: a piercing shot must consider every
            // enemy on the segment, and the ledger decides which of them it may damage.
            RaycastHit[] hits = Physics.SphereCastAll(
                origin, _radiusUnits, _direction, step, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; ++i)
            {
                var actor = hits[i].collider.GetComponentInParent<CombatActor>();
                if (actor == null)
                {
                    // Geometry. A non-piercing shot stops; a piercing one would too, since it
                    // cannot pass through a wall.
                    Despawn();
                    return;
                }

                if (!actor.IsAlive || _attacker == null || !_attacker.IsHostileTo(actor))
                    continue;

                if (_ledger.TryRegisterHit(actor.ActorId) != ProjectileHitVerdict.Allowed)
                    continue;

                // The attacker rolls, because the chance is its stat. A shot in flight whose
                // attacker has died rolls nothing, which is the same answer as a zero chance.
                bool crit = _attacker != null && _attacker.RollCrit(_damage);
                DamageOutcome outcome = actor.ReceiveDamage(_damage, _attackerStats, crit);
                if (_attacker != null && outcome.Lifesteal.Total > 0f)
                    _attacker.Heal(outcome.Lifesteal.Total);

                // 탄환 적중: the trigger G10 and G17 are written against. The fraction rides
                // along so an extra or burst shot's effects land smaller without the cards
                // knowing which kind of shot fired them.
                if (_attacker != null)
                    _attacker.NotifyHitLanded(
                        actor, _damage, outcome, HitOrigin.Projectile, _onHitEffectScale);

                if (_ledger.IsSpent)
                {
                    Despawn();
                    return;
                }
            }

            transform.position = origin + _direction * step;

            if (--_ticksRemaining <= 0)
                Despawn();
        }

        private void Despawn()
        {
            _launched = false;
            if (_runner != null)
                _runner.Unregister(this);
            Destroy(gameObject);
        }
    }
}
