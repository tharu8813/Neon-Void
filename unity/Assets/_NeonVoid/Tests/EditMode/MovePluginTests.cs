using System;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// `MovePlugin` against [확정] docs/04 section 3, whose whole point is that an instance
    /// moves rather than being copied.
    /// </summary>
    public sealed class MovePluginTests
    {
        private const string Root = "/tmp/neonvoid-move-tests";

        private FakeSaveFileSystem _files;
        private SaveSlotLayout _layout;
        private SaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _files = new FakeSaveFileSystem();
            _layout = SaveSlotLayout.Under(Root);
            _store = new SaveStore(_files, _layout);
            _store.Open();
            _store.CreateSlot("vaccine-1", "create:vaccine-1");
        }

        private VaccineSaveState Vaccine => _store.Snapshot.vaccine;

        private SaveCommitResult Move(
            string instanceId, PluginHolding from, PluginHolding to, string nonce = "n1") =>
            _store.Commit(new MovePluginCommand(instanceId, from, to, nonce));

        // --- the move ----------------------------------------------------------------------

        [Test]
        public void TakingAnInstanceOutOfTheArchiveRemovesItFromTheArchive()
        {
            // [확정] docs/04 section 3.4: 보관 DB에서 출전 목록으로 꺼내면 DB에서 사라진다.
            Vaccine.archivePluginInstanceIds.Add("inst-1");

            SaveCommitResult result = Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout);

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.IsEmpty(Vaccine.archivePluginInstanceIds, "The instance was copied, not moved.");
            Assert.AreEqual(1, Vaccine.carriedPluginInstanceIds.Count);
            Assert.AreEqual("inst-1", Vaccine.carriedPluginInstanceIds[0]);
        }

        [Test]
        public void PuttingAnInstanceBackIntoTheArchiveEmptiesTheLoadoutSlot()
        {
            // 플레이어가 직접 DB로 넣어야 한다 (section 3.5). The same command, the other way.
            Vaccine.carriedPluginInstanceIds.Add("inst-1");

            Move("inst-1", PluginHolding.Loadout, PluginHolding.Archive);

            Assert.IsEmpty(Vaccine.carriedPluginInstanceIds);
            Assert.AreEqual("inst-1", Vaccine.archivePluginInstanceIds[0]);
        }

        [Test]
        public void AnInstanceNeverExistsInBothPlaces()
        {
            // docs/04 section 7's forbidden result, checked after every step of a round trip
            // rather than only at the end.
            Vaccine.archivePluginInstanceIds.Add("inst-1");

            Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout, "out");
            AssertHeldOnce("inst-1");

            Move("inst-1", PluginHolding.Loadout, PluginHolding.Archive, "back");
            AssertHeldOnce("inst-1");

            Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout, "out-again");
            AssertHeldOnce("inst-1");
        }

        [Test]
        public void MovingAnInstanceThatIsNotThereIsRefused()
        {
            SaveCommitResult result = Move("inst-missing", PluginHolding.Archive, PluginHolding.Loadout);

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("not in the Archive", result.Detail);
        }

        [Test]
        public void MovingAnInstanceThatIsAlreadyAtTheDestinationSaysSo()
        {
            // A different mistake from "it does not exist", and the UI needs to tell them apart.
            Vaccine.carriedPluginInstanceIds.Add("inst-1");

            SaveCommitResult result = Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout);

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("already in the Loadout", result.Detail);
        }

        [Test]
        public void MovingSomewhereIsNotMovingNowhere()
        {
            Assert.Throws<ArgumentException>(
                () => new MovePluginCommand("inst-1", PluginHolding.Archive, PluginHolding.Archive, "n1"));
        }

        [Test]
        public void AMoveNeedsAnInstanceAndANonce()
        {
            Assert.Throws<ArgumentException>(
                () => new MovePluginCommand("", PluginHolding.Archive, PluginHolding.Loadout, "n1"));
            Assert.Throws<ArgumentException>(
                () => new MovePluginCommand("inst-1", PluginHolding.Archive, PluginHolding.Loadout, " "));
        }

        [Test]
        public void ADestroyedVaccineHasNoDatabaseToMoveAnythingOutOf()
        {
            // 4번째 사망에서 해당 백신에 속한 DB까지 파기 (section 3.6).
            Vaccine.archivePluginInstanceIds.Add("inst-1");
            for (int sequence = 1; sequence <= 4; ++sequence)
                _store.Commit(new RegisterDeathCommand(DeathId.For(1L, 1, sequence), DeathSettlement.None));

            SaveCommitResult result = Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout);

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            StringAssert.Contains("destroyed", result.Detail);
        }

        // --- duplicates --------------------------------------------------------------------

        [Test]
        public void RepeatingOneMoveMovesItOnce()
        {
            // A retry after a lost ack must not move the instance and then move it back, which
            // is what a second application of the same move would look like from the UI.
            Vaccine.archivePluginInstanceIds.Add("inst-1");
            SaveCommitResult first = Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout, "hub-click-7");

            SaveCommitResult retry = Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout, "hub-click-7");

            Assert.AreEqual(SaveCommitVerdict.Committed, first.Verdict, first.Detail);
            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, retry.Verdict);
            Assert.AreEqual(first.Result, retry.Result);
            AssertHeldOnce("inst-1");
            Assert.AreEqual(1, Vaccine.carriedPluginInstanceIds.Count);
        }

        [Test]
        public void TheSameDirectionUnderANewNonceIsANewMove()
        {
            // Out, back, out again. Without the nonce the third move would be answered from the
            // receipt for the first one and silently do nothing.
            Vaccine.archivePluginInstanceIds.Add("inst-1");

            Assert.AreEqual(
                SaveCommitVerdict.Committed,
                Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout, "click-1").Verdict);
            Assert.AreEqual(
                SaveCommitVerdict.Committed,
                Move("inst-1", PluginHolding.Loadout, PluginHolding.Archive, "click-2").Verdict);
            Assert.AreEqual(
                SaveCommitVerdict.Committed,
                Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout, "click-3").Verdict);

            Assert.AreEqual(1, Vaccine.carriedPluginInstanceIds.Count);
            Assert.IsEmpty(Vaccine.archivePluginInstanceIds);
        }

        [Test]
        public void AFailedWriteMovesNothingAndTheRetryMovesItOnce()
        {
            Vaccine.archivePluginInstanceIds.Add("inst-1");
            _files.WriteDenied.Add(_layout.TempPath);

            SaveCommitResult failed = Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout);

            Assert.AreEqual(SaveCommitVerdict.WriteFailed, failed.Verdict);
            Assert.AreEqual(1, Vaccine.archivePluginInstanceIds.Count, "A failed write moved the instance.");
            Assert.IsEmpty(Vaccine.carriedPluginInstanceIds);

            _files.WriteDenied.Clear();
            Assert.AreEqual(
                SaveCommitVerdict.Committed,
                Move("inst-1", PluginHolding.Archive, PluginHolding.Loadout).Verdict);
            AssertHeldOnce("inst-1");
        }

        // --- the limits that do not exist --------------------------------------------------

        [Test]
        public void NeitherTheArchiveNorTheLoadoutHasACapacity()
        {
            // [확정] docs/04 section 3.3: 보관 DB 용량 무제한, 출전 플러그인의 개수 제한 없음. A
            // limit is exactly the sort of thing added later as a sensible default, so the
            // absence of one is held by a test.
            const int many = 200;
            for (int i = 0; i < many; ++i)
                Vaccine.archivePluginInstanceIds.Add($"inst-{i}");

            for (int i = 0; i < many; ++i)
            {
                SaveCommitResult result = Move(
                    $"inst-{i}", PluginHolding.Archive, PluginHolding.Loadout, $"click-{i}");
                Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            }

            Assert.AreEqual(many, Vaccine.carriedPluginInstanceIds.Count);
            Assert.IsEmpty(Vaccine.archivePluginInstanceIds);
        }

        [Test]
        public void ADeathAfterAMoveLosesTheLoadoutAndKeepsWhatWasPutBack()
        {
            // The two [확정] rules meeting: 출전 소지분은 소실, and an instance the player moved
            // into the archive first is kept. This is the decision the hub exists to offer.
            Vaccine.archivePluginInstanceIds.Add("kept");
            Vaccine.archivePluginInstanceIds.Add("risked");
            Move("risked", PluginHolding.Archive, PluginHolding.Loadout, "take-it");

            _store.Commit(new RegisterDeathCommand(DeathId.For(1L, 1, 1), DeathSettlement.None));

            Assert.IsEmpty(Vaccine.carriedPluginInstanceIds, "The risked instance survived the death.");
            Assert.AreEqual(1, Vaccine.archivePluginInstanceIds.Count);
            Assert.AreEqual("kept", Vaccine.archivePluginInstanceIds[0]);
        }

        private void AssertHeldOnce(string instanceId)
        {
            int count = 0;
            if (Vaccine.archivePluginInstanceIds.Contains(instanceId))
                count++;
            if (Vaccine.carriedPluginInstanceIds.Contains(instanceId))
                count++;

            Assert.AreEqual(1, count, $"{instanceId} is held {count} times, not once.");
        }
    }
}
