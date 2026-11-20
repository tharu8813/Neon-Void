using System;
using System.IO;
using System.Text;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The save transaction against a real filesystem.
    /// <para>
    /// Everything else about the save store is tested through <see cref="FakeSaveFileSystem"/>,
    /// which is the only way to provoke docs/04 section 7's fault list on demand - but it also
    /// means <see cref="DiskSaveFileSystem"/> itself, the part that touches an actual disk, had
    /// never executed. That is exactly the failure this project keeps finding: a thing that is
    /// written, wired and covered by tests that reach a stand-in for it instead of it. The
    /// atomic replace, the flush, the UTF-8 encoding and the directory creation are real
    /// platform behaviour and a fake cannot say whether any of them work.
    /// </para>
    /// <para>
    /// docs/04 section 4 permits exactly this shape of test and no other: <i>테스트용 저장
    /// 서비스는 반드시 임시 디렉터리를 주입받으며 실제 사용자 저장 디렉터리에 접근하면 테스트
    /// 실패</i>. The root here is a fresh directory under the OS temp path, deleted afterwards,
    /// and <see cref="TheTestRootIsNotThePlayersSaveDirectory"/> holds that it is nowhere near
    /// the real one.
    /// </para>
    /// </summary>
    public sealed class DiskSaveFileSystemTests
    {
        private string _root;
        private DiskSaveFileSystem _files;
        private SaveSlotLayout _layout;

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

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-savetests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _files = new DiskSaveFileSystem();
            _layout = SaveSlotLayout.Under(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private SaveStore OpenedStore()
        {
            var store = new SaveStore(_files, _layout);
            store.Open();
            return store;
        }

        // --- where the test is allowed to write -------------------------------------------

        [Test]
        public void TheTestRootIsNotThePlayersSaveDirectory()
        {
            // The guard docs/04 section 4 asks for, stated as an assertion rather than as a
            // convention somebody has to keep following.
            string player = Application.persistentDataPath.Replace('\\', '/');
            string mine = _root.Replace('\\', '/');

            Assert.IsFalse(
                mine.StartsWith(player, StringComparison.OrdinalIgnoreCase),
                $"The test root '{mine}' is inside the player's save directory.");
            StringAssert.StartsWith(
                Path.GetTempPath().Replace('\\', '/'), mine, "The test root is not a temporary directory.");
        }

        [Test]
        public void TheLayoutDirectoryIsCreatedOnOpen()
        {
            Assert.IsFalse(Directory.Exists(_layout.Directory));

            OpenedStore();

            Assert.IsTrue(Directory.Exists(_layout.Directory), "Opening did not create the slot directory.");
        }

        // --- the real round trip -----------------------------------------------------------

        [Test]
        public void ASlotSurvivesBeingWrittenToDiskAndReadBackByAnotherStore()
        {
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:vaccine-1");
            store.Commit(new AddXp("xp:1", 4321L));

            Assert.IsTrue(File.Exists(_layout.SnapshotPath), "No snapshot was written.");

            // A second store with no shared memory: this is what the next launch of the game
            // sees, and the only honest test of whether the bytes on disk are a save.
            var relaunched = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            SaveOpenResult result = relaunched.Open();

            Assert.AreEqual(SaveOpenVerdict.Loaded, result.Verdict, result.Detail);
            Assert.AreEqual(2, relaunched.Snapshot.snapshotRevision);
            Assert.AreEqual(4321L, relaunched.Snapshot.vaccine.permanentXp);
            Assert.IsNotNull(relaunched.Snapshot.FindReceipt("xp:1"));
        }

        [Test]
        public void TheFileOnDiskIsUtf8WithoutAByteOrderMark()
        {
            // JsonUtility is given a document with no non-ASCII content today, but a vaccine id
            // or a receipt note could carry any character, and a BOM would change the bytes the
            // checksum was taken over on some readers and not others.
            SaveStore store = OpenedStore();
            store.CreateSlot("백신-1", "create:1");

            byte[] bytes = File.ReadAllBytes(_layout.SnapshotPath);

            Assert.IsFalse(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "The snapshot starts with a UTF-8 byte order mark.");
            StringAssert.Contains(
                "snapshotChecksum", Encoding.UTF8.GetString(bytes), "The bytes are not the document.");

            var relaunched = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            Assert.AreEqual(SaveOpenVerdict.Loaded, relaunched.Open().Verdict);
            Assert.AreEqual("백신-1", relaunched.Snapshot.vaccine.id);
        }

        [Test]
        public void ReplacingKeepsThePreviousRevisionAsTheBackupAndLeavesNoTemporaryFile()
        {
            // docs/04 section 5 step 5: 이전 정상본 백업 → atomic replace. The backup is what
            // every recovery path depends on, and whether File.Replace actually produces one is
            // platform behaviour, not something a fake can establish.
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:1");
            store.Commit(new AddXp("xp:1", 100L));
            store.Commit(new AddXp("xp:2", 100L));

            Assert.IsTrue(File.Exists(_layout.BackupPath), "No backup was left behind.");
            Assert.IsFalse(File.Exists(_layout.TempPath), "The temporary file was not consumed by the replace.");

            SaveParseResult backup = SaveSerialization.Verify(File.ReadAllText(_layout.BackupPath));
            Assert.IsTrue(backup.Ok, backup.Detail);
            Assert.AreEqual(
                store.Snapshot.snapshotRevision - 1, backup.Document.snapshotRevision,
                "The backup is not the revision before the current one.");
        }

        [Test]
        public void ADamagedSnapshotOnDiskIsRecoveredFromTheBackupAndKeptForInspection()
        {
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:1");
            store.Commit(new AddXp("xp:1", 100L));
            store.Commit(new AddXp("xp:2", 100L));

            // A byte rotted inside a number: valid JSON, wrong checksum. This is the case the
            // checksum exists for, and it is indistinguishable from a hand-edited save.
            string text = File.ReadAllText(_layout.SnapshotPath);
            File.WriteAllText(_layout.SnapshotPath, text.Replace("\"permanentXp\":200", "\"permanentXp\":999999"));
            string damaged = File.ReadAllText(_layout.SnapshotPath);

            var relaunched = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            SaveOpenResult result = relaunched.Open();

            Assert.AreEqual(SaveOpenVerdict.RecoveredFromBackup, result.Verdict, result.Detail);
            Assert.AreEqual(100L, relaunched.Snapshot.vaccine.permanentXp, "The restored save is not the backup.");
            Assert.AreEqual(1, relaunched.QuarantinedSnapshots);
            Assert.AreEqual(
                damaged, File.ReadAllText(relaunched.Layout.QuarantinePath(1)),
                "The damaged file was not kept intact on disk.");
        }

        [Test]
        public void ATruncatedSnapshotWithNoBackupHaltsAndIsLeftExactlyAsItWas()
        {
            // 프로세스 kill 직전/직후 재실행, with the snapshot caught half written and nothing
            // to fall back to. docs/04: 사용자 데이터를 덮어쓰지 않고 읽기 전용 상태로 정지.
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:1");

            string text = File.ReadAllText(_layout.SnapshotPath);
            File.WriteAllText(_layout.SnapshotPath, text.Substring(0, text.Length / 2));
            byte[] truncated = File.ReadAllBytes(_layout.SnapshotPath);

            var relaunched = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            SaveOpenResult result = relaunched.Open();

            Assert.AreEqual(SaveOpenVerdict.Halted, result.Verdict);
            Assert.AreEqual(SaveStoreState.Halted, relaunched.State);
            Assert.AreEqual(truncated, File.ReadAllBytes(_layout.SnapshotPath), "The damaged save was rewritten.");
            Assert.AreEqual(
                SaveCommitVerdict.Refused, relaunched.CreateSlot("vaccine-2", "create:2").Verdict,
                "A halted store started a new save over the damaged one.");
        }

        [Test]
        public void AnAbandonedTemporaryFileOnDiskIsClearedOnOpen()
        {
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:1");
            File.WriteAllText(_layout.TempPath, "{ half a write");

            var relaunched = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));

            Assert.AreEqual(SaveOpenVerdict.Loaded, relaunched.Open().Verdict);
            Assert.IsFalse(File.Exists(_layout.TempPath));
            Assert.AreEqual(1, relaunched.Snapshot.snapshotRevision);
        }

        [Test]
        public void AReadOnlySnapshotIsAFailedCommitAndNotALostSave()
        {
            // 파일 쓰기 권한 없음, provoked for real rather than simulated: the replace cannot
            // touch a read-only destination.
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:1");
            var info = new FileInfo(_layout.SnapshotPath) { IsReadOnly = true };
            try
            {
                SaveCommitResult result = store.Commit(new AddXp("xp:1", 100L));

                Assert.AreEqual(SaveCommitVerdict.WriteFailed, result.Verdict, result.Detail);
                Assert.IsFalse(result.Acknowledged);
                Assert.AreEqual(1, store.Snapshot.snapshotRevision);
            }
            finally
            {
                info.IsReadOnly = false;
            }

            SaveCommitResult retry = store.Commit(new AddXp("xp:1", 100L));

            Assert.AreEqual(SaveCommitVerdict.Committed, retry.Verdict, retry.Detail);
            Assert.AreEqual(100L, store.Snapshot.vaccine.permanentXp, "The retry applied twice.");
        }

        [Test]
        public void ADeathWrittenToDiskSurvivesARelaunchAndChargesOneClone()
        {
            // The whole path, on a real disk: a death commits, the process goes away, the next
            // launch reads the receipt and refuses to charge a second clone. docs/04 section 7:
            // 사망 직후 앱 강제 종료 → 마지막 확정 사망영수증으로 1회 복구. 금지: 복제 2회 차감.
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:1");
            store.Commit(new AllocateRunIdCommand("session-1"));
            var service = new SaveDeathCommitService(store);
            var report = new DeathReport(DeathId.For(1L, 1, 1), 600L);
            service.RequestDeathCommit(report);

            Assert.AreEqual(2, store.Snapshot.vaccine.copiesRemaining);

            var relaunched = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            relaunched.Open();
            var relaunchedService = new SaveDeathCommitService(relaunched);
            DeathCommitAck ack = default;
            relaunchedService.CommitCompleted += a => ack = a;
            relaunchedService.RequestDeathCommit(report);

            Assert.IsTrue(ack.Succeeded, ack.FailureReason);
            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, relaunchedService.LastCommit.Verdict);
            Assert.AreEqual(
                2, relaunched.Snapshot.vaccine.copiesRemaining,
                "A relaunch charged a second clone for one death.");
        }

        [Test]
        public void NothingIsWrittenOutsideTheSlotDirectory()
        {
            SaveStore store = OpenedStore();
            store.CreateSlot("vaccine-1", "create:1");
            store.Commit(new AddXp("xp:1", 100L));

            foreach (string path in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            {
                StringAssert.StartsWith(
                    _layout.Directory, Path.GetDirectoryName(path) + Path.DirectorySeparatorChar,
                    $"{path} is outside the slot directory.");
            }
        }

        [Test]
        public void TheRealFileSystemRefusesALegacyFileName()
        {
            // The guard is on the path, so it holds for the real implementation too - and this
            // is the one that could actually delete the previous project's data.
            foreach (string name in LegacyDataGuard.ProtectedFileNames)
            {
                string path = Path.Combine(_root, name);
                Assert.Throws<InvalidOperationException>(() => _files.WriteAllText(path, "{}"));
                Assert.Throws<InvalidOperationException>(() => _files.Delete(path));
                Assert.Throws<InvalidOperationException>(() => _files.Exists(path));
                Assert.IsFalse(File.Exists(path), $"{name} was created by a refused write.");
            }
        }
    }
}
