using System;

namespace NeonVoid.Core
{
    /// <summary>
    /// Turns real elapsed time into a whole number of fixed logic ticks.
    /// <para>
    /// The tick delta is an exact compile-time constant derived from
    /// <see cref="DesignConstants.SimulationTicksPerSecond"/> and is deliberately NOT
    /// Unity's <c>Time.fixedDeltaTime</c>: a project setting or a frame-rate change must
    /// never be able to alter a simulation result. Simulation code advances one tick at a
    /// time and never sees a variable delta.
    /// </para>
    /// <para>
    /// Catch-up is bounded by <see cref="MaxTicksPerAdvance"/>. Time beyond that budget is
    /// counted in <see cref="DroppedTicks"/> rather than silently discarded, so a stall is
    /// observable instead of becoming an invisible gameplay difference.
    /// </para>
    /// Pure C#: no UnityEngine types, so it is exercised without opening the Editor.
    /// </summary>
    public sealed class SimulationClock
    {
        /// <summary>Exact seconds advanced by one logic tick (1/60 at 60 Hz).</summary>
        public const double TickDeltaSeconds = 1.0 / DesignConstants.SimulationTicksPerSecond;

        private double _accumulatorSeconds;

        public SimulationClock(int maxTicksPerAdvance = 8)
        {
            if (maxTicksPerAdvance < 1)
                throw new ArgumentOutOfRangeException(nameof(maxTicksPerAdvance),
                    "A clock that can never advance a tick would freeze the simulation.");
            MaxTicksPerAdvance = maxTicksPerAdvance;
        }

        /// <summary>Ticks executed since construction. The simulation's only time axis.</summary>
        public long TickIndex { get; private set; }

        /// <summary>Upper bound on ticks a single <see cref="Advance"/> may run.</summary>
        public int MaxTicksPerAdvance { get; }

        /// <summary>Ticks skipped because a frame exceeded the catch-up budget.</summary>
        public long DroppedTicks { get; private set; }

        /// <summary>
        /// Fraction of a tick already accumulated, in [0, 1). Presentation may use this to
        /// interpolate between the previous and current tick. Simulation must not.
        /// </summary>
        public double InterpolationAlpha => _accumulatorSeconds / TickDeltaSeconds;

        /// <summary>
        /// Adds <paramref name="realDeltaSeconds"/> of wall time and returns how many whole
        /// ticks to run now. Callers run exactly that many ticks.
        /// </summary>
        public int Advance(double realDeltaSeconds)
        {
            if (double.IsNaN(realDeltaSeconds) || double.IsInfinity(realDeltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds),
                    "A non-finite frame delta would corrupt the accumulator.");
            if (realDeltaSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds),
                    "Time does not run backwards in the simulation.");

            _accumulatorSeconds += realDeltaSeconds;

            int ticks = 0;
            while (_accumulatorSeconds >= TickDeltaSeconds && ticks < MaxTicksPerAdvance)
            {
                _accumulatorSeconds -= TickDeltaSeconds;
                ticks++;
            }

            if (_accumulatorSeconds >= TickDeltaSeconds)
            {
                long overflow = (long)(_accumulatorSeconds / TickDeltaSeconds);
                DroppedTicks += overflow;
                _accumulatorSeconds -= overflow * TickDeltaSeconds;
            }

            TickIndex += ticks;
            return ticks;
        }
    }
}
