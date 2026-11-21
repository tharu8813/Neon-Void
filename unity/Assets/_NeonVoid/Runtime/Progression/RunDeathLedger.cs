namespace NeonVoid.Progression
{
    /// <summary>Where a run stands with respect to death.</summary>
    public enum RunDeathPhase
    {
        /// <summary>No death has been registered. The run is live.</summary>
        Alive = 0,

        /// <summary>A death is recorded and the save service has not answered yet.</summary>
        AwaitingCommit = 1,

        /// <summary>The save service confirmed the write. Only now may results be shown.</summary>
        Committed = 2,

        /// <summary>The save service refused or failed. The same death may be retried.</summary>
        CommitFailed = 3,
    }

    /// <summary>Answer to a registration attempt.</summary>
    public enum DeathRegistrationVerdict
    {
        /// <summary>Recorded. Exactly one of these ever happens per death.</summary>
        Accepted = 0,

        /// <summary>Already recorded under the same id. Nothing changed, nothing charged twice.</summary>
        DuplicateIgnored = 1,

        /// <summary>A different death arrived while one was already recorded. Refused.</summary>
        RejectedRunAlreadyEnded = 2,

        /// <summary>The id was not usable as a dedupe key.</summary>
        RejectedInvalidId = 3,
    }

    /// <summary>Answer to a request to open the ledger for a new run.</summary>
    public enum RunResetVerdict
    {
        /// <summary>The previous run's death was settled and cleared. The new run may begin.</summary>
        Reset = 0,

        /// <summary>Nothing had happened. The new run may begin.</summary>
        AlreadyClean = 1,

        /// <summary>
        /// A death is still waiting for the save service, or its write failed. Nothing is
        /// cleared and the new run must not begin.
        /// </summary>
        RefusedUnsettled = 2,
    }

    /// <summary>Answer to an acknowledgement from the save service.</summary>
    public enum DeathAckVerdict
    {
        /// <summary>Applied: the phase moved to Committed or CommitFailed.</summary>
        Applied = 0,

        /// <summary>No death is waiting for an answer.</summary>
        RejectedNothingPending = 1,

        /// <summary>The answer is about a different death than the one waiting.</summary>
        RejectedWrongDeath = 2,

        /// <summary>This death was already committed. A second success is not a second payout.</summary>
        DuplicateIgnored = 3,
    }

    /// <summary>
    /// Tracks the one death that ends a run, and refuses to let it count twice.
    /// <para>
    /// [확정] CLAUDE.md: <i>보상/사망/철수/DB 이동은 저장 커밋 승인 후 UI 및 게임 상태 확정.
    /// 실패·ACK 유실·중복 시 재지급 금지.</i> That is what this type is: a gate that stays shut
    /// until an acknowledgement arrives for the exact death that is waiting.
    /// </para>
    /// <para>
    /// <b>Why a second, different death is refused rather than queued.</b> A death ends the
    /// mission. Anything that produces a second death afterwards - a damage-over-time tick that
    /// resolves in the same frame, a retry path that re-runs the handler, a second actor
    /// reported by mistake - is a bug, and queueing it would turn that bug into a second clone
    /// charged for one death. Refusing is recoverable; charging is not.
    /// </para>
    /// <para>
    /// <b>Why the same death is accepted silently.</b> After the app dies before an ack, the
    /// next launch re-reports the same death. That is not a bug and must not warn, charge, or
    /// reject - it must be recognised. Hence DuplicateIgnored rather than a rejection.
    /// </para>
    /// <para>
    /// No Unity types, no clock, no storage: the whole state machine is testable.
    /// </para>
    /// </summary>
    public sealed class RunDeathLedger
    {
        private DeathReport _pending;
        private bool _hasPending;

        /// <summary>Where the run stands.</summary>
        public RunDeathPhase Phase { get; private set; } = RunDeathPhase.Alive;

        /// <summary>True once a death has been recorded, whatever happened to its commit.</summary>
        public bool HasRegisteredDeath => _hasPending;

        /// <summary>The recorded death, or <see cref="DeathId.None"/> while the run is live.</summary>
        public DeathId RegisteredDeathId => _hasPending ? _pending.Id : DeathId.None;

        /// <summary>The recorded death report. Meaningless unless <see cref="HasRegisteredDeath"/>.</summary>
        public DeathReport RegisteredDeath => _pending;

        /// <summary>Why the last commit failed, or empty.</summary>
        public string LastFailureReason { get; private set; } = string.Empty;

        /// <summary>
        /// Whether the death screen may show losses and the game state may move on.
        /// [확정] False in every phase but Committed: before the write is acknowledged there is
        /// no truthful number to show.
        /// </summary>
        public bool MayReleaseResults => Phase == RunDeathPhase.Committed;

        /// <summary>Records a death, or explains why it was not recorded.</summary>
        public DeathRegistrationVerdict TryRegisterDeath(in DeathReport report)
        {
            if (!report.Id.IsValid)
                return DeathRegistrationVerdict.RejectedInvalidId;

            if (_hasPending)
                return _pending.Id == report.Id
                    ? DeathRegistrationVerdict.DuplicateIgnored
                    : DeathRegistrationVerdict.RejectedRunAlreadyEnded;

            _pending = report;
            _hasPending = true;
            Phase = RunDeathPhase.AwaitingCommit;
            LastFailureReason = string.Empty;
            return DeathRegistrationVerdict.Accepted;
        }

        /// <summary>
        /// Applies an answer from the save service. An answer naming another death is refused
        /// outright: accepting it would release results for a write that never happened.
        /// </summary>
        public DeathAckVerdict Acknowledge(in DeathCommitAck ack)
        {
            if (!_hasPending)
                return DeathAckVerdict.RejectedNothingPending;

            if (ack.Id != _pending.Id)
                return DeathAckVerdict.RejectedWrongDeath;

            if (Phase == RunDeathPhase.Committed)
                return DeathAckVerdict.DuplicateIgnored;

            if (ack.Succeeded)
            {
                Phase = RunDeathPhase.Committed;
                LastFailureReason = string.Empty;
            }
            else
            {
                Phase = RunDeathPhase.CommitFailed;
                LastFailureReason = ack.FailureReason;
            }

            return DeathAckVerdict.Applied;
        }

        /// <summary>
        /// Opens the ledger for a new run, and says whether the new run may begin at all.
        /// <para>
        /// <b>Why this exists.</b> A ledger holds the one death that ends a run, and before the
        /// 디펜더 허브 a session <i>was</i> one run - so nothing ever had to clear it. Now a
        /// session is a series of missions, and a ledger that still held the first mission's
        /// death would answer the second mission's death with
        /// <see cref="DeathRegistrationVerdict.RejectedRunAlreadyEnded"/>: no clone charged, no
        /// death recorded, a vaccine that is immortal from the second mission on. That is the
        /// exact mirror of the double charge this type was written to prevent, and worse,
        /// because a charge that never happens leaves nothing behind to notice.
        /// </para>
        /// <para>
        /// <b>An unsettled death refuses.</b> [확정] CLAUDE.md: 보상/사망/철수/DB 이동은 저장
        /// 커밋 승인 후 UI 및 게임 상태 확정. A death that is waiting for the disk, or whose write
        /// failed, has not been paid for - and starting the next mission on top of it would
        /// confirm new game state over an unconfirmed one, and lose the death if the player
        /// never comes back to it. The caller retries the commit instead.
        /// </para>
        /// </summary>
        public RunResetVerdict TryStartNewRun()
        {
            if (!_hasPending)
                return RunResetVerdict.AlreadyClean;

            if (Phase != RunDeathPhase.Committed)
                return RunResetVerdict.RefusedUnsettled;

            _pending = default;
            _hasPending = false;
            Phase = RunDeathPhase.Alive;
            LastFailureReason = string.Empty;
            return RunResetVerdict.Reset;
        }

        /// <summary>
        /// Puts a failed commit back in the waiting state so it can be sent again.
        /// <b>The death keeps its id</b>: a retry that minted a new one would reach the save
        /// service as a second death and charge a second clone.
        /// </summary>
        public bool TryBeginRetry(out DeathReport report)
        {
            report = _pending;
            if (Phase != RunDeathPhase.CommitFailed)
                return false;

            Phase = RunDeathPhase.AwaitingCommit;
            return true;
        }
    }
}
