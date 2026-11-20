using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// Whether a point is inside a cone on the XZ plane. Pure: the caller finds the candidates
    /// with physics, this decides which of them the cone actually covers.
    /// </summary>
    public static class ConeTargeting
    {
        /// <summary>
        /// True when <paramref name="pointXZ"/> is within <paramref name="radiusUnits"/> of the
        /// origin and within <paramref name="halfAngleDegrees"/> of the facing direction.
        /// <para>
        /// A target standing exactly on the caster counts as inside. Its direction is
        /// undefined, and normalising a zero vector would make the angle test NaN - which
        /// compares false, so the enemy pressed right up against the player would be the one
        /// target a cone could never hit.
        /// </para>
        /// </summary>
        public static bool IsInsideCone(
            Vector2 originXZ,
            Vector2 facingXZ,
            Vector2 pointXZ,
            float radiusUnits,
            float halfAngleDegrees)
        {
            if (radiusUnits <= 0f)
                return false;

            Vector2 offset = pointXZ - originXZ;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > radiusUnits * radiusUnits)
                return false;

            // On top of the caster: inside, by the argument above.
            if (distanceSqr <= 1e-8f)
                return true;

            // A cone with no facing cannot be aimed, so it covers the whole circle rather than
            // silently hitting nothing.
            if (facingXZ.sqrMagnitude <= 1e-8f)
                return true;

            if (halfAngleDegrees >= 180f)
                return true;

            float cosHalfAngle = Mathf.Cos(halfAngleDegrees * Mathf.Deg2Rad);
            float cosToTarget = Vector2.Dot(facingXZ.normalized, offset / Mathf.Sqrt(distanceSqr));

            // A small tolerance so a target sitting exactly on the edge is included rather than
            // flickering in and out with floating point noise.
            return cosToTarget >= cosHalfAngle - 1e-5f;
        }
    }
}
