using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NeonVoid.Missions;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The mission flow as the scene actually wires it.
    /// <para>
    /// The flow itself and its save commands are covered in EditMode. What is only reachable
    /// here is the thirty lines in <see cref="MissionRunDirector.Awake"/> that decide which gate
    /// the flow gets - a real one over the connected store, or the prototype one - and whether
    /// the mission begins at all. That shape of joining code is where this project keeps finding
    /// gaps, so it is driven in a running scene.
    /// </para>
    /// <para>
    /// The store is built over a temporary directory and injected: docs/04 section 4 fails a test
    /// that reaches the player's own save directory.
    /// </para>
    /// </summary>
    public sealed class MissionFlowPlayModeTests
    {
        private const int Objectives = 3;

        private string _root;
        private PrototypeArena _arena;
        private GameObject _saveObject;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-playflow-" + Guid.NewGuid().ToString("N"));
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

        /// <summary>
        /// Stands a prepared save up <b>before</b> the arena, because the mission director
        /// resolves its gate in Awake: a save that arrives afterwards is a save this mission
        /// never saw.
        /// </summary>
        private SaveStore ConnectSave()
        {
            var store = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));

            _saveObject = new GameObject("Vaccine Save Director");
            _saveObject.SetActive(false);
            VaccineSaveDirector director = _saveObject.AddComponent<VaccineSaveDirector>();
            typeof(VaccineSaveDirector)
                .GetField("writeToRealSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, true);
            director.UseStore(store, sessionNonce: "playmode-flow");
            _saveObject.SetActive(true);

            Assert.IsTrue(director.CanCommit, director.Result.Detail);
            return store;
        }

        private static readonly string[] NoLearns = new string[0];

        private static void ClearArea(MissionFlow flow, HardwareId area)
        {
            Assert.IsTrue(flow.TryEnterArea(area, Objectives), flow.LastRefusal);
            for (int i = 0; i < Objectives; ++i)
                Assert.IsTrue(flow.TryCompleteObjective(), flow.LastRefusal);

            Assert.IsTrue(flow.TryDefeatBoss(), flow.LastRefusal);
            Assert.IsTrue(flow.TryPurify(), flow.LastRefusal);
        }

        [UnityTest]
        public IEnumerator TheDirectorWakesUpOnTheMotherboardWithTheSavesRunId()
        {
            SaveStore store = ConnectSave();
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            MissionFlow flow = _arena.Mission.Flow;

            Assert.AreEqual(MissionFlowPhase.Motherboard, flow.Phase, flow.LastRefusal);
            Assert.AreEqual(AreaProgressPhase.None, flow.AreaPhase);
            Assert.IsTrue(flow.Run.IsRunning);
            Assert.AreEqual(
                store.Snapshot.runsStarted, flow.Run.Identity.RunId,
                "the mission is not on the run id the save allocated");
            Assert.AreEqual(1, flow.Run.MissionLevel, "[확정] 새 의뢰 Lv.1");
        }

        [UnityTest]
        public IEnumerator APurificationInAPlayingSceneReachesTheFileOnDisk()
        {
            SaveStore store = ConnectSave();
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            MissionFlow flow = _arena.Mission.Flow;
            long runId = flow.Run.Identity.RunId;

            ClearArea(flow, HardwareId.Cpu);

            Assert.AreEqual(
                1, store.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(runId)),
                "the purification never reached the store");
            Assert.AreEqual(1, flow.Run.PurificationsCompleted);
            Assert.Greater(flow.Run.MissionXp, 0L, "the area's 의뢰 XP was not awarded");

            // And a second process reading the same directory sees it, which is the only thing
            // that makes it permanent.
            var reopened = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            reopened.Open();
            Assert.AreEqual(
                1, reopened.Snapshot.CountReceipts(CleanseHardwareCommand.ReceiptPrefixFor(runId)));
        }

        [UnityTest]
        public IEnumerator AnExtractionInAPlayingSceneGrowsTheVaccineOnDisk()
        {
            SaveStore store = ConnectSave();
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            MissionFlow flow = _arena.Mission.Flow;
            ClearArea(flow, HardwareId.Cpu);

            Assert.IsTrue(flow.TryExtract(NoLearns), flow.LastRefusal);

            Assert.AreEqual(MissionFlowPhase.Extracted, flow.Phase);
            Assert.AreEqual(MissionPhase.Completed, flow.Run.Phase);

            // GDD v1 section 5.6: 정화 1개 = 165 백신 XP at the standard three objectives.
            var reopened = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            reopened.Open();
            Assert.AreEqual(165L, reopened.Snapshot.vaccine.permanentXp, "the extraction is not on disk");
            Assert.AreEqual(store.Snapshot.vaccine.permanentXp, reopened.Snapshot.vaccine.permanentXp);
        }

        [UnityTest]
        public IEnumerator WithNoSaveInTheSceneTheAreaIsPlayableAndTheExtractionIsNot()
        {
            // The prototype gate. 의뢰 XP is temporary either way, so the loop is playable; the
            // extraction's whole product is permanent, so it is refused rather than shown as a
            // reward nothing wrote (docs/04 section 7).
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            MissionFlow flow = _arena.Mission.Flow;
            ClearArea(flow, HardwareId.Cpu);
            Assert.AreEqual(1, flow.PurifiedAreas.Count);

            Assert.IsFalse(flow.TryExtract(NoLearns), "an extraction committed with no save");
            StringAssert.Contains("no save is connected", flow.LastRefusal);
            Assert.AreEqual(MissionPhase.InProgress, flow.Run.Phase);
        }

        [UnityTest]
        public IEnumerator ADeathInAPlayingSceneFailsTheFlow()
        {
            // [확정] docs/00: 사망은 해당 의뢰 실패. The death director calls FailMission, which
            // now goes through the flow - so a death has to leave the flow somewhere a mission
            // cannot be continued from.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            MissionFlow flow = _arena.Mission.Flow;
            Assert.IsTrue(flow.TryEnterArea(HardwareId.Cpu, Objectives), flow.LastRefusal);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(1);

            Assert.AreEqual(MissionFlowPhase.Failed, flow.Phase);
            Assert.AreEqual(MissionPhase.Failed, flow.Run.Phase);
            Assert.IsFalse(flow.TryCompleteObjective(), "a failed mission kept playing");
            Assert.IsFalse(flow.TryExtract(NoLearns));
        }
    }
}
