using System;
using System.Collections.Generic;

namespace NeonVoid.Combat.Status
{
    /// <summary>One running status effect.</summary>
    public struct StatusInstance
    {
        public StatusEffectId Id;

        /// <summary>Ticks left. Reaching zero removes the instance.</summary>
        public int RemainingTicks;

        /// <summary>Stack count for the stacking policies; 1 otherwise.</summary>
        public int Stacks;

        /// <summary>Magnitude of a single stack.</summary>
        public float MagnitudePerStack;

        /// <summary>Combined magnitude of this instance.</summary>
        public float TotalMagnitude => MagnitudePerStack * Stacks;
    }

    /// <summary>What an application did. Useful for the HUD and for asserting in tests.</summary>
    public enum StatusApplyOutcome
    {
        Applied = 0,
        RefreshedExisting = 1,
        AddedStack = 2,

        /// <summary>A Unique effect was already running, or a weaker ReplaceStronger lost.</summary>
        Ignored = 3,

        /// <summary>Tenacity and resistance left nothing, or the source asked for no duration.</summary>
        ExpiredImmediately = 4,
    }

    /// <summary>
    /// The statuses running on one actor.
    /// <para>
    /// The container executes whichever <see cref="StatusStackPolicy"/> each definition carries
    /// and has no per-effect knowledge of its own. docs/02 section 5 requires exactly that:
    /// stacking, reapplication and expiry are per-effect data, and "하드코딩" of one uniform
    /// behaviour is called out as wrong.
    /// </para>
    /// <para>
    /// Instances are kept in application order and ticked in that order, so expiry is
    /// deterministic when several effects end on the same tick.
    /// </para>
    /// </summary>
    public sealed class StatusContainer
    {
        private readonly IReadOnlyDictionary<StatusEffectId, StatusEffectDefinition> _catalog;
        private readonly List<StatusInstance> _instances = new List<StatusInstance>();

        public StatusContainer(IReadOnlyDictionary<StatusEffectId, StatusEffectDefinition> catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Running instances, in application order.</summary>
        public IReadOnlyList<StatusInstance> Instances => _instances;

        public bool Has(StatusEffectId id) => IndexOf(id) >= 0;

        /// <summary>Total stacks across every instance of this effect.</summary>
        public int StacksOf(StatusEffectId id)
        {
            int total = 0;
            for (int i = 0; i < _instances.Count; ++i)
                if (_instances[i].Id == id)
                    total += _instances[i].Stacks;
            return total;
        }

        /// <summary>Combined magnitude across every instance of this effect.</summary>
        public float TotalMagnitudeOf(StatusEffectId id)
        {
            float total = 0f;
            for (int i = 0; i < _instances.Count; ++i)
                if (_instances[i].Id == id)
                    total += _instances[i].TotalMagnitude;
            return total;
        }

        /// <summary>Longest remaining duration of this effect, or 0 when it is not running.</summary>
        public int RemainingTicksOf(StatusEffectId id)
        {
            int longest = 0;
            for (int i = 0; i < _instances.Count; ++i)
                if (_instances[i].Id == id && _instances[i].RemainingTicks > longest)
                    longest = _instances[i].RemainingTicks;
            return longest;
        }

        /// <summary>
        /// Applies an effect, honouring its stack policy and the target's resistances.
        /// </summary>
        /// <param name="id">Effect to apply.</param>
        /// <param name="magnitude">Magnitude of this application.</param>
        /// <param name="tenacityPercent">Target tenacity, 0..100. Control effects only.</param>
        /// <param name="controlResistancePercent">Boss control resistance, 0..100.</param>
        /// <param name="durationTicksOverride">Duration before resistance; defaults to the catalogue value.</param>
        public StatusApplyOutcome Apply(
            StatusEffectId id,
            float magnitude,
            float tenacityPercent = 0f,
            float controlResistancePercent = 0f,
            int durationTicksOverride = -1)
        {
            if (!_catalog.TryGetValue(id, out StatusEffectDefinition definition))
                throw new InvalidOperationException($"Status {id} is not in the catalogue.");

            int requested = durationTicksOverride >= 0 ? durationTicksOverride : definition.BaseDurationTicks;
            int duration = TenacityMath.EffectiveDurationTicks(
                definition, requested, tenacityPercent, controlResistancePercent);

            if (duration <= 0)
                return StatusApplyOutcome.ExpiredImmediately;

            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                magnitude = 0f;

            int index = IndexOf(id);

            switch (definition.StackPolicy)
            {
                case StatusStackPolicy.Unique:
                    if (index >= 0)
                        return StatusApplyOutcome.Ignored;
                    return AddInstance(id, duration, 1, magnitude);

                case StatusStackPolicy.RefreshDuration:
                    if (index < 0)
                        return AddInstance(id, duration, 1, magnitude);
                    return Replace(index, duration, 1, magnitude, StatusApplyOutcome.RefreshedExisting);

                case StatusStackPolicy.ReplaceStronger:
                    if (index < 0)
                        return AddInstance(id, duration, 1, magnitude);
                    // A weaker source is ignored outright, timer included. Refreshing on a
                    // weaker hit would let a trickle of weak applications keep a strong
                    // debuff alive indefinitely.
                    if (magnitude <= _instances[index].MagnitudePerStack)
                        return StatusApplyOutcome.Ignored;
                    return Replace(index, duration, 1, magnitude, StatusApplyOutcome.RefreshedExisting);

                case StatusStackPolicy.IndependentStacks:
                    // Each application is its own instance with its own timer.
                    return AddInstance(id, duration, 1, magnitude);

                case StatusStackPolicy.AdditiveStacks:
                {
                    if (index < 0)
                        return AddInstance(id, duration, 1, magnitude);

                    StatusInstance existing = _instances[index];
                    if (existing.Stacks >= definition.MaxStacks)
                    {
                        // At the cap the effect still refreshes, so a capped debuff does not
                        // fall off while the source keeps applying it.
                        return Replace(index, duration, existing.Stacks, existing.MagnitudePerStack,
                            StatusApplyOutcome.RefreshedExisting);
                    }

                    return Replace(index, duration, existing.Stacks + 1, existing.MagnitudePerStack,
                        StatusApplyOutcome.AddedStack);
                }

                case StatusStackPolicy.UnboundedAggregated:
                {
                    if (index < 0)
                        return AddInstance(id, duration, 1, magnitude);

                    StatusInstance existing = _instances[index];
                    // No cap on purpose: capping would change a game rule to save frame time,
                    // which docs/02 section 6 forbids. The count is observable so the cost can
                    // be measured instead.
                    return Replace(index, duration, existing.Stacks + 1, existing.MagnitudePerStack,
                        StatusApplyOutcome.AddedStack);
                }

                default:
                    throw new InvalidOperationException($"Unhandled stack policy {definition.StackPolicy} for {id}.");
            }
        }

        /// <summary>
        /// Advances every instance by one logic tick and removes those that ended. Returns the
        /// effects that expired on this tick, in application order.
        /// </summary>
        public IReadOnlyList<StatusEffectId> Tick()
        {
            List<StatusEffectId> expired = null;

            for (int i = _instances.Count - 1; i >= 0; --i)
            {
                StatusInstance instance = _instances[i];
                instance.RemainingTicks--;

                if (instance.RemainingTicks > 0)
                {
                    _instances[i] = instance;
                    continue;
                }

                (expired ??= new List<StatusEffectId>()).Add(instance.Id);
                _instances.RemoveAt(i);
            }

            if (expired == null)
                return Array.Empty<StatusEffectId>();

            // Removal ran backwards for safety; report in application order.
            expired.Reverse();
            return expired;
        }

        /// <summary>Removes every instance of an effect, e.g. on a cleanse.</summary>
        public int Remove(StatusEffectId id)
        {
            int removed = 0;
            for (int i = _instances.Count - 1; i >= 0; --i)
            {
                if (_instances[i].Id != id)
                    continue;
                _instances.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void Clear() => _instances.Clear();

        private int IndexOf(StatusEffectId id)
        {
            for (int i = 0; i < _instances.Count; ++i)
                if (_instances[i].Id == id)
                    return i;
            return -1;
        }

        private StatusApplyOutcome AddInstance(StatusEffectId id, int duration, int stacks, float magnitude)
        {
            _instances.Add(new StatusInstance
            {
                Id = id,
                RemainingTicks = duration,
                Stacks = stacks,
                MagnitudePerStack = magnitude,
            });
            return StatusApplyOutcome.Applied;
        }

        private StatusApplyOutcome Replace(
            int index, int duration, int stacks, float magnitude, StatusApplyOutcome outcome)
        {
            StatusInstance instance = _instances[index];
            instance.RemainingTicks = duration;
            instance.Stacks = stacks;
            instance.MagnitudePerStack = magnitude;
            _instances[index] = instance;
            return outcome;
        }
    }
}
