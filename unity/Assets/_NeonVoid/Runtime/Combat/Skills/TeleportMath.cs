using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Where a blink wants to land, before geometry gets a say. Pure; the caller is responsible
    /// for refusing a destination inside a wall, which needs the scene.
    /// </summary>
    public static class TeleportMath
    {
        /// <summary>
        /// The point at most <paramref name="maxDistanceUnits"/> from the caster in the
        /// direction of the aim. Aiming further away blinks the full distance rather than
        /// refusing, so the skill is usable without pixel-accurate cursor placement.
        /// <para>
        /// Aiming at your own feet returns your own position: the cast is still spent, but
        /// nothing is normalised, so no NaN reaches the transform.
        /// </para>
        /// </summary>
        public static Vector2 Resolve(Vector2 selfXZ, Vector2 aimXZ, float maxDistanceUnits)
        {
            if (maxDistanceUnits <= 0f)
                return selfXZ;

            Vector2 offset = aimXZ - selfXZ;
            float distance = offset.magnitude;
            if (distance <= 1e-5f)
                return selfXZ;

            return distance <= maxDistanceUnits
                ? aimXZ
                : selfXZ + offset / distance * maxDistanceUnits;
        }
    }
}
