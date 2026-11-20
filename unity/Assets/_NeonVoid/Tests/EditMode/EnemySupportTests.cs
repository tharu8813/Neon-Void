using System;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Status;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// `enemy.support` from docs/01: 버프, answered by 우선 처치. The brain's only job here is
    /// deciding when help happens; who gets it and what it is belong to the agent and the
    /// archetype, which is the same split as attacking.
    /// </summary>
    public sealed class EnemySupportTests
    {
        private const int Player = 10;

        private static EnemyBrainConfig Config()
        {
            EnemyBrainConfig config = EnemyBrainConfig.Support();
            config.Validate();
            return config;
        }

        private static EnemyPerception See(float distance, bool stunned = false) =>
            new EnemyPerception(
                Vector2.zero,
                new[] { new PerceivedActor(Player, new Vector2(distance, 0f), true) },
                stunned: stunned);

        [Test]
        public void ItHelpsOnTheFirstTickItHasSomethingToFight()
        {
            // Waiting out a full interval before the first round would mean the player kills
            // the group's first member before the support has done anything at all.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(ref state, See(10f), config);

            Assert.IsTrue(intent.WantsSupport);
        }

        [Test]
        public void ItHelpsExactlyOnceEveryInterval()
        {
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            int rounds = 0;

            // Three intervals' worth of ticks should be three rounds of help.
            for (int i = 0; i < config.SupportIntervalTicks * 3; ++i)
                if (EnemyBrain.Decide(ref state, See(10f), config).WantsSupport)
                    rounds++;

            Assert.AreEqual(3, rounds, "Support did not arrive on its own interval.");
        }

        [Test]
        public void ItDoesNotHelpWhileStunned()
        {
            // 정지 denies every action, and handing out buffs is an action.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(ref state, See(10f, stunned: true), config);

            Assert.IsFalse(intent.WantsSupport);
        }

        [Test]
        public void ItDoesNotHelpWithNothingToFight()
        {
            // No target means no fight. A support buffing an empty room would burn its interval
            // and arrive on cooldown when the fight actually started.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, new EnemyPerception(Vector2.zero, Array.Empty<PerceivedActor>()), config);

            Assert.IsFalse(intent.HasTarget);
            Assert.IsFalse(intent.WantsSupport);
        }

        [Test]
        public void SupportAndAttackingAreIndependent()
        {
            // A support that had to choose between helping and hitting would spend a fight
            // doing neither, and 우선 처치 only means something while its help keeps arriving.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            // Close enough to attack as well as support.
            EnemyIntent intent = EnemyBrain.Decide(ref state, See(1.5f), config);

            Assert.IsTrue(intent.WantsSupport);
            Assert.IsTrue(intent.WantsAttack);
        }

        [Test]
        public void ItKeepsItsDistanceFurtherThanAnythingThatFights()
        {
            // The player has to go out of their way to reach it; otherwise it dies to whatever
            // they were already doing and there is no decision to make.
            Assert.Greater(
                EnemyBrainConfig.Support().PreferredRange, EnemyBrainConfig.Chaser().PreferredRange);
            Assert.Greater(
                EnemyBrainConfig.Support().PreferredRange, EnemyBrainConfig.Ranged().PreferredRange);
        }

        // --- the data refuses a pointless support ------------------------------------------

        [Test]
        public void ASupportWithNoIntervalIsRefused()
        {
            EnemyBrainConfig config = EnemyBrainConfig.Support();
            config.SupportIntervalTicks = 0;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void ASupportThatAlsoChargesIsRefused()
        {
            // Both behaviours would have to agree about which one owns a tick, and nothing in
            // docs/01 asks for that combination.
            EnemyBrainConfig config = EnemyBrainConfig.Support();
            config.UsesCharge = true;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void ASupportWithNoBuffNamedIsRefused()
        {
            // Killing it first has to change something.
            EnemyArchetype archetype = EnemyArchetypeCatalog.Support();
            archetype.SupportStatus = StatusEffectId.None;

            Assert.Throws<InvalidOperationException>(() => archetype.Validate());
        }

        [Test]
        public void ASupportWhoseBuffLapsesBetweenRoundsIsRefused()
        {
            // The failure it prevents is quiet: the group is unbuffed for most of the fight, so
            // killing the support changes little and the archetype looks merely weak.
            EnemyArchetype archetype = EnemyArchetypeCatalog.Support();
            archetype.Brain.SupportIntervalTicks =
                StatusCatalog.Draft()[archetype.SupportStatus].BaseDurationTicks + 1;

            Assert.Throws<InvalidOperationException>(() => archetype.Validate());
        }

        [Test]
        public void ASupportWithNoReachIsRefused()
        {
            EnemyArchetype archetype = EnemyArchetypeCatalog.Support();
            archetype.SupportRadiusUnits = 0f;

            Assert.Throws<InvalidOperationException>(() => archetype.Validate());
        }

        [Test]
        public void TheSupportArchetypeValidatesAndIsNearlyHarmlessAlone()
        {
            // Its danger is entirely what it does to the others.
            EnemyArchetype support = EnemyArchetypeCatalog.Support();
            Assert.DoesNotThrow(() => support.Validate());

            Assert.AreEqual("enemy.support", support.DocumentId);
            Assert.Less(support.AdRatio, EnemyArchetypeCatalog.Chaser().AdRatio);
            Assert.Less(support.MaxHealth, EnemyArchetypeCatalog.Chaser().MaxHealth);
            Assert.AreEqual(StatusEffectId.Empower, support.SupportStatus);
        }

        [Test]
        public void TheOtherArchetypesDoNotSupportAnybody()
        {
            Assert.IsFalse(EnemyBrainConfig.Chaser().UsesSupport);
            Assert.IsFalse(EnemyBrainConfig.Ranged().UsesSupport);
            Assert.IsFalse(EnemyBrainConfig.Charger().UsesSupport);
            Assert.AreEqual(StatusEffectId.None, EnemyArchetypeCatalog.Chaser().SupportStatus);
        }
    }
}
