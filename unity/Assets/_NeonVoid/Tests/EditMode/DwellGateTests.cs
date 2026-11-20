using System;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The 정화 콘솔 and 철수 패드's "stand here for a moment".
    /// <para>
    /// Small, and worth its own tests for one reason: the gate opens something that commits to
    /// disk. A gate that reported completion on every tick after the dwell would ask the save for
    /// a purification sixty times a second, and the store would answer AlreadyApplied every time
    /// - a loop that looks like it works.
    /// </para>
    /// </summary>
    public sealed class DwellGateTests
    {
        private static DwellGate Gate(float radius = 2f, int ticks = 3) => new DwellGate(radius, ticks);

        [Test]
        public void TheDwellCompletesOnTheTickThatMeetsTheCount()
        {
            DwellGate gate = Gate(ticks: 3);

            Assert.IsFalse(gate.Tick(0f), "it opened on the first tick");
            Assert.IsFalse(gate.Tick(0f));
            Assert.IsTrue(gate.Tick(0f), "it did not open on the third");
            Assert.IsTrue(gate.IsSatisfied);
        }

        [Test]
        public void ItOpensExactlyOnceWhileThePlayerStandsThere()
        {
            // This is the one that matters: each true is a save commit.
            DwellGate gate = Gate(ticks: 2);
            gate.Tick(0f);
            Assert.IsTrue(gate.Tick(0f));

            for (int i = 0; i < 100; ++i)
                Assert.IsFalse(gate.Tick(0f), $"it opened again on tick {i}");

            Assert.AreEqual(2, gate.TicksHeld, "the count kept climbing after the dwell was met");
        }

        [Test]
        public void SteppingOffEmptiesTheDwell()
        {
            // [초안]: resetting is the reading a player can see - the bar empties when you leave.
            DwellGate gate = Gate(radius: 2f, ticks: 3);
            gate.Tick(0f);
            gate.Tick(0f);
            Assert.AreEqual(2, gate.TicksHeld);

            Assert.IsFalse(gate.Tick(5f), "a tick outside the radius counted");
            Assert.AreEqual(0, gate.TicksHeld);
            Assert.IsFalse(gate.IsOccupied);

            Assert.IsFalse(gate.Tick(0f), "two ticks of credit survived leaving");
            Assert.IsFalse(gate.Tick(0f));
            Assert.IsTrue(gate.Tick(0f), "the full dwell did not open it again");
        }

        [Test]
        public void TheEdgeOfTheRadiusCounts()
        {
            DwellGate gate = Gate(radius: 2f, ticks: 1);

            Assert.IsTrue(gate.Tick(2f), "standing exactly on the edge was outside");
        }

        [Test]
        public void JustOutsideTheRadiusDoesNot()
        {
            DwellGate gate = Gate(radius: 2f, ticks: 1);

            Assert.IsFalse(gate.Tick(2.0001f));
            Assert.IsFalse(gate.IsOccupied);
        }

        [Test]
        public void AnUnknownDistanceIsOutside()
        {
            // The runtime hands over NaN when it has no player to measure - a scene still
            // loading, a player destroyed. Treating that as "inside" would open the gate on its
            // own with nobody there.
            DwellGate gate = Gate(ticks: 1);

            Assert.IsFalse(gate.Tick(float.NaN));
            Assert.IsFalse(gate.IsOccupied);
        }

        [Test]
        public void ResettingPutsTheGateBackToEmpty()
        {
            // Used when what the gate opened was refused, so the player dwells again rather than
            // the game retrying every tick.
            DwellGate gate = Gate(ticks: 2);
            gate.Tick(0f);
            gate.Tick(0f);
            Assert.IsTrue(gate.IsSatisfied);

            gate.Reset();

            Assert.AreEqual(0, gate.TicksHeld);
            Assert.IsFalse(gate.IsSatisfied);
            Assert.IsFalse(gate.IsOccupied);
            gate.Tick(0f);
            Assert.IsTrue(gate.Tick(0f), "the gate could not be re-earned after a reset");
        }

        [Test]
        public void ProgressRunsFromNoneToFullAndStops()
        {
            DwellGate gate = Gate(ticks: 4);
            Assert.AreEqual(0f, gate.Progress, 1e-6f);

            gate.Tick(0f);
            Assert.AreEqual(0.25f, gate.Progress, 1e-6f);

            gate.Tick(0f);
            gate.Tick(0f);
            gate.Tick(0f);
            Assert.AreEqual(1f, gate.Progress, 1e-6f);

            gate.Tick(0f);
            Assert.AreEqual(1f, gate.Progress, 1e-6f, "progress went past full");
        }

        [Test]
        public void AGateNobodyCouldOpenIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DwellGate(0f, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DwellGate(-1f, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DwellGate(float.NaN, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DwellGate(2f, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DwellGate(2f, -1));
        }
    }
}
