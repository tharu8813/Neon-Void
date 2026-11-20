using System;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// When a persistent field damages, and when it is over. Pure, because an off-by-one here
    /// is a balance change: one extra tick on a five second field is a free 20% of its damage.
    /// </summary>
    [Serializable]
    public struct FieldSchedule
    {
        /// <summary>How long the field lives, in ticks.</summary>
        public int DurationTicks;

        /// <summary>Ticks between damage events.</summary>
        public int TickIntervalTicks;

        public static FieldSchedule Of(int durationTicks, int tickIntervalTicks) => new FieldSchedule
        {
            DurationTicks = durationTicks,
            TickIntervalTicks = tickIntervalTicks,
        };

        public void Validate()
        {
            if (DurationTicks < 1)
                throw new InvalidOperationException($"{nameof(DurationTicks)} must be at least one tick.");
            if (TickIntervalTicks < 1)
                throw new InvalidOperationException(
                    $"{nameof(TickIntervalTicks)} must be at least one tick, or the field would damage every tick of its life.");
        }

        /// <summary>
        /// True once the field has run its course. Checked before damage, so the tick on which
        /// a field expires does not also get to hurt anybody.
        /// </summary>
        public bool IsExpired(int elapsedTicks) => elapsedTicks >= DurationTicks;

        /// <summary>
        /// True on the ticks that deal damage. The field damages immediately on the tick it
        /// appears (elapsed 0) and every interval after, so dropping one on top of an enemy
        /// does something at once instead of looking inert for half a second.
        /// </summary>
        public bool DamagesOnTick(int elapsedTicks)
        {
            if (elapsedTicks < 0 || IsExpired(elapsedTicks) || TickIntervalTicks < 1)
                return false;

            return elapsedTicks % TickIntervalTicks == 0;
        }

        /// <summary>
        /// How many damage events a full lifetime produces. Exists so the number can be
        /// asserted against the design rather than counted by hand from play.
        /// </summary>
        public int DamageEventCount()
        {
            if (DurationTicks < 1 || TickIntervalTicks < 1)
                return 0;

            return (DurationTicks - 1) / TickIntervalTicks + 1;
        }
    }
}
