using System.Collections.Generic;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.Projectiles
{
    /// <summary>
    /// L02 부메랑 프로토콜's shot: thrown out, damages an area around itself, comes back to the
    /// thrower, and holds the basic attack until it does.
    /// <para>
    /// <b>Two legs, one hit each.</b> docs/03 states 부메랑 범위 피해 + 귀환. The return leg is
    /// the point of a boomerang, so an enemy may be caught once on the way out and once on the
    /// way back - the ledger is cleared when it turns. Without that clear the return leg would
    /// pass harmlessly through everything it already hit, which is a boomerang that comes back
    /// for decoration.
    /// </para>
    /// <para>
    /// <b>It returns to where the thrower is now</b>, not to where the throw happened. A
    /// boomerang that flew back to an empty patch of floor the player had already left would
    /// hold the attack (see below) for a journey to nowhere.
    /// </para>
    /// <para>
    /// <b>귀환 전 추가 기본공격 불가 is enforced by releasing a callback, exactly once.</b> The
    /// attacker counts what is in flight and will not fire while that count is above zero, so a
    /// boomerang that was destroyed without releasing - scene teardown, a despawn, the owner
    /// dying - would stop the player attacking for the rest of the mission. It is therefore
    /// released from <see cref="OnDisable"/> as well, and the flag makes the second call a
    /// no-op.
    /// </para>
    /// </summary>
    public sealed class BoomerangProjectile : MonoBehaviour, ISimulationTickReceiver
    {
        /// <summary>[초안] reach of the 범위 피해 around the boomerang. docs/03 gives no number.</summary>
        public const float DraftAreaRadiusUnits = 1.6f;

        /// <summary>[초안] ticks it flies outward before turning. 36 ticks is 0.6 s.</summary>
        public const int DraftOutboundTicks = 36;

        /// <summary>[초안] units per second it travels, out and back.</summary>
        public const float DraftSpeedUnitsPerSecond = 11f;

        /// <summary>[초안] how close to the thrower counts as caught.</summary>
        public const float DraftCatchRadiusUnits = 0.7f;

        private readonly HashSet<int> _hitThisLeg = new HashSet<int>();

        private CombatActor _attacker;
        private CombatStats _attackerStats;
        private DamageRequest _damage;
        private Vector3 _direction;
        private float _speedUnitsPerSecond;
        private float _areaRadiusUnits;
        private float _onHitEffectScale = 1f;
        private int _outboundTicksRemaining;
        private int _returnTicksRemaining;
        private bool _returning;
        private bool _released;
        private bool _thrown;
        private System.Action _returned;
        private SimulationRunner _runner;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Projectiles;

        /// <summary>Whether it has turned around and is coming back.</summary>
        public bool IsReturning => _returning;

        /// <summary>Distinct actors it has caught on the leg it is flying now.</summary>
        public int CaughtThisLeg => _hitThisLeg.Count;

        /// <summary>The direction it is travelling, for a test.</summary>
        public Vector3 Direction => _direction;

        /// <param name="returned">
        /// Called exactly once, when the boomerang is back or gone for good. This is what lifts
        /// 귀환 전 추가 기본공격 불가, so it must happen on every path out of flight.
        /// </param>
        public void Throw(
            CombatActor attacker,
            in DamageRequest damage,
            Vector3 direction,
            float speedUnitsPerSecond,
            int outboundTicks,
            float areaRadiusUnits,
            float onHitEffectScale,
            System.Action returned)
        {
            _attacker = attacker;
            _attackerStats = attacker != null ? attacker.Stats : CombatStats.StartingVaccine();
            _damage = damage;
            _direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            _speedUnitsPerSecond = speedUnitsPerSecond > 0f ? speedUnitsPerSecond : DraftSpeedUnitsPerSecond;
            _areaRadiusUnits = areaRadiusUnits > 0f && !float.IsNaN(areaRadiusUnits)
                ? areaRadiusUnits
                : DraftAreaRadiusUnits;
            _onHitEffectScale =
                float.IsNaN(onHitEffectScale) || float.IsInfinity(onHitEffectScale) || onHitEffectScale <= 0f
                    ? 1f
                    : Mathf.Min(1f, onHitEffectScale);
            _outboundTicksRemaining = Mathf.Max(1, outboundTicks);

            // The return leg is given a budget of its own rather than running until it arrives.
            // The thrower moves, so a boomerang chasing a player who runs the other way could
            // otherwise never catch up - and the attack would stay held for as long as that
            // took. Twice the outward flight plus a margin is enough for any honest return.
            _returnTicksRemaining = _outboundTicksRemaining * 3;

            _returned = returned;
            _released = false;
            _returning = false;
            _thrown = true;
            _hitThisLeg.Clear();

            _runner = FindFirstObjectByType<SimulationRunner>();
            if (_runner != null)
            {
                _runner.Register(this);
            }
            else
            {
                Debug.LogError($"{name}: no SimulationRunner; this 부메랑 will never fly.", this);
                Despawn();
            }
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (!_thrown)
                return;

            // A boomerang whose thrower is gone has nothing to return to. It is dropped rather
            // than left flying, and dropping it releases the hold - the dead player's attack
            // being blocked matters the moment they are revived or the run is scored.
            if (_attacker == null || !_attacker.IsAlive)
            {
                Despawn();
                return;
            }

            if (!_returning)
            {
                if (--_outboundTicksRemaining <= 0)
                {
                    _returning = true;
                    _hitThisLeg.Clear();
                }
            }
            else if (--_returnTicksRemaining <= 0)
            {
                Despawn();
                return;
            }

            if (_returning)
            {
                Vector3 home = _attacker.transform.position;
                Vector3 toHome = home - transform.position;
                toHome.y = 0f;

                if (toHome.sqrMagnitude <= DraftCatchRadiusUnits * DraftCatchRadiusUnits)
                {
                    Harvest();
                    Despawn();
                    return;
                }

                _direction = toHome.normalized;
            }

            float step = _speedUnitsPerSecond * (float)SimulationClock.TickDeltaSeconds;
            transform.position += _direction * step;

            Harvest();
        }

        /// <summary>
        /// Everything hostile within reach of where it is now, once per leg each.
        /// <para>
        /// An overlap at its own position rather than a sweep along the step: 범위 피해 is an
        /// area around the boomerang, and the area is wider than one tick of travel at the
        /// drafted speed, so nothing is tunnelled through.
        /// </para>
        /// </summary>
        private void Harvest()
        {
            Collider[] inside = Physics.OverlapSphere(
                transform.position, _areaRadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < inside.Length; ++i)
            {
                var actor = inside[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive)
                    continue;
                if (_attacker == null || !_attacker.IsHostileTo(actor))
                    continue;
                if (!_hitThisLeg.Add(actor.ActorId))
                    continue;

                bool crit = _attacker.RollCrit(_damage);
                DamageOutcome outcome = actor.ReceiveDamage(_damage, _attackerStats, crit);
                if (outcome.Lifesteal.Total > 0f)
                    _attacker.Heal(outcome.Lifesteal.Total);

                // 탄환 적중 like any other basic attack landing, and flagged as area damage is
                // not this layer's call - the request carries that. What matters here is that
                // G10/G17 keep triggering off the basic attack after L02 replaces its shape.
                _attacker.NotifyHitLanded(
                    actor, _damage, outcome, HitOrigin.Projectile, _onHitEffectScale);
            }
        }

        private void Despawn()
        {
            _thrown = false;
            Release();

            if (_runner != null)
                _runner.Unregister(this);

            Destroy(gameObject);
        }

        /// <summary>Lifts the hold on the basic attack. Safe to call more than once.</summary>
        private void Release()
        {
            if (_released)
                return;

            _released = true;
            System.Action callback = _returned;
            _returned = null;
            callback?.Invoke();
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);

            // Destroyed by something other than its own flight: still release, or the attacker
            // waits for a 귀환 that can no longer happen.
            Release();
        }
    }
}
