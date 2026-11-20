using System;

namespace NeonVoid.Progression.Save
{
    /// <summary>What the store can do right now.</summary>
    public enum SaveStoreState
    {
        /// <summary><see cref="SaveStore.Open"/> has not been called.</summary>
        Unopened = 0,

        /// <summary>No snapshot exists. Only slot creation is accepted.</summary>
        Empty = 1,

        /// <summary>A verified snapshot is loaded. Commands may run.</summary>
        Ready = 2,

        /// <summary>
        /// Something on disk could not be trusted and could not be recovered. [확정] docs/04
        /// section 5: 데이터 불일치 시 사용자 데이터를 덮어쓰지 않고 읽기 전용 복구/오류 상태로
        /// 정지. Every command is refused from here, and nothing is written or deleted.
        /// </summary>
        Halted = 3,
    }

    /// <summary>How opening the store ended.</summary>
    public enum SaveOpenVerdict
    {
        /// <summary>A snapshot was read and verified.</summary>
        Loaded = 0,

        /// <summary>Nothing is there yet. Not an error; a new player.</summary>
        NoSave = 1,

        /// <summary>
        /// The snapshot failed verification and the backup took its place. The bad file is kept
        /// under a quarantine name.
        /// </summary>
        RecoveredFromBackup = 2,

        /// <summary>Refused. <see cref="SaveStore.HaltReason"/> says why.</summary>
        Halted = 3,
    }

    public readonly struct SaveOpenResult
    {
        public readonly SaveOpenVerdict Verdict;
        public readonly string Detail;

        public SaveOpenResult(SaveOpenVerdict verdict, string detail)
        {
            Verdict = verdict;
            Detail = detail ?? string.Empty;
        }

        public bool Usable => Verdict == SaveOpenVerdict.Loaded
            || Verdict == SaveOpenVerdict.NoSave
            || Verdict == SaveOpenVerdict.RecoveredFromBackup;
    }

    /// <summary>How a commit ended.</summary>
    public enum SaveCommitVerdict
    {
        /// <summary>Written, replaced and verified by re-reading. The only state change.</summary>
        Committed = 0,

        /// <summary>
        /// This receipt id was already committed. The stored result is returned unchanged;
        /// nothing is applied a second time.
        /// </summary>
        AlreadyApplied = 1,

        /// <summary>The command refused its own preconditions. Nothing was written.</summary>
        Rejected = 2,

        /// <summary>
        /// Storage failed. The committed state is unchanged and has been verified to be
        /// unchanged - which is what lets the caller retry with the same receipt id.
        /// </summary>
        WriteFailed = 3,

        /// <summary>The store is halted or not open.</summary>
        Refused = 4,
    }

    public readonly struct SaveCommitResult
    {
        public readonly SaveCommitVerdict Verdict;
        public readonly string ReceiptId;

        /// <summary>Revision the command's result belongs to; 0 when nothing was applied.</summary>
        public readonly int Revision;

        /// <summary>The command's result, from this commit or from the stored receipt.</summary>
        public readonly string Result;

        public readonly string Detail;

        internal SaveCommitResult(
            SaveCommitVerdict verdict, string receiptId, int revision, string result, string detail)
        {
            Verdict = verdict;
            ReceiptId = receiptId ?? string.Empty;
            Revision = revision;
            Result = result ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        /// <summary>
        /// True when permanent state holds this command's result - whether this call wrote it or
        /// an earlier one did. <b>This is the only condition under which docs/04 section 5 step
        /// 7 permits the UI, combat or growth services to show the outcome.</b>
        /// </summary>
        public bool Acknowledged =>
            Verdict == SaveCommitVerdict.Committed || Verdict == SaveCommitVerdict.AlreadyApplied;

        public override string ToString() =>
            $"{Verdict} {ReceiptId} rev{Revision} {(Detail.Length > 0 ? Detail : Result)}";
    }

    /// <summary>
    /// The save transaction from docs/04 section 5, as the single place permanent state changes.
    /// <para>
    /// The seven steps the document lists are the seven things this class does, in that order:
    /// validate the command and the receipt log, compute an immutable next state, serialize the
    /// snapshot and the receipt together, write a temporary file and verify its checksum,
    /// back up the previous snapshot and replace it atomically, <b>re-read the result and verify
    /// it again</b>, and only then return an acknowledgement. Nothing here publishes an event or
    /// touches a UI: the caller gets a result and decides what to show, which is how step 7
    /// stays enforceable.
    /// </para>
    /// <para>
    /// <b>The two failures that matter are different.</b> A command refused by its own rules
    /// (<see cref="SaveCommitVerdict.Rejected"/>) is a game answer. Storage failing
    /// (<see cref="SaveCommitVerdict.WriteFailed"/>) is not an answer at all, and the caller
    /// must be able to retry it with the same receipt id and get the same outcome. Collapsing
    /// the two into one "failed" would make a retry after a write failure indistinguishable
    /// from a retry of something the rules had already declined.
    /// </para>
    /// <para>Pure C# apart from the JSON writer; no scene, no MonoBehaviour, no clock.</para>
    /// </summary>
    public sealed class SaveStore
    {
        private readonly ISaveFileSystem _files;
        private readonly SaveSlotLayout _layout;

        public SaveStore(ISaveFileSystem files, SaveSlotLayout layout)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public SaveStoreState State { get; private set; } = SaveStoreState.Unopened;

        /// <summary>Why the store halted, for the recovery screen. Empty when it has not.</summary>
        public string HaltReason { get; private set; } = string.Empty;

        /// <summary>
        /// The committed snapshot, or null. Handed out as the live object rather than a copy so
        /// readers see what was actually verified; commands never receive it, they receive a
        /// clone.
        /// </summary>
        public SaveDocument Snapshot { get; private set; }

        public SaveSlotLayout Layout => _layout;

        /// <summary>Snapshots moved aside as unreadable. Non-zero means something went wrong once.</summary>
        public int QuarantinedSnapshots { get; private set; }

        /// <summary>
        /// Reads and verifies the slot, recovering from the backup if the snapshot is bad.
        /// <para>
        /// A newer schema or a foreign product halts immediately and does <b>not</b> fall back
        /// to the backup. Falling back there would quietly replace a save written by a newer
        /// build with this build's older copy of it - a silent rollback of the player's
        /// progress, which is the one thing docs/04 section 7 says a future schema must never
        /// trigger.
        /// </para>
        /// </summary>
        public SaveOpenResult Open()
        {
            try
            {
                _files.EnsureDirectory(_layout.Directory);
                DiscardAbandonedTemp();

                if (!_files.Exists(_layout.SnapshotPath))
                    return OpenFromBackupOrEmpty();

                SaveParseResult snapshot = Read(_layout.SnapshotPath);
                if (snapshot.Ok)
                {
                    Snapshot = snapshot.Document;
                    State = SaveStoreState.Ready;
                    return new SaveOpenResult(SaveOpenVerdict.Loaded, $"revision {Snapshot.snapshotRevision}");
                }

                // A schema mismatch or a foreign file is a safe error, not damage, so the
                // backup is not consulted. The backup belongs to this same build: falling back
                // to it would quietly replace a newer build's save with an older copy of it,
                // and for an older schema it would just produce the same refusal one revision
                // down. docs/04 section 7 allows only 안전 오류 here, never 자동 변환·삭제.
                if (snapshot.Verdict == SaveParseVerdict.FutureSchema
                    || snapshot.Verdict == SaveParseVerdict.OlderSchema
                    || snapshot.Verdict == SaveParseVerdict.ForeignProduct)
                {
                    return Halt($"{snapshot.Verdict}: {snapshot.Detail}");
                }

                return RecoverFromBackup(snapshot);
            }
            catch (SaveIoException e)
            {
                return Halt($"storage is unreadable: {e.Message}");
            }
        }

        /// <summary>
        /// Creates the slot's first snapshot. Accepted only when nothing is there: an existing
        /// save is never initialised over, whatever a caller asks for.
        /// </summary>
        public SaveCommitResult CreateSlot(string vaccineId, string receiptId)
        {
            if (State == SaveStoreState.Halted || State == SaveStoreState.Unopened)
                return Refused(receiptId);

            if (State == SaveStoreState.Ready)
            {
                return new SaveCommitResult(
                    SaveCommitVerdict.Rejected, receiptId, 0, string.Empty,
                    "a save already exists in this slot; creating one over it would destroy it");
            }

            var command = new CreateSlotCommand(vaccineId, receiptId);
            var fresh = new SaveDocument { saveSlotId = _layout.SlotId, snapshotRevision = 0 };
            return Run(command, fresh);
        }

        /// <summary>
        /// Runs one command as a transaction against the committed snapshot.
        /// </summary>
        public SaveCommitResult Commit(ISaveCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (string.IsNullOrWhiteSpace(command.ReceiptId))
                throw new ArgumentException($"{command.Name} has no receipt id to be deduped by.", nameof(command));

            if (State != SaveStoreState.Ready)
                return Refused(command.ReceiptId);

            // Step 1, the half that matters most: a command that has already been committed is
            // answered from the receipt log and never runs again. This is checked before the
            // command is given any state at all, so an Apply with a side effect still could not
            // happen twice.
            SaveReceiptRecord existing = Snapshot.FindReceipt(command.ReceiptId);
            if (existing != null)
            {
                return new SaveCommitResult(
                    SaveCommitVerdict.AlreadyApplied, existing.receiptId, existing.revisionApplied,
                    existing.result, $"already committed by {existing.command}");
            }

            return Run(command, Snapshot.Clone());
        }

        private SaveCommitResult Run(ISaveCommand command, SaveDocument next)
        {
            // Step 2: the command sees only the copy.
            SaveCommandOutcome outcome;
            try
            {
                outcome = command.Apply(next);
            }
            catch (Exception e)
            {
                // A throwing command is a bug, not a rule - but the distinction that matters
                // here is that nothing has been written, so it is reported as a refusal of the
                // command rather than as a storage failure the caller should retry forever.
                return new SaveCommitResult(
                    SaveCommitVerdict.Rejected, command.ReceiptId, 0, string.Empty,
                    $"{command.Name} threw: {e.Message}");
            }

            if (!outcome.Accepted)
            {
                return new SaveCommitResult(
                    SaveCommitVerdict.Rejected, command.ReceiptId, 0, string.Empty, outcome.RejectionReason);
            }

            next.schemaVersion = SaveDocument.CurrentSchemaVersion;
            next.product = SaveDocument.ProductTag;
            next.saveSlotId = _layout.SlotId;
            next.snapshotRevision += 1;
            next.receipts.Add(new SaveReceiptRecord
            {
                receiptId = command.ReceiptId,
                command = command.Name,
                revisionApplied = next.snapshotRevision,
                result = outcome.Result,
            });

            try
            {
                next.Validate();
            }
            catch (Exception e)
            {
                // The command accepted but produced a state the schema forbids. Refusing is the
                // only safe answer: writing it would put a document on disk that this same code
                // will refuse to load next launch, turning a bug into a halted save.
                return new SaveCommitResult(
                    SaveCommitVerdict.Rejected, command.ReceiptId, 0, string.Empty,
                    $"{command.Name} produced an invalid state: {e.Message}");
            }

            return Write(command, next, outcome);
        }

        private SaveCommitResult Write(ISaveCommand command, SaveDocument next, in SaveCommandOutcome outcome)
        {
            // Step 3 and 4: one file holding the snapshot and the receipt, written to a
            // temporary name and read back before it is allowed anywhere near the real one.
            string text = SaveSerialization.Stamp(next);
            try
            {
                _files.WriteAllText(_layout.TempPath, text);

                SaveParseResult written = Read(_layout.TempPath);
                if (!written.Ok)
                {
                    TryDelete(_layout.TempPath);
                    return WriteFailed(command, $"the temporary file did not verify ({written.Verdict}: {written.Detail})");
                }

                // Step 5: back up the previous snapshot and replace atomically.
                _files.Replace(_layout.TempPath, _layout.SnapshotPath, _layout.BackupPath);
            }
            catch (SaveIoException e)
            {
                TryDelete(_layout.TempPath);
                return WriteFailed(command, e.Message);
            }

            // Step 6: re-read what is now on disk. The acknowledgement is this read, not the
            // write - a write that returned successfully and left something else behind is
            // exactly the case the checksum exists for.
            SaveParseResult verified;
            try
            {
                verified = Read(_layout.SnapshotPath);
            }
            catch (SaveIoException e)
            {
                return Halted(command, $"the snapshot could not be re-read after replacing it: {e.Message}");
            }

            if (!verified.Ok)
                return RollBack(command, verified);

            Snapshot = verified.Document;
            State = SaveStoreState.Ready;

            // Step 7 is the caller's: this returns an acknowledgement and publishes nothing.
            return new SaveCommitResult(
                SaveCommitVerdict.Committed, command.ReceiptId, Snapshot.snapshotRevision,
                outcome.Result, string.Empty);
        }

        /// <summary>
        /// The replaced snapshot did not verify. The backup is the last snapshot that did, so
        /// put it back and report a failed write - the caller may retry with the same receipt
        /// id, and because the retry sees the restored state it computes the same next state.
        /// </summary>
        private SaveCommitResult RollBack(ISaveCommand command, in SaveParseResult failure)
        {
            if (!_files.Exists(_layout.BackupPath))
            {
                return Halted(
                    command,
                    $"the new snapshot did not verify ({failure.Detail}) and there is no backup to restore");
            }

            SaveParseResult backup;
            try
            {
                backup = Read(_layout.BackupPath);
            }
            catch (SaveIoException e)
            {
                return Halted(command, $"the new snapshot did not verify and the backup is unreadable: {e.Message}");
            }

            if (!backup.Ok)
            {
                return Halted(
                    command,
                    $"the new snapshot did not verify ({failure.Detail}) and neither does the backup ({backup.Detail})");
            }

            try
            {
                Quarantine(_layout.SnapshotPath);
                _files.WriteAllText(_layout.SnapshotPath, SaveSerialization.Stamp(backup.Document));
            }
            catch (SaveIoException e)
            {
                return Halted(command, $"the backup could not be restored: {e.Message}");
            }

            Snapshot = backup.Document;
            State = SaveStoreState.Ready;
            return WriteFailed(command, $"the write did not verify and revision {backup.Document.snapshotRevision} was restored");
        }

        private SaveOpenResult OpenFromBackupOrEmpty()
        {
            if (!_files.Exists(_layout.BackupPath))
            {
                State = SaveStoreState.Empty;
                Snapshot = null;
                return new SaveOpenResult(SaveOpenVerdict.NoSave, "no snapshot in this slot");
            }

            SaveParseResult backup = Read(_layout.BackupPath);
            if (!backup.Ok)
            {
                // A backup without a snapshot that also fails to verify is not an empty slot.
                // Treating it as one would let the next CreateSlot write over a save that
                // might still be recoverable by hand.
                return Halt($"the snapshot is missing and the backup does not verify ({backup.Detail})");
            }

            _files.WriteAllText(_layout.SnapshotPath, SaveSerialization.Stamp(backup.Document));
            Snapshot = backup.Document;
            State = SaveStoreState.Ready;
            return new SaveOpenResult(
                SaveOpenVerdict.RecoveredFromBackup,
                $"the snapshot was missing; revision {Snapshot.snapshotRevision} was restored from the backup");
        }

        private SaveOpenResult RecoverFromBackup(in SaveParseResult snapshotFailure)
        {
            if (!_files.Exists(_layout.BackupPath))
                return Halt($"the snapshot does not verify ({snapshotFailure.Detail}) and there is no backup");

            SaveParseResult backup = Read(_layout.BackupPath);
            if (!backup.Ok)
            {
                return Halt(
                    $"the snapshot does not verify ({snapshotFailure.Detail}) and neither does the backup " +
                    $"({backup.Detail})");
            }

            Quarantine(_layout.SnapshotPath);
            _files.WriteAllText(_layout.SnapshotPath, SaveSerialization.Stamp(backup.Document));
            Snapshot = backup.Document;
            State = SaveStoreState.Ready;
            return new SaveOpenResult(
                SaveOpenVerdict.RecoveredFromBackup,
                $"revision {Snapshot.snapshotRevision} was restored after {snapshotFailure.Verdict}");
        }

        /// <summary>
        /// Moves an unusable snapshot aside under a numbered name. It is never deleted: docs/04
        /// forbids destroying user data on a mismatch, and a file a person might rescue by hand
        /// is not the save system's to throw away.
        /// </summary>
        private void Quarantine(string path)
        {
            for (int index = 1; index < 1000; ++index)
            {
                string candidate = _layout.QuarantinePath(index);
                if (_files.Exists(candidate))
                    continue;

                _files.Move(path, candidate);
                QuarantinedSnapshots++;
                return;
            }

            throw new SaveIoException($"a thousand quarantined snapshots already exist beside '{path}'");
        }

        /// <summary>
        /// A temporary file left behind by a process that died mid-transaction. It is discarded
        /// rather than adopted: the replace never happened, so no acknowledgement was ever
        /// given for it, and the caller's retry will produce it again from the same receipt id.
        /// Adopting it would commit something the caller still believes failed.
        /// </summary>
        private void DiscardAbandonedTemp()
        {
            if (_files.Exists(_layout.TempPath))
                TryDelete(_layout.TempPath);
        }

        private SaveParseResult Read(string path) => SaveSerialization.Verify(_files.ReadAllText(path));

        private void TryDelete(string path)
        {
            try
            {
                _files.Delete(path);
            }
            catch (SaveIoException)
            {
                // Leaving a stale temporary file behind is untidy and harmless: it is never
                // read as state, and the next write overwrites it. Failing the commit over it
                // would turn a tidy-up problem into a lost save.
            }
        }

        private SaveOpenResult Halt(string reason)
        {
            State = SaveStoreState.Halted;
            HaltReason = reason;
            Snapshot = null;
            return new SaveOpenResult(SaveOpenVerdict.Halted, reason);
        }

        private SaveCommitResult Halted(ISaveCommand command, string reason)
        {
            State = SaveStoreState.Halted;
            HaltReason = reason;
            Snapshot = null;
            return new SaveCommitResult(SaveCommitVerdict.Refused, command.ReceiptId, 0, string.Empty, reason);
        }

        private SaveCommitResult WriteFailed(ISaveCommand command, string detail) =>
            new SaveCommitResult(SaveCommitVerdict.WriteFailed, command.ReceiptId, 0, string.Empty, detail);

        private SaveCommitResult Refused(string receiptId) => new SaveCommitResult(
            SaveCommitVerdict.Refused, receiptId, 0, string.Empty,
            State == SaveStoreState.Halted ? HaltReason : "the store is not open");
    }
}
