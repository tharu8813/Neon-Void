using System;

namespace NeonVoid.Progression.Save
{
    /// <summary>How preparing a slot for play ended.</summary>
    public enum SlotBootstrapVerdict
    {
        /// <summary>An existing vaccine is loaded and a run id was allocated.</summary>
        Ready = 0,

        /// <summary>There was no save; a first vaccine was created.</summary>
        CreatedSlot = 1,

        /// <summary>The previous vaccine was destroyed; its successor was created.</summary>
        StartedNextVaccine = 2,

        /// <summary>
        /// The vaccine is destroyed and starting a successor was not permitted. Not an error -
        /// docs/04 ends 최종 파기 at a selection screen, and this is what waiting for one looks
        /// like.
        /// </summary>
        AwaitingNextVaccine = 3,

        /// <summary>The store halted. Nothing may be written until a person looks at it.</summary>
        Halted = 4,

        /// <summary>A write failed. The save is intact and unchanged; play must not commit.</summary>
        WriteFailed = 5,

        /// <summary>
        /// No store was built at all - permanent writes are switched off, or there was no save
        /// directory to use. Distinct from a failure so a session deliberately played without
        /// consequences does not report as a broken save.
        /// </summary>
        NotConnected = 6,
    }

    public readonly struct SlotBootstrapResult
    {
        public readonly SlotBootstrapVerdict Verdict;

        /// <summary>The run id this session must use, or 0 when none was allocated.</summary>
        public readonly long RunId;

        public readonly string VaccineId;
        public readonly int Generation;
        public readonly int CopiesRemaining;
        public readonly string Detail;

        internal SlotBootstrapResult(
            SlotBootstrapVerdict verdict, long runId, string vaccineId, int generation,
            int copiesRemaining, string detail)
        {
            Verdict = verdict;
            RunId = runId;
            VaccineId = vaccineId ?? string.Empty;
            Generation = generation;
            CopiesRemaining = copiesRemaining;
            Detail = detail ?? string.Empty;
        }

        /// <summary>
        /// True when a run may begin and its deaths may be committed. Everything else - a halt,
        /// a failed write, a vaccine waiting to be replaced - means the save cannot take a
        /// result, and docs/04 is clear that play must not pretend otherwise.
        /// </summary>
        public bool CanCommit =>
            RunId > 0L
            && (Verdict == SlotBootstrapVerdict.Ready
                || Verdict == SlotBootstrapVerdict.CreatedSlot
                || Verdict == SlotBootstrapVerdict.StartedNextVaccine);

        public override string ToString() =>
            $"{Verdict} run={RunId} vaccine={VaccineId} gen={Generation} clones={CopiesRemaining} {Detail}";
    }

    /// <summary>
    /// Gets a save slot from "whatever is on disk" to "a run may begin", through the save
    /// transaction and nothing else.
    /// <para>
    /// Pure, and separate from the component that owns it, so the whole sequence - a missing
    /// save, a destroyed vaccine, a halted store, a disk that refuses a write - can be tested
    /// against a temporary directory. docs/04 section 4 forbids a test reaching the player's
    /// save directory, and a MonoBehaviour that resolved its own path could not be tested at
    /// all.
    /// </para>
    /// <para>
    /// <b>It allocates the run id, and that is the point.</b> A run whose id came from a
    /// serialized constant mints the same death receipt every launch, and the save - correctly -
    /// refuses the second launch's first death as one it has already settled. So no run begins
    /// here without an id from permanent state, and if the allocation cannot be committed, the
    /// answer is that play may not commit anything rather than that play may use a guess.
    /// </para>
    /// </summary>
    public static class VaccineSlotBootstrap
    {
        /// <summary>
        /// Opens the slot if needed, makes sure there is a living vaccine, and takes a run id.
        /// </summary>
        /// <param name="sessionNonce">
        /// Stable for this session and different from every other. The run id allocation is
        /// retried under it, so a lost acknowledgement returns the id already taken instead of
        /// burning another, while a genuinely new session takes a new id.
        /// </param>
        /// <param name="newVaccineId">
        /// Supplies an id for a vaccine that has to be created. Called at most once.
        /// </param>
        /// <param name="mayStartNextVaccine">
        /// Whether a destroyed vaccine may be replaced automatically. docs/04 wants a selection
        /// screen here; false is the honest answer while there is none.
        /// </param>
        public static SlotBootstrapResult Prepare(
            SaveStore store,
            string sessionNonce,
            Func<string> newVaccineId,
            bool mayStartNextVaccine)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(sessionNonce))
                throw new ArgumentException("A session needs a nonce to retry under.", nameof(sessionNonce));
            if (newVaccineId == null)
                throw new ArgumentNullException(nameof(newVaccineId));

            if (store.State == SaveStoreState.Unopened)
                store.Open();

            if (store.State == SaveStoreState.Halted)
                return Fail(SlotBootstrapVerdict.Halted, store.HaltReason);

            SlotBootstrapVerdict verdict = SlotBootstrapVerdict.Ready;

            if (store.State == SaveStoreState.Empty)
            {
                string id = newVaccineId();
                SaveCommitResult created = store.CreateSlot(id, $"create:{id}");
                if (!created.Acknowledged)
                    return Fail(VerdictFor(store, created), created.Detail);

                verdict = SlotBootstrapVerdict.CreatedSlot;
            }
            else if (!store.Snapshot.vaccine.alive)
            {
                if (!mayStartNextVaccine)
                {
                    return new SlotBootstrapResult(
                        SlotBootstrapVerdict.AwaitingNextVaccine, 0L, store.Snapshot.vaccine.id,
                        store.Snapshot.vaccine.generation, 0,
                        "the vaccine was destroyed; docs/04 ends 최종 파기 at a selection screen");
                }

                // Keyed on the generation that ended, so a retry cannot create two successors.
                int destroyed = store.Snapshot.vaccine.generation;
                SaveCommitResult started = store.Commit(
                    new StartNextVaccineCommand(newVaccineId(), $"nextvaccine:after-g{destroyed}"));
                if (!started.Acknowledged)
                    return Fail(VerdictFor(store, started), started.Detail);

                verdict = SlotBootstrapVerdict.StartedNextVaccine;
            }

            SaveCommitResult allocated = store.Commit(new AllocateRunIdCommand(sessionNonce));
            if (!allocated.Acknowledged)
                return Fail(VerdictFor(store, allocated), allocated.Detail);

            long runId = AllocateRunIdCommand.RunIdIn(allocated.Result);
            if (runId <= 0L)
            {
                // The receipt is the authority and it did not contain an id, so there is no id
                // to play under. Inventing one here would recreate the collision this whole
                // allocation exists to prevent.
                return Fail(
                    SlotBootstrapVerdict.WriteFailed,
                    $"the run id receipt did not carry an id ('{allocated.Result}')");
            }

            VaccineSaveState vaccine = store.Snapshot.vaccine;
            return new SlotBootstrapResult(
                verdict, runId, vaccine.id, vaccine.generation, vaccine.copiesRemaining,
                $"revision {store.Snapshot.snapshotRevision}");
        }

        private static SlotBootstrapVerdict VerdictFor(SaveStore store, in SaveCommitResult result) =>
            store.State == SaveStoreState.Halted
                ? SlotBootstrapVerdict.Halted
                : SlotBootstrapVerdict.WriteFailed;

        private static SlotBootstrapResult Fail(SlotBootstrapVerdict verdict, string detail) =>
            new SlotBootstrapResult(verdict, 0L, string.Empty, 0, 0, detail);

        /// <summary>The result for a session with no store, so nothing reports a false Ready.</summary>
        public static SlotBootstrapResult NotConnected(string detail) =>
            new SlotBootstrapResult(SlotBootstrapVerdict.NotConnected, 0L, string.Empty, 0, 0, detail);
    }
}
