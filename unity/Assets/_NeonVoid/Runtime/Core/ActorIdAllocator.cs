using System;

namespace NeonVoid.Core
{
    /// <summary>
    /// Hands out actor identities that are never reused.
    /// <para>
    /// This exists because of a hazard that only shows up once enemies are pooled. A projectile
    /// remembers which actors it has already hit, by id. If a dead enemy's id is recycled onto
    /// a freshly spawned one, a projectile still in flight will decide it has "already hit"
    /// that enemy and pass straight through it. The bug is rare, depends on pool timing, and
    /// looks exactly like a missed collision, so it is close to impossible to find by playing.
    /// </para>
    /// <para>
    /// Pooling the GameObject is fine and expected; pooling the <i>identity</i> is not. Every
    /// spawn takes a fresh id from here, even when the object behind it is recycled.
    /// </para>
    /// </summary>
    public sealed class ActorIdAllocator
    {
        /// <summary>Reserved for "no actor". Never issued.</summary>
        public const int NoActor = 0;

        private int _next;

        public ActorIdAllocator(int firstId = 1)
        {
            if (firstId <= NoActor)
                throw new ArgumentOutOfRangeException(nameof(firstId), "Ids must be above the reserved NoActor value.");
            _next = firstId;
        }

        /// <summary>How many ids have been issued.</summary>
        public int IssuedCount { get; private set; }

        /// <summary>
        /// Issues the next identity. Throws rather than wrapping: a wrapped id would silently
        /// reintroduce exactly the aliasing this type exists to prevent.
        /// </summary>
        public int Next()
        {
            if (_next == int.MaxValue)
                throw new InvalidOperationException(
                    "Actor ids are exhausted. Wrapping would alias a new actor onto a dead one.");

            int id = _next++;
            IssuedCount++;
            return id;
        }

        public static bool IsValid(int actorId) => actorId != NoActor;
    }
}
