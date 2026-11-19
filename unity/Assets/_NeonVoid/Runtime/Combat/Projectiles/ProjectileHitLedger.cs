using System;
using System.Collections.Generic;
using NeonVoid.Core;

namespace NeonVoid.Combat.Projectiles
{
    /// <summary>Why a projectile was or was not allowed to damage a target.</summary>
    public enum ProjectileHitVerdict
    {
        /// <summary>The hit counts; apply damage.</summary>
        Allowed = 0,

        /// <summary>This projectile already damaged this actor. docs/02 section 9 item 8.</summary>
        AlreadyHit = 1,

        /// <summary>The projectile ran out of targets it may pass through.</summary>
        PierceExhausted = 2,

        /// <summary>The target id is not a real actor; the hit is discarded.</summary>
        InvalidTarget = 3,
    }

    /// <summary>
    /// What one projectile has already hit.
    /// <para>
    /// [확정] docs/02 section 5 (Pierce) and section 9 item 8: a projectile passes through
    /// several enemies, but the <b>same</b> enemy may not be damaged twice by the <b>same</b>
    /// projectile. Physics alone does not give this: a piercing projectile overlapping a large
    /// collider reports a trigger every physics step, so without a ledger a slow projectile
    /// inside a boss hitbox deals damage every frame and the boss melts in a way that scales
    /// with frame rate.
    /// </para>
    /// <para>
    /// Identity is the stable actor id and nothing else. Keying on a collider, a transform or a
    /// boss "phase" would let a phase transition look like a new target and hand the projectile
    /// a second free hit, which docs/02 section 9 item 8 names as a case that must stay safe.
    /// </para>
    /// Pure C#, no Unity types, so the rule is tested without a scene.
    /// </summary>
    public sealed class ProjectileHitLedger
    {
        /// <summary>Pierce budget meaning "no limit", for the infinite pierce status.</summary>
        public const int UnlimitedTargets = int.MaxValue;

        private readonly HashSet<int> _damagedActorIds = new HashSet<int>();

        /// <param name="maxDistinctTargets">
        /// How many different actors this projectile may damage. 1 is an ordinary
        /// non-piercing shot; <see cref="UnlimitedTargets"/> is infinite pierce.
        /// </param>
        public ProjectileHitLedger(int maxDistinctTargets = 1)
        {
            if (maxDistinctTargets < 1)
                throw new ArgumentOutOfRangeException(nameof(maxDistinctTargets),
                    "A projectile that may damage nobody would be a silently dead shot.");
            MaxDistinctTargets = maxDistinctTargets;
        }

        public int MaxDistinctTargets { get; }

        /// <summary>Distinct actors damaged so far.</summary>
        public int DamagedCount => _damagedActorIds.Count;

        /// <summary>True once the pierce budget is used up and the projectile should despawn.</summary>
        public bool IsSpent => _damagedActorIds.Count >= MaxDistinctTargets;

        /// <summary>Whether this projectile already damaged that actor.</summary>
        public bool HasDamaged(int actorId) => _damagedActorIds.Contains(actorId);

        /// <summary>
        /// Asks whether the projectile may damage <paramref name="actorId"/>, without recording
        /// anything. Useful for a caller that wants to look before it commits.
        /// </summary>
        public ProjectileHitVerdict Evaluate(int actorId)
        {
            if (!ActorIdAllocator.IsValid(actorId))
                return ProjectileHitVerdict.InvalidTarget;
            if (_damagedActorIds.Contains(actorId))
                return ProjectileHitVerdict.AlreadyHit;
            if (IsSpent)
                return ProjectileHitVerdict.PierceExhausted;
            return ProjectileHitVerdict.Allowed;
        }

        /// <summary>
        /// Evaluates and, when allowed, records the hit. Call this once per contact; the caller
        /// applies damage only for <see cref="ProjectileHitVerdict.Allowed"/>.
        /// <para>
        /// Recording happens here rather than in the caller on purpose: a caller that applied
        /// damage and forgot to record would re-hit on the next physics step, which is the exact
        /// failure this type prevents.
        /// </para>
        /// </summary>
        public ProjectileHitVerdict TryRegisterHit(int actorId)
        {
            ProjectileHitVerdict verdict = Evaluate(actorId);
            if (verdict == ProjectileHitVerdict.Allowed)
                _damagedActorIds.Add(actorId);
            return verdict;
        }

        /// <summary>
        /// Resets the ledger for reuse from an object pool. The projectile's identity as a shot
        /// is what the ledger tracks, so a pooled projectile must start a genuinely empty one.
        /// </summary>
        public void ResetForReuse() => _damagedActorIds.Clear();

        /// <summary>
        /// Actors damaged so far. Ordering is not guaranteed and must not be used to drive
        /// simulation; it is for diagnostics only.
        /// </summary>
        public IReadOnlyCollection<int> DamagedActorIds => _damagedActorIds;
    }
}
