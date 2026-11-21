using System;

namespace NeonVoid.Progression
{
    /// <summary>How a commit attempt ended.</summary>
    public readonly struct DeathCommitAck
    {
        /// <summary>The death this answer is about. An answer for another death is not an answer.</summary>
        public readonly DeathId Id;

        /// <summary>True only when permanent state was written and verified.</summary>
        public readonly bool Succeeded;

        /// <summary>Why it failed, for the recovery screen. Empty on success.</summary>
        public readonly string FailureReason;

        /// <summary>
        /// Whether permanent state says the vaccine is still alive after this death. False on a
        /// failure, because nothing is known then.
        /// <para>
        /// It is on the answer rather than inferred by whoever reads it, because the only place
        /// that knows is the write itself: [확정] docs/04 section 2 makes the fourth death
        /// 영구 파기, and the third and the fourth are told apart by a counter in the file.
        /// A revive decided on anything else would put a destroyed vaccine back on its feet.
        /// </para>
        /// </summary>
        public readonly bool VaccineSurvived;

        private DeathCommitAck(DeathId id, bool succeeded, string failureReason, bool vaccineSurvived)
        {
            Id = id;
            Succeeded = succeeded;
            FailureReason = failureReason ?? string.Empty;
            VaccineSurvived = vaccineSurvived;
        }

        /// <summary>
        /// A committed death. <paramref name="vaccineSurvived"/> has no default on purpose: a
        /// caller that forgot it would be voting to revive, and the one death it matters for is
        /// the one that destroys the vaccine.
        /// </summary>
        public static DeathCommitAck Success(DeathId id, bool vaccineSurvived) =>
            new DeathCommitAck(id, true, string.Empty, vaccineSurvived);

        public static DeathCommitAck Failure(DeathId id, string reason) =>
            new DeathCommitAck(
                id, false, string.IsNullOrEmpty(reason) ? "unspecified failure" : reason, false);
    }

    /// <summary>
    /// The seam between combat and permanent state. **Nothing implements this yet, on purpose.**
    /// <para>
    /// The consequences of a death - clone decrement, the 20~70% XP settlement (D-012), losing
    /// the plugins carried into the mission, final destruction on the fourth death - are
    /// permanent writes with receipts, backups and checksums behind them, and CLAUDE.md forbids
    /// standing one up as a temporary mock: a mock that charges a clone has already changed the
    /// player's save, and a mock that silently succeeds teaches the UI to show results that
    /// were never written.
    /// </para>
    /// <para>
    /// So combat commands, waits, and does nothing until it is answered. With no service
    /// registered the run sits in <see cref="RunDeathPhase.AwaitingCommit"/> and says so, which
    /// is an honest unfinished state rather than a wrong finished one.
    /// </para>
    /// <para>
    /// An implementation must be idempotent per <see cref="DeathId"/>: a retry after a lost ack
    /// is the same death, not a second one.
    /// </para>
    /// </summary>
    public interface IDeathCommitService
    {
        /// <summary>
        /// Asks for this death to be written to permanent state. May answer later; the ledger
        /// stays in AwaitingCommit until it does.
        /// </summary>
        void RequestDeathCommit(in DeathReport report);

        /// <summary>Raised once per finished attempt, successful or not.</summary>
        event Action<DeathCommitAck> CommitCompleted;
    }
}
