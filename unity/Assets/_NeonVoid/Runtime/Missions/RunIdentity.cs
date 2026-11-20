using System;

namespace NeonVoid.Missions
{
    /// <summary>
    /// [확정] docs/06 section 4 keeps mission, combat and reward randomness in separate
    /// streams, and presentation randomness out of all of them.
    /// <para>
    /// Separate so that drawing one more reward card cannot shift what the next map generates.
    /// Sharing one generator makes every system's output depend on how often every other
    /// system happened to ask for a number, which is exactly the kind of coupling that makes a
    /// seed useless for reproducing a run.
    /// </para>
    /// </summary>
    public enum RandomStream
    {
        /// <summary>Map generation, enemy placement, environment rolls.</summary>
        Mission = 1,

        /// <summary>Crit rolls, proc chances, anything resolved during a fight.</summary>
        Combat = 2,

        /// <summary>Reward cards, rerolls, plugin rolls.</summary>
        Reward = 3,
    }

    /// <summary>
    /// Which run this is and what it was seeded with.
    /// <para>
    /// This is the identity the whole run hangs off: docs/04 lists runId and 시드 as part of
    /// 의뢰 상태, and the death receipt is keyed on the run id. Until this existed both were
    /// hardcoded - the receipt said run 1 forever, and the plugin roll used one constant seed,
    /// so every run rolled the same "random" stats.
    /// </para>
    /// <para>
    /// <b>Nothing here is saved.</b> docs/04 puts 의뢰 상태 in permanent storage behind an
    /// approved save commit, and no save service exists. This is the in-memory half: it gives
    /// the run an identity for as long as the process lives, and the save service will later be
    /// handed the same values rather than inventing its own.
    /// </para>
    /// </summary>
    [Serializable]
    public readonly struct RunIdentity
    {
        public readonly long RunId;

        /// <summary>The seed the run was started from. Reproducing a run needs this and the id.</summary>
        public readonly uint Seed;

        public RunIdentity(long runId, uint seed)
        {
            if (runId <= 0L)
                throw new ArgumentOutOfRangeException(nameof(runId), "Run ids start at 1; 0 means no run.");

            RunId = runId;
            Seed = seed;
        }

        /// <summary>The absent run. Not usable as an identity.</summary>
        public static RunIdentity None => default;

        public bool IsValid => RunId > 0L;

        /// <summary>
        /// The seed one stream starts from, derived from the run and the stream.
        /// <para>
        /// Derived rather than stored, so a run is reproducible from two numbers: the same run
        /// and seed always produce the same per-stream seeds, and adding a fourth stream later
        /// cannot change the three that exist.
        /// </para>
        /// <para>
        /// Integer operations only, all unchecked. A derivation that touched a float would be
        /// free to differ between platforms, and a seed that differs between platforms is not
        /// a seed.
        /// </para>
        /// </summary>
        public uint SeedFor(RandomStream stream)
        {
            unchecked
            {
                uint h = Seed ^ 0x9E3779B9u;
                h = (h ^ (uint)RunId) * 2654435761u;
                h = (h ^ (uint)(RunId >> 32)) * 2246822519u;
                h = (h ^ ((uint)stream * 2654435769u)) * 3266489917u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return h;
            }
        }

        public override string ToString() => IsValid ? $"run{RunId}#{Seed}" : "run-none";
    }
}
