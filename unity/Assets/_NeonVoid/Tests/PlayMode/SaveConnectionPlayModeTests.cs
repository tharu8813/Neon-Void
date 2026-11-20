using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NeonVoid.Missions;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The player dies in a running scene and a clone is spent in a file on disk.
    /// <para>
    /// Everything between combat and the bytes is tested elsewhere - the ledger, the commit
    /// service, the transaction, the real filesystem - but the component that joins them was
    /// about thirty lines that nothing executed: find the death director, decide whether the
    /// slot can take a commit, register a service that can. That is the shape of gap this
    /// project keeps finding things in, so it is driven here end to end.
    /// </para>
    /// <para>
    /// The store is built over a temporary directory and injected, because docs/04 section 4
    /// fails a test that reaches the player's save directory. The component has no serialized
    /// path override, so only a test can do this.
    /// </para>
    /// </summary>
    public sealed class SaveConnectionPlayModeTests
    {
        private string _root;
        private PrototypeArena _arena;
        private GameObject _saveObject;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-playsave-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_saveObject != null)
                UnityEngine.Object.Destroy(_saveObject);
            _arena?.Dispose();
            _arena = null;

            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private SaveStore NewStore() =>
            new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));

        /// <summary>
        /// Adds the director without letting its Awake resolve the real save root: a component
        /// added to a deactivated object does not wake until the object is activated.
        /// </summary>
        private VaccineSaveDirector AddDirector(bool writeToRealSaveDirectory, SaveStore store)
        {
            _saveObject = new GameObject("Vaccine Save Director");
            _saveObject.SetActive(false);
            VaccineSaveDirector director = _saveObject.AddComponent<VaccineSaveDirector>();

            typeof(VaccineSaveDirector)
                .GetField("writeToRealSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, writeToRealSaveDirectory);

            if (store != null)
                director.UseStore(store, sessionNonce: "playmode-session");

            _saveObject.SetActive(true);
            return director;
        }

        [UnityTest]
        public IEnumerator ADeathInAPlayingSceneSpendsACloneOnDisk()
        {
            _arena = PrototypeArena.Build();
            SaveStore store = NewStore();
            VaccineSaveDirector director = AddDirector(true, store);

            Assert.IsTrue(director.CanCommit, director.Result.Detail);
            Assert.AreEqual(
                VaccineSaveState.DesignStartingCopies, store.Snapshot.vaccine.copiesRemaining);
            Assert.AreEqual(RunDeathPhase.Alive, _arena.DeathDirector.Ledger.Phase);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(
                RunDeathPhase.Committed, _arena.DeathDirector.Ledger.Phase,
                _arena.DeathDirector.Ledger.LastFailureReason);
            Assert.IsTrue(
                _arena.DeathDirector.Ledger.MayReleaseResults,
                "Results may only be released once the save has acknowledged a verified write.");

            // The assertion that matters: not that the object in memory changed, but that the
            // bytes a new process would read say a clone was spent.
            var reopened = NewStore();
            Assert.AreEqual(SaveOpenVerdict.Loaded, reopened.Open().Verdict);
            Assert.AreEqual(2, reopened.Snapshot.vaccine.copiesRemaining);
            Assert.AreEqual(1, reopened.Snapshot.vaccine.deathCount);
            Assert.AreEqual(1, reopened.Snapshot.runsStarted, "The run id was not taken from the save.");
        }

        [UnityTest]
        public IEnumerator TheSameDeathAfterARelaunchSpendsNoSecondClone()
        {
            // docs/04 section 7: 사망 직후 앱 강제 종료 → 마지막 확정 사망영수증으로 1회 복구.
            // 금지: 복제 2회 차감. The scene going away and coming back is the relaunch.
            _arena = PrototypeArena.Build();
            AddDirector(true, NewStore());
            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(RunDeathPhase.Committed, _arena.DeathDirector.Ledger.Phase);
            DeathId died = _arena.DeathDirector.Ledger.RegisteredDeathId;

            // The same death reported again by a fresh service over a fresh store, exactly as
            // the next launch would.
            var relaunched = NewStore();
            relaunched.Open();
            var service = new SaveDeathCommitService(relaunched);
            DeathCommitAck ack = default;
            service.CommitCompleted += a => ack = a;
            service.RequestDeathCommit(new DeathReport(died, 0L));

            Assert.IsTrue(ack.Succeeded, ack.FailureReason);
            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, service.LastCommit.Verdict);
            Assert.AreEqual(
                2, relaunched.Snapshot.vaccine.copiesRemaining,
                "A relaunch charged a second clone for one death.");
        }

        [UnityTest]
        public IEnumerator WithPermanentWritesOffADeathIsReportedAndLeftUncommitted()
        {
            // The switch on the component, which is the way back to playing without
            // consequences. It has to leave the run in the honest unfinished state rather than
            // in a failure: nothing was attempted, so nothing failed.
            _arena = PrototypeArena.Build();
            VaccineSaveDirector director = AddDirector(false, null);

            Assert.IsFalse(director.CanCommit);
            Assert.AreEqual(SlotBootstrapVerdict.NotConnected, director.Result.Verdict);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(
                RunDeathPhase.AwaitingCommit, _arena.DeathDirector.Ledger.Phase,
                "A death with no save connected must wait, not succeed and not fail.");
            Assert.IsFalse(_arena.DeathDirector.Ledger.MayReleaseResults);
            Assert.IsFalse(
                Directory.Exists(Path.Combine(_root, SaveSlotLayout.ProductDirectory)),
                "Something wrote a save while permanent writes were off.");
        }

        [UnityTest]
        public IEnumerator AHaltedSaveRegistersNoServiceSoTheDeathWaitsInsteadOfFailing()
        {
            // docs/04: a halted save writes nothing. Registering a service that cannot write
            // would turn every death into a failure dialogue; waiting is what the ledger was
            // built to do.
            SaveStore first = NewStore();
            first.Open();
            first.CreateSlot("vaccine-1", "create:1");
            string snapshot = first.Layout.SnapshotPath;

            // A value changed by hand: valid JSON, wrong checksum, and no backup yet to fall
            // back to - which is the read-only halt.
            File.WriteAllText(
                snapshot, File.ReadAllText(snapshot).Replace("\"deathCount\":0", "\"deathCount\":2"));

            _arena = PrototypeArena.Build();
            SaveStore halted = NewStore();

            LogAssert.ignoreFailingMessages = true;
            VaccineSaveDirector director = AddDirector(true, halted);

            Assert.AreEqual(SaveStoreState.Halted, halted.State, "The damaged save was accepted.");
            Assert.IsFalse(director.CanCommit);
            Assert.AreEqual(SlotBootstrapVerdict.Halted, director.Result.Verdict);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(
                RunDeathPhase.AwaitingCommit, _arena.DeathDirector.Ledger.Phase,
                "A death against a halted save must wait, not report a loss.");
        }

        [UnityTest]
        public IEnumerator TheRunIdComesFromTheSaveRatherThanASerializedConstant()
        {
            // Two sessions in a row over the same files. A serialized run id would hand both
            // the same one, and the second session's first death would be answered from the
            // first session's receipt - no clone spent, no error, nothing to see.
            long firstRun = RunIdOfAFreshSession();
            yield return null;   // let the destroyed objects actually go before the next scan
            long secondRun = RunIdOfAFreshSession();

            Assert.AreEqual(1L, firstRun);
            Assert.AreEqual(2L, secondRun, "The second session did not take a new run id.");
        }

        /// <summary>
        /// A save director and a mission, in that order, because the mission reads its run id
        /// in its own Awake. No arena: this is about where the number comes from.
        /// </summary>
        private long RunIdOfAFreshSession()
        {
            var saveObject = new GameObject("Vaccine Save Director");
            saveObject.SetActive(false);
            VaccineSaveDirector director = saveObject.AddComponent<VaccineSaveDirector>();
            director.UseStore(NewStore(), "session-" + Guid.NewGuid().ToString("N"));
            saveObject.SetActive(true);

            var missionObject = new GameObject("Mission Run Director");
            long runId = missionObject.AddComponent<MissionRunDirector>().Identity.RunId;

            UnityEngine.Object.Destroy(missionObject);
            UnityEngine.Object.Destroy(saveObject);
            return runId;
        }

    }
}
