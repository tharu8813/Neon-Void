using System;
using System.Collections.Generic;

namespace NeonVoid.Combat.Status
{
    /// <summary>
    /// What one status effect is: the data the container executes.
    /// <para>
    /// <see cref="IsControl"/> is the single most load-bearing flag here. docs/02 section 5
    /// says tenacity applies to <b>control</b> effects and warns against "over-application"
    /// that would zero out damage over time or stat reductions. Marking the wrong effect as
    /// control silently hands players tenacity-based immunity to damage the design meant them
    /// to take.
    /// </para>
    /// </summary>
    [Serializable]
    public struct StatusEffectDefinition
    {
        public StatusEffectId Id;

        /// <summary>True for a beneficial effect. Tenacity never shortens a buff.</summary>
        public bool IsBuff;

        /// <summary>
        /// [확정] Whether this is a control effect, and therefore the only kind tenacity and
        /// boss control resistance may shorten.
        /// </summary>
        public bool IsControl;

        public StatusStackPolicy StackPolicy;

        /// <summary>Upper bound for AdditiveStacks. Ignored by the unbounded policy.</summary>
        public int MaxStacks;

        /// <summary>[초안] default duration in ticks before tenacity.</summary>
        public int BaseDurationTicks;

        public void Validate()
        {
            if (Id == StatusEffectId.None)
                throw new InvalidOperationException("A status definition needs a real id.");
            if (BaseDurationTicks < 1)
                throw new InvalidOperationException($"{Id}: duration must be at least one tick.");
            if (StackPolicy == StatusStackPolicy.AdditiveStacks && MaxStacks < 1)
                throw new InvalidOperationException($"{Id}: AdditiveStacks needs MaxStacks of at least 1.");
            if (IsBuff && IsControl)
                throw new InvalidOperationException(
                    $"{Id}: a buff cannot be a control effect; tenacity would then shorten a benefit.");
        }
    }

    /// <summary>
    /// The shipped catalogue. Every duration and stack choice is <b>[초안]</b>; the buff/debuff
    /// split and the control classification follow docs/02 section 5 directly.
    /// </summary>
    public static class StatusCatalog
    {
        /// <summary>
        /// [확정] The control effects, i.e. the ones tenacity may shorten. Movement and action
        /// denial only. Weaken, AttackSpeedDown, IntegrityAttack and FirewallBreach are
        /// deliberately absent: they are stat reduction and damage over time, and letting
        /// tenacity shrink them is exactly the over-application the document rules out.
        /// </summary>
        public static readonly StatusEffectId[] ControlEffects =
        {
            StatusEffectId.Slow,
            StatusEffectId.Malfunction,
            StatusEffectId.Silence,
            StatusEffectId.Root,
            StatusEffectId.Stun,
        };

        public static bool IsControlEffect(StatusEffectId id)
        {
            for (int i = 0; i < ControlEffects.Length; ++i)
                if (ControlEffects[i] == id)
                    return true;
            return false;
        }

        /// <summary>All seventeen definitions, keyed by id.</summary>
        public static IReadOnlyDictionary<StatusEffectId, StatusEffectDefinition> Draft()
        {
            var catalog = new Dictionary<StatusEffectId, StatusEffectDefinition>();

            void Add(StatusEffectId id, bool isBuff, StatusStackPolicy policy, int maxStacks, int durationTicks)
            {
                var definition = new StatusEffectDefinition
                {
                    Id = id,
                    IsBuff = isBuff,
                    IsControl = IsControlEffect(id),
                    StackPolicy = policy,
                    MaxStacks = maxStacks,
                    BaseDurationTicks = durationTicks,
                };
                definition.Validate();
                catalog.Add(id, definition);
            }

            // Buffs. 60 ticks = 1 second at 60 Hz.
            Add(StatusEffectId.Empower, true, StatusStackPolicy.AdditiveStacks, 5, 300);
            Add(StatusEffectId.Haste, true, StatusStackPolicy.RefreshDuration, 1, 180);
            Add(StatusEffectId.ProjectileSpeedUp, true, StatusStackPolicy.RefreshDuration, 1, 300);
            Add(StatusEffectId.AttackSpeedUp, true, StatusStackPolicy.AdditiveStacks, 5, 300);
            Add(StatusEffectId.SkillHaste, true, StatusStackPolicy.AdditiveStacks, 10, 300);
            Add(StatusEffectId.Regeneration, true, StatusStackPolicy.IndependentStacks, 1, 300);
            // Unique: a second invulnerability must not extend the first, which would make
            // overlapping sources into near-permanent immunity.
            Add(StatusEffectId.Invulnerable, true, StatusStackPolicy.Unique, 1, 30);
            Add(StatusEffectId.Pierce, true, StatusStackPolicy.RefreshDuration, 1, 300);

            // Debuffs.
            Add(StatusEffectId.Weaken, false, StatusStackPolicy.ReplaceStronger, 1, 240);
            Add(StatusEffectId.Slow, false, StatusStackPolicy.ReplaceStronger, 1, 120);
            Add(StatusEffectId.AttackSpeedDown, false, StatusStackPolicy.ReplaceStronger, 1, 240);
            Add(StatusEffectId.Malfunction, false, StatusStackPolicy.RefreshDuration, 1, 120);
            Add(StatusEffectId.Silence, false, StatusStackPolicy.RefreshDuration, 1, 90);
            Add(StatusEffectId.Root, false, StatusStackPolicy.RefreshDuration, 1, 60);
            Add(StatusEffectId.Stun, false, StatusStackPolicy.RefreshDuration, 1, 45);
            Add(StatusEffectId.IntegrityAttack, false, StatusStackPolicy.IndependentStacks, 1, 180);
            // The named unbounded case from docs/02 section 6.
            Add(StatusEffectId.FirewallBreach, false, StatusStackPolicy.UnboundedAggregated, 0, 300);

            return catalog;
        }
    }
}
