using System;

namespace NeonVoid.Missions
{
    /// <summary>
    /// "Stand here for a moment." A radius and a number of ticks, held on the 60 Hz logic tick.
    /// <para>
    /// This is how a 정화 콘솔 and a 철수 패드 are reached without an interact key: D-009 in
    /// docs/08 leaves the control scheme undecided, and inventing a keybinding would answer it.
    /// A dwell answers nothing - it is a place and a duration, both [초안] - and it is
    /// deterministic, which an input event dispatched between ticks would not be.
    /// </para>
    /// <para>
    /// <b>Leaving resets the dwell.</b> [초안]: the documents say nothing, and resetting is the
    /// reading a player can see - the bar empties when you step off. Accumulating across visits
    /// would let someone brush past a pad five times and trigger it on the fifth, with nothing on
    /// screen explaining why.
    /// </para>
    /// Pure C#: no Unity types, no clock of its own.
    /// </summary>
    public sealed class DwellGate
    {
        private readonly float _radiusUnits;
        private readonly int _requiredTicks;

        public DwellGate(float radiusUnits, int requiredTicks)
        {
            if (float.IsNaN(radiusUnits) || radiusUnits <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radiusUnits), radiusUnits, "A zone nobody can stand in would never open.");
            }

            if (requiredTicks < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredTicks), requiredTicks,
                    "A dwell of zero ticks fires the instant the player touches the edge, which is not a dwell.");
            }

            _radiusUnits = radiusUnits;
            _requiredTicks = requiredTicks;
        }

        /// <summary>How far from the centre counts as standing here.</summary>
        public float RadiusUnits => _radiusUnits;

        /// <summary>Ticks of continuous presence the gate asks for.</summary>
        public int RequiredTicks => _requiredTicks;

        /// <summary>Consecutive ticks the player has been inside.</summary>
        public int TicksHeld { get; private set; }

        /// <summary>Whether the player was inside on the last tick.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>0 to 1, for a progress ring. 1 only once the dwell is complete.</summary>
        public float Progress =>
            TicksHeld >= _requiredTicks ? 1f : (float)TicksHeld / _requiredTicks;

        /// <summary>
        /// Advances one tick and says whether the dwell <b>completed on this tick</b>.
        /// <para>
        /// True exactly once per dwell, not every tick after it: the thing a gate opens -
        /// a purification, an extraction - must not be asked for again every tick while the
        /// player stands still. Standing on past the required count keeps the gate satisfied and
        /// reports false.
        /// </para>
        /// </summary>
        public bool Tick(float distanceFromCentre)
        {
            bool inside = !float.IsNaN(distanceFromCentre) && distanceFromCentre <= _radiusUnits;
            IsOccupied = inside;

            if (!inside)
            {
                TicksHeld = 0;
                return false;
            }

            if (TicksHeld >= _requiredTicks)
                return false;

            TicksHeld++;
            return TicksHeld >= _requiredTicks;
        }

        /// <summary>Whether the dwell has been completed and not left since.</summary>
        public bool IsSatisfied => TicksHeld >= _requiredTicks;

        /// <summary>
        /// Puts the gate back to empty. Used when what the gate opened was refused - a
        /// purification the save would not take - so the player dwells again rather than the
        /// game retrying every tick behind their back.
        /// </summary>
        public void Reset()
        {
            TicksHeld = 0;
            IsOccupied = false;
        }

        public override string ToString() => $"dwell {TicksHeld}/{_requiredTicks} within {_radiusUnits}u";
    }
}
