using System;
using NeonVoid.Core;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    public sealed class SimulationClockTests
    {
        [Test]
        public void TickDeltaIsExactlyOneSixtiethAndNotAProjectSetting()
        {
            Assert.AreEqual(60, DesignConstants.SimulationTicksPerSecond);
            Assert.AreEqual(1.0 / 60.0, SimulationClock.TickDeltaSeconds, 0.0);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void OneSecondOfWallTimeYieldsSixtyTicksAtAnyFrameRate(int framesPerSecond)
        {
            var clock = new SimulationClock(maxTicksPerAdvance: 64);
            double frameDelta = 1.0 / framesPerSecond;

            int ticks = 0;
            for (int frame = 0; frame < framesPerSecond; ++frame)
                ticks += clock.Advance(frameDelta);

            // Wall time -> tick conversion carries sub-tick accumulator error, so one second
            // may land one tick either side. What must not drift is the simulation itself,
            // which only ever sees whole ticks. Covered by the motion determinism test.
            Assert.That(ticks, Is.InRange(59, 60), $"{framesPerSecond} fps produced {ticks} ticks in one second.");
            Assert.AreEqual(ticks, clock.TickIndex);
            Assert.AreEqual(0, clock.DroppedTicks);
        }

        [Test]
        public void CatchUpIsBoundedAndTheSkippedTimeIsReportedNotHidden()
        {
            var clock = new SimulationClock(maxTicksPerAdvance: 4);

            // A two second stall: 120 ticks of debt against a 4 tick budget.
            int ticks = clock.Advance(2.0);

            Assert.AreEqual(4, ticks, "The clock must not try to run two seconds of simulation in one frame.");
            Assert.AreEqual(4, clock.TickIndex);
            Assert.Greater(clock.DroppedTicks, 0, "Skipped time has to be observable, not silently discarded.");
            Assert.AreEqual(116, clock.DroppedTicks);
        }

        [Test]
        public void AccumulatorCarriesRemainderBetweenFramesInsteadOfLosingIt()
        {
            var clock = new SimulationClock();
            // Half a tick is exact in binary floating point, so this isolates the carry
            // behaviour from accumulator rounding.
            double halfATick = SimulationClock.TickDeltaSeconds * 0.5;

            Assert.AreEqual(0, clock.Advance(halfATick), "Half a tick is not a tick yet.");
            Assert.AreEqual(1, clock.Advance(halfATick), "The carried half tick was lost.");
            Assert.AreEqual(0, clock.Advance(halfATick));
            Assert.AreEqual(1, clock.Advance(halfATick));
            Assert.AreEqual(2, clock.TickIndex);
        }

        [Test]
        public void TheAccumulatorDoesNotDriftOverAnExtendedRun()
        {
            // Rounding can cost a tick at a boundary, which is why the per-second test allows
            // 59 or 60. What must never happen is that the error compounds: ten seconds of
            // awkwardly sized frames must still be ten seconds of simulation, not nine.
            var clock = new SimulationClock(maxTicksPerAdvance: 64);
            double frameDelta = SimulationClock.TickDeltaSeconds * (2.0 / 3.0);
            int frames = (int)(10.0 / frameDelta);

            int ticks = 0;
            for (int frame = 0; frame < frames; ++frame)
                ticks += clock.Advance(frameDelta);

            Assert.That(ticks, Is.InRange(599, 600), "Ten seconds produced " + ticks + " ticks.");
            Assert.AreEqual(0, clock.DroppedTicks);
        }

        [Test]
        public void InterpolationAlphaStaysWithinOneTick()
        {
            var clock = new SimulationClock();
            clock.Advance(SimulationClock.TickDeltaSeconds * 1.5);

            Assert.That(clock.InterpolationAlpha, Is.InRange(0.0, 1.0));
        }

        [Test]
        public void NonFiniteOrNegativeDeltasAreRejectedBeforeTheyCorruptTheAccumulator()
        {
            var clock = new SimulationClock();

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-0.016));
            Assert.AreEqual(0, clock.TickIndex, "A rejected frame must not advance the simulation.");
        }

        [Test]
        public void AClockThatCouldNeverTickIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationClock(maxTicksPerAdvance: 0));
        }
    }
}
