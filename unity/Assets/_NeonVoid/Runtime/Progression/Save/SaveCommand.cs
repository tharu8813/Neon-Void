using System;

namespace NeonVoid.Progression.Save
{
    /// <summary>What a command decided, before anything has been written.</summary>
    public readonly struct SaveCommandOutcome
    {
        /// <summary>True when the command's preconditions held and it computed a next state.</summary>
        public readonly bool Accepted;

        /// <summary>
        /// The command's own result, serialized by the command. Stored in the receipt and
        /// returned verbatim to a later retry, so the second ask cannot be answered differently
        /// from the first.
        /// </summary>
        public readonly string Result;

        /// <summary>Why it was refused. Empty when accepted.</summary>
        public readonly string RejectionReason;

        private SaveCommandOutcome(bool accepted, string result, string reason)
        {
            Accepted = accepted;
            Result = result ?? string.Empty;
            RejectionReason = reason ?? string.Empty;
        }

        public static SaveCommandOutcome Accept(string result) =>
            new SaveCommandOutcome(true, result, string.Empty);

        public static SaveCommandOutcome Reject(string reason) =>
            new SaveCommandOutcome(false, string.Empty, string.IsNullOrEmpty(reason) ? "refused" : reason);
    }

    /// <summary>
    /// One domain command from docs/04 section 5's table, as something the store can run inside
    /// a transaction.
    /// <para>
    /// A command checks its own preconditions and computes the next state; it never writes, and
    /// it is never told whether the write succeeded. That split is the point of the table: the
    /// document's rule is that a result reaches the UI, combat or growth services only after a
    /// verified commit (step 7), and a command that could write would be able to break that on
    /// its own.
    /// </para>
    /// <para>
    /// <b>The receipt id is the command's identity, not a fresh number.</b> It must be derivable
    /// from the thing that happened - a <see cref="DeathId"/>, a reward id, a reroll index - so
    /// that a retry after a lost acknowledgement carries the same id as the attempt that may
    /// already have been committed. An id minted per attempt would make every retry a new
    /// command and charge the player twice, which is the exact failure docs/04 section 7 lists
    /// under 저장 성공 ACK 유실.
    /// </para>
    /// </summary>
    public interface ISaveCommand
    {
        /// <summary>Stable idempotency key for the event this command is about.</summary>
        string ReceiptId { get; }

        /// <summary>Short name recorded in the receipt, e.g. <c>RegisterDeath</c>.</summary>
        string Name { get; }

        /// <summary>
        /// Checks preconditions against <paramref name="next"/> and edits it in place.
        /// <paramref name="next"/> is already a deep copy of the committed snapshot, so edits
        /// made before a later check fails are discarded with it and cannot leak into the
        /// committed state.
        /// </summary>
        SaveCommandOutcome Apply(SaveDocument next);
    }

    /// <summary>
    /// Creates a slot's first snapshot. Separate from the others because it is the one command
    /// with no prior state to validate against.
    /// </summary>
    public sealed class CreateSlotCommand : ISaveCommand
    {
        private readonly string _vaccineId;

        public CreateSlotCommand(string vaccineId, string receiptId)
        {
            if (string.IsNullOrWhiteSpace(vaccineId))
                throw new ArgumentException("A new vaccine needs an id.", nameof(vaccineId));
            if (string.IsNullOrWhiteSpace(receiptId))
                throw new ArgumentException("A command needs a receipt id.", nameof(receiptId));

            _vaccineId = vaccineId;
            ReceiptId = receiptId;
        }

        public string ReceiptId { get; }

        public string Name => "CreateSlot";

        public SaveCommandOutcome Apply(SaveDocument next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            next.vaccine = VaccineSaveState.NewVaccine(_vaccineId);
            return SaveCommandOutcome.Accept(
                $"vaccineId={_vaccineId};copiesRemaining={next.vaccine.copiesRemaining}");
        }
    }
}
