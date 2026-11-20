using System;
using System.Collections.Generic;

namespace NeonVoid.Combat.Proc
{
    /// <summary>What one drain did, and what it could not do.</summary>
    public sealed class ProcDrainReport
    {
        public int Resolved { get; internal set; }
        public int BlockedByCycle { get; internal set; }
        public int BlockedByOccurrenceLimit { get; internal set; }
        public int BlockedByRepeatOnSameTarget { get; internal set; }
        public int BlockedByInternalCooldown { get; internal set; }
        public int BlockedByDepthBudget { get; internal set; }

        /// <summary>
        /// True when the event budget ran out with work still queued. This is a failure of the
        /// simulation to apply rules it wanted to apply, not a rule outcome, and docs/02
        /// section 6 forbids letting it pass silently.
        /// </summary>
        public bool BudgetExhausted { get; internal set; }

        /// <summary>Events still queued when the budget ran out. Never discarded quietly.</summary>
        public IReadOnlyList<ProcEvent> Abandoned { get; internal set; } = Array.Empty<ProcEvent>();

        /// <summary>Refusals caused by a resource limit rather than a game rule.</summary>
        public IReadOnlyList<ProcDecision> BudgetRefusals { get; internal set; } = Array.Empty<ProcDecision>();

        public int TotalBlocked =>
            BlockedByCycle + BlockedByOccurrenceLimit + BlockedByRepeatOnSameTarget
            + BlockedByInternalCooldown + BlockedByDepthBudget;

        /// <summary>
        /// True when nothing was lost to a resource limit. A chain that ended because the rules
        /// said so is healthy; one that ended because the budget ran out is not.
        /// </summary>
        public bool CompletedWithinBudget => !BudgetExhausted && BlockedByDepthBudget == 0;

        public override string ToString() =>
            $"resolved={Resolved} blocked={TotalBlocked} (cycle={BlockedByCycle}, " +
            $"sameTarget={BlockedByRepeatOnSameTarget}, limit={BlockedByOccurrenceLimit}, " +
            $"icd={BlockedByInternalCooldown}, depth={BlockedByDepthBudget}) " +
            $"budgetExhausted={BudgetExhausted} abandoned={Abandoned.Count}";
    }

    /// <summary>
    /// The single place triggered effects are ordered and resolved, as docs/02 section 6
    /// requires ("simulation changes happen in one event queue").
    /// <para>
    /// Resolution is strict FIFO. That makes the order a function of enqueue order alone, so a
    /// replay of the same inputs resolves the same chains in the same sequence. A priority
    /// queue keyed on anything derived from world state would reorder with it and break the
    /// determinism the whole 60 Hz design rests on.
    /// </para>
    /// <para>
    /// The queue never applies damage or grants anything itself. Handlers do that, and VFX code
    /// is not a handler: docs/02 section 6 forbids presentation from creating damage or XP.
    /// </para>
    /// </summary>
    public sealed class ProcQueue
    {
        /// <summary>[초안] events one drain may resolve before the budget is declared spent.</summary>
        public const int DefaultMaxEventsPerDrain = 256;

        private readonly Queue<ProcEvent> _pending = new Queue<ProcEvent>();
        private readonly ProcChainGuard _guard;
        private readonly int _maxEventsPerDrain;
        private long _nextEventId = 1;

        // Refusals are tallied by the queue itself rather than by callers. Asking handlers to
        // report their own refusals would make under-reporting the default failure mode, and a
        // refusal that nobody counted is exactly the silent rule loss docs/02 section 6 forbids.
        private readonly ProcDrainReport _tally = new ProcDrainReport();
        private readonly List<ProcDecision> _budgetRefusals = new List<ProcDecision>();

        public ProcQueue(ProcChainGuard guard, int maxEventsPerDrain = DefaultMaxEventsPerDrain)
        {
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _maxEventsPerDrain = maxEventsPerDrain < 1 ? 1 : maxEventsPerDrain;
        }

        public int PendingCount => _pending.Count;

        /// <summary>Ids already handed out, so a replay can assert it produced the same events.</summary>
        public long IssuedEventCount => _nextEventId - 1;

        /// <summary>
        /// Offers an effect to the queue. The guard decides; an allowed effect is queued and
        /// the returned decision says what happened either way.
        /// </summary>
        public ProcDecision TryEnqueue(
            int effectId,
            int sourceActorId,
            int targetActorId,
            ProcChainPath path,
            long tick,
            int runId = 0,
            int attackId = 0,
            DamageType damageType = DamageType.Physical,
            bool isArea = false,
            bool isPeriodic = false,
            bool isCritical = false,
            int randomStreamId = 0,
            float onHitEffectScale = 1f)
        {
            path ??= ProcChainPath.Root;
            ProcDecision decision = _guard.Evaluate(effectId, targetActorId, path, tick);
            if (!decision.IsAllowed)
            {
                Tally(decision);
                return decision;
            }

            _pending.Enqueue(new ProcEvent(
                _nextEventId++, runId, tick, effectId, sourceActorId, targetActorId, attackId,
                damageType, isArea, isPeriodic, isCritical, path, randomStreamId, onHitEffectScale));

            _guard.NoteFired(effectId, targetActorId, tick);
            return decision;
        }

        /// <summary>
        /// Resolves queued events in order, letting each handler enqueue follow-ups. Returns a
        /// report; callers are expected to surface <see cref="ProcDrainReport.BudgetExhausted"/>
        /// rather than ignore it.
        /// </summary>
        public ProcDrainReport Drain(Action<ProcEvent, ProcQueue> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            int resolved = 0;
            bool budgetExhausted = false;
            IReadOnlyList<ProcEvent> abandoned = Array.Empty<ProcEvent>();

            while (_pending.Count > 0)
            {
                if (resolved >= _maxEventsPerDrain)
                {
                    budgetExhausted = true;
                    abandoned = new List<ProcEvent>(_pending);
                    _pending.Clear();
                    break;
                }

                ProcEvent current = _pending.Dequeue();
                resolved++;
                // Handlers may enqueue follow-ups here; refusals they hit are tallied by
                // TryEnqueue, so they land in this same report without the handler's help.
                handler(current, this);
            }

            var report = new ProcDrainReport
            {
                Resolved = resolved,
                BlockedByCycle = _tally.BlockedByCycle,
                BlockedByOccurrenceLimit = _tally.BlockedByOccurrenceLimit,
                BlockedByRepeatOnSameTarget = _tally.BlockedByRepeatOnSameTarget,
                BlockedByInternalCooldown = _tally.BlockedByInternalCooldown,
                BlockedByDepthBudget = _tally.BlockedByDepthBudget,
                BudgetExhausted = budgetExhausted,
                Abandoned = abandoned,
                BudgetRefusals = new List<ProcDecision>(_budgetRefusals),
            };

            ResetTally();
            return report;
        }

        private void Tally(in ProcDecision decision)
        {
            switch (decision.Verdict)
            {
                case ProcVerdict.BlockedByCycle:
                    _tally.BlockedByCycle++;
                    break;
                case ProcVerdict.BlockedByOccurrenceLimit:
                    _tally.BlockedByOccurrenceLimit++;
                    break;
                case ProcVerdict.BlockedByRepeatOnSameTarget:
                    _tally.BlockedByRepeatOnSameTarget++;
                    break;
                case ProcVerdict.BlockedByInternalCooldown:
                    _tally.BlockedByInternalCooldown++;
                    break;
                case ProcVerdict.BlockedByDepthBudget:
                    _tally.BlockedByDepthBudget++;
                    _budgetRefusals.Add(decision);
                    break;
            }
        }

        private void ResetTally()
        {
            _tally.BlockedByCycle = 0;
            _tally.BlockedByOccurrenceLimit = 0;
            _tally.BlockedByRepeatOnSameTarget = 0;
            _tally.BlockedByInternalCooldown = 0;
            _tally.BlockedByDepthBudget = 0;
            _budgetRefusals.Clear();
        }

        /// <summary>Drops pending work and refusal tallies. Used between runs, never mid-tick.</summary>
        public void Clear()
        {
            _pending.Clear();
            ResetTally();
        }
    }
}
