using System.Collections.Generic;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// A field that sits in the world and damages hostile actors inside it on a schedule.
    /// <para>
    /// This is the only effect in the project that is both area and periodic, which makes it
    /// the only place the omnivamp efficiency rule from docs/02 is observable: 0.33 applied
    /// <b>once</b>, chosen by branch rather than multiplied per property. Until this existed
    /// the rule was covered by tests and reachable by nothing.
    /// </para>
    /// <para>
    /// The owner's stats are copied when the field begins, the same convention
    /// <see cref="Projectiles.CombatProjectile"/> uses: the field outlives the cast, and an
    /// effect already in the world resolving with stats it was not created with is the kind of
    /// difference nobody can see and nobody can reproduce.
    /// </para>
    /// </summary>
    public sealed class PersistentDamageField : MonoBehaviour, ISimulationTickReceiver
    {
        private CombatActor _owner;
        private CombatStats _ownerStats;
        private DamageRequest _damage;
        private SkillStatusEffect _status;
        private float _radiusUnits;
        private FieldSchedule _schedule;
        private int _elapsedTicks;
        private bool _active;
        private SimulationRunner _runner;

        // Reused so a damage event allocates nothing, and so one actor with several colliders
        // inside the field is still damaged once.
        private readonly HashSet<int> _damagedThisEvent = new HashSet<int>();

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects;

        /// <summary>Arms the field. Called by the caster right after the cast is allowed.</summary>
        public void Begin(
            CombatActor owner,
            in DamageRequest damage,
            in SkillStatusEffect status,
            float radiusUnits,
            FieldSchedule schedule)
        {
            _owner = owner;
            _ownerStats = owner != null ? owner.Stats : CombatStats.StartingVaccine();
            _damage = damage;
            _status = status;
            _radiusUnits = Mathf.Max(0.01f, radiusUnits);
            _schedule = schedule;
            _elapsedTicks = 0;
            _active = true;

            _runner = FindFirstObjectByType<SimulationRunner>();
            if (_runner != null)
                _runner.Register(this);
            else
                Debug.LogError($"{name}: no SimulationRunner; this field will never burn.", this);
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.Unregister(this);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (!_active)
                return;

            // Expiry is checked before damage, so the tick a field dies on does not also hurt.
            if (_schedule.IsExpired(_elapsedTicks))
            {
                Despawn();
                return;
            }

            if (_schedule.DamagesOnTick(_elapsedTicks))
                Burn();

            _elapsedTicks++;
        }

        private void Burn()
        {
            _damagedThisEvent.Clear();
            Collider[] inside = Physics.OverlapSphere(
                transform.position, _radiusUnits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < inside.Length; ++i)
            {
                var actor = inside[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive)
                    continue;
                if (_owner == null || !_owner.IsHostileTo(actor))
                    continue;
                if (!_damagedThisEvent.Add(actor.ActorId))
                    continue;

                // isOnHitEvent: false - a field tick is not a hit. On-hit lifesteal is paid per
                // attack that lands, and letting a damage-over-time tick count as one would
                // turn a five second field into five seconds of on-hit sustain.
                DamageOutcome outcome = actor.ReceiveDamage(
                    _damage, _ownerStats, critRolled: false, isOnHitEvent: false);

                if (_owner != null && outcome.Lifesteal.Total > 0f)
                    _owner.Heal(outcome.Lifesteal.Total);

                if (_status.Applies)
                    actor.ApplyStatus(_status.Id, _status.Magnitude, 0f, _status.DurationTicksOverride);
            }
        }

        private void Despawn()
        {
            _active = false;
            if (_runner != null)
                _runner.Unregister(this);
            Destroy(gameObject);
        }
    }
}
