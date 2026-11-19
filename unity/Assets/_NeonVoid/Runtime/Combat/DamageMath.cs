using System;

namespace NeonVoid.Combat
{
    /// <summary>
    /// The damage formula from docs/02 section 3. Pure static math, no Unity types, so every
    /// test vector in the document can be checked directly.
    /// <para>
    /// The rules that are <b>[확정]</b>: three damage types, physical reduced by armor, magic by
    /// magic resistance, true damage bypassing both resistances and both penetrations, crit
    /// allowed per attack, penetration split into flat and percent. The arithmetic below is the
    /// document's <b>[초안]</b> formula and is tuning, not an approved constant.
    /// </para>
    /// </summary>
    public static class DamageMath
    {
        /// <summary>
        /// Resistance remaining after penetration: percent first, then flat, exactly as
        /// docs/02 section 3 orders it. The order matters: flat-then-percent would scale the
        /// flat reduction too and produce a different number for the same stats.
        /// <para>
        /// Penetration cannot push a positive resistance below zero. Letting it do so would
        /// turn penetration into bonus damage through the negative-resistance branch, which is
        /// an amplifier the document does not grant. A resistance that is <i>already</i>
        /// negative is left alone.
        /// </para>
        /// </summary>
        public static float ResistanceAfterPenetration(float resistance, float percentPen, float flatPen)
        {
            if (resistance <= 0f)
                return resistance;

            float afterPercent = resistance * (1f - Clamp01(percentPen));
            float afterFlat = afterPercent - Math.Max(0f, flatPen);
            return Math.Max(0f, afterFlat);
        }

        /// <summary>
        /// Fraction of raw damage that survives the given resistance.
        /// <para>
        /// Positive resistance reduces damage along <c>100 / (100 + r)</c>: r = 100 leaves 50%.
        /// Negative resistance amplifies along <c>2 - 100 / (100 - r)</c>: r = -100 gives 150%,
        /// and the curve saturates toward 2x instead of growing without bound, so a deeply
        /// shredded target cannot take arbitrarily large damage.
        /// </para>
        /// </summary>
        public static float MitigationMultiplier(float resistanceAfterPenetration)
        {
            float r = resistanceAfterPenetration;
            if (float.IsNaN(r))
                return 1f;

            return r >= 0f
                ? 100f / (100f + r)
                : 2f - 100f / (100f - r);
        }

        /// <summary>
        /// Raw damage before any resistance: <c>flat + AD*adRatio + AP*apRatio</c>, then the
        /// critical multiplier when the attack both allows a crit and rolled one.
        /// </summary>
        public static float RawDamage(in DamageRequest request, in CombatStats attacker, bool critRolled)
        {
            float raw = request.Flat
                        + attacker.AttackDamage * request.AdRatio
                        + attacker.AbilityPower * request.ApRatio;

            if (request.CanCrit && critRolled)
                raw *= attacker.CritDamageMultiplier;

            return Math.Max(0f, raw);
        }

        /// <summary>
        /// Resolves one hit all the way to the damage that reaches health, before shields.
        /// <para>
        /// Shield absorption is deliberately NOT applied here: docs/08 D-004 leaves the order
        /// and interaction of multiple shields unresolved, so inventing one would bake an
        /// unapproved rule into the damage pipeline.
        /// </para>
        /// </summary>
        public static DamageResolution Resolve(
            in DamageRequest request,
            in CombatStats attacker,
            in CombatStats defender,
            bool critRolled)
        {
            float raw = RawDamage(request, attacker, critRolled);
            bool isCritical = request.CanCrit && critRolled;

            if (request.Type == DamageType.True)
            {
                // [확정] True damage consults neither resistance nor penetration.
                return new DamageResolution(raw, 1f, raw, isCritical, request.Type);
            }

            float resistance = defender.ResistanceFor(request.Type);
            float afterPen = ResistanceAfterPenetration(
                resistance,
                attacker.PercentPenetrationFor(request.Type),
                attacker.FlatPenetrationFor(request.Type));

            float mitigation = MitigationMultiplier(afterPen);
            float applied = Math.Max(0f, raw * mitigation);

            return new DamageResolution(raw, mitigation, applied, isCritical, request.Type);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value))
                return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
