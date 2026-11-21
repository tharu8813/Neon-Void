using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Progression;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// GDD v1 section 13.1's table, reproduced here as a check on the implementation rather
    /// than as a second copy of it. Every number is [초안] - the document says so and D-011
    /// holds the balance open - so this is a doc-fidelity test: it fails when the code and the
    /// published table disagree, which is the only thing it can honestly say.
    /// </summary>
    public sealed class StatInvestmentTableTests
    {
        [Test]
        public void TheTableIsTheOneTheDocumentPublishes()
        {
            // 투자 능력치 | 1회 증가량 | 소비 포인트, row by row. The two 관통 rows each say
            // 선택한 종류, so each is two choices at the same price.
            Expect(InvestableStat.MaxHealth, 5f, 1);
            Expect(InvestableStat.AttackDamage, 0.25f, 1);
            Expect(InvestableStat.AbilityPower, 1f, 1);
            Expect(InvestableStat.PhysicalArmor, 2f, 1);
            Expect(InvestableStat.MagicResistance, 2f, 1);
            Expect(InvestableStat.MoveSpeed, 0.05f, 1);
            Expect(InvestableStat.AttacksPerSecond, 0.05f, 1);
            Expect(InvestableStat.ProjectileSpeed, 0.02f, 1);
            Expect(InvestableStat.CritChance, 0.5f, 1);
            Expect(InvestableStat.CritDamage, 1f, 1);
            Expect(InvestableStat.SkillHaste, 2f, 1);
            Expect(InvestableStat.Luck, 0.5f, 1);
            Expect(InvestableStat.MaxMana, 5f, 1);
            Expect(InvestableStat.ManaRegen, 0.25f, 1);
            Expect(InvestableStat.ProjectileSize, 1f, 2);
            Expect(InvestableStat.PhysicalPenFlat, 0.5f, 2);
            Expect(InvestableStat.MagicPenFlat, 0.5f, 2);
            Expect(InvestableStat.PhysicalPenPercent, 0.25f, 2);
            Expect(InvestableStat.MagicPenPercent, 0.25f, 2);
            Expect(InvestableStat.OnHitLifesteal, 0.25f, 2);
            Expect(InvestableStat.Omnivamp, 0.25f, 3);
            Expect(InvestableStat.Tenacity, 1f, 2);

            Assert.AreEqual(22, StatInvestments.All.Length, "the table gained or lost a row");
        }

        [Test]
        public void EveryStatInTheEnumIsInTheTable()
        {
            foreach (InvestableStat stat in Enum.GetValues(typeof(InvestableStat)))
                Assert.DoesNotThrow(() => StatInvestments.For(stat), stat.ToString());

            Assert.Throws<ArgumentOutOfRangeException>(() => StatInvestments.For((InvestableStat)99));
        }

        [Test]
        public void SaveKeysAreUniqueAndRoundTrip()
        {
            // A player's allocation is stored against these, so two rows sharing one would move
            // their points onto the wrong stat.
            var keys = new HashSet<string>();
            for (int i = 0; i < StatInvestments.All.Length; ++i)
            {
                StatInvestmentStep row = StatInvestments.All[i];
                Assert.IsNotEmpty(row.SaveKey, row.Stat.ToString());
                Assert.IsTrue(keys.Add(row.SaveKey), $"{row.SaveKey} is used twice");

                Assert.IsTrue(StatInvestments.TryParse(row.SaveKey, out InvestableStat parsed));
                Assert.AreEqual(row.Stat, parsed);
            }

            Assert.IsFalse(StatInvestments.TryParse("not-a-stat", out _));
            Assert.IsFalse(StatInvestments.TryParse(null, out _));
        }

        [Test]
        public void ARowNothingAppliesSaysSo()
        {
            // Taking a point for one of these would spend something permanent on nothing, so
            // the table carries the row and names what it is waiting for.
            for (int i = 0; i < StatInvestments.All.Length; ++i)
            {
                StatInvestmentStep row = StatInvestments.All[i];
                string why = StatInvestments.WhyNotConnected(row.Stat);

                if (row.IsConnected)
                    Assert.IsEmpty(why, $"{row.Stat} works and still explains itself");
                else
                    Assert.IsNotEmpty(why, $"{row.Stat} does nothing and does not say why");
            }
        }

        [Test]
        public void TheSevenRowsWithNoFieldInTheStatBlockAreTheOnesRefused()
        {
            // Named explicitly, so connecting one is a change to this list rather than a quiet
            // behaviour change nobody notices.
            var expected = new HashSet<InvestableStat>
            {
                InvestableStat.MoveSpeed,
                InvestableStat.AttacksPerSecond,
                InvestableStat.ProjectileSpeed,
                InvestableStat.Luck,
                InvestableStat.MaxMana,
                InvestableStat.ManaRegen,
                InvestableStat.ProjectileSize,
            };

            var actual = new HashSet<InvestableStat>();
            for (int i = 0; i < StatInvestments.All.Length; ++i)
            {
                if (!StatInvestments.All[i].IsConnected)
                    actual.Add(StatInvestments.All[i].Stat);
            }

            CollectionAssert.AreEquivalent(expected, actual);
        }

        private static void Expect(InvestableStat stat, float increment, int cost)
        {
            StatInvestmentStep row = StatInvestments.For(stat);
            Assert.AreEqual(increment, row.DraftIncrementPerStep, 1e-6f, $"{stat} increment");
            Assert.AreEqual(cost, row.DraftPointCost, $"{stat} cost");
        }
    }

    /// <summary>
    /// docs/02 section 2's second arrow: base → E 승인 영구 투자, 원본 기반 재계산.
    /// </summary>
    public sealed class StatInvestmentApplicationTests
    {
        private static readonly List<VaccineStatInvestment> Nothing = new List<VaccineStatInvestment>();

        [Test]
        public void NothingInvestedChangesNothing()
        {
            CombatStats start = CombatStats.StartingVaccine();
            CombatStats after = StatInvestments.Apply(start, Nothing);

            Assert.AreEqual(start.MaxHealth, after.MaxHealth);
            Assert.AreEqual(start.AttackDamage, after.AttackDamage);
            Assert.AreEqual(start.CritDamageMultiplier, after.CritDamageMultiplier);
        }

        [Test]
        public void OneStepIsOneIncrementAndTenStepsAreTen()
        {
            CombatStats start = CombatStats.StartingVaccine();

            Assert.AreEqual(
                start.MaxHealth + StatInvestments.DraftMaxHealthPerStep,
                StatInvestments.Apply(start, One(InvestableStat.MaxHealth, 1)).MaxHealth, 1e-4f);

            Assert.AreEqual(
                start.MaxHealth + (StatInvestments.DraftMaxHealthPerStep * 10f),
                StatInvestments.Apply(start, One(InvestableStat.MaxHealth, 10)).MaxHealth, 1e-4f);
        }

        [Test]
        public void ApplyingTwiceToTheSameBaseGivesTheSameAnswer()
        {
            // 원본 기반 재계산 (docs/02 section 2). Accumulating onto the live stats instead
            // would double every investment the second time anything asked for a refresh, and
            // the document names 구역 재진입 as where that would happen.
            CombatStats start = CombatStats.StartingVaccine();
            List<VaccineStatInvestment> allocation = One(InvestableStat.AttackDamage, 8);

            CombatStats first = StatInvestments.Apply(start, allocation);
            CombatStats second = StatInvestments.Apply(start, allocation);

            Assert.AreEqual(first.AttackDamage, second.AttackDamage, 1e-4f);
            Assert.AreEqual(start.AttackDamage + 2f, first.AttackDamage, 1e-4f);
        }

        [Test]
        public void PercentagePointsBecomeTheUnitsTheStatBlockUses()
        {
            // The two conversions where the table and the stat block disagree about units, and
            // where getting it wrong is a hundredfold error rather than a visible one.
            CombatStats start = CombatStats.StartingVaccine();

            // 치명타 피해 +1%p per step, held as a multiplier: 1.20 → 1.25 after five steps.
            Assert.AreEqual(
                1.25f, StatInvestments.Apply(start, One(InvestableStat.CritDamage, 5)).CritDamageMultiplier,
                1e-4f);

            // 비율 관통 +0.25%p per step, held as a 0..1 fraction: four steps is 1%p = 0.01.
            Assert.AreEqual(
                0.01f, StatInvestments.Apply(start, One(InvestableStat.PhysicalPenPercent, 4)).PhysicalPenPercent,
                1e-5f);

            // 치명타 확률 is already a percentage in the stat block, so it is not converted.
            Assert.AreEqual(
                start.CritChancePercent + 1f,
                StatInvestments.Apply(start, One(InvestableStat.CritChance, 2)).CritChancePercent, 1e-4f);
        }

        [Test]
        public void EveryConnectedStatActuallyMovesSomething()
        {
            // The switch and the IsConnected flag are two lists that have to agree. A row
            // marked connected whose case was never written would throw; one that silently did
            // nothing would take the player's point.
            CombatStats start = CombatStats.StartingVaccine();

            for (int i = 0; i < StatInvestments.All.Length; ++i)
            {
                StatInvestmentStep row = StatInvestments.All[i];
                if (!row.IsConnected)
                    continue;

                CombatStats after = StatInvestments.Apply(start, One(row.Stat, 20));
                Assert.AreNotEqual(
                    Describe(start), Describe(after), $"{row.Stat} is connected but changed nothing");
            }
        }

        [Test]
        public void ARowThisBuildCannotApplyIsSkippedRatherThanThrowing()
        {
            // A save written by a later build may hold a stat this one does not apply. Losing
            // that row's effect is right; refusing to load the save is not.
            CombatStats start = CombatStats.StartingVaccine();
            Assert.DoesNotThrow(() => StatInvestments.Apply(start, One(InvestableStat.Luck, 4)));
        }

        [Test]
        public void ZeroOrNegativeStepsDoNothing()
        {
            CombatStats start = CombatStats.StartingVaccine();
            Assert.AreEqual(
                start.MaxHealth, StatInvestments.Apply(start, One(InvestableStat.MaxHealth, 0)).MaxHealth);
            Assert.AreEqual(
                start.MaxHealth, StatInvestments.Apply(start, One(InvestableStat.MaxHealth, -3)).MaxHealth);
            Assert.AreEqual(start.MaxHealth, StatInvestments.Apply(start, null).MaxHealth);
        }

        private static List<VaccineStatInvestment> One(InvestableStat stat, int steps) =>
            new List<VaccineStatInvestment> { new VaccineStatInvestment(stat, steps, steps) };

        private static string Describe(in CombatStats s) =>
            $"{s.AttackDamage}|{s.AbilityPower}|{s.CritDamageMultiplier}|{s.CritChancePercent}|" +
            $"{s.PhysicalPenFlat}|{s.PhysicalPenPercent}|{s.MagicPenFlat}|{s.MagicPenPercent}|" +
            $"{s.PhysicalArmor}|{s.MagicResistance}|{s.MaxHealth}|{s.OnHitLifestealPercent}|" +
            $"{s.OmnivampPercent}|{s.SkillHaste}|{s.TenacityPercent}";
    }
}
