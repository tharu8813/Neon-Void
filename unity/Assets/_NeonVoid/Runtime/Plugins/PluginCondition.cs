using System;
using UnityEngine;

namespace NeonVoid.Plugins
{
    /// <summary>What has to be true for a conditional passive to contribute.</summary>
    public enum PluginConditionKind
    {
        /// <summary>No condition. The passive always contributes.</summary>
        None = 0,

        /// <summary>
        /// The carrier has taken no damage for a number of ticks. docs/03 G11 무피격 가속:
        /// 피해를 받으면 비활성화.
        /// </summary>
        Undamaged = 1,
    }

    /// <summary>
    /// A gate on a carried plugin's contribution, evaluated every tick.
    /// <para>
    /// <b>Why a gate and not a status effect.</b> A status would need a duration, and docs/03
    /// leaves G11's 재활성화 시점 미확정 - any duration written into the status catalogue would
    /// answer that question silently and permanently. A gate answers it in one place, as an
    /// [초안] number on the card, where a decision can replace it.
    /// </para>
    /// <para>
    /// Pure data plus one predicate: no Unity types, no clock. The director counts the ticks,
    /// because it is the thing that hears the damage.
    /// </para>
    /// </summary>
    [Serializable]
    public struct PluginCondition
    {
        [Tooltip("What has to be true. None means the passive always contributes.")]
        public PluginConditionKind Kind;

        [Tooltip(
            "[초안] ticks without taking damage before the passive comes back. docs/03 G11 " +
            "leaves 재활성화 시점 미확정, so this is a draft bound to be decided.")]
        public int TicksUndamaged;

        public bool IsNone => Kind == PluginConditionKind.None;

        public static PluginCondition None => default;

        /// <summary>
        /// 무피격 가속 - the passive is live again once the carrier has gone
        /// <paramref name="ticksUndamaged"/> ticks without being hurt.
        /// </summary>
        public static PluginCondition UndamagedFor(int ticks) => new PluginCondition
        {
            Kind = PluginConditionKind.Undamaged,
            TicksUndamaged = ticks,
        };

        /// <summary>
        /// Whether the gate is open. <paramref name="ticksSinceDamage"/> is how long the
        /// carrier has gone unhurt; it is <see cref="int.MaxValue"/> when they have never been
        /// hurt at all, so a card is live from the start rather than needing a first hit to
        /// begin counting.
        /// </summary>
        public bool IsMet(int ticksSinceDamage)
        {
            switch (Kind)
            {
                case PluginConditionKind.None:
                    return true;

                case PluginConditionKind.Undamaged:
                    return ticksSinceDamage >= TicksUndamaged;

                default:
                    return false;
            }
        }

        public void Validate(string proposalId)
        {
            if (!Enum.IsDefined(typeof(PluginConditionKind), Kind))
                throw new InvalidOperationException($"{proposalId}: {Kind} is not a known condition.");

            if (Kind == PluginConditionKind.None)
                return;

            if (TicksUndamaged < 0)
            {
                throw new InvalidOperationException(
                    $"{proposalId}: a condition cannot ask for {TicksUndamaged} ticks.");
            }
        }

        public override string ToString() =>
            Kind == PluginConditionKind.None ? "always" : $"{Kind} {TicksUndamaged}t";
    }
}
