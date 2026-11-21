using System;
using System.Collections.Generic;
using System.Reflection;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The save transaction from docs/04 section 5, and the boundary cases section 7 lists as
    /// the ones that must be tested rather than reasoned about.
    /// <para>
    /// Everything here runs against <see cref="FakeSaveFileSystem"/>, in memory, so the
    /// player's save directory is not merely avoided - it is unreachable. docs/04 section 4
    /// requires exactly that of the test save service.
    /// </para>
    /// </summary>
    public sealed class SaveStoreTests
    {
        private const string Root = "/tmp/neonvoid-tests";

        private FakeSaveFileSystem _files;
        private SaveSlotLayout _layout;
        private SaveStore _store;

        /// <summary>A command with no rules of its own, so the store is what is under test.</summary>
        private sealed class AddXp : ISaveCommand
        {
            private readonly long _amount;

            public AddXp(string receiptId, long amount)
            {
                ReceiptId = receiptId;
                _amount = amount;
            }

            public string ReceiptId { get; }

            public string Name => "AddXp";

            public SaveCommandOutcome Apply(SaveDocument next)
            {
                next.vaccine.permanentXp += _amount;
                return SaveCommandOutcome.Accept($"permanentXp={next.vaccine.permanentXp}");
            }
        }

        private sealed class AlwaysRefuses : ISaveCommand
        {
            public AlwaysRefuses(string receiptId) => ReceiptId = receiptId;

            public string ReceiptId { get; }

            public string Name => "AlwaysRefuses";

            public SaveCommandOutcome Apply(SaveDocument next)
            {
                next.vaccine.permanentXp += 9999L;   // edited, then refused: must not survive
                return SaveCommandOutcome.Reject("the rule says no");
            }
        }

        private sealed class Throws : ISaveCommand
        {
            public Throws(string receiptId) => ReceiptId = receiptId;

            public string ReceiptId { get; }

            public string Name => "Throws";

            public SaveCommandOutcome Apply(SaveDocument next) =>
                throw new InvalidOperationException("a bug in a command");
        }

        private sealed class ProducesInvalidState : ISaveCommand
        {
            public ProducesInvalidState(string receiptId) => ReceiptId = receiptId;

            public string ReceiptId { get; }

            public string Name => "ProducesInvalidState";

            public SaveCommandOutcome Apply(SaveDocument next)
            {
                next.vaccine.copiesRemaining = -1;   // the schema forbids it
                return SaveCommandOutcome.Accept("accepted something impossible");
            }
        }

        /// <summary>
        /// The schema field exactly as this build writes it. Taken from the constant rather
        /// than typed out: the two tests that rewrite it broke the moment the version was
        /// bumped, and a test that fails because the text it edits moved is noise, not a
        /// finding.
        /// </summary>
        private static string Written => "\"schemaVersion\":" + SaveDocument.CurrentSchemaVersion;

        [SetUp]
        public void SetUp()
        {
            _files = new FakeSaveFileSystem();
            _layout = SaveSlotLayout.Under(Root);
            _store = new SaveStore(_files, _layout);
        }

        private SaveStore Opened()
        {
            _store.Open();
            _store.CreateSlot("vaccine-1", "create:vaccine-1");
            return _store;
        }

        // --- the document ------------------------------------------------------------------

        [Test]
        public void TheSaveDocumentHoldsNoFloatingPointNumbers()
        {
            // The checksum is verified by re-serializing what was read back, so verification
            // depends on serialization being an exact round trip. A float whose text form
            // round-trips differently on another runtime would make a healthy save look corrupt
            // there and nowhere else - the worst kind of bug to receive a report of. Every
            // quantity in the save is a counting number, so this costs nothing to hold.
            var pending = new Queue<Type>();
            pending.Enqueue(typeof(SaveDocument));
            var seen = new HashSet<Type>();

            while (pending.Count > 0)
            {
                Type type = pending.Dequeue();
                if (!seen.Add(type))
                    continue;

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Type fieldType = field.FieldType;
                    if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                        fieldType = fieldType.GetGenericArguments()[0];

                    Assert.AreNotEqual(typeof(float), fieldType, $"{type.Name}.{field.Name} is a float.");
                    Assert.AreNotEqual(typeof(double), fieldType, $"{type.Name}.{field.Name} is a double.");
                    Assert.AreNotEqual(typeof(decimal), fieldType, $"{type.Name}.{field.Name} is a decimal.");

                    if (!fieldType.IsPrimitive && fieldType != typeof(string))
                        pending.Enqueue(fieldType);
                }
            }
        }

        [Test]
        public void StampingThenVerifyingIsAnExactRoundTrip()
        {
            SaveDocument document = SaveDocument.NewSlot("slot-1", "vaccine-1");
            document.vaccine.permanentXp = 123456789L;
            document.vaccine.archivePluginInstanceIds.Add("inst-7");
            document.receipts.Add(new SaveReceiptRecord
            {
                receiptId = "r1", command = "AddXp", revisionApplied = 1, result = "permanentXp=1",
            });

            string text = SaveSerialization.Stamp(document);
            SaveParseResult parsed = SaveSerialization.Verify(text);

            Assert.IsTrue(parsed.Ok, parsed.Detail);
            Assert.AreEqual(123456789L, parsed.Document.vaccine.permanentXp);
            Assert.AreEqual("inst-7", parsed.Document.vaccine.archivePluginInstanceIds[0]);
            Assert.AreEqual(text, SaveSerialization.Stamp(parsed.Document), "Serialization is not stable.");
        }

        [Test]
        public void AnInstanceInBothTheArchiveAndTheLoadoutIsRefused()
        {
            // [확정] docs/04 section 7: DB 출전 이동 → 금지 결과는 'DB+소지 중복 잔존'. A save
            // holding one instance twice has duplicated a plugin, and a duplicate is worse than
            // a loss because nothing later can tell which copy is real.
            SaveDocument document = SaveDocument.NewSlot("slot-1", "vaccine-1");
            document.vaccine.archivePluginInstanceIds.Add("inst-1");
            document.vaccine.carriedPluginInstanceIds.Add("inst-1");

            Assert.Throws<InvalidOperationException>(() => document.Validate());
        }

        [Test]
        public void MoreClonesThanTheVaccineStartedWithIsRefused()
        {
            // 복제 회복은 어떤 수단으로도 불가 (docs/04 section 2).
            SaveDocument document = SaveDocument.NewSlot("slot-1", "vaccine-1");
            document.vaccine.copiesRemaining = VaccineSaveState.DesignStartingCopies + 1;

            Assert.Throws<InvalidOperationException>(() => document.Validate());
        }

        // --- the legacy guard --------------------------------------------------------------

        [Test]
        public void EveryLegacyFileNameIsRefusedWhereverItAppears()
        {
            // CLAUDE.md and docs/04 section 4: Unity never reads, converts or overwrites these.
            foreach (string name in LegacyDataGuard.ProtectedFileNames)
            {
                Assert.Throws<InvalidOperationException>(
                    () => _files.WriteAllText($"/anywhere/{name}", "{}"), $"{name} was writable.");
                Assert.Throws<InvalidOperationException>(
                    () => _files.ReadAllText($"/anywhere/{name}"), $"{name} was readable.");
                Assert.Throws<InvalidOperationException>(
                    () => _files.Delete($"/anywhere/{name}"), $"{name} was deletable.");
            }
        }

        [Test]
        public void GodotLeftoversAreRefusedToo()
        {
            Assert.IsTrue(LegacyDataGuard.IsProtected("/x/main.tscn"));
            Assert.IsTrue(LegacyDataGuard.IsProtected("/x/player.gd"));
            Assert.IsFalse(LegacyDataGuard.IsProtected("/x/slot-1.snapshot.json"));
        }

        [Test]
        public void TheLayoutLivesUnderTheProductAndSchemaDirectories()
        {
            StringAssert.Contains(SaveSlotLayout.ProductDirectory, _layout.Directory);
            StringAssert.Contains(SaveSlotLayout.SchemaDirectory, _layout.Directory);
            StringAssert.StartsWith(Root, _layout.Directory);
        }

        [Test]
        public void ASlotIdThatCouldEscapeItsDirectoryIsRefused()
        {
            // A slot id becomes a file name. With enough '..' it becomes a path into the
            // legacy directory, which is the one place this save system must never write.
            Assert.Throws<ArgumentException>(() => SaveSlotLayout.Under(Root, "../../NeonVoid/save"));
            Assert.Throws<ArgumentException>(() => SaveSlotLayout.Under(Root, "a/b"));
            Assert.Throws<ArgumentException>(() => SaveSlotLayout.Under("  ", "slot-1"));
        }

        [Test]
        public void TheLegacyRootCheckRefusesTheOldLocationAndNotTheGamesOwn()
        {
            // The first version of this refused any path with a folder called NeonVoid in it,
            // which would have refused the game's OWN save directory the moment the product was
            // named NeonVoid - Unity's data path is AppData/LocalLow/<company>/<product>. The
            // legacy location is AppData/Local/NeonVoid, and Local and LocalLow are different
            // directories.
            Assert.IsTrue(UnitySaveRoot.IsLegacyRoot(@"C:\Users\p\AppData\Local\NeonVoid"));
            Assert.IsTrue(UnitySaveRoot.IsLegacyRoot(@"C:\Users\p\AppData\Local\NeonVoid\saves"));
            Assert.IsTrue(UnitySaveRoot.IsLegacyRoot("/home/p/appdata/local/neonvoid/"));

            Assert.IsFalse(
                UnitySaveRoot.IsLegacyRoot(@"C:\Users\p\AppData\LocalLow\Studio\NeonVoid"),
                "The game's own save directory was refused as legacy data.");
            Assert.IsFalse(UnitySaveRoot.IsLegacyRoot(@"C:\Users\p\AppData\LocalLow\NeonVoid\NeonVoid"));
            Assert.IsFalse(UnitySaveRoot.IsLegacyRoot(@"C:\Projects\NeonVoid"));
            Assert.IsFalse(UnitySaveRoot.IsLegacyRoot(null));
        }

        // --- opening -----------------------------------------------------------------------

        [Test]
        public void AnEmptySlotIsNotAnError()
        {
            SaveOpenResult result = _store.Open();

            Assert.AreEqual(SaveOpenVerdict.NoSave, result.Verdict);
            Assert.AreEqual(SaveStoreState.Empty, _store.State);
            Assert.IsNull(_store.Snapshot);
        }

        [Test]
        public void CreatingASlotWritesRevisionOneAndReadsBack()
        {
            _store.Open();
            SaveCommitResult created = _store.CreateSlot("vaccine-1", "create:vaccine-1");

            Assert.AreEqual(SaveCommitVerdict.Committed, created.Verdict, created.Detail);
            Assert.AreEqual(1, created.Revision);
            Assert.AreEqual(SaveStoreState.Ready, _store.State);
            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies, _store.Snapshot.vaccine.copiesRemaining,
                "[확정] docs/04 section 2 starts a vaccine with three clones.");

            var reopened = new SaveStore(_files, _layout);
            SaveOpenResult result = reopened.Open();

            Assert.AreEqual(SaveOpenVerdict.Loaded, result.Verdict, result.Detail);
            Assert.AreEqual(1, reopened.Snapshot.snapshotRevision);
            Assert.AreEqual("vaccine-1", reopened.Snapshot.vaccine.id);
        }

        [Test]
        public void CreatingASlotOverAnExistingOneIsRefusedWithoutWriting()
        {
            SaveStore store = Opened();
            int writes = _files.WriteCount;

            SaveCommitResult second = store.CreateSlot("vaccine-2", "create:vaccine-2");

            Assert.AreEqual(SaveCommitVerdict.Rejected, second.Verdict);
            Assert.AreEqual("vaccine-1", store.Snapshot.vaccine.id);
            Assert.AreEqual(writes, _files.WriteCount, "A refused creation wrote something.");
        }

        [Test]
        public void CommandsAreRefusedBeforeTheStoreIsOpened()
        {
            SaveCommitResult result = _store.Commit(new AddXp("r1", 10L));

            Assert.AreEqual(SaveCommitVerdict.Refused, result.Verdict);
            Assert.IsFalse(result.Acknowledged);
        }

        // --- committing --------------------------------------------------------------------

        [Test]
        public void ACommittedCommandAdvancesTheRevisionAndKeepsItsReceipt()
        {
            SaveStore store = Opened();

            SaveCommitResult result = store.Commit(new AddXp("xp:1", 250L));

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual(2, result.Revision);
            Assert.AreEqual("permanentXp=250", result.Result);
            Assert.AreEqual(250L, store.Snapshot.vaccine.permanentXp);
            Assert.IsNotNull(store.Snapshot.FindReceipt("xp:1"));
            Assert.IsTrue(result.Acknowledged);
        }

        [Test]
        public void TheSameReceiptIdIsNeverAppliedTwice()
        {
            // [확정] docs/04 section 5: 완료된 command를 다시 보낼 때는 같은 결과를 반환, and
            // section 7 forbids re-issuing points after a lost ack. This is the single most
            // important behaviour in the file.
            SaveStore store = Opened();
            SaveCommitResult first = store.Commit(new AddXp("xp:1", 250L));

            SaveCommitResult retry = store.Commit(new AddXp("xp:1", 250L));

            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, retry.Verdict);
            Assert.AreEqual(first.Result, retry.Result, "The retry was answered differently.");
            Assert.AreEqual(first.Revision, retry.Revision);
            Assert.AreEqual(250L, store.Snapshot.vaccine.permanentXp, "The command applied twice.");
            Assert.IsTrue(retry.Acknowledged, "A replay must still tell the caller it is written.");
        }

        [Test]
        public void AHundredDuplicateCallsApplyOnce()
        {
            // docs/04 section 7's test scenario, literally: 동일 사망/철수/보상 100회 중복 호출.
            SaveStore store = Opened();
            for (int i = 0; i < 100; ++i)
                store.Commit(new AddXp("xp:same", 7L));

            Assert.AreEqual(7L, store.Snapshot.vaccine.permanentXp);
            Assert.AreEqual(2, store.Snapshot.snapshotRevision);
            Assert.AreEqual(1, CountReceipts(store, "xp:same"));
        }

        [Test]
        public void ARetryWithADifferentAmountStillReturnsTheCommittedResult()
        {
            // The receipt is the authority, not the arguments. A retry that recomputed its
            // payload from a slightly different place must not be able to change what was
            // already written.
            SaveStore store = Opened();
            store.Commit(new AddXp("xp:1", 250L));

            SaveCommitResult retry = store.Commit(new AddXp("xp:1", 999L));

            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, retry.Verdict);
            Assert.AreEqual("permanentXp=250", retry.Result);
            Assert.AreEqual(250L, store.Snapshot.vaccine.permanentXp);
        }

        [Test]
        public void ARefusedCommandLeavesNothingBehind()
        {
            SaveStore store = Opened();
            int writes = _files.WriteCount;

            SaveCommitResult result = store.Commit(new AlwaysRefuses("no:1"));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            Assert.AreEqual("the rule says no", result.Detail);
            Assert.AreEqual(0L, store.Snapshot.vaccine.permanentXp, "A refused command's edit survived.");
            Assert.AreEqual(1, store.Snapshot.snapshotRevision);
            Assert.AreEqual(writes, _files.WriteCount);
            Assert.IsNull(store.Snapshot.FindReceipt("no:1"), "A refusal was given a receipt.");
        }

        [Test]
        public void ARefusedCommandMayBeRetriedUnderTheSameReceiptId()
        {
            // A rejection is not a commit, so its id was never spent. A store that remembered
            // refusals would make a precondition that becomes true later permanently unusable.
            SaveStore store = Opened();
            store.Commit(new AlwaysRefuses("id:1"));

            SaveCommitResult second = store.Commit(new AddXp("id:1", 5L));

            Assert.AreEqual(SaveCommitVerdict.Committed, second.Verdict, second.Detail);
        }

        [Test]
        public void ACommandThatThrowsIsARejectionAndNotASave()
        {
            SaveStore store = Opened();
            int writes = _files.WriteCount;

            SaveCommitResult result = store.Commit(new Throws("boom:1"));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("a bug in a command", result.Detail);
            Assert.AreEqual(writes, _files.WriteCount);
        }

        [Test]
        public void AStateTheSchemaForbidsIsNeverWritten()
        {
            // Writing it would put a document on disk that this same code refuses to load,
            // turning one bug into a save that halts on every launch.
            SaveStore store = Opened();
            int writes = _files.WriteCount;

            SaveCommitResult result = store.Commit(new ProducesInvalidState("bad:1"));

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("invalid state", result.Detail);
            Assert.AreEqual(writes, _files.WriteCount);
            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies, store.Snapshot.vaccine.copiesRemaining);
        }

        // --- storage failures --------------------------------------------------------------

        [Test]
        public void AWriteThatCannotHappenLeavesTheCommittedStateAlone()
        {
            // docs/04 section 7: 저장 실패 → 보상 미확정·상태 유지. 금지: UI 먼저 '보상 획득 완료'.
            SaveStore store = Opened();
            _files.WriteDenied.Add(_layout.TempPath);

            SaveCommitResult result = store.Commit(new AddXp("xp:1", 250L));

            Assert.AreEqual(SaveCommitVerdict.WriteFailed, result.Verdict);
            Assert.IsFalse(result.Acknowledged, "A failed write was reported as acknowledged.");
            Assert.AreEqual(0L, store.Snapshot.vaccine.permanentXp);
            Assert.AreEqual(1, store.Snapshot.snapshotRevision);
        }

        [Test]
        public void TheSameCommandSucceedsOnceStorageWorksAgainAndAppliesOnlyOnce()
        {
            SaveStore store = Opened();
            _files.WriteDenied.Add(_layout.TempPath);
            store.Commit(new AddXp("xp:1", 250L));

            _files.WriteDenied.Clear();
            SaveCommitResult retry = store.Commit(new AddXp("xp:1", 250L));

            Assert.AreEqual(SaveCommitVerdict.Committed, retry.Verdict, retry.Detail);
            Assert.AreEqual(250L, store.Snapshot.vaccine.permanentXp, "The retry applied the command twice.");
            Assert.AreEqual(2, store.Snapshot.snapshotRevision);
        }

        [Test]
        public void ATornTemporaryFileNeverReachesTheSnapshot()
        {
            // docs/04 section 7 lists 임시 파일 손상 as a scenario to test. Step 4's checksum
            // verification of the temporary file is what catches it, before step 5 replaces
            // anything.
            SaveStore store = Opened();
            int replacesBefore = _files.ReplaceCount;   // creating the slot already did one
            _files.TearWrites = true;

            SaveCommitResult result = store.Commit(new AddXp("xp:1", 250L));

            Assert.AreEqual(SaveCommitVerdict.WriteFailed, result.Verdict);
            Assert.AreEqual(1, store.Snapshot.snapshotRevision);
            Assert.AreEqual(replacesBefore, _files.ReplaceCount, "A torn file was moved over the snapshot.");
        }

        [Test]
        public void AFailedReplaceIsAFailedCommitAndNotALostSave()
        {
            SaveStore store = Opened();
            _files.ReplaceFails = true;

            SaveCommitResult result = store.Commit(new AddXp("xp:1", 250L));

            Assert.AreEqual(SaveCommitVerdict.WriteFailed, result.Verdict);
            Assert.AreEqual(SaveStoreState.Ready, _store.State);
            Assert.AreEqual(1, store.Snapshot.snapshotRevision);
            Assert.AreEqual(0L, store.Snapshot.vaccine.permanentXp);
        }

        [Test]
        public void ASnapshotThatDoesNotVerifyIsRecoveredFromTheBackup()
        {
            // docs/04 section 5: write failed / torn → 마지막 검증된 정상 스냅샷을 복구.
            SaveStore store = Opened();
            store.Commit(new AddXp("xp:1", 250L));       // revision 2, and revision 1 is backed up
            _files.BreakChecksum(_layout.SnapshotPath);
            string damaged = _files.Peek(_layout.SnapshotPath);

            var reopened = new SaveStore(_files, _layout);
            SaveOpenResult result = reopened.Open();

            Assert.AreEqual(SaveOpenVerdict.RecoveredFromBackup, result.Verdict, result.Detail);
            Assert.AreEqual(1, reopened.Snapshot.snapshotRevision, "The restored snapshot is not the backup.");
            Assert.AreEqual(1, reopened.QuarantinedSnapshots);
            Assert.AreEqual(
                damaged, _files.Peek(_layout.QuarantinePath(1)),
                "The damaged file was not kept intact for inspection.");
        }

        [Test]
        public void ASnapshotThatDoesNotVerifyWithNoBackupHaltsWithoutDestroyingAnything()
        {
            // docs/04 section 5: 데이터 불일치 시 사용자 데이터를 덮어쓰지 않고 읽기 전용
            // 복구/오류 상태로 정지. Starting a fresh save over it would be the one
            // unrecoverable mistake available here.
            Opened();
            _files.Delete(_layout.BackupPath);
            _files.BreakChecksum(_layout.SnapshotPath);
            string damaged = _files.Peek(_layout.SnapshotPath);

            var reopened = new SaveStore(_files, _layout);
            SaveOpenResult result = reopened.Open();

            Assert.AreEqual(SaveOpenVerdict.Halted, result.Verdict);
            Assert.AreEqual(SaveStoreState.Halted, reopened.State);
            Assert.IsNotEmpty(reopened.HaltReason);
            Assert.AreEqual(damaged, _files.Peek(_layout.SnapshotPath), "The damaged save was modified.");

            Assert.AreEqual(SaveCommitVerdict.Refused, reopened.Commit(new AddXp("xp:1", 1L)).Verdict);
            Assert.AreEqual(
                SaveCommitVerdict.Refused, reopened.CreateSlot("vaccine-2", "create:2").Verdict,
                "A halted store offered to start a new save over the damaged one.");
        }

        [Test]
        public void AFutureSchemaHaltsRatherThanRollingBackToOurBackup()
        {
            // docs/04 section 7: 미래 스키마 발견 → 안전 오류. 금지: 자동 변환·삭제. Falling back
            // to the backup would be neither of those by name and exactly that in effect: this
            // build would quietly replace a newer build's save with its own older copy.
            SaveStore store = Opened();
            store.Commit(new AddXp("xp:1", 250L));
            _files.Rewrite(_layout.SnapshotPath, Written, "\"schemaVersion\":99");
            string newer = _files.Peek(_layout.SnapshotPath);

            var reopened = new SaveStore(_files, _layout);
            SaveOpenResult result = reopened.Open();

            Assert.AreEqual(SaveOpenVerdict.Halted, result.Verdict);
            StringAssert.Contains("newer build", reopened.HaltReason);
            Assert.AreEqual(newer, _files.Peek(_layout.SnapshotPath), "The newer save was touched.");
            Assert.AreEqual(0, reopened.QuarantinedSnapshots, "The newer save was quarantined.");
        }

        [Test]
        public void AnOlderSchemaHaltsBecauseThereIsNoAutomaticMigration()
        {
            Opened();
            _files.Rewrite(_layout.SnapshotPath, Written, "\"schemaVersion\":0");

            var reopened = new SaveStore(_files, _layout);

            Assert.AreEqual(SaveOpenVerdict.Halted, reopened.Open().Verdict);
            StringAssert.Contains("migration", reopened.HaltReason);
        }

        [Test]
        public void AFileFromAnotherProductIsNeverParsedIntoOurFields()
        {
            // A legacy save copied into the directory by hand, or a file from a different game.
            // Parsing it would fill our defaults from nothing and call the result a save.
            _files.Plant(_layout.SnapshotPath, "{\"schemaVersion\":1,\"product\":\"NeonVoidPython\"}");

            SaveOpenResult result = _store.Open();

            Assert.AreEqual(SaveOpenVerdict.Halted, result.Verdict);
            StringAssert.Contains("NeonVoidPython", _store.HaltReason);
        }

        [Test]
        public void ASnapshotMissingWithABackupPresentIsRestored()
        {
            SaveStore store = Opened();
            store.Commit(new AddXp("xp:1", 250L));
            _files.Delete(_layout.SnapshotPath);

            var reopened = new SaveStore(_files, _layout);
            SaveOpenResult result = reopened.Open();

            Assert.AreEqual(SaveOpenVerdict.RecoveredFromBackup, result.Verdict, result.Detail);
            Assert.AreEqual(1, reopened.Snapshot.snapshotRevision);
        }

        [Test]
        public void AnAbandonedTemporaryFileIsDiscardedRatherThanAdopted()
        {
            // The process died between the write and the replace, so no acknowledgement was
            // ever given for it. Adopting it would commit something the caller still believes
            // failed - and the caller's retry produces it again from the same receipt id, so
            // nothing is lost by discarding it.
            SaveStore store = Opened();
            SaveDocument pretend = store.Snapshot.Clone();
            pretend.snapshotRevision = 99;
            pretend.vaccine.permanentXp = 999L;
            _files.Plant(_layout.TempPath, SaveSerialization.Stamp(pretend));

            var reopened = new SaveStore(_files, _layout);
            reopened.Open();

            Assert.AreEqual(1, reopened.Snapshot.snapshotRevision);
            Assert.IsFalse(_files.Exists(_layout.TempPath), "The abandoned temporary file is still there.");
        }

        [Test]
        public void TheStoreNeverWritesOutsideItsOwnSlotDirectory()
        {
            SaveStore store = Opened();
            store.Commit(new AddXp("xp:1", 250L));

            foreach (string path in _files.Paths)
                StringAssert.StartsWith(_layout.Directory, path, $"{path} is outside the slot directory.");
        }

        private static int CountReceipts(SaveStore store, string receiptId)
        {
            int count = 0;
            for (int i = 0; i < store.Snapshot.receipts.Count; ++i)
            {
                if (store.Snapshot.receipts[i].receiptId == receiptId)
                    count++;
            }

            return count;
        }
    }
}
