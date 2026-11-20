using System;
using UnityEngine;

namespace NeonVoid.Combat.AI
{
    /// <summary>
    /// Tunables for one enemy archetype. All values [초안]; `enemy.chaser` from docs/01 is the
    /// shape this covers, with ranged and charger behaviour still to come.
    /// </summary>
    [Serializable]
    public struct EnemyBrainConfig
    {
        /// <summary>Distance at which an idle enemy notices a target.</summary>
        public float AggroRadius;

        /// <summary>
        /// Distance at which it gives up. Must exceed <see cref="AggroRadius"/>: equal radii
        /// make an enemy standing exactly on the boundary acquire and drop its target on
        /// alternating ticks, which reads as a twitching enemy that never commits.
        /// </summary>
        public float LeashRadius;

        /// <summary>Distance it tries to hold. It stops approaching here.</summary>
        public float PreferredRange;

        /// <summary>
        /// Width of the band around <see cref="PreferredRange"/> in which it neither advances
        /// nor retreats. Without it an enemy oscillates across the exact range every tick.
        /// </summary>
        public float RangeToleranceUnits;

        /// <summary>Ticks between attacks.</summary>
        public int AttackIntervalTicks;

        /// <summary>Distance within which an attack may be attempted.</summary>
        public float AttackRange;

        /// <summary>
        /// Whether this archetype needs to see its target to attack it. True for anything that
        /// fires: docs/01 lists LOS as an implementation requirement for `enemy.ranged`.
        /// A contact attacker is already limited by having to reach the target.
        /// </summary>
        public bool RequiresLineOfSight;

        public static EnemyBrainConfig Chaser() => new EnemyBrainConfig
        {
            AggroRadius = 12f,
            LeashRadius = 18f,
            PreferredRange = 1.8f,
            RangeToleranceUnits = 0.4f,
            AttackIntervalTicks = 60,
            AttackRange = 2.4f,
        };

        /// <summary>
        /// [초안] `enemy.ranged` from docs/01: 근/원 조정·발사. The band is wide here because it
        /// is doing real work - a ranged enemy spends its life holding a distance, where the
        /// chaser only brushes past its band on the way in.
        /// </summary>
        public static EnemyBrainConfig Ranged() => new EnemyBrainConfig
        {
            AggroRadius = 16f,
            LeashRadius = 22f,
            PreferredRange = 8f,
            RangeToleranceUnits = 1f,
            AttackIntervalTicks = 90,
            AttackRange = 11f,
            RequiresLineOfSight = true,
        };

        public void Validate()
        {
            if (AggroRadius <= 0f)
                throw new InvalidOperationException($"{nameof(AggroRadius)} must be above zero.");
            if (LeashRadius <= AggroRadius)
                throw new InvalidOperationException(
                    $"{nameof(LeashRadius)} must exceed {nameof(AggroRadius)}, or an enemy on the boundary flickers between acquiring and dropping its target.");
            if (PreferredRange < 0f)
                throw new InvalidOperationException($"{nameof(PreferredRange)} must not be negative.");
            if (RangeToleranceUnits <= 0f)
                throw new InvalidOperationException(
                    $"{nameof(RangeToleranceUnits)} must be above zero, or the enemy oscillates across its preferred range every tick.");
            if (AttackIntervalTicks < 1)
                throw new InvalidOperationException($"{nameof(AttackIntervalTicks)} must be at least one tick.");
            if (AttackRange <= 0f)
                throw new InvalidOperationException($"{nameof(AttackRange)} must be above zero.");
        }
    }

    /// <summary>Everything the brain remembers between ticks.</summary>
    public struct EnemyBrainState
    {
        /// <summary>Currently chosen target, or 0.</summary>
        public int TargetActorId;

        /// <summary>Ticks until the next attack is allowed.</summary>
        public int AttackCooldownTicks;

        public static EnemyBrainState Fresh() => new EnemyBrainState { TargetActorId = 0, AttackCooldownTicks = 0 };
    }

    /// <summary>
    /// One tick of enemy decision making, as a pure function.
    /// <para>
    /// [확정] docs/02 section 8: this selects a target, decides where to move and whether it
    /// wants to attack. It never subtracts health. The returned <see cref="EnemyIntent"/> has
    /// no field it could do that with, so the rule holds structurally rather than by review.
    /// </para>
    /// <para>
    /// Two pieces of hysteresis are not optional polish. Leash exceeding aggro stops an enemy
    /// on the boundary flickering between chasing and idling on alternating ticks; the range
    /// tolerance band stops it stepping back and forth across its preferred distance forever.
    /// Both read as broken AI rather than as tuning, and neither shows up in a single-tick test.
    /// </para>
    /// No Unity scene types, no randomness, no time: same inputs, same decision.
    /// </summary>
    public static class EnemyBrain
    {
        public static EnemyIntent Decide(
            ref EnemyBrainState state,
            in EnemyPerception perception,
            in EnemyBrainConfig config)
        {
            if (state.AttackCooldownTicks > 0)
                state.AttackCooldownTicks--;

            // 정지: every action denied, but the cooldown above still runs down, so an enemy
            // does not emerge from a stun with a stale attack banked.
            if (perception.Stunned)
            {
                state.TargetActorId = ResolveTarget(ref state, perception, config);
                return new EnemyIntent(Vector2.zero, 0f, Vector2.zero, false, state.TargetActorId);
            }

            int targetId = ResolveTarget(ref state, perception, config);
            state.TargetActorId = targetId;

            if (targetId == 0 || !perception.TryGet(targetId, out PerceivedActor target))
                return EnemyIntent.Idle;

            Vector2 toTarget = target.PositionXZ - perception.SelfPositionXZ;
            float distance = toTarget.magnitude;
            Vector2 facing = distance > 1e-4f ? toTarget / distance : Vector2.zero;

            // docs/01 requires LOS for `enemy.ranged`, and it changes both halves of the
            // decision: an enemy that cannot see its target must not shoot through the wall,
            // and standing still behind cover forever is not a tactic. It closes in instead,
            // which is how peeking out from behind cover emerges without a special case.
            bool blocked = config.RequiresLineOfSight && !target.HasLineOfSight;

            Vector2 move = Vector2.zero;
            if (!perception.Rooted)
            {
                float near = config.PreferredRange - config.RangeToleranceUnits;
                float far = config.PreferredRange + config.RangeToleranceUnits;

                if (blocked)
                    move = facing;              // get a line on the target
                else if (distance > far)
                    move = facing;              // close in
                else if (distance < near)
                    move = -facing;             // back off
                // inside the band with a clear line: hold, deliberately
            }

            bool wantsAttack = !blocked && state.AttackCooldownTicks <= 0 && distance <= config.AttackRange;
            if (wantsAttack)
                state.AttackCooldownTicks = config.AttackIntervalTicks;

            return new EnemyIntent(move, perception.SpeedMultiplier, facing, wantsAttack, targetId);
        }

        /// <summary>
        /// Keeps the current target while it stays alive and inside the leash radius, otherwise
        /// picks the nearest living candidate inside the aggro radius.
        /// <para>
        /// Ties are broken by the lower actor id rather than by whichever happened to be
        /// earlier in the array, so two equidistant enemies cannot be chased in a different
        /// order on a replay of the same inputs.
        /// </para>
        /// </summary>
        private static int ResolveTarget(
            ref EnemyBrainState state, in EnemyPerception perception, in EnemyBrainConfig config)
        {
            if (state.TargetActorId != 0
                && perception.TryGet(state.TargetActorId, out PerceivedActor current)
                && current.IsAlive
                && (current.PositionXZ - perception.SelfPositionXZ).magnitude <= config.LeashRadius)
                return state.TargetActorId;

            int bestId = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < perception.Candidates.Length; ++i)
            {
                PerceivedActor candidate = perception.Candidates[i];
                if (!candidate.IsAlive)
                    continue;

                float distance = (candidate.PositionXZ - perception.SelfPositionXZ).magnitude;
                if (distance > config.AggroRadius)
                    continue;

                if (distance < bestDistance - 1e-5f ||
                    (Mathf.Abs(distance - bestDistance) <= 1e-5f && candidate.ActorId < bestId))
                {
                    bestDistance = distance;
                    bestId = candidate.ActorId;
                }
            }

            return bestId;
        }
    }
}
