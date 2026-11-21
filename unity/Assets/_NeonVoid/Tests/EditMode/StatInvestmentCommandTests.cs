using NeonVoid.Progression;
using NeonVoid.Progression.Save;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Spending and refunding 영구 능력치 포인트 through the save transaction.
    /// <para>
    /// [확정] GDD v1 section 4.3: 레벨업마다 영구 포인트를 지급하고, 허브에서 재분배 비용 없이
    /// 언제든 초기화·재투자한다. Both halves of that sentence are what these hold - that a point
    /// can be spent at all, and that every one of them comes back for nothing.
    /// </para>
    /// </summary>
    public sealed class StatInvestmentCommandTests
    {
        private const string Root = "/tmp/neonvoid-invest-tests";

        private FakeSaveFileSystem _files;
        private SaveStore _store;

        [SetUp]
        public void SetUp()
        {
            _files = new FakeSaveFileSystem();
            _store = new SaveStore(_files, SaveSlotLayout.Under(Root));
            _store.Open();
            _store.CreateSlot("vaccine-1", "create:vaccine-1");
        }

        private VaccineSaveState Vaccine => _store.Snapshot.vaccine;

        private void GivePoints(int points) => Vaccine.unusedStatPoints = points;

        private SaveCommitResult Invest(InvestableStat stat, int steps = 1, string nonce = "n1") =>
            _store.Commit(new InvestStatPointsCommand(stat, steps, nonce));

        private SaveCommitResult Redistribute(string nonce = "r1") =>
            _store.Commit(new RedistributeStatPointsCommand(nonce));

        // --- spending ----------------------------------------------------------------------

        [Test]
        public void APointSpentLeavesTheUnusedPoolAndLandsOnTheStat()
        {
            GivePoints(3);

            SaveCommitResult result = Invest(InvestableStat.MaxHealth, 2);

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual(1, Vaccine.unusedStatPoints);
            Assert.AreEqual(1, Vaccine.permanentInvestments.Count);
            Assert.AreEqual("maxHealth", Vaccine.permanentInvestments[0].stat);
            Assert.AreEqual(2, Vaccine.permanentInvestments[0].steps);
            Assert.AreEqual(2, Vaccine.permanentInvestments[0].pointsSpent);
        }

        [Test]
        public void AStatThatCostsTwoTakesTwoPointsPerStep()
        {
            GivePoints(4);

            Assert.AreEqual(SaveCommitVerdict.Committed, Invest(InvestableStat.Tenacity, 2).Verdict);
            Assert.AreEqual(0, Vaccine.unusedStatPoints);
            Assert.AreEqual(2, Vaccine.permanentInvestments[0].steps);
            Assert.AreEqual(4, Vaccine.permanentInvestments[0].pointsSpent);
        }

        [Test]
        public void InvestingInTheSameStatAgainAddsToTheSameEntry()
        {
            GivePoints(5);
            Invest(InvestableStat.AttackDamage, 2, "n1");
            Invest(InvestableStat.AttackDamage, 3, "n2");

            Assert.AreEqual(1, Vaccine.permanentInvestments.Count, "one stat produced two entries");
            Assert.AreEqual(5, Vaccine.permanentInvestments[0].steps);
            Assert.AreEqual(5, Vaccine.permanentInvestments[0].pointsSpent);
            Assert.AreEqual(0, Vaccine.unusedStatPoints);
        }

        [Test]
        public void MorePointsThanTheVaccineHasIsRefusedRatherThanClamped()
        {
            // A player asking for more than they have has been shown a stale number, and
            // committing a smaller purchase than they asked for is the quiet correction
            // docs/04 section 7 forbids.
            GivePoints(1);

            SaveCommitResult result = Invest(InvestableStat.MaxHealth, 5);

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            Assert.AreEqual(1, Vaccine.unusedStatPoints, "a refused purchase still took a point");
            Assert.IsEmpty(Vaccine.permanentInvestments);
        }

        [Test]
        public void AStatNothingAppliesIsRefused()
        {
            // 이동속도 is a real row of the table with no field in the combat stat block.
            // Taking the point would spend something permanent on nothing at all.
            GivePoints(10);

            SaveCommitResult result = Invest(InvestableStat.MoveSpeed);

            Assert.AreEqual(SaveCommitVerdict.Rejected, result.Verdict);
            Assert.AreEqual(10, Vaccine.unusedStatPoints);
            Assert.IsEmpty(Vaccine.permanentInvestments);
        }

        [Test]
        public void TheSamePurchaseTwiceIsAppliedOnce()
        {
            GivePoints(4);

            SaveCommitResult first = Invest(InvestableStat.MaxHealth, 2, "same");
            SaveCommitResult again = Invest(InvestableStat.MaxHealth, 2, "same");

            Assert.AreEqual(SaveCommitVerdict.Committed, first.Verdict);
            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, again.Verdict);
            Assert.AreEqual(first.Result, again.Result, "the replay did not return the first result");
            Assert.AreEqual(2, Vaccine.unusedStatPoints, "the retry spent the points a second time");
        }

        [Test]
        public void ADifferentPurchaseUnderTheSameNonceIsNotAReplay()
        {
            // The receipt carries the purchase, not just the nonce. Without that, a terminal
            // whose acknowledgement was lost and then bought something else would be handed
            // the first purchase's result and the player would get a stat they did not pick.
            GivePoints(4);

            Invest(InvestableStat.MaxHealth, 1, "same");
            SaveCommitResult other = Invest(InvestableStat.AbilityPower, 1, "same");

            Assert.AreEqual(SaveCommitVerdict.Committed, other.Verdict, other.Detail);
            Assert.AreEqual(2, Vaccine.permanentInvestments.Count);
            Assert.AreEqual(2, Vaccine.unusedStatPoints);
        }

        [Test]
        public void ADestroyedVaccineCannotInvest()
        {
            // 4번째 사망에서 해당 백신의 영구 성장이 파기된다 (docs/04 section 2).
            GivePoints(5);
            Vaccine.alive = false;
            Vaccine.copiesRemaining = 0;
            Vaccine.deathCount = VaccineSaveState.DesignDeathsBeforeFinalDestruction;

            Assert.AreEqual(SaveCommitVerdict.Rejected, Invest(InvestableStat.MaxHealth).Verdict);
            Assert.AreEqual(SaveCommitVerdict.Rejected, Redistribute().Verdict);
        }

        // --- 무료 재분배 --------------------------------------------------------------------

        [Test]
        public void RedistributingGivesEveryPointBackAndCostsNothing()
        {
            // [확정] 재분배 비용 없이 언제든 초기화·재투자.
            GivePoints(9);
            Invest(InvestableStat.Omnivamp, 2, "a");      // 3 points each
            Invest(InvestableStat.MaxHealth, 3, "b");     // 1 point each

            Assert.AreEqual(0, Vaccine.unusedStatPoints);

            SaveCommitResult result = Redistribute();

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual(9, Vaccine.unusedStatPoints, "재분배가 무료가 아니다");
            Assert.IsEmpty(Vaccine.permanentInvestments);
        }

        [Test]
        public void TheRefundIsWhatWasPaidAndNotWhatItWouldCostNow()
        {
            // The costs are [초안] and will be retuned (D-011). This is a save written when a
            // step cost 7, which no row costs today: pricing the refund from the current table
            // would create points out of a patch, or destroy them.
            Vaccine.permanentInvestments.Add(
                new StatInvestmentState { stat = "maxHealth", steps = 2, pointsSpent = 14 });

            SaveCommitResult result = Redistribute();

            Assert.AreEqual(SaveCommitVerdict.Committed, result.Verdict, result.Detail);
            Assert.AreEqual(14, Vaccine.unusedStatPoints);
        }

        [Test]
        public void RedistributingWithNothingInvestedWritesNothing()
        {
            GivePoints(4);
            int revision = _store.Snapshot.snapshotRevision;

            Assert.AreEqual(SaveCommitVerdict.Rejected, Redistribute().Verdict);
            Assert.AreEqual(revision, _store.Snapshot.snapshotRevision, "a no-op wrote a revision");
        }

        [Test]
        public void RedistributingTwiceUnderTheSameNonceRefundsOnce()
        {
            GivePoints(4);
            Invest(InvestableStat.MaxHealth, 4);

            SaveCommitResult first = Redistribute("r");
            SaveCommitResult again = Redistribute("r");

            Assert.AreEqual(SaveCommitVerdict.Committed, first.Verdict);
            Assert.AreEqual(SaveCommitVerdict.AlreadyApplied, again.Verdict);
            Assert.AreEqual(4, Vaccine.unusedStatPoints, "the replay refunded a second time");
        }

        [Test]
        public void InvestingAgainAfterARedistributionStartsFromEmpty()
        {
            GivePoints(4);
            Invest(InvestableStat.MaxHealth, 4, "a");
            Redistribute();
            Invest(InvestableStat.AbilityPower, 4, "b");

            Assert.AreEqual(1, Vaccine.permanentInvestments.Count);
            Assert.AreEqual("abilityPower", Vaccine.permanentInvestments[0].stat);
            Assert.AreEqual(0, Vaccine.unusedStatPoints);
        }

        // --- what survives a reload ---------------------------------------------------------

        [Test]
        public void TheAllocationIsStillThereWhenTheSaveIsReadBack()
        {
            GivePoints(6);
            Invest(InvestableStat.MaxHealth, 3, "a");
            Invest(InvestableStat.Tenacity, 1, "b");

            var reopened = new SaveStore(_files, SaveSlotLayout.Under(Root));
            reopened.Open();

            Assert.AreEqual(SaveStoreState.Ready, reopened.State, reopened.HaltReason);
            System.Collections.Generic.List<VaccineStatInvestment> allocation =
                VaccineInvestments.Read(reopened.Snapshot.vaccine);

            Assert.AreEqual(2, allocation.Count);
            Assert.AreEqual(5, VaccineInvestments.PointsSpent(reopened.Snapshot.vaccine));
            Assert.AreEqual(1, reopened.Snapshot.vaccine.unusedStatPoints);
        }

        [Test]
        public void ARowThisBuildDoesNotKnowIsIgnoredRatherThanRefusingTheSave()
        {
            // A save written by a later build. Losing that row's effect here is right;
            // refusing to load the whole save over it is not.
            Vaccine.permanentInvestments.Add(
                new StatInvestmentState { stat = "somethingNew", steps = 1, pointsSpent = 1 });

            Assert.DoesNotThrow(() => _store.Snapshot.Validate());
            Assert.IsEmpty(VaccineInvestments.Read(Vaccine), "an unknown stat was applied anyway");
            Assert.AreEqual(1, VaccineInvestments.PointsSpent(Vaccine), "its points stopped counting");
        }

        // --- the document's own invariants ---------------------------------------------------

        [Test]
        public void ADocumentHoldingOneStatTwiceIsRefused()
        {
            Vaccine.permanentInvestments.Add(
                new StatInvestmentState { stat = "maxHealth", steps = 1, pointsSpent = 1 });
            Vaccine.permanentInvestments.Add(
                new StatInvestmentState { stat = "maxHealth", steps = 1, pointsSpent = 1 });

            Assert.Throws<System.InvalidOperationException>(() => _store.Snapshot.Validate());
        }

        [Test]
        public void ADocumentPayingFewerPointsThanStepsIsRefused()
        {
            // A step costs at least one point, so this is two numbers that disagree - and a
            // refund would then hand back less than was paid.
            Vaccine.permanentInvestments.Add(
                new StatInvestmentState { stat = "maxHealth", steps = 3, pointsSpent = 1 });

            Assert.Throws<System.InvalidOperationException>(() => _store.Snapshot.Validate());
        }

        [Test]
        public void ADocumentWithAnEmptyEntryIsRefused()
        {
            Vaccine.permanentInvestments.Add(
                new StatInvestmentState { stat = "maxHealth", steps = 0, pointsSpent = 0 });

            Assert.Throws<System.InvalidOperationException>(() => _store.Snapshot.Validate());
        }
    }
}
