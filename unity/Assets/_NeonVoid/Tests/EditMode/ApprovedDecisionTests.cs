using System.Collections.Generic;
using NeonVoid.Missions;
using NeonVoid.Plugins;
using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The decisions the planner approved, held where they can break.
    /// <para>
    /// docs/08 section 5 says an approved decision is recorded with its affected files and its
    /// tests. These are those tests for the ones whose rule is a number or a fact rather than a
    /// behaviour with a home of its own - the shield readings live in
    /// <c>ShieldStackTests</c> and the kill stacks in <c>KillStackPlayModeTests</c>.
    /// </para>
    /// </summary>
    public sealed class ApprovedDecisionTests
    {
        private const string Root = "C:/neonvoid-approved-test";

        // --- D-019: R02 킬 스택 -------------------------------------------------------------

        [Test]
        public void TheKillStackCapIsTwenty()
        {
            // [확정] D-019, approved 2026-11-22: 최대 20회.
            PluginDefinition r02 = PluginCatalog.KillStack();

            Assert.AreEqual("R02", r02.ProposalId);
            Assert.AreEqual(20, r02.Growth.MaxStacks);
            Assert.AreEqual(PluginEffectKind.GrowingPassive, r02.Effect);
            Assert.AreEqual(PluginTriggerKind.OnKill, r02.Trigger);
        }

        [Test]
        public void TheKillStackHasNoDurationSoNothingButTheMissionEndsIt()
        {
            // The stack is a growing stat contribution, not a 상태이상: a duration would answer
            // "how fast does it decay" without anyone deciding that.
            Assert.AreEqual(-1, PluginCatalog.KillStack().StatusDurationTicksOverride);
        }

        // --- D-019: the mission is what ends it ------------------------------------------------

        [Test]
        public void AMissionRaisesItsEndOnceWhenItFails()
        {
            var flow = NewFlow();
            int ended = 0;
            flow.MissionEnded += () => ++ended;

            Assert.IsTrue(flow.TryBeginMission(new RunIdentity(1L, 7u), 0L), flow.LastRefusal);
            Assert.AreEqual(0, ended, "a mission that began was reported as ended");

            Assert.IsTrue(flow.TryFail(), flow.LastRefusal);
            Assert.AreEqual(1, ended);

            // Returning to the hub is the walk back, not a second ending.
            Assert.IsTrue(flow.TryReturnToHub(), flow.LastRefusal);
            Assert.AreEqual(1, ended, "the walk back counted as another mission ending");
        }

        [Test]
        public void AMissionThatNeverBeganNeverEnds()
        {
            var flow = NewFlow();
            int ended = 0;
            flow.MissionEnded += () => ++ended;

            Assert.IsFalse(flow.TryFail(), "there was no mission to fail");
            Assert.AreEqual(0, ended);
        }

        private static MissionFlow NewFlow() =>
            new MissionFlow(new MissionRunState(), PrototypeMissionCommits.Instance);

        // --- D-016: 최종 파기는 미정산분을 지급하지 않는다 ---------------------------------------

        [Test]
        public void TheFourthDeathPaysNothingAndSaysSoOnItsReceipt()
        {
            // [확정] D-016, approved 2026-11-22. The same transaction destroys this vaccine's
            // growth, so paying first and deleting after would write a number onto a receipt
            // that the player never had.
            var files = new FakeSaveFileSystem();
            var store = new SaveStore(files, SaveSlotLayout.Under(Root));
            store.Open();
            store.CreateSlot("vaccine-1", "create:vaccine-1");
            store.Commit(new AllocateRunIdCommand("approved-test"));

            var settlement = new DeathSettlement(permanentXp: 500L, statPoints: 5, purifications: 3);

            for (int sequence = 1;
                 sequence <= VaccineSaveState.DesignDeathsBeforeFinalDestruction;
                 ++sequence)
            {
                SaveCommitResult commit = store.Commit(
                    new RegisterDeathCommand(DeathId.For(1L, 1, sequence), settlement));
                Assert.IsTrue(commit.Acknowledged, commit.Detail);
            }

            VaccineSaveState vaccine = store.Snapshot.vaccine;
            Assert.IsFalse(vaccine.alive, "the fourth death did not destroy the vaccine");
            Assert.AreEqual(0L, vaccine.permanentXp, "destruction settled XP into a destroyed vaccine");
            Assert.AreEqual(0, vaccine.unusedStatPoints, "destruction settled points into a destroyed vaccine");

            SaveReceiptRecord receipt = ReceiptFor(store, DeathId.For(1L, 1, 4));
            Assert.IsNotNull(receipt, "the fourth death left no receipt");
            StringAssert.Contains("settlement-moot-on-destruction", receipt.result);
        }

        [Test]
        public void TheThirdDeathStillSettlesBecauseTheVaccineIsStillThere()
        {
            // The other side of the same rule: only the destruction refuses to pay.
            var files = new FakeSaveFileSystem();
            var store = new SaveStore(files, SaveSlotLayout.Under(Root));
            store.Open();
            store.CreateSlot("vaccine-1", "create:vaccine-1");
            store.Commit(new AllocateRunIdCommand("approved-test"));

            var settlement = new DeathSettlement(permanentXp: 120L, statPoints: 2, purifications: 1);
            for (int sequence = 1; sequence <= 3; ++sequence)
                Assert.IsTrue(store.Commit(
                    new RegisterDeathCommand(DeathId.For(1L, 1, sequence), settlement)).Acknowledged);

            Assert.IsTrue(store.Snapshot.vaccine.alive, "복제 0 is a last chance, not a destruction");
            Assert.Greater(store.Snapshot.vaccine.permanentXp, 0L, "three deaths settled nothing");
        }

        private static SaveReceiptRecord ReceiptFor(SaveStore store, DeathId id)
        {
            IReadOnlyList<SaveReceiptRecord> receipts = store.Snapshot.receipts;
            for (int i = 0; i < receipts.Count; ++i)
            {
                if (receipts[i].receiptId.Contains(id.ToString()))
                    return receipts[i];
            }

            return null;
        }
    }
}
