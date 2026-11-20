using UnityEngine;

namespace NeonVoid.Combat.Skills
{
    /// <summary>
    /// A straight band on the XZ plane: a length along a facing and a half-width either side.
    /// <para>
    /// The companion to <see cref="ConeTargeting"/>, for the boss patterns docs/01 describes as
    /// 직선 - CPU's 직선 펄스 회로, GPU's 세 방향 순차 광선. A cone cannot express one: widening a
    /// cone to cover a corridor makes it wider the further it reaches, so the far end of a
    /// telegraph would be dangerous in places the near end was not.
    /// </para>
    /// Pure: no state, no allocation, no engine calls beyond vector arithmetic.
    /// </summary>
    public static class LineTargeting
    {
        /// <summary>
        /// True when <paramref name="pointXZ"/> lies within the band that runs
        /// <paramref name="lengthUnits"/> from the origin along <paramref name="facingXZ"/>, and
        /// no further than <paramref name="halfWidthUnits"/> from its centre line.
        /// <para>
        /// Behind the origin is outside: a pulse fired forwards must not hit what is behind it.
        /// Standing exactly on the origin is inside, for the same reason
        /// <see cref="ConeTargeting"/> says so - it is the one position a shape should never be
        /// unable to hit.
        /// </para>
        /// </summary>
        public static bool IsInsideLine(
            Vector2 originXZ,
            Vector2 facingXZ,
            Vector2 pointXZ,
            float lengthUnits,
            float halfWidthUnits)
        {
            if (lengthUnits <= 0f || halfWidthUnits <= 0f)
                return false;

            Vector2 offset = pointXZ - originXZ;
            if (offset.sqrMagnitude <= 1e-8f)
                return true;

            // A band with no facing cannot be aimed. Unlike a cone, which falls back to the whole
            // circle, a line falls back to a circle of its own half-width: a 1-unit band that
            // suddenly covered an 18-unit disc would be a far bigger lie than one that covers a
            // small one.
            if (facingXZ.sqrMagnitude <= 1e-8f)
                return offset.sqrMagnitude <= halfWidthUnits * halfWidthUnits;

            Vector2 forward = facingXZ.normalized;
            float along = Vector2.Dot(offset, forward);
            if (along < 0f || along > lengthUnits)
                return false;

            float across = Mathf.Abs((offset.x * forward.y) - (offset.y * forward.x));
            return across <= halfWidthUnits;
        }

        /// <summary>
        /// True when <paramref name="pointXZ"/> is inside the annulus between
        /// <paramref name="innerRadiusUnits"/> and <paramref name="outerRadiusUnits"/>.
        /// <para>
        /// An inner radius above zero is a safe middle, which is what makes a pulse a decision
        /// rather than a damage tick: docs/01's CPU danger is 주기적 과열/코어펄스, and a ring
        /// whose safe spot is next to the boss asks the player to move toward it.
        /// </para>
        /// </summary>
        public static bool IsInsideRing(
            Vector2 originXZ,
            Vector2 pointXZ,
            float innerRadiusUnits,
            float outerRadiusUnits)
        {
            if (outerRadiusUnits <= 0f || innerRadiusUnits < 0f || innerRadiusUnits >= outerRadiusUnits)
                return false;

            float distanceSqr = (pointXZ - originXZ).sqrMagnitude;
            return distanceSqr >= innerRadiusUnits * innerRadiusUnits
                   && distanceSqr <= outerRadiusUnits * outerRadiusUnits;
        }
    }
}
