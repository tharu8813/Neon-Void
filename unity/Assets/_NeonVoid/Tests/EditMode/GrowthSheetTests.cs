using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Progression;
using NeonVoid.UI;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// What the 백신 성장 터미널 shows, and in whose units.
    /// <para>
    /// [확정] docs/05 section 2 asks the 영구 스탯 screen for 보유 포인트, 구매 전후 값,
    /// 실제 방어감소율 and 무료 재분배, and forbids 저장 전 값 확정 표시. The hard part is the
    /// second one: three rows store their value in a different unit from the one the table
    /// increments in, so a preview computed as "current plus the increment" is wrong exactly
    /// where a player would check it.
    /// </para>
    /// </summary>
    public sealed class GrowthSheetTests
    {
        private static CombatStats Base()
        {
            CombatStats stats = CombatStats.StartingVaccine();
            return stats;
        }

        private static GrowthSheet Open(
            int unusedPoints = 50,
            IReadOnlyList<VaccineStatInvestment> allocation = null,
            bool canInvest = true,
            string refusal = "") =>
            GrowthSheet.Build(
                Base(),
                allocation ?? new VaccineStatInvestment[0],
                unusedPoints,
                pointsSpent: 0,
                canInvest: canInvest,
                refusal: refusal);

        private static GrowthSheetRow RowFor(in GrowthSheet sheet, InvestableStat stat)
        {
            for (int i = 0; i < sheet.Rows.Count; ++i)
            {
                if (sheet.Rows[i].Stat == stat)
                    return sheet.Rows[i];
            }

            Assert.Fail($"{stat} is not on the sheet");
            return default;
        }

        // --- the table ------------------------------------------------------------------------

        [Test]
        public void TheClosedSheetDrawsNothing()
        {
            Assert.IsFalse(GrowthSheet.Closed.Shown);
            Assert.IsEmpty(GrowthSheet.Closed.Rows);
        }

        [Test]
        public void EveryRowOfTheTableIsListedIncludingTheOnesNothingApplies()
        {
            // A table that hid the unconnected rows would look shorter than GDD v1 section 13.1.
            GrowthSheet sheet = Open();

            Assert.AreEqual(StatInvestments.All.Length, sheet.Rows.Count);
            Assert.IsTrue(sheet.Shown);
        }

        [Test]
        public void ARowNothingAppliesOffersNoPurchaseAndSaysWhy()
        {
            GrowthSheetRow speed = RowFor(Open(), InvestableStat.MoveSpeed);

            Assert.IsFalse(speed.Connected);
            Assert.IsFalse(speed.Affordable, "a point would be spent on nothing at all");
            Assert.IsFalse(speed.HasPreview, "a stat nothing reads was given a 구매 후 값");
            Assert.IsNotEmpty(speed.Unavailable);
        }

        // --- 구매 전후 값 ------------------------------------------------------------------

        [Test]
        public void AConnectedRowShowsWhatItIsAndWhatOneMorePointWouldMakeIt()
        {
            GrowthSheetRow ad = RowFor(Open(), InvestableStat.AttackDamage);

            Assert.IsTrue(ad.Connected);
            Assert.IsTrue(ad.Affordable);
            Assert.IsNotEmpty(ad.Current);
            Assert.IsTrue(ad.HasPreview);
            Assert.AreNotEqual(
                ad.Current, ad.Preview, "the 구매 후 값 is identical to the 구매 전 값");
        }

        [Test]
        public void ThePreviewIsNeverWrittenIntoTheCurrentValue()
        {
            // [확정] docs/05 section 2: 저장 전 값 확정 표시 금지. The current reading has to go
            // on saying what the save holds while the preview sits beside it.
            CombatStats uninvested = Base();
            GrowthSheet sheet = Open();

            for (int i = 0; i < sheet.Rows.Count; ++i)
            {
                GrowthSheetRow row = sheet.Rows[i];
                if (!row.Connected)
                    continue;

                string committed = StatInvestments.Format(
                    row.Stat, StatInvestments.ValueOf(uninvested, row.Stat));

                Assert.AreEqual(
                    committed, row.Current,
                    $"{row.Name}: the row shows a value the save has not taken");
            }
        }

        [Test]
        public void ThePreviewUsesTheRealApplicationAndNotTheTablesIncrement()
        {
            // 치명타 피해 increments in percentage points and is stored as a multiplier, so
            // "current + increment" would be off by a factor of a hundred.
            GrowthSheetRow crit = RowFor(Open(), InvestableStat.CritDamage);

            CombatStats after = StatInvestments.Apply(
                Base(), new[] { new VaccineStatInvestment(InvestableStat.CritDamage, 1, crit.Cost) });

            Assert.AreEqual(
                StatInvestments.Format(InvestableStat.CritDamage,
                    StatInvestments.ValueOf(after, InvestableStat.CritDamage)),
                crit.Preview);

            float increment = StatInvestments.For(InvestableStat.CritDamage).DraftIncrementPerStep;
            float naive = StatInvestments.ValueOf(Base(), InvestableStat.CritDamage) + increment;
            Assert.AreNotEqual(
                StatInvestments.Format(InvestableStat.CritDamage, naive), crit.Preview,
                "the preview added the table's increment straight onto a multiplier");
        }

        [Test]
        public void APenetrationRowStoredAsAFractionIsShownAsAPercentage()
        {
            // PhysicalPenPercent lives as 0..1 and reads as a percentage. A row that printed
            // the fraction would say 0.05 where the player expects 5%.
            GrowthSheetRow pen = RowFor(Open(), InvestableStat.PhysicalPenPercent);

            StringAssert.Contains("%", pen.Current);
            StringAssert.Contains("%", pen.Preview);
        }

        [Test]
        public void AnInvestedRowCountsItsStepsAndPreviewsTheNextOne()
        {
            var allocation = new[] { new VaccineStatInvestment(InvestableStat.AttackDamage, 3, 3) };
            GrowthSheetRow ad = RowFor(Open(allocation: allocation), InvestableStat.AttackDamage);

            CombatStats withThree = StatInvestments.Apply(Base(), allocation);
            CombatStats withFour = StatInvestments.Apply(
                Base(), new[] { new VaccineStatInvestment(InvestableStat.AttackDamage, 4, 4) });

            Assert.AreEqual(3, ad.Steps);
            Assert.AreEqual(
                StatInvestments.Format(
                    InvestableStat.AttackDamage,
                    StatInvestments.ValueOf(withThree, InvestableStat.AttackDamage)),
                ad.Current);
            Assert.AreEqual(
                StatInvestments.Format(
                    InvestableStat.AttackDamage,
                    StatInvestments.ValueOf(withFour, InvestableStat.AttackDamage)),
                ad.Preview);
        }

        // --- 실제 방어감소율 ------------------------------------------------------------------

        [Test]
        public void TheTwoResistanceRowsCarryTheirActualDamageReduction()
        {
            // docs/05 section 2 asks for it by name, because the curve is not linear: the raw
            // number alone does not say what a point bought.
            GrowthSheet sheet = Open();

            foreach (InvestableStat stat in
                     new[] { InvestableStat.PhysicalArmor, InvestableStat.MagicResistance })
            {
                GrowthSheetRow row = RowFor(sheet, stat);
                Assert.IsNotEmpty(row.CurrentReduction, $"{row.Name} has no 실제 방어감소율");
                Assert.IsNotEmpty(row.PreviewReduction, $"{row.Name} previews no 실제 방어감소율");
            }
        }

        [Test]
        public void NoOtherRowPretendsToHaveADamageReduction()
        {
            GrowthSheet sheet = Open();

            for (int i = 0; i < sheet.Rows.Count; ++i)
            {
                GrowthSheetRow row = sheet.Rows[i];
                if (StatInvestments.HasDamageReduction(row.Stat))
                    continue;

                Assert.IsEmpty(row.CurrentReduction, $"{row.Name} claims a 방어감소율");
            }
        }

        [Test]
        public void TheReductionFollowsTheCurveCombatUses()
        {
            // 100 armor halves incoming damage: 100 / (100 + 100).
            Assert.AreEqual(50f, StatInvestments.DamageReductionPercent(100f), 0.001f);
            Assert.AreEqual(0f, StatInvestments.DamageReductionPercent(0f), 0.001f);
            Assert.Less(
                StatInvestments.DamageReductionPercent(-100f), 0f,
                "shredded armor should amplify damage, not reduce it");
        }

        // --- what cannot be bought ---------------------------------------------------------

        [Test]
        public void ASaveThatCannotCommitOffersNoPurchaseAndSaysSo()
        {
            // Said rather than hidden: a terminal whose buttons silently did nothing would read
            // as broken, and "the save is not committing" is the actual reason.
            GrowthSheet sheet = Open(canInvest: false);

            Assert.IsFalse(sheet.CanInvest);
            Assert.IsNotEmpty(sheet.Unavailable);

            for (int i = 0; i < sheet.Rows.Count; ++i)
            {
                Assert.IsFalse(sheet.Rows[i].Affordable, $"{sheet.Rows[i].Name} offered a purchase");
                Assert.IsFalse(sheet.Rows[i].HasPreview, "a purchase that cannot happen was previewed");
            }
        }

        [Test]
        public void ARowTheVaccineCannotAffordIsNotOfferedEither()
        {
            GrowthSheet sheet = Open(unusedPoints: 0);

            for (int i = 0; i < sheet.Rows.Count; ++i)
                Assert.IsFalse(sheet.Rows[i].Affordable);
        }

        [Test]
        public void TheSheetCarriesTheLastRefusal()
        {
            Assert.AreEqual("이 수치를 읽는 곳이 아직 없다", Open(refusal: "이 수치를 읽는 곳이 아직 없다").Refusal);
        }
    }
}
