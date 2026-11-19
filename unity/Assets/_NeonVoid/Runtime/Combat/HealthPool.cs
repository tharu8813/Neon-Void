using System;

namespace NeonVoid.Combat
{
    /// <summary>
    /// Hit points. A value type so a test can snapshot it, and so nothing can hold a stale
    /// reference to an actor's health after it dies.
    /// </summary>
    [Serializable]
    public struct HealthPool
    {
        public float Current;
        public float Max;

        public static HealthPool Full(float max) => new HealthPool { Current = max, Max = max };

        public bool IsAlive => Current > 0f;

        public float Fraction => Max > 0f ? Math.Min(1f, Math.Max(0f, Current / Max)) : 0f;

        /// <summary>
        /// Removes damage and reports how much health was actually lost, which is not the same
        /// as the damage asked for when the target had less health than that. Lifesteal and
        /// kill credit both need the real figure, not the requested one.
        /// </summary>
        public float Remove(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
                return 0f;

            float removed = Math.Min(amount, Math.Max(0f, Current));
            Current -= removed;
            if (Current < 0f)
                Current = 0f;
            return removed;
        }

        /// <summary>Heals, capped at <see cref="Max"/>. Returns health actually restored.</summary>
        public float Heal(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
                return 0f;

            float before = Current;
            Current = Math.Min(Max, Current + amount);
            return Current - before;
        }
    }
}
