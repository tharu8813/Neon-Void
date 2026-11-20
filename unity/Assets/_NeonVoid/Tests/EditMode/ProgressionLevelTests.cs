using System;
using NeonVoid.Progression;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The two level curves of docs/04 section 1, and what a grant of XP does to a vaccine.
    /// <para>
    /// The numbers are [초안] (D-011), so these tests assert the ones the document states and
    /// the <i>shape</i> of everything else - levels never fall, several levels can be gained at
    /// once, points follow levels. A retune should break the three tests that quote the document
    /// and no others; that is the point of writing them this way.
    /// </para>
    /// </summary>
    public sealed class ProgressionLevelTests
    {
        // --- the curve ---------------------------------------------------------------------

        [Test]
        public void TheMissionCurveIsTheOneTheDocumentStates()
        {
            // docs/04 section 1: 임무 레벨 100+30(L-1) XP.
            LevelCurve mission = ProgressionCurves.DraftMissionLevels;

            Assert.AreEqual(100L, mission.XpToAdvanceFrom(1), "level 1 to 2");
            Assert.AreEqual(130L, mission.XpToAdvanceFrom(2), "level 2 to 3");
            Assert.AreEqual(160L, mission.XpToAdvanceFrom(3), "level 3 to 4");
        }

        [Test]
        public void TheVaccineCurveIsTheOneTheDocumentStates()
        {
            // docs/04 section 1: 백신 레벨 180+90(L-1) XP.
            LevelCurve vaccine = ProgressionCurves.DraftVaccineLevels;

            Assert.AreEqual(180L, vaccine.XpToAdvanceFrom(1));
            Assert.AreEqual(270L, vaccine.XpToAdvanceFrom(2));
            Assert.AreEqual(360L, vaccine.XpToAdvanceFrom(3));
        }

        [Test]
        public void TheTwoCurvesAreNotTheSameCurve()
        {
            // [확정] C09 in docs/08: 백신 레벨과 의뢰 레벨을 한 변수로 혼합하지 않는다. The same XP
            // buying the same level on both would be the first symptom of them having been
            // merged somewhere.
            long xp = 500L;

            Assert.AreNotEqual(
                ProgressionCurves.DraftMissionLevels.LevelFor(xp),
                ProgressionCurves.DraftVaccineLevels.LevelFor(xp),
                "The mission and vaccine curves are indistinguishable, which is how C09 gets violated.");
        }

        [Test]
        public void LevelOneCostsNothingAndTheThresholdsAreTheRunningTotal()
        {
            LevelCurve mission = ProgressionCurves.DraftMissionLevels;

            Assert.AreEqual(0L, mission.CumulativeXpForLevel(1), "everybody starts at level 1");
            Assert.AreEqual(100L, mission.CumulativeXpForLevel(2));
            Assert.AreEqual(230L, mission.CumulativeXpForLevel(3), "100 + 130");
            Assert.AreEqual(390L, mission.CumulativeXpForLevel(4), "100 + 130 + 160");
        }

        [Test]
        public void OneClearedAreaIsWorthFourMissionLevelsOnTheDraftNumbers()
        {
            // GDD v1 section 5.4 publishes this row itself: 720 XP, 기준 종료 레벨 5. Asserting
            // it means the curve here and the worked example in the design document agree, and a
            // retune of either number shows up as a failure rather than in play.
            LevelCurve mission = ProgressionCurves.DraftMissionLevels;

            int reached = mission.LevelFor(MissionXpBudget.DraftFirstAreaXp);

            Assert.AreEqual(5, reached, "720 XP should carry a mission from Lv.1 to Lv.5");
            Assert.AreEqual(140L, mission.XpIntoLevel(reached, MissionXpBudget.DraftFirstAreaXp));
        }

        [Test]
        public void ALevelAlreadyReachedIsAFloorAndNotRecomputed()
        {
            // The curve may be retuned upward; an approved level is never taken back
            // ([확정] docs/04 section 2). So advancing from a level the total does not pay for
            // keeps the level rather than demoting.
            LevelCurve mission = ProgressionCurves.DraftMissionLevels;

            Assert.AreEqual(7, mission.AdvanceFrom(7, 0L));
            Assert.AreEqual(0L, mission.XpIntoLevel(7, 0L), "a progress bar must not go negative");
        }

        [Test]
        public void ACurveThatCouldNeverTerminateIsRefused()
        {
            // The level walk relies on every step costing something. A free step would loop to
            // the arithmetic ceiling on the first grant, so the refusal is in the constructor.
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelCurve(0L, 30L));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelCurve(-1L, 30L));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelCurve(100L, -1L));
        }

        [Test]
        public void ADefaultedCurveSaysSoRatherThanBehavingLikeAFreeOne()
        {
            Assert.IsFalse(default(LevelCurve).IsValid);
            Assert.IsTrue(ProgressionCurves.DraftMissionLevels.IsValid);
        }

        [Test]
        public void LevelsAndTotalsBelowTheirFloorsAreRefused()
        {
            LevelCurve mission = ProgressionCurves.DraftMissionLevels;

            Assert.Throws<ArgumentOutOfRangeException>(() => mission.XpToAdvanceFrom(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => mission.CumulativeXpForLevel(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => mission.AdvanceFrom(1, -1L));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => mission.CumulativeXpForLevel(LevelCurve.MaxSupportedLevel + 1));
        }

        [Test]
        public void TheWalkStopsAtTheArithmeticCeilingRatherThanOverflowing()
        {
            // A cheap curve and an enormous total is the shape a corrupt save would have. It must
            // come back, and it must not wrap into a negative threshold on the way.
            var cheap = new LevelCurve(1L, 0L);

            Assert.AreEqual(LevelCurve.MaxSupportedLevel, cheap.LevelFor(long.MaxValue / 4L));
        }

        // --- granting to a vaccine ---------------------------------------------------------

        [Test]
        public void OneLevelsWorthOfXpRaisesTheLevelAndPaysAPoint()
        {
            // docs/04 section 1: 백신 레벨 1당 영구 포인트 1.
            VaccineGrowth growth = VaccineLevelPolicy.Draft.Grant(
                level: 1, totalXp: 0L, unusedStatPoints: 0, gainedXp: 180L);

            Assert.AreEqual(2, growth.Level);
            Assert.AreEqual(180L, growth.TotalXp);
            Assert.AreEqual(1, growth.LevelsGained);
            Assert.AreEqual(1, growth.PointsGained);
            Assert.AreEqual(1, growth.UnusedStatPoints);
        }

        [Test]
        public void OneGrantCanCrossSeveralLevelsAtOnce()
        {
            // 180 + 270 = 450 is exactly level 3 on the draft vaccine curve. A grant that only
            // ever advanced one level would bank the rest and hand out a point short.
            VaccineGrowth growth = VaccineLevelPolicy.Draft.Grant(1, 0L, 0, 450L);

            Assert.AreEqual(3, growth.Level);
            Assert.AreEqual(2, growth.LevelsGained);
            Assert.AreEqual(2, growth.UnusedStatPoints);
        }

        [Test]
        public void XpJustShortOfTheNextThresholdDoesNotLevel()
        {
            VaccineGrowth growth = VaccineLevelPolicy.Draft.Grant(1, 0L, 0, 179L);

            Assert.AreEqual(1, growth.Level);
            Assert.AreEqual(179L, growth.TotalXp, "the XP is kept even though no level was bought");
            Assert.AreEqual(0, growth.UnusedStatPoints);
        }

        [Test]
        public void PointsAlreadyHeldAreAddedToAndNeverReplaced()
        {
            // [확정] docs/04 section 2: 이미 승인된 영구 XP/포인트는 빼앗지 않는다.
            VaccineGrowth growth = VaccineLevelPolicy.Draft.Grant(2, 180L, 5, 270L);

            Assert.AreEqual(3, growth.Level);
            Assert.AreEqual(450L, growth.TotalXp);
            Assert.AreEqual(6, growth.UnusedStatPoints, "the 5 points already held were lost");
        }

        [Test]
        public void GrantingNothingToAConsistentVaccineChangesNothing()
        {
            VaccineGrowth growth = VaccineLevelPolicy.Draft.Grant(2, 200L, 1, 0L);

            Assert.AreEqual(2, growth.Level);
            Assert.AreEqual(200L, growth.TotalXp);
            Assert.AreEqual(1, growth.UnusedStatPoints);
            Assert.AreEqual(0, growth.LevelsGained);
        }

        [Test]
        public void ALevelBehindACheaperCurveCatchesUpOnTheNextGrant()
        {
            // This is the retune path, and it is why Grant(0) is not defined as a no-op. A
            // vaccine holding 450 XP at level 1 under a curve where that buys level 3 is owed
            // two levels, and the next commit is where it gets them.
            var cheaper = new VaccineLevelPolicy(new LevelCurve(100L, 50L), pointsPerLevel: 1);

            VaccineGrowth growth = cheaper.Grant(1, 450L, 0, 0L);

            Assert.AreEqual(4, growth.Level, "100 + 150 + 200 = 450");
            Assert.AreEqual(3, growth.LevelsGained);
            Assert.AreEqual(3, growth.UnusedStatPoints);
        }

        [Test]
        public void ALevelAheadOfADearerCurveIsNotTakenAway()
        {
            var dearer = new VaccineLevelPolicy(new LevelCurve(10_000L, 1L), pointsPerLevel: 1);

            VaccineGrowth growth = dearer.Grant(9, 0L, 4, 180L);

            Assert.AreEqual(9, growth.Level, "a retune demoted an approved level");
            Assert.AreEqual(0, growth.LevelsGained);
            Assert.AreEqual(4, growth.UnusedStatPoints);
            Assert.AreEqual(180L, growth.TotalXp);
        }

        [Test]
        public void APolicyThatPaysNoPointsStillLevels()
        {
            // Points per level is a [초안] dial too, and zero has to mean "no points", not
            // "no levels" - the level itself gates other things.
            var noPoints = new VaccineLevelPolicy(ProgressionCurves.DraftVaccineLevels, pointsPerLevel: 0);

            VaccineGrowth growth = noPoints.Grant(1, 0L, 0, 180L);

            Assert.AreEqual(2, growth.Level);
            Assert.AreEqual(0, growth.UnusedStatPoints);
        }

        [Test]
        public void NegativeXpIsRefusedRatherThanSubtracted()
        {
            // A loss of permanent XP would be a different command with a different approval.
            Assert.Throws<ArgumentOutOfRangeException>(() => VaccineLevelPolicy.Draft.Grant(1, 100L, 0, -1L));
        }

        [Test]
        public void ADefaultedPolicyRefusesToGrantInsteadOfGrantingForFree()
        {
            Assert.IsFalse(default(VaccineLevelPolicy).IsValid);
            Assert.Throws<InvalidOperationException>(() => default(VaccineLevelPolicy).Grant(1, 0L, 0, 100L));
            Assert.Throws<ArgumentException>(
                () => new VaccineLevelPolicy(default, pointsPerLevel: 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new VaccineLevelPolicy(ProgressionCurves.DraftVaccineLevels, pointsPerLevel: -1));
        }
    }
}
