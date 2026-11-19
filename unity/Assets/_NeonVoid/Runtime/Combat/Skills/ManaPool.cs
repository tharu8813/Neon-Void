using System;
using NeonVoid.Core;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Mana and its automatic regeneration.
    /// <para>
    /// [확정] docs/02 section 7: mana regenerates automatically. The rate ([초안] 5 per second)
    /// and the pool size ([초안] 100) are tuning.
    /// </para>
    /// <para>
    /// Regeneration is applied per tick as <c>rate / 60</c> rather than per second, so a cast
    /// that happens between two seconds sees the mana that has genuinely accrued. Accruing a
    /// whole second's worth at once would make a skill castable or not depending on which side
    /// of an arbitrary boundary the input landed on.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ManaPool
    {
        public float Current;
        public float Max;

        /// <summary>[초안] mana restored per second.</summary>
        public float RegenPerSecond;

        public static ManaPool Starting() => new ManaPool
        {
            Current = 100f,
            Max = 100f,
            RegenPerSecond = 5f,
        };

        public bool IsFull => Current >= Max;

        /// <summary>Fraction filled, for the HUD. 0 when the pool has no size.</summary>
        public float Fraction => Max > 0f ? Math.Min(1f, Math.Max(0f, Current / Max)) : 0f;

        /// <summary>Whether a cost is affordable right now.</summary>
        public bool CanAfford(float cost) =>
            !float.IsNaN(cost) && cost <= Current + 1e-4f;

        /// <summary>
        /// Spends mana when affordable. Returns false and changes nothing otherwise, so a
        /// refused cast cannot leave the pool partly drained.
        /// </summary>
        public bool TrySpend(float cost)
        {
            if (float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0f)
                return false;
            if (!CanAfford(cost))
                return false;

            Current = Math.Max(0f, Current - cost);
            return true;
        }

        /// <summary>Advances regeneration by one logic tick.</summary>
        public void Tick()
        {
            if (Max <= 0f || RegenPerSecond <= 0f || float.IsNaN(RegenPerSecond))
                return;

            Current = Math.Min(Max, Current + RegenPerSecond * (float)SimulationClock.TickDeltaSeconds);
        }

        /// <summary>Restores the pool to full, e.g. when a mission starts.</summary>
        public void Refill() => Current = Max;

        public void Validate()
        {
            if (Max <= 0f || float.IsNaN(Max) || float.IsInfinity(Max))
                throw new InvalidOperationException($"{nameof(Max)} must be a finite value above zero.");
            if (float.IsNaN(RegenPerSecond) || RegenPerSecond < 0f)
                throw new InvalidOperationException($"{nameof(RegenPerSecond)} must be finite and non-negative.");
            if (Current < 0f || Current > Max)
                throw new InvalidOperationException($"{nameof(Current)} must sit between zero and {nameof(Max)}.");
        }
    }
}
