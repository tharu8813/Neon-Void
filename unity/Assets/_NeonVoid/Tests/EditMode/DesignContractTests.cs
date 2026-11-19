using NeonVoid.Core;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    public sealed class DesignContractTests
    {
        [Test]
        public void ConstantRulesRemainIndependentOfPrototypeBalances()
        {
            Assert.AreEqual(60, DesignConstants.SimulationTicksPerSecond);
            Assert.AreEqual(5, DesignConstants.HardwareCount);
            Assert.AreEqual(3, DesignConstants.InitialCopies);
            Assert.AreEqual(3, DesignConstants.ActiveSkillSlots);
            Assert.AreEqual(3, DesignConstants.RewardCardSlots);
            Assert.AreEqual(2, DesignConstants.LearnablePluginsPerSuccessfulRun);
        }

        [Test]
        public void SeedProducesRepeatableSeries()
        {
            var left = new DeterministicRng(42);
            var right = new DeterministicRng(42);
            for (int i = 0; i < 1000; ++i)
                Assert.AreEqual(left.NextUInt(), right.NextUInt());
        }

        [Test]
        public void AoeAndPeriodicDoNotMultiplyVampReductionTwice()
        {
            // Demonstrates the confirmed rule, not a completed damage engine.
            const float damage = 100f;
            const float omnivamp = 0.10f;
            float expected = damage * omnivamp * DesignConstants.AreaOrPeriodicOmnivampEfficiency;
            Assert.AreEqual(3.3f, expected, 0.001f);
        }
    }
}
