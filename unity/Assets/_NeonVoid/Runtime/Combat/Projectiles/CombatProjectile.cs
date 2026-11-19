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
        private const float Radius = 0.13f;

        private CombatActor _attacker;
        private CombatStats _attackerStats;
        private DamageRequest _damage;
        private Vector3 _direction;
        private float _speedUnitsPerSecond;
        private int _ticksRemaining;
        private ProjectileHitLedger _ledger;
        private SimulationRunner _runner;
        private bool _launched;

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
            int maxTargets)
        {
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

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (!_launched)
                return;

            float step = _speedUnitsPerSecond * (float)SimulationClock.TickDeltaSeconds;
            Vector3 origin = transform.position;

            // RaycastHitAll rather than the first hit: a piercing shot must consider every
            // enemy on the segment, and the ledger decides which of them it may damage.
            RaycastHit[] hits = Physics.SphereCastAll(origin, Radius, _direction, step, ~0, QueryTriggerInteraction.Ignore);
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

                DamageOutcome outcome = actor.ReceiveDamage(_damage, _attackerStats, critRolled: false);
                if (_attacker != null && outcome.Lifesteal.Total > 0f)
                    _attacker.Heal(outcome.Lifesteal.Total);

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
