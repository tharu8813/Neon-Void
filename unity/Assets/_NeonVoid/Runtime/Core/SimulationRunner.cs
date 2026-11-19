using System.Collections.Generic;
using UnityEngine;

namespace NeonVoid.Core
{
    /// <summary>Anything that advances with the 60 Hz logic tick.</summary>
    public interface ISimulationTickReceiver
    {
        /// <summary>
        /// Lower runs first. Order is an explicit number rather than registration order so a
        /// prefab being enabled a frame earlier cannot change simulation results.
        /// </summary>
        int TickOrder { get; }

        /// <summary>Advance exactly one logic tick.</summary>
        void OnSimulationTick(long tickIndex);
    }

    /// <summary>
    /// The one clock the whole simulation runs on, as docs/06 section 4 requires
    /// (SimulationClock separate from RenderClock, one consistent fixed order).
    /// <para>
    /// Before this existed the player driver owned a private clock. That was fine while
    /// movement was the only thing simulated, but the moment combat also ticks, two private
    /// clocks drift apart: each accumulates its own rounding, so status durations, cooldowns
    /// and movement stop agreeing about what tick it is. Ticking everything from one
    /// accumulator removes that whole class of bug rather than making it small.
    /// </para>
    /// <para>
    /// Receivers are sorted by <see cref="ISimulationTickReceiver.TickOrder"/> and every
    /// receiver completes a tick before the next tick starts, so a chain of effects can never
    /// straddle two ticks.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationRunner : MonoBehaviour
    {
        /// <summary>Order slots, so tick order reads as intent rather than as magic numbers.</summary>
        public static class Order
        {
            public const int Input = 0;
            public const int Player = 100;
            public const int Actors = 200;
            public const int Projectiles = 300;
            public const int Effects = 400;
        }

        [Tooltip("Ticks a single frame may run while catching up after a stall.")]
        [SerializeField, Min(1)] private int maxTicksPerFrame = 8;

        private readonly List<ISimulationTickReceiver> _receivers = new List<ISimulationTickReceiver>();
        private SimulationClock _clock;
        private bool _needsSort;

        /// <summary>Ticks run so far.</summary>
        public long TickIndex => _clock?.TickIndex ?? 0L;

        /// <summary>Ticks abandoned because a frame exceeded the catch-up budget.</summary>
        public long DroppedTicks => _clock?.DroppedTicks ?? 0L;

        /// <summary>Fraction of a tick accumulated, for presentation interpolation only.</summary>
        public double InterpolationAlpha => _clock?.InterpolationAlpha ?? 0.0;

        private void Awake() => _clock = new SimulationClock(maxTicksPerFrame);

        public void Register(ISimulationTickReceiver receiver)
        {
            if (receiver == null || _receivers.Contains(receiver))
                return;
            _receivers.Add(receiver);
            _needsSort = true;
        }

        public void Unregister(ISimulationTickReceiver receiver) => _receivers.Remove(receiver);

        private void Update()
        {
            int ticks = _clock.Advance(Time.deltaTime);
            if (ticks <= 0)
                return;

            if (_needsSort)
            {
                _receivers.Sort((a, b) => a.TickOrder.CompareTo(b.TickOrder));
                _needsSort = false;
            }

            for (int t = 0; t < ticks; ++t)
            {
                long tickIndex = _clock.TickIndex - ticks + t + 1;

                // Indexed loop: a receiver may unregister itself mid-tick (an enemy dying), and
                // a foreach would throw on the modified list.
                for (int i = 0; i < _receivers.Count; ++i)
                    _receivers[i]?.OnSimulationTick(tickIndex);
            }
        }
    }
}
