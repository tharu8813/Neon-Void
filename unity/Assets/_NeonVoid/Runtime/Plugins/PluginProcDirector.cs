using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Core;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>
    /// Runs the plugin procs in the scene: listens to the owner's landed hits, lets the queue
    /// decide, and applies what the queue allowed.
    /// <para>
    /// The queue is drained once per logic tick, after projectiles and effects have run, so
    /// every hit landed this tick is considered together and in enqueue order. Resolving
    /// immediately on each hit would make the order depend on which projectile happened to
    /// move first.
    /// </para>
    /// <para>
    /// [확정] docs/02 section 6: simulation changes happen in one event queue, and presentation
    /// never creates damage. Everything here is simulation, and every refusal is counted by the
    /// queue rather than by this class.
    /// </para>
    /// <para>
    /// The three prototype plugins are granted at startup. That is <b>not</b> how plugins are
    /// obtained - docs/03 gives them to the player through the three-card reward screen, which
    /// needs the reward pool and the save commit behind it. This grants them so the proc path
    /// can be exercised at all.
    /// </para>
    /// </summary>
    public sealed class PluginProcDirector : MonoBehaviour, ISimulationTickReceiver
    {
        [Header("Wiring")]
        [Tooltip("The actor whose hits trigger these plugins.")]
        [SerializeField] private CombatActor owner;

        [Tooltip("Prototype: grant the three draft plugins when the mission starts.")]
        [SerializeField] private bool grantPrototypePluginsOnStart = true;

        private ProcChainGuard _guard;
        private ProcQueue _queue;
        private ProcTriggerRouter _router;
        private SimulationRunner _runner;
        private readonly Dictionary<int, CombatActor> _actorsById = new Dictionary<int, CombatActor>();
        private readonly HashSet<int> _damagedByExplosion = new HashSet<int>();

        /// <summary>The last drain's report, for inspection and for the HUD later.</summary>
        public ProcDrainReport LastDrain { get; private set; }

        public PluginLoadout Loadout => _router?.Loadout;

        int ISimulationTickReceiver.TickOrder => SimulationRunner.Order.Effects + 1;

        private void Awake()
        {
            if (owner == null)
                owner = GetComponent<CombatActor>();

            _guard = new ProcChainGuard();
            _queue = new ProcQueue(_guard);
            _router = new ProcTriggerRouter(new PluginLoadout(), _guard, _queue);
        }

        private void Start()
        {
            RefreshActorIndex();

            if (!grantPrototypePluginsOnStart)
                return;

            IReadOnlyList<PluginDefinition> prototype = PluginCatalog.Prototype();
            for (int i = 0; i < prototype.Count; ++i)
            {
                PluginGrantVerdict verdict = _router.TryGrant(prototype[i], out string reason);
                if (verdict == PluginGrantVerdict.Granted)
                    Debug.Log($"Plugin {prototype[i].ProposalId} carried.", this);
                else
                    Debug.LogWarning($"Plugin {prototype[i].ProposalId} not carried: {verdict} - {reason}", this);
            }
        }

        private void OnEnable()
        {
            if (owner == null)
            {
                Debug.LogError($"{name}: no owner CombatActor; no plugin will ever trigger.", this);
                return;
            }
            owner.HitLanded += OnOwnerHitLanded;

            _runner = _runner != null ? _runner : FindFirstObjectByType<SimulationRunner>();
            if (_runner == null)
            {
                Debug.LogError($"{name}: no SimulationRunner; queued procs would never resolve.", this);
                return;
            }
            _runner.Register(this);
        }

        private void OnDisable()
        {
            if (owner != null)
                owner.HitLanded -= OnOwnerHitLanded;
            if (_runner != null)
                _runner.Unregister(this);
        }

        /// <summary>
        /// Rebuilds the id lookup. Called at startup rather than per tick, like the enemy
        /// agents' own roster; a spawner must call this when it adds an actor.
        /// </summary>
        public void RefreshActorIndex()
        {
            _actorsById.Clear();
            foreach (CombatActor actor in FindObjectsByType<CombatActor>(FindObjectsSortMode.None))
                if (actor != null)
                    _actorsById[actor.ActorId] = actor;
        }

        private void OnOwnerHitLanded(HitReport report)
        {
            long tick = _runner != null ? _runner.TickIndex : 0L;
            _router.OnHitLanded(report, tick);
        }

        void ISimulationTickReceiver.OnSimulationTick(long tickIndex)
        {
            if (_router.PendingCount == 0)
                return;

            ProcDrainReport report = _router.Drain(Resolve);
            LastDrain = report;

            // docs/02 section 6 forbids losing rules quietly. A chain that ended because the
            // rules said so is healthy; one that ended because the budget ran out is not, and
            // the player would have no way of telling the difference.
            if (!report.CompletedWithinBudget)
                Debug.LogError(
                    $"{name}: proc drain hit a resource limit on tick {tickIndex} ({report}). " +
                    "Effects the rules allowed did not happen.", this);
        }

        private void Resolve(ProcEvent evt, PluginDefinition plugin)
        {
            if (!_actorsById.TryGetValue(evt.TargetActorId, out CombatActor target) || target == null)
                return;

            switch (plugin.Effect)
            {
                case PluginEffectKind.ChainToNearbyEnemy:
                    ChainOnward(evt, plugin, target);
                    break;
                case PluginEffectKind.ExplodeAroundTarget:
                    Explode(plugin, target);
                    break;
                case PluginEffectKind.MarkTarget:
                    Mark(plugin, target);
                    break;
            }
        }

        /// <summary>
        /// G10: hits the nearest other hostile. The hop is enqueued again by the hit it lands,
        /// with this event's ancestry attached, so the guard can refuse a hop back onto an enemy
        /// already in the chain.
        /// </summary>
        private void ChainOnward(ProcEvent evt, in PluginDefinition plugin, CombatActor from)
        {
            CombatActor next = NearestHostileOtherThan(from, plugin.RadiusUnits, evt.TargetActorId);
            if (next == null)
                return;

            Strike(next, plugin, HitOrigin.Skill);
        }

        /// <summary>G17: everything hostile around the target, the target included.</summary>
        private void Explode(in PluginDefinition plugin, CombatActor at)
        {
            _damagedByExplosion.Clear();
            Collider[] inside = Physics.OverlapSphere(
                at.transform.position, plugin.RadiusUnits, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < inside.Length; ++i)
            {
                var actor = inside[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || owner == null || !owner.IsHostileTo(actor))
                    continue;
                if (!_damagedByExplosion.Add(actor.ActorId))
                    continue;

                Strike(actor, plugin, HitOrigin.Skill);
            }
        }

        /// <summary>G12: the mark itself, applied through the target so its tenacity applies.</summary>
        private void Mark(in PluginDefinition plugin, CombatActor target)
        {
            if (!target.IsAlive)
                return;

            target.ApplyStatus(
                plugin.AppliedStatus, plugin.StatusMagnitude, 0f, plugin.StatusDurationTicksOverride);
        }

        private void Strike(CombatActor target, in PluginDefinition plugin, HitOrigin origin)
        {
            if (owner == null || !target.IsAlive)
                return;

            DamageOutcome outcome = target.ReceiveDamage(
                plugin.Damage, owner.Stats, critRolled: false, isOnHitEvent: false);

            if (outcome.Lifesteal.Total > 0f)
                owner.Heal(outcome.Lifesteal.Total);

            // Reported like any other hit, which is what lets a plugin effect trigger another
            // plugin - and what makes the ancestry carried by the router load-bearing rather
            // than decorative.
            owner.NotifyHitLanded(target, plugin.Damage, outcome, origin);
        }

        private CombatActor NearestHostileOtherThan(CombatActor from, float radiusUnits, int excludeActorId)
        {
            Collider[] nearby = Physics.OverlapSphere(
                from.transform.position, radiusUnits, ~0, QueryTriggerInteraction.Ignore);

            CombatActor best = null;
            float bestDistanceSqr = float.MaxValue;
            int bestId = int.MaxValue;

            for (int i = 0; i < nearby.Length; ++i)
            {
                var actor = nearby[i].GetComponentInParent<CombatActor>();
                if (actor == null || !actor.IsAlive || actor.ActorId == excludeActorId)
                    continue;
                if (owner == null || !owner.IsHostileTo(actor))
                    continue;

                float distanceSqr = (actor.transform.position - from.transform.position).sqrMagnitude;

                // Ties broken by the lower actor id rather than by collider order, which the
                // physics query does not promise to repeat.
                bool closer = distanceSqr < bestDistanceSqr - 1e-5f;
                bool tiedButLowerId = Mathf.Abs(distanceSqr - bestDistanceSqr) <= 1e-5f && actor.ActorId < bestId;
                if (!closer && !tiedButLowerId)
                    continue;

                best = actor;
                bestDistanceSqr = distanceSqr;
                bestId = actor.ActorId;
            }

            return best;
        }
    }
}
