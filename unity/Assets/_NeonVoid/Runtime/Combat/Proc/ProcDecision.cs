namespace NeonVoid.Combat.Proc
{
    /// <summary>Why a triggered effect was allowed or refused.</summary>
    public enum ProcVerdict
    {
        /// <summary>The effect may fire.</summary>
        Allowed = 0,

        /// <summary>The effect already fired in this chain and may not repeat.</summary>
        BlockedByCycle = 1,

        /// <summary>A self-chaining effect reached its per-chain bound.</summary>
        BlockedByOccurrenceLimit = 2,

        /// <summary>The effect already fired at this exact target in this chain.</summary>
        BlockedByRepeatOnSameTarget = 3,

        /// <summary>The chain reached the depth budget.</summary>
        BlockedByDepthBudget = 4,

        /// <summary>The effect is still on internal cooldown for this target.</summary>
        BlockedByInternalCooldown = 5,
    }

    /// <summary>
    /// A permission answer, carrying enough context to explain itself.
    /// <para>
    /// The verdict is kept rather than reduced to a bool because docs/02 section 6 forbids
    /// silently dropping game rules under load: a refusal has to be attributable. "Blocked by
    /// a cycle" and "blocked because the stability budget ran out" are very different facts,
    /// and only the second one means the simulation failed to do its job.
    /// </para>
    /// </summary>
    public readonly struct ProcDecision
    {
        public readonly ProcVerdict Verdict;
        public readonly int EffectId;
        public readonly int TargetActorId;

        /// <summary>Ancestry at the moment of the decision, for diagnostics.</summary>
        public readonly string ChainPath;

        public ProcDecision(ProcVerdict verdict, int effectId, int targetActorId, string chainPath)
        {
            Verdict = verdict;
            EffectId = effectId;
            TargetActorId = targetActorId;
            ChainPath = chainPath;
        }

        public bool IsAllowed => Verdict == ProcVerdict.Allowed;

        /// <summary>
        /// True when the refusal came from a resource limit rather than a game rule. These are
        /// the ones worth surfacing as diagnostics: the simulation wanted to apply a rule and
        /// could not.
        /// </summary>
        public bool IsBudgetRefusal => Verdict == ProcVerdict.BlockedByDepthBudget;

        public override string ToString() =>
            $"{Verdict} effect={EffectId} target={TargetActorId} path=[{ChainPath}]";
    }
}
