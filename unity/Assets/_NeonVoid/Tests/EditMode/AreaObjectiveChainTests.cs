using System;
using NeonVoid.Missions;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The order a <see cref="AreaObjectiveKind.Sequence"/> chain has to be walked in, and the
    /// clock SSD's chain runs against. All [초안]: docs/01 section 2 gives SSD
    /// 레일을 짧은 시간에 연결 and GPU 렌더 큐 정렬, and nothing else.
    /// </summary>
    public sealed class AreaObjectiveChainTests
    {
        private const int Window = 300;

        // --- order ---------------------------------------------------------------------------

        [Test]
        public void WalkingTheNodesInOrderCompletesTheChain()
        {
            var chain = new AreaObjectiveChain(3);

            Assert.AreEqual(AreaChainOffer.Accepted, chain.Offer(0));
            Assert.AreEqual(1, chain.Lit);
            Assert.IsTrue(chain.IsRunning);
            Assert.IsFalse(chain.IsComplete);

            Assert.AreEqual(AreaChainOffer.Accepted, chain.Offer(1));
            Assert.AreEqual(AreaChainOffer.Completed, chain.Offer(2));

            Assert.IsTrue(chain.IsComplete);
            Assert.IsFalse(chain.IsRunning);
            Assert.AreEqual(1f, chain.Progress);
        }

        [Test]
        public void ANodeOutOfOrderSendsTheChainBackToTheStart()
        {
            var chain = new AreaObjectiveChain(3);
            chain.Offer(0);

            Assert.AreEqual(AreaChainOffer.OutOfOrder, chain.Offer(2), "node 2 was taken before node 1");
            Assert.AreEqual(0, chain.Lit, "the chain kept what it had");
            Assert.IsFalse(chain.IsRunning);

            // And it can be walked again from the start.
            Assert.AreEqual(AreaChainOffer.Accepted, chain.Offer(0));
            Assert.AreEqual(AreaChainOffer.Accepted, chain.Offer(1));
            Assert.AreEqual(AreaChainOffer.Completed, chain.Offer(2));
        }

        [Test]
        public void TheFirstNodeOutOfOrderIsTheOneAfterNothing()
        {
            // Offering node 1 first is out of order; offering node 0 first never is.
            var chain = new AreaObjectiveChain(2);
            Assert.AreEqual(AreaChainOffer.OutOfOrder, chain.Offer(1));
            Assert.AreEqual(AreaChainOffer.Accepted, chain.Offer(0));
        }

        [Test]
        public void HoldingALitNodeAgainChangesNothing()
        {
            // A player standing on a node they already lit must not reset their own chain.
            var chain = new AreaObjectiveChain(3);
            chain.Offer(0);
            chain.Offer(1);

            Assert.AreEqual(AreaChainOffer.AlreadyLit, chain.Offer(0));
            Assert.AreEqual(AreaChainOffer.AlreadyLit, chain.Offer(1));
            Assert.AreEqual(2, chain.Lit, "re-holding a lit node moved the chain");

            Assert.AreEqual(AreaChainOffer.Completed, chain.Offer(2));
        }

        [Test]
        public void AFinishedChainIsFinished()
        {
            var chain = new AreaObjectiveChain(2);
            chain.Offer(0);
            chain.Offer(1);

            Assert.AreEqual(AreaChainOffer.AlreadyComplete, chain.Offer(0));
            Assert.AreEqual(AreaChainOffer.AlreadyComplete, chain.Offer(1));
            Assert.IsTrue(chain.IsComplete, "a finished chain came undone");
        }

        [Test]
        public void AChainOfOneNodeLandsOnItsOnlyNode()
        {
            // The hardware table never produces one, but the type is given a length and has to
            // behave for every length it accepts.
            var chain = new AreaObjectiveChain(1);
            Assert.AreEqual(AreaChainOffer.Completed, chain.Offer(0));
            Assert.IsTrue(chain.IsComplete);
        }

        [Test]
        public void ANodeThatIsNotInTheChainIsARefusal()
        {
            var chain = new AreaObjectiveChain(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => chain.Offer(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => chain.Offer(-1));
        }

        // --- the clock -----------------------------------------------------------------------

        [Test]
        public void AChainWithNoWindowNeverRunsOut()
        {
            var chain = new AreaObjectiveChain(2, AreaObjectiveChain.NoWindow);
            chain.Offer(0);

            Assert.IsFalse(chain.HasWindow);
            Assert.AreEqual(-1, chain.TicksLeft, "a chain with no clock reported one");

            for (int i = 0; i < 10_000; ++i)
                Assert.IsFalse(chain.Tick(), $"it expired on tick {i}");

            Assert.AreEqual(1, chain.Lit);
            Assert.AreEqual(AreaChainOffer.Completed, chain.Offer(1));
        }

        [Test]
        public void TheClockStartsOnTheFirstNodeAndNotBefore()
        {
            // docs/01 asks for a short connection, not a short area: a window that began when
            // the player entered would be a timer on the whole room.
            var chain = new AreaObjectiveChain(2, Window);

            Assert.AreEqual(-1, chain.TicksLeft, "the clock was running before anything was held");
            for (int i = 0; i < Window * 2; ++i)
                Assert.IsFalse(chain.Tick(), "an idle chain expired");

            chain.Offer(0);
            Assert.AreEqual(Window, chain.TicksLeft);
        }

        [Test]
        public void TheWindowExpiresOnItsLastTickAndResetsTheChain()
        {
            var chain = new AreaObjectiveChain(3, Window);
            chain.Offer(0);
            chain.Offer(1);

            for (int tick = 1; tick < Window; ++tick)
                Assert.IsFalse(chain.Tick(), $"it expired early, on tick {tick} of {Window}");

            Assert.IsTrue(chain.Tick(), $"it did not expire on tick {Window}");
            Assert.AreEqual(0, chain.Lit, "an expired chain kept its nodes");
            Assert.AreEqual(-1, chain.TicksLeft);

            // Once, not every tick afterwards: the runtime resets the nodes on a true, and doing
            // that sixty times a second would be a chain nobody could start again.
            Assert.IsFalse(chain.Tick(), "it expired twice");
        }

        [Test]
        public void AChainThatLandsInsideItsWindowStopsTheClock()
        {
            var chain = new AreaObjectiveChain(2, Window);
            chain.Offer(0);
            chain.Tick();

            Assert.AreEqual(AreaChainOffer.Completed, chain.Offer(1));
            Assert.AreEqual(-1, chain.TicksLeft, "the clock kept running on a finished chain");

            for (int i = 0; i < Window * 2; ++i)
                Assert.IsFalse(chain.Tick(), "a finished chain expired");

            Assert.IsTrue(chain.IsComplete, "a finished chain came undone on a tick");
        }

        [Test]
        public void ANodeOutOfOrderStopsTheClockToo()
        {
            var chain = new AreaObjectiveChain(3, Window);
            chain.Offer(0);
            chain.Tick();

            chain.Offer(2);

            Assert.AreEqual(-1, chain.TicksLeft, "the clock kept running after the chain broke");
            Assert.AreEqual(Window, Restarted(chain), "the second walk got less time than the first");
        }

        [Test]
        public void ResetEmptiesEverything()
        {
            var chain = new AreaObjectiveChain(3, Window);
            chain.Offer(0);
            chain.Tick();
            chain.Reset();

            Assert.AreEqual(0, chain.Lit);
            Assert.AreEqual(-1, chain.TicksLeft);
            Assert.IsFalse(chain.IsRunning);
            Assert.AreEqual(0f, chain.Progress);
        }

        [Test]
        public void AChainRefusesToBeBuiltWrong()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new AreaObjectiveChain(0), "a chain of no nodes would be complete before it began");
            Assert.Throws<ArgumentOutOfRangeException>(() => new AreaObjectiveChain(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AreaObjectiveChain(2, -1));
        }

        [Test]
        public void ProgressCountsNodesAndNotTicks()
        {
            var chain = new AreaObjectiveChain(4);
            Assert.AreEqual(0f, chain.Progress);

            chain.Offer(0);
            Assert.AreEqual(0.25f, chain.Progress, 0.0001f);

            chain.Offer(1);
            Assert.AreEqual(0.5f, chain.Progress, 0.0001f);
        }

        private static int Restarted(AreaObjectiveChain chain)
        {
            chain.Offer(0);
            return chain.TicksLeft;
        }
    }
}
