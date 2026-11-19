namespace NeonVoid.Combat.Proc
{
    /// <summary>
    /// One triggered effect waiting to resolve, carrying the metadata docs/02 section 6
    /// requires on every proc event.
    /// <para>
    /// The chain path travels with the event rather than being reconstructed later. A queue
    /// that rebuilt ancestry from "whatever is currently resolving" would attribute the wrong
    /// parent to anything that waited behind another branch, and the cycle rule would then be
    /// judging a chain that never existed.
    /// </para>
    /// </summary>
    public readonly struct ProcEvent
    {
        /// <summary>Monotonic id, unique within a run. Also the deterministic tiebreaker.</summary>
        public readonly long EventId;

        /// <summary>Run this event belongs to, so replays cannot mix two runs.</summary>
        public readonly int RunId;

        /// <summary>Logic tick the event was queued on.</summary>
        public readonly long Tick;

        public readonly int EffectId;
        public readonly int SourceActorId;
        public readonly int TargetActorId;

        /// <summary>The attack that started this causal chain, kept for attribution.</summary>
        public readonly int AttackId;

        public readonly DamageType DamageType;
        public readonly bool IsArea;
        public readonly bool IsPeriodic;
        public readonly bool IsCritical;

        /// <summary>Ancestry used by the cycle rule.</summary>
        public readonly ProcChainPath ChainPath;

        /// <summary>
        /// Which deterministic RNG stream this effect must draw from. docs/06 section 4 keeps
        /// mission, combat and reward randomness in separate streams, and presentation
        /// randomness out of all of them.
        /// </summary>
        public readonly int RandomStreamId;

        public ProcEvent(
            long eventId,
            int runId,
            long tick,
            int effectId,
            int sourceActorId,
            int targetActorId,
            int attackId,
            DamageType damageType,
            bool isArea,
            bool isPeriodic,
            bool isCritical,
            ProcChainPath chainPath,
            int randomStreamId)
        {
            EventId = eventId;
            RunId = runId;
            Tick = tick;
            EffectId = effectId;
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
            AttackId = attackId;
            DamageType = damageType;
            IsArea = isArea;
            IsPeriodic = isPeriodic;
            IsCritical = isCritical;
            ChainPath = chainPath ?? ProcChainPath.Root;
            RandomStreamId = randomStreamId;
        }

        /// <summary>The path a child of this event inherits.</summary>
        public ProcChainPath ChildPath => ChainPath.Append(EffectId, TargetActorId);

        public override string ToString() =>
            $"#{EventId} t{Tick} effect={EffectId} {SourceActorId}->{TargetActorId} path=[{ChainPath}]";
    }
}
