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

        /// <summary>Whether this archetype charges instead of attacking where it stands.</summary>
        public bool UsesCharge;

        /// <summary>
        /// Ticks of wind-up before a charge commits. This is the dodge window, so it must be
        /// above zero: 회피 타이밍 is the thing docs/01 says this archetype teaches, and a
        /// charge with no warning teaches nothing.
        /// </summary>
        public int ChargeTelegraphTicks;

        /// <summary>Ticks the dash itself lasts.</summary>
        public int ChargeDurationTicks;

        /// <summary>Ticks of recovery afterwards, which is the window to punish it.</summary>
        public int ChargeRecoverTicks;

        /// <summary>Multiplier on base move speed during the dash.</summary>
        public float ChargeSpeedMultiplier;

        /// <summary>Distance at which it begins a wind-up.</summary>
        public float ChargeTriggerRange;

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

        /// <summary>
        /// [초안] `enemy.charger` from docs/01: 경고 뒤 돌진. It keeps its distance while it
        /// waits, so the player has somewhere to dodge to, then commits.
        /// </summary>
        public static EnemyBrainConfig Charger() => new EnemyBrainConfig
        {
            AggroRadius = 14f,
            LeashRadius = 20f,
            PreferredRange = 6f,
            RangeToleranceUnits = 1.2f,
            AttackIntervalTicks = 150,
            AttackRange = 2.6f,
            UsesCharge = true,
            ChargeTelegraphTicks = 36,        // 0.6 s of warning
            ChargeDurationTicks = 30,         // 0.5 s of dash
            ChargeRecoverTicks = 48,          // 0.8 s to be punished in
            ChargeSpeedMultiplier = 4.2f,
            ChargeTriggerRange = 8f,
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

            if (!UsesCharge)
                return;

            // A charge with no wind-up cannot be dodged, and dodging it is the one thing this
            // archetype exists to teach (docs/01: 회피 타이밍). Refusing beats shipping an
            // unavoidable attack that looks like a balance complaint.
            if (ChargeTelegraphTicks < 1)
                throw new InvalidOperationException(
                    $"{nameof(ChargeTelegraphTicks)} must be at least one tick, or the charge is unavoidable.");
            if (ChargeDurationTicks < 1)
                throw new InvalidOperationException($"{nameof(ChargeDurationTicks)} must be at least one tick.");
            if (ChargeRecoverTicks < 0)
                throw new InvalidOperationException($"{nameof(ChargeRecoverTicks)} must not be negative.");
            if (ChargeSpeedMultiplier <= 1f)
                throw new InvalidOperationException(
                    $"{nameof(ChargeSpeedMultiplier)} must exceed 1, or the charge is slower than walking.");
            if (ChargeTriggerRange <= 0f || ChargeTriggerRange > AggroRadius)
                throw new InvalidOperationException(
                    $"{nameof(ChargeTriggerRange)} must be above zero and within {nameof(AggroRadius)}; it cannot charge what it has not noticed.");
        }
    }

    /// <summary>Everything the brain remembers between ticks.</summary>
    public struct EnemyBrainState
    {
        /// <summary>Currently chosen target, or 0.</summary>
        public int TargetActorId;

        /// <summary>Ticks until the next attack is allowed.</summary>
        public int AttackCooldownTicks;

        /// <summary>Where a charge is, if one is running.</summary>
        public EnemyChargePhase ChargePhase;

        /// <summary>Ticks left in the current charge phase.</summary>
        public int ChargePhaseTicksRemaining;

        /// <summary>
        /// The direction the dash is locked to, chosen when the wind-up began.
        /// <para>
        /// Locked, and that is the whole point. An enemy that re-aimed during its dash would
        /// follow the player through the dodge, and the telegraph would be a lie: there would
        /// be no moment at which moving saved you.
        /// </para>
        /// </summary>
        public Vector2 ChargeDirectionXZ;

        /// <summary>Whether this charge has already connected. One hit per charge.</summary>
        public bool ChargeHasHit;

        public static EnemyBrainState Fresh() => new EnemyBrainState
        {
            TargetActorId = 0,
            AttackCooldownTicks = 0,
            ChargePhase = EnemyChargePhase.None,
            ChargePhaseTicksRemaining = 0,
            ChargeDirectionXZ = Vector2.zero,
            ChargeHasHit = false,
        };
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

                // 정지 cancels a charge outright rather than pausing it. A dash frozen
                // mid-flight would resume along a direction the player watched it commit to
                // seconds earlier, with no warning left to read; winding up again is both
                // fairer and what a stun is for.
                state.ChargePhase = EnemyChargePhase.None;
                state.ChargePhaseTicksRemaining = 0;

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

            if (config.UsesCharge)
                return DecideCharge(ref state, config, distance, facing, move, perception.SpeedMultiplier, targetId);

            bool wantsAttack = !blocked && state.AttackCooldownTicks <= 0 && distance <= config.AttackRange;
            if (wantsAttack)
                state.AttackCooldownTicks = config.AttackIntervalTicks;

            return new EnemyIntent(move, perception.SpeedMultiplier, facing, wantsAttack, targetId);
        }

        /// <summary>
        /// The charger's wind-up, dash and recovery.
        /// <para>
        /// [확정 요구] docs/01 asks for 경고 프레임과 판정 동기. The phase on the returned intent
        /// is the same value that decides whether a hit may land, so the warning the player sees
        /// and the damage that follows cannot drift apart: there is only one timer.
        /// </para>
        /// <para>
        /// Three properties make the dodge real, and each would be easy to lose:
        /// the wind-up does not move, so there is a moment to read; the direction is locked when
        /// the wind-up ends, so moving out of the line works; and the dash connects at most once,
        /// so brushing past is not thirty hits.
        /// </para>
        /// </summary>
        private static EnemyIntent DecideCharge(
            ref EnemyBrainState state,
            in EnemyBrainConfig config,
            float distance,
            Vector2 facing,
            Vector2 approachMove,
            float speedMultiplier,
            int targetId)
        {
            // Not charging yet: hold the band until it is close enough and off cooldown. The
            // cooldown is what stops a charger charging without pause.
            if (state.ChargePhase == EnemyChargePhase.None)
            {
                bool canBegin = state.AttackCooldownTicks <= 0 && distance <= config.ChargeTriggerRange;
                if (!canBegin)
                    return new EnemyIntent(
                        approachMove, speedMultiplier, facing, false, targetId, EnemyChargePhase.None);

                state.ChargePhase = EnemyChargePhase.Telegraph;
                state.ChargePhaseTicksRemaining = config.ChargeTelegraphTicks;
            }

            // ChargePhaseTicksRemaining counts the intents this phase has left to emit,
            // INCLUDING this one. Emit first, then spend one: that is what makes each phase
            // last exactly as many ticks as its config says, which is the whole of
            // 경고 프레임과 판정 동기. Decrementing first gives the entry tick away, and the
            // same mistake in the other direction gives the dash a free tick.
            switch (state.ChargePhase)
            {
                case EnemyChargePhase.Telegraph:
                {
                    // Standing still, facing the target, doing nothing: the dodge window. The
                    // facing is tracked right up to the last wind-up tick, so what the player
                    // reads is current - and then it is frozen, below.
                    var intent = new EnemyIntent(
                        Vector2.zero, speedMultiplier, facing, false, targetId, EnemyChargePhase.Telegraph);

                    if (--state.ChargePhaseTicksRemaining <= 0)
                    {
                        state.ChargePhase = EnemyChargePhase.Dashing;
                        state.ChargePhaseTicksRemaining = config.ChargeDurationTicks;
                        state.ChargeHasHit = false;

                        // The direction is fixed HERE, from the last facing the player had a
                        // chance to read, and never touched again during the dash. An enemy
                        // that re-aimed mid-dash would follow the player through the dodge and
                        // the telegraph would be a lie.
                        state.ChargeDirectionXZ =
                            facing.sqrMagnitude > 1e-6f ? facing.normalized : state.ChargeDirectionXZ;
                    }

                    return intent;
                }

                case EnemyChargePhase.Dashing:
                {
                    // At most one hit per charge: brushing past something must not be thirty
                    // hits, one per tick of contact.
                    bool connects = !state.ChargeHasHit && distance <= config.AttackRange;
                    if (connects)
                        state.ChargeHasHit = true;

                    var intent = new EnemyIntent(
                        state.ChargeDirectionXZ,
                        speedMultiplier * config.ChargeSpeedMultiplier,
                        state.ChargeDirectionXZ,
                        connects,
                        targetId,
                        EnemyChargePhase.Dashing);

                    if (--state.ChargePhaseTicksRemaining <= 0)
                    {
                        state.ChargePhase = config.ChargeRecoverTicks > 0
                            ? EnemyChargePhase.Recover
                            : EnemyChargePhase.None;
                        state.ChargePhaseTicksRemaining = config.ChargeRecoverTicks;
                        state.AttackCooldownTicks = config.AttackIntervalTicks;
                    }

                    return intent;
                }

                case EnemyChargePhase.Recover:
                {
                    // Spent and standing still: the window to punish it in.
                    var intent = new EnemyIntent(
                        Vector2.zero, speedMultiplier, facing, false, targetId, EnemyChargePhase.Recover);

                    if (--state.ChargePhaseTicksRemaining <= 0)
                        state.ChargePhase = EnemyChargePhase.None;

                    return intent;
                }
            }

            return new EnemyIntent(
                approachMove, speedMultiplier, facing, false, targetId, EnemyChargePhase.None);
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
