using System;

namespace NeonVoid.Combat.Proc
{
    /// <summary>
    /// What one triggered effect is allowed to do inside a causal chain.
    /// <para>
    /// The default is the safe one: an effect may not re-enter a chain it already fired in.
    /// That alone guarantees every chain terminates, because each step either introduces a new
    /// effect id or is refused, and the set of effect ids is finite.
    /// </para>
    /// <para>
    /// An effect that is genuinely meant to repeat — chain lightning hopping between enemies is
    /// the obvious one — opts in through <see cref="CanRetriggerItself"/>. docs/02 section 6
    /// warns specifically against blocking "legal chains where the target changes", and this is
    /// the opt-in it asks for: data on the effect, not a special case buried in the guard.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ProcEffectRule
    {
        /// <summary>Stable identity of the effect. Chain membership is keyed on this.</summary>
        public int EffectId;

        /// <summary>
        /// Whether this effect may fire more than once in one chain. When false (the default)
        /// a second appearance anywhere in the ancestry is a cycle.
        /// </summary>
        public bool CanRetriggerItself;

        /// <summary>
        /// Upper bound on appearances within one chain when <see cref="CanRetriggerItself"/> is
        /// true. A self-chaining effect without a bound would never terminate, so a value below
        /// 1 is rejected by <see cref="Validate"/>.
        /// </summary>
        public int MaxOccurrencesPerChain;

        /// <summary>
        /// Internal cooldown in ticks, per effect and target. Zero means no cooldown. This is
        /// what stops a legal self-chaining effect from firing on the same target every tick
        /// forever, which no cycle rule can catch because each activation is a new chain.
        /// </summary>
        public int InternalCooldownTicks;

        /// <summary>
        /// Even a self-chaining effect must not hit the same target twice in one chain. Chain
        /// lightning hops onward; bouncing back is the A-&gt;B-&gt;A case the document blocks.
        /// </summary>
        public static ProcEffectRule SingleUse(int effectId) => new ProcEffectRule
        {
            EffectId = effectId,
            CanRetriggerItself = false,
            MaxOccurrencesPerChain = 1,
            InternalCooldownTicks = 0,
        };

        /// <summary>An effect that may hop onward to new targets, up to a bound.</summary>
        public static ProcEffectRule SelfChaining(int effectId, int maxOccurrences, int internalCooldownTicks = 0) =>
            new ProcEffectRule
            {
                EffectId = effectId,
                CanRetriggerItself = true,
                MaxOccurrencesPerChain = maxOccurrences,
                InternalCooldownTicks = internalCooldownTicks,
            };

        public void Validate()
        {
            if (MaxOccurrencesPerChain < 1)
                throw new InvalidOperationException(
                    $"Effect {EffectId}: {nameof(MaxOccurrencesPerChain)} must be at least 1.");
            if (CanRetriggerItself && MaxOccurrencesPerChain < 2)
                throw new InvalidOperationException(
                    $"Effect {EffectId}: {nameof(CanRetriggerItself)} with a bound below 2 can never repeat; the flag is misleading.");
            if (InternalCooldownTicks < 0)
                throw new InvalidOperationException(
                    $"Effect {EffectId}: {nameof(InternalCooldownTicks)} must not be negative.");
        }
    }
}
