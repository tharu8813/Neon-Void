using UnityEngine;

namespace NeonVoid.Combat.AI
{
    /// <summary>
    /// Where a charging enemy is in its wind-up, dash and recovery.
    /// <para>
    /// Carried on the intent because docs/01 asks for 경고 프레임과 판정 동기 - the warning the
    /// player reacts to and the hit that lands must be the same event. Presentation reading the
    /// phase from the same intent that produced the hit is what keeps them in step; a telegraph
    /// animation with its own timer would drift from the damage by exactly as much as nobody
    /// would notice until it felt unfair.
    /// </para>
    /// </summary>
    public enum EnemyChargePhase
    {
        /// <summary>Not charging.</summary>
        None = 0,

        /// <summary>Winding up, standing still. This is the window the player dodges in.</summary>
        Telegraph = 1,

        /// <summary>Committed and moving fast along a direction chosen when the wind-up began.</summary>
        Dashing = 2,

        /// <summary>Spent, standing still. This is the window the player punishes.</summary>
        Recover = 3,
    }

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

        /// <summary>Which part of a charge this is, for both movement and the telegraph.</summary>
        public readonly EnemyChargePhase ChargePhase;

        public EnemyIntent(
            Vector2 moveXZ,
            float speedMultiplier,
            Vector2 faceXZ,
            bool wantsAttack,
            int targetActorId,
            EnemyChargePhase chargePhase = EnemyChargePhase.None)
        {
            MoveXZ = moveXZ;
            SpeedMultiplier = speedMultiplier;
            FaceXZ = faceXZ;
            WantsAttack = wantsAttack;
            TargetActorId = targetActorId;
            ChargePhase = chargePhase;
        }

        /// <summary>Nothing this tick: no movement, no attack, no target.</summary>
        public static EnemyIntent Idle => new EnemyIntent(Vector2.zero, 1f, Vector2.zero, false, 0);

        /// <summary>True while the enemy is committed to a charge it can no longer aim.</summary>
        public bool IsCharging => ChargePhase == EnemyChargePhase.Dashing;

        public bool HasTarget => TargetActorId != 0;
    }
}
