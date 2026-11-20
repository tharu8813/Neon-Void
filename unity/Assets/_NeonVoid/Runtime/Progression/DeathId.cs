using System;

namespace NeonVoid.Progression
{
    /// <summary>
    /// The dedupe key for one death, as docs/04 section "RegisterDeath(deathId)" requires:
    /// <i>중복 deathId로 두 번 차감 금지</i>.
    /// <para>
    /// <b>Why this is composed rather than a fresh Guid.</b> The id exists so the same death
    /// cannot be charged twice, and the retry that most needs deduping is the one after the app
    /// died before the save was acknowledged. A Guid minted at the moment of death is gone by
    /// then: the next launch would mint a different one for the same death, the save service
    /// would see a death it had never heard of, and the player would be charged a second clone
    /// for dying once. That is the exact failure docs/04 calls out, and a random id cannot
    /// prevent it.
    /// </para>
    /// <para>
    /// Composed from the run, the actor and a counter within the run, the id is reproducible:
    /// the same run reaching the same death produces the same key on a replay and after a
    /// crash, so the second attempt is recognisable as the first one.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct DeathId : IEquatable<DeathId>
    {
        /// <summary>Which run this death happened in.</summary>
        public readonly long RunId;

        /// <summary>Which actor died. The run's own death is the player's actor.</summary>
        public readonly int ActorId;

        /// <summary>1-based count of this actor's deaths within the run.</summary>
        public readonly int Sequence;

        private DeathId(long runId, int actorId, int sequence)
        {
            RunId = runId;
            ActorId = actorId;
            Sequence = sequence;
        }

        /// <summary>The absent id. Never accepted by the ledger.</summary>
        public static DeathId None => default;

        /// <summary>True when every part is present. A partially filled id is not usable as a key.</summary>
        public bool IsValid => RunId > 0L && ActorId > 0 && Sequence > 0;

        public static DeathId For(long runId, int actorId, int sequence)
        {
            if (runId <= 0L)
                throw new ArgumentOutOfRangeException(nameof(runId), "A death belongs to a run; run ids start at 1.");
            if (actorId <= 0)
                throw new ArgumentOutOfRangeException(nameof(actorId), "Actor id 0 is ActorIdAllocator.NoActor.");
            if (sequence <= 0)
                throw new ArgumentOutOfRangeException(nameof(sequence), "Death sequence is 1-based.");

            return new DeathId(runId, actorId, sequence);
        }

        public bool Equals(DeathId other) =>
            RunId == other.RunId && ActorId == other.ActorId && Sequence == other.Sequence;

        public override bool Equals(object obj) => obj is DeathId other && Equals(other);

        public override int GetHashCode() => (RunId, ActorId, Sequence).GetHashCode();

        public static bool operator ==(DeathId a, DeathId b) => a.Equals(b);

        public static bool operator !=(DeathId a, DeathId b) => !a.Equals(b);

        /// <summary>
        /// Stable text form, so a receipt written by the save service can be matched back to a
        /// death by eye in a log. Changing this format changes how old receipts read.
        /// </summary>
        public override string ToString() =>
            IsValid ? $"run{RunId}-actor{ActorId}-death{Sequence}" : "death-none";
    }
}
