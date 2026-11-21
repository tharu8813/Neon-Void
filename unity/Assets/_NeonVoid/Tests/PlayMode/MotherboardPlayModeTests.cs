using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NeonVoid.Combat;
using NeonVoid.Missions;
using NeonVoid.Missions.Generation;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The mission walked instead of called: stand in a portal, the area is built, clear it,
    /// purify, come back, and the motherboard is there again with one area crossed off.
    /// <para>
    /// Everything under this has its own tests. What only this can show is that the loop closes
    /// on itself - that an area goes away when it is left, that the next one can be entered, and
    /// that nothing of the first is still standing. An area that was built and never destroyed
    /// would pass every other test in the project.
    /// </para>
    /// </summary>
    public sealed class MotherboardPlayModeTests
    {
        private const int DwellTicks = 2;
        private const float PortalRadius = 3f;
        private const float RingRadius = 9f;

        private string _root;
        private PrototypeArena _arena;
        private GameObject _saveObject;
        private GameObject _motherboardObject;
        private MotherboardDirector _motherboard;
        private SaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-playboard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_motherboardObject != null)
                UnityEngine.Object.Destroy(_motherboardObject);
            if (_saveObject != null)
                UnityEngine.Object.Destroy(_saveObject);

            _arena?.Dispose();
            _arena = null;
            _motherboard = null;
            _store = null;

            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private MissionFlow Flow => _arena.Mission.Flow;

        private void ConnectSave()
        {
            _store = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));

            _saveObject = new GameObject("Vaccine Save Director");
            _saveObject.SetActive(false);
            VaccineSaveDirector director = _saveObject.AddComponent<VaccineSaveDirector>();
            typeof(VaccineSaveDirector)
                .GetField("writeToRealSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, true);
            director.UseStore(_store, sessionNonce: "playmode-board");
            _saveObject.SetActive(true);

            Assert.IsTrue(director.CanCommit, director.Result.Detail);
        }

        /// <summary>
        /// A motherboard far from the arena's own ground, with short portal dwells. Built after
        /// the arena so its Awake finds the mission and the player.
        /// </summary>
        private IEnumerator Motherboard()
        {
            ConnectSave();
            _arena = PrototypeArena.Build();

            CombatStats stats = PrototypeArena.PlayerStats();
            stats.MaxHealth = 1_000_000f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, stats);

            _motherboardObject = new GameObject("Motherboard");
            _motherboardObject.SetActive(false);
            _motherboardObject.transform.position = new Vector3(-400f, 0f, -400f);
            _motherboard = _motherboardObject.AddComponent<MotherboardDirector>();
            _motherboard.Configure(_arena.Mission, null, _arena.PlayerObject.transform);
            _motherboardObject.SetActive(true);
            _motherboard.ConfigurePortals(RingRadius, PortalRadius, DwellTicks);

            StandOn(_motherboardObject.transform.position);
            yield return _arena.Tick(1);
        }

        private void StandOn(Vector3 destination)
        {
            Transform player = _arena.PlayerObject.transform;
            float y = destination.y + AreaWorldBuilder.PlayerStandingLiftUnits;
            player.position = new Vector3(player.position.x, y, player.position.z);
            _arena.Driver.TeleportTo(new Vector2(destination.x, destination.z));
        }

        private static readonly string[] NoLearns = new string[0];

        /// <summary>Objectives, boss and 정화, from inside whatever area is standing.</summary>
        private IEnumerator ClearTheStandingArea()
        {
            AreaBuildResult area = _motherboard.CurrentArea;
            Assert.IsNotNull(area, "no area is standing");

            // Whatever this hardware's objectives are: the player chose the area, so the
            // shapes are not known here.
            _arena.MakePlayerUnkillable();
            yield return _arena.ClearObjectives(area);
            Assert.AreEqual(AreaProgressPhase.Boss, Flow.AreaPhase, Flow.LastRefusal);

            PrototypeArena.Kill(area.BossGate.Boss);
            yield return _arena.Tick(1);
            Assert.AreEqual(AreaProgressPhase.Purification, Flow.AreaPhase);

            area.Runtime.ConfigureDwell(PortalRadius, DwellTicks, PortalRadius, DwellTicks);
            StandOn(_motherboard.CurrentArea.PurificationConsole.position);
            yield return _arena.Tick(DwellTicks + 1);

            Assert.AreEqual(AreaProgressPhase.Purified, Flow.AreaPhase, Flow.LastRefusal);
        }

        // --- the board --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheMotherboardOffersFivePortalsAndTheMissionsOwnBoard()
        {
            yield return Motherboard();

            Assert.AreEqual(MissionFlowPhase.Motherboard, Flow.Phase);
            Assert.AreEqual(NeonVoid.Core.DesignConstants.HardwareCount, _motherboard.Board.Count);
            Assert.GreaterOrEqual(_motherboard.Board.EasyCount, 1, "[확정] 최소 하나 Easy");

            for (int i = 0; i < HardwareIds.All.Length; ++i)
            {
                HardwareId hardware = HardwareIds.All[i];
                Assert.IsNotNull(_motherboard.Portal(hardware), $"{hardware} has no portal");
                Assert.IsNotNull(_motherboard.PortalDwell(hardware), $"{hardware} has no dwell");
            }

            Assert.IsNull(_motherboard.CurrentArea, "an area was standing before anybody entered one");
        }

        [UnityTest]
        public IEnumerator TheBoardIsTheSameBoardEveryTimeItIsAsked()
        {
            yield return Motherboard();

            string first = _motherboard.Board.ToString();
            yield return _arena.Tick(5);

            Assert.AreEqual(first, _motherboard.Board.ToString(), "the board changed under the player");
        }

        // --- walking in -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator StandingInAPortalBuildsItsAreaAndWalksThePlayerIn()
        {
            yield return Motherboard();

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(DwellTicks + 2);

            Assert.IsNotNull(_motherboard.CurrentArea, "the portal never opened");
            Assert.AreEqual(HardwareId.Cpu, _motherboard.CurrentArea.Layout.Spec.Hardware);
            Assert.AreEqual(MissionFlowPhase.InArea, Flow.Phase);
            Assert.AreEqual(AreaProgressPhase.Objectives, Flow.AreaPhase);

            // The area is built from the seed the mission already fixed, not one rolled at the
            // portal - docs/04's 동일 seed 재사용.
            uint expected = AreaSeeds.For(Flow.Run.SeedFor(RandomStream.Mission), HardwareId.Cpu);
            Assert.AreEqual(expected, _motherboard.CurrentArea.Layout.Spec.Seed);

            // And the player is standing at the area's entrance, not still on the motherboard.
            // The area's world origin is recovered from a marker whose cell is known, because the
            // builder's offset is its own business.
            AreaLayout layout = _motherboard.CurrentArea.Layout;
            Vector3 origin = _motherboard.CurrentArea.PurificationConsole.position
                             - layout.WorldPositionOf(layout.Purification);
            Vector3 entrance = origin + layout.WorldPositionOf(layout.Start);
            Vector3 at = _arena.PlayerObject.transform.position;

            Assert.Greater(
                Vector3.Distance(new Vector3(at.x, 0f, at.z), _motherboardObject.transform.position), 50f,
                "the player is still on the motherboard");
            Assert.AreEqual(entrance.x, at.x, 1f, "the player is not at the area's entrance");
            Assert.AreEqual(entrance.z, at.z, 1f);
        }

        [UnityTest]
        public IEnumerator WalkingPastAPortalDoesNotEnterIt()
        {
            // The dwell is a duration as well as a place. A portal that fired on contact would
            // take the player into an area they walked across the room to look at.
            yield return Motherboard();

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(1);
            StandOn(_motherboardObject.transform.position);
            yield return _arena.Tick(DwellTicks * 4);

            Assert.IsNull(_motherboard.CurrentArea, "a portal fired on contact");
            Assert.AreEqual(MissionFlowPhase.Motherboard, Flow.Phase);
        }

        // --- and out again ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator PurifyingAndReturningTakesTheAreaAwayAndCrossesItOff()
        {
            yield return Motherboard();

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(DwellTicks + 2);
            AreaBuildResult area = _motherboard.CurrentArea;
            GameObject areaRoot = area.Root;

            yield return ClearTheStandingArea();

            Assert.IsTrue(Flow.TryReturnToMotherboard(), Flow.LastRefusal);
            yield return _arena.Tick(2);

            Assert.IsNull(_motherboard.CurrentArea, "the area is still standing");
            Assert.IsTrue(areaRoot == null, "the area's objects were left in the scene");
            Assert.AreEqual(MissionFlowPhase.Motherboard, Flow.Phase);
            Assert.IsTrue(Flow.IsPurified(HardwareId.Cpu));

            // Back on the motherboard, not wherever the area used to be.
            Vector3 at = _arena.PlayerObject.transform.position;
            Assert.Less(
                Vector3.Distance(new Vector3(at.x, 0f, at.z), _motherboardObject.transform.position), 5f,
                "the player did not come back to the motherboard");
        }

        [UnityTest]
        public IEnumerator APurifiedAreasPortalIsDead()
        {
            // 이미 정화한 하드웨어의 완료 상태는 런 동안 유지 + 중복 정화 금지. The portal does
            // nothing rather than taking a second and a half to say no.
            yield return Motherboard();

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(DwellTicks + 2);
            yield return ClearTheStandingArea();
            Flow.TryReturnToMotherboard();
            yield return _arena.Tick(2);

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(DwellTicks * 4);

            Assert.IsNull(_motherboard.CurrentArea, "a purified area was entered again");
            Assert.AreEqual(0f, _motherboard.PortalDwell(HardwareId.Cpu).Progress, 1e-6f, "its dwell still counts");
        }

        [UnityTest]
        public IEnumerator AnotherAreaCanBeEnteredAfterTheFirstIsDone()
        {
            // 입장 순서는 자유, and the second area has to be the only one standing.
            yield return Motherboard();

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(DwellTicks + 2);
            yield return ClearTheStandingArea();
            Flow.TryReturnToMotherboard();
            yield return _arena.Tick(2);

            StandOn(_motherboard.Portal(HardwareId.Gpu).position);
            yield return _arena.Tick(DwellTicks + 2);

            Assert.IsNotNull(_motherboard.CurrentArea, "the second portal never opened");
            Assert.AreEqual(HardwareId.Gpu, _motherboard.CurrentArea.Layout.Spec.Hardware);
            Assert.AreEqual(1, Flow.PurifiedAreas.Count, "the first area was forgotten");

            // And it is the ONLY area standing. An area built and never destroyed would pass
            // every other test in the project.
            HardwareAreaRuntime[] standing =
                UnityEngine.Object.FindObjectsByType<HardwareAreaRuntime>(FindObjectsSortMode.None);
            Assert.AreEqual(1, standing.Length, "more than one area is standing in the scene");
        }

        [UnityTest]
        public IEnumerator ADeathTakesTheAreaAwayToo()
        {
            // A failed mission is not continued, so the room it failed in must not be left up.
            yield return Motherboard();

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(DwellTicks + 2);
            GameObject areaRoot = _motherboard.CurrentArea.Root;

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(3);

            Assert.AreEqual(MissionFlowPhase.Failed, Flow.Phase);
            Assert.IsNull(_motherboard.CurrentArea);
            Assert.IsTrue(areaRoot == null, "the failed mission's area is still in the scene");
        }

        [UnityTest]
        public IEnumerator AnExtractionFromInsideAnAreaTakesItAwayAsWell()
        {
            yield return Motherboard();

            StandOn(_motherboard.Portal(HardwareId.Cpu).position);
            yield return _arena.Tick(DwellTicks + 2);
            yield return ClearTheStandingArea();

            GameObject areaRoot = _motherboard.CurrentArea.Root;
            Assert.IsTrue(Flow.TryExtract(NoLearns), Flow.LastRefusal);
            yield return _arena.Tick(2);

            Assert.AreEqual(MissionFlowPhase.Extracted, Flow.Phase);
            Assert.IsNull(_motherboard.CurrentArea);
            Assert.IsTrue(areaRoot == null);

            var reopened = new SaveStore(new DiskSaveFileSystem(), SaveSlotLayout.Under(_root));
            reopened.Open();
            Assert.Greater(reopened.Snapshot.vaccine.permanentXp, 0L, "the extraction banked nothing");
        }
    }
}
