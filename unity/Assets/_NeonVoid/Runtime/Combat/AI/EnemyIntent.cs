using UnityEngine;

namespace NeonVoid.Combat.AI
{
    /// <summary>
    /// What an enemy brain decided to do this tick.
    /// <para>
    /// [확정] docs/02 section 8: AI handles target selection, pathing and <b>attack intent</b>
    /// only, and may never subtract health itself. That rule is enforced structurally rather
    /// than by convention: this type has no damage, no health and no target reference it could
    /// mutate. A brain physically cannot deal damage, so the rule cannot be broken by a later
    /// edit that forgets it.
    /// </para>
    /// <para>
    /// Everything an enemy does to a player therefore goes through the same
    /// <c>CombatActor.ReceiveDamage</c> entry point the player's own attacks use, which is what
    /// gives enemies and players identical armour, invulnerability, shield and status handling.
    /// </para>
    /// </summary>
    public readonly struct EnemyIntent
    {
        /// <summary>Desired movement direction on the XZ plane. Zero means hold position.</summary>
        public readonly Vector2 MoveXZ;

        /// <summary>
        /// Fraction of base move speed to use this tick, after slows. 1 is unimpeded.
        /// </summary>
        public readonly float SpeedMultiplier;

        /// <summary>Direction the enemy wants to face. Zero means keep the current facing.</summary>
        public readonly Vector2 FaceXZ;

        /// <summary>True when the enemy wants to attack its target this tick.</summary>
        public readonly bool WantsAttack;

        /// <summary>Actor the intent is aimed at, or 0 when there is no target.</summary>
        public readonly int TargetActorId;

        public EnemyIntent(Vector2 moveXZ, float speedMultiplier, Vector2 faceXZ, bool wantsAttack, int targetActorId)
        {
            MoveXZ = moveXZ;
            SpeedMultiplier = speedMultiplier;
            FaceXZ = faceXZ;
            WantsAttack = wantsAttack;
            TargetActorId = targetActorId;
        }

        /// <summary>Nothing this tick: no movement, no attack, no target.</summary>
        public static EnemyIntent Idle => new EnemyIntent(Vector2.zero, 1f, Vector2.zero, false, 0);

        public bool HasTarget => TargetActorId != 0;
    }
}
