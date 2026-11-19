using System.Collections.Generic;

namespace NeonVoid.Combat.Proc
{
    /// <summary>
    /// Decides whether a triggered effect may fire, per docs/02 section 6.
    /// <para>
    /// The document is explicit about both halves of this, and they pull in opposite
    /// directions. <c>basic hit -&gt; chain lightning -&gt; explosion -&gt; damage over time</c>
    /// is <b>legal and must be allowed</b>. What must be blocked is a cycle: the same effect
    /// re-invoking itself along the same activation path. A guard that only counted chain depth
    /// would permit infinite loops; one that refused every repeated effect id would kill chain
    /// lightning. This type sits between the two.
    /// </para>
    /// <para>
    /// Order of checks matters and is deliberate: game rules are evaluated before resource
    /// limits, so an effect that was going to be refused by a cycle rule anyway is never
    /// reported as a budget casualty. Diagnostics would otherwise blame the budget for
    /// refusals the rules made.
    /// </para>
    /// Pure and side-effect free apart from the cooldown table it is given.
    /// </summary>
    public sealed class ProcChainGuard
    {
        /// <summary>
        /// Depth beyond which a chain is refused. A bound is required: the effect catalogue is
        /// finite, so non-repeating chains terminate on their own, but self-chaining effects
        /// combined with each other can still grow a chain far past anything playable.
        /// [초안] 16 — not an approved value.
        /// </summary>
        public const int DefaultMaxChainDepth = 16;

        private readonly Dictionary<int, ProcEffectRule> _rules = new Dictionary<int, ProcEffectRule>();

        // (effectId, targetActorId) -> tick the effect last fired there.
        private readonly Dictionary<long, long> _lastFiredTick = new Dictionary<long, long>();

        public ProcChainGuard(int maxChainDepth = DefaultMaxChainDepth)
        {
            MaxChainDepth = maxChainDepth < 1 ? 1 : maxChainDepth;
        }

        public int MaxChainDepth { get; }

        /// <summary>Registers or replaces an effect's rule.</summary>
        public void Register(ProcEffectRule rule)
        {
            rule.Validate();
            _rules[rule.EffectId] = rule;
        }

        /// <summary>
        /// An unregistered effect falls back to the safe rule: one use per chain. Defaulting to
        /// "anything goes" would mean a catalogue typo silently enables an infinite loop.
        /// </summary>
        public ProcEffectRule RuleFor(int effectId) =>
            _rules.TryGetValue(effectId, out ProcEffectRule rule) ? rule : ProcEffectRule.SingleUse(effectId);

        /// <summary>
        /// Decides whether <paramref name="effectId"/> may fire at <paramref name="targetActorId"/>
        /// given the ancestry in <paramref name="path"/>. Does not mutate anything; call
        /// <see cref="NoteFired"/> once the effect actually fires.
        /// </summary>
        public ProcDecision Evaluate(int effectId, int targetActorId, ProcChainPath path, long tick)
        {
            ProcEffectRule rule = RuleFor(effectId);
            string pathText = path.ToString();

            // 1. An ordinary effect reappearing anywhere in its own chain. This is the A->B->A
            //    shape the document names, and it is reported as a cycle regardless of target.
            if (!rule.CanRetriggerItself && path.ContainsEffect(effectId))
                return new ProcDecision(ProcVerdict.BlockedByCycle, effectId, targetActorId, pathText);

            // 2. A self-chaining effect bouncing back onto a target it already struck in this
            //    chain. Chain lightning hops onward to new enemies; returning to one it just
            //    hit is the same loop by another name, so the opt-in does not cover it.
            if (path.ContainsEffectOnTarget(effectId, targetActorId))
                return new ProcDecision(ProcVerdict.BlockedByRepeatOnSameTarget, effectId, targetActorId, pathText);

            // 3. A self-chaining effect that reached its bound.
            if (rule.CanRetriggerItself && path.CountEffect(effectId) >= rule.MaxOccurrencesPerChain)
                return new ProcDecision(ProcVerdict.BlockedByOccurrenceLimit, effectId, targetActorId, pathText);

            // 4. Internal cooldown, per effect and target. This is the only guard that can stop
            //    an effect re-firing across SEPARATE chains, which no path check can see.
            if (rule.InternalCooldownTicks > 0
                && _lastFiredTick.TryGetValue(CooldownKey(effectId, targetActorId), out long lastTick)
                && tick - lastTick < rule.InternalCooldownTicks)
                return new ProcDecision(ProcVerdict.BlockedByInternalCooldown, effectId, targetActorId, pathText);

            // 5. Resource limit last, so it never takes the blame for a rule refusal above.
            if (path.Depth >= MaxChainDepth)
                return new ProcDecision(ProcVerdict.BlockedByDepthBudget, effectId, targetActorId, pathText);

            return new ProcDecision(ProcVerdict.Allowed, effectId, targetActorId, pathText);
        }

        /// <summary>Records that the effect fired, starting its internal cooldown.</summary>
        public void NoteFired(int effectId, int targetActorId, long tick)
        {
            if (RuleFor(effectId).InternalCooldownTicks > 0)
                _lastFiredTick[CooldownKey(effectId, targetActorId)] = tick;
        }

        /// <summary>Clears cooldown state. Used when a run ends, never mid-run.</summary>
        public void ResetCooldowns() => _lastFiredTick.Clear();

        private static long CooldownKey(int effectId, int targetActorId) =>
            ((long)effectId << 32) ^ (uint)targetActorId;
    }
}
