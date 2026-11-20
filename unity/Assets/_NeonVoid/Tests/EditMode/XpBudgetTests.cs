using System;
using NeonVoid.Progression;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The XP economy against the worked tables GDD v1 publishes for it.
    /// <para>
    /// Every number is [초안 v0.1] and D-011 owns the decision. What these tests hold is not that
    /// the numbers are right - nobody has approved them - but that <b>the code and the design
    /// document compute the same thing</b>. The GDD prints its own results: five rows of zone XP
    /// with the mission level each cumulative total reaches, a table of vaccine thresholds, the
    /// extraction reward for one to five purified areas, and the vaccine level after one, two,
    /// five and ten full clears. Reproducing those rows is a far stronger check than asserting
    /// the formulas back to themselves, because it catches a formula transcribed correctly and
    /// read wrongly.
    /// </para>
    /// </summary>
    public sealed class XpBudgetTests
    {
        // --- GDD v1 section 5.4: 구역 XP ---------------------------------------------------

        [Test]
        public void TheZoneXpBudgetReproducesTheDocumentsTable()
        {
            // 구역 XP = round(720 × (1 + 0.18 × 이미 정화한 하드웨어 수)).
            long[] published = { 720L, 850L, 979L, 1109L, 1238L };

            for (int alreadyPurified = 0; alreadyPurified < published.Length; ++alreadyPurified)
            {
                Assert.AreEqual(
                    published[alreadyPurified], MissionXpBudget.XpForArea(alreadyPurified),
                    $"the {alreadyPurified + 1}th area's budget");
            }
        }

        [Test]
        public void TheCumulativeZoneXpReachesTheMissionLevelsTheDocumentPrints()
        {
            // The other half of the same table: 누적 의뢰 XP 720/1,570/2,549/3,658/4,896 with
            // 기준 종료 레벨 5/8/11/14/16. This is the budget and the curve checked together,
            // which is where a rounding or an off-by-one in either would show.
            long[] publishedCumulative = { 720L, 1570L, 2549L, 3658L, 4896L };
            int[] publishedLevels = { 5, 8, 11, 14, 16 };

            LevelCurve mission = ProgressionCurves.DraftMissionLevels;
            long running = 0L;

            for (int area = 0; area < publishedCumulative.Length; ++area)
            {
                running += MissionXpBudget.XpForArea(area);

                Assert.AreEqual(publishedCumulative[area], running, $"cumulative after {area + 1} areas");
                Assert.AreEqual(
                    publishedLevels[area], mission.LevelFor(running), $"mission level after {area + 1} areas");
            }
        }

        [Test]
        public void ASixthPurifiedAreaIsRefusedRatherThanPaidFor()
        {
            // There are five hardware areas [확정]. A sixth purification means one was counted
            // twice, and scaling the reward for it would pay for the bug.
            Assert.Throws<ArgumentOutOfRangeException>(() => MissionXpBudget.XpForArea(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => MissionXpBudget.XpForArea(-1));
        }

        // --- GDD v1 section 5.6: 백신 영구 XP ----------------------------------------------

        [Test]
        public void TheVaccineCurveReproducesTheDocumentsThresholdTable()
        {
            // 다음 백신 레벨까지 필요한 XP = 180 + 90 × (L - 1), with the cumulative column the
            // GDD prints beside it.
            long[] nextLevelXp = { 180L, 270L, 360L, 450L, 540L, 630L, 720L, 810L, 900L, 990L };
            long[] cumulative = { 0L, 180L, 450L, 810L, 1260L, 1800L, 2430L, 3150L, 3960L, 4860L };

            LevelCurve vaccine = ProgressionCurves.DraftVaccineLevels;

            for (int i = 0; i < nextLevelXp.Length; ++i)
            {
                int level = i + 1;
                Assert.AreEqual(nextLevelXp[i], vaccine.XpToAdvanceFrom(level), $"next level XP at Lv.{level}");
                Assert.AreEqual(cumulative[i], vaccine.CumulativeXpForLevel(level), $"cumulative at Lv.{level}");
            }
        }

        [Test]
        public void TheExtractionRewardReproducesTheDocumentsTable()
        {
            // 정화 구역 수 → 성공 철수 시 백신 XP: 165 / 330 / 495 / 660 / 1,045. The last row is
            // where the 220 full-clear bonus appears, so a bonus paid at the wrong count or paid
            // twice shows up here.
            long[] published = { 165L, 330L, 495L, 660L, 1045L };

            for (int areas = 1; areas <= published.Length; ++areas)
            {
                Assert.AreEqual(
                    published[areas - 1], VaccineXpBudget.XpEarnedAtStandardObjectiveRate(areas),
                    $"{areas} purified areas");
            }
        }

        [Test]
        public void TheFullClearBonusIsPaidOnlyForTheWholeSetOfHardware()
        {
            // 5개 전부 정화 뒤 성공 철수 시 220XP 추가. Four areas must not see it.
            long four = VaccineXpBudget.XpEarnedAtStandardObjectiveRate(4);
            long five = VaccineXpBudget.XpEarnedAtStandardObjectiveRate(5);

            Assert.AreEqual(660L, four, "the bonus leaked into a partial clear");
            Assert.AreEqual(
                four + 165L + VaccineXpBudget.DraftFullClearBonusXp, five,
                "the fifth area is worth one area plus the bonus, and nothing else");
        }

        [Test]
        public void TheVaccineLevelAfterRepeatedFullClearsMatchesTheDocumentsExample()
        {
            // 전체 정화 1회/2회/5회/10회 누적 → Lv.4 / Lv.6 / Lv.10 / Lv.14, 파기 없이 모두
            // 성공한 가정. This is the budget, the curve and the level walk all at once, over ten
            // missions - the longest chain of this arithmetic the document checks itself.
            long perFullClear = VaccineXpBudget.XpEarnedAtStandardObjectiveRate(5);
            VaccineLevelPolicy policy = VaccineLevelPolicy.Draft;

            int level = 1;
            long total = 0L;
            int points = 0;
            var expected = new (int clears, int level)[] { (1, 4), (2, 6), (5, 10), (10, 14) };
            int next = 0;

            for (int clear = 1; clear <= 10; ++clear)
            {
                VaccineGrowth growth = policy.Grant(level, total, points, perFullClear);
                level = growth.Level;
                total = growth.TotalXp;
                points = growth.UnusedStatPoints;

                if (next < expected.Length && expected[next].clears == clear)
                {
                    Assert.AreEqual(expected[next].level, level, $"vaccine level after {clear} full clears");
                    ++next;
                }
            }

            Assert.AreEqual(expected.Length, next, "not every published row was checked");
            Assert.AreEqual(
                level - 1, points,
                "백신 레벨 1당 영구 포인트 1: thirteen levels gained should be thirteen points.");
        }

        [Test]
        public void ObjectivesAndPurificationsArePaidSeparately()
        {
            // 주요 목표 1개당 15XP, 하드웨어 정화 120XP. A mission that purified one area without
            // completing the standard three objectives is paid for what it did.
            Assert.AreEqual(120L, VaccineXpBudget.XpEarned(1, 0));
            Assert.AreEqual(135L, VaccineXpBudget.XpEarned(1, 1));
            Assert.AreEqual(165L, VaccineXpBudget.XpEarned(1, 3), "the standard rate's own row");
        }

        [Test]
        public void MoreAreasThanThereIsHardwareIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => VaccineXpBudget.XpEarned(6, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => VaccineXpBudget.XpEarned(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => VaccineXpBudget.XpEarned(1, -1));
        }

        [Test]
        public void APurificationIsWorthMoreToAMissionThanToTheVaccine()
        {
            // RunXP is 임시 카드 지급 and VaccineXP is the permanent score ([확정] docs/04
            // section 1), and the draft numbers make them differ by more than four times. If the
            // two ever came out equal it would most likely be because one was wired to the
            // other's budget.
            Assert.Greater(MissionXpBudget.XpForArea(0), VaccineXpBudget.XpEarned(1, 3) * 4L);
        }
    }
}
