using System;
using System.Collections.Generic;
using NeonVoid.Combat.AI;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// `enemy.charger` from docs/01: 경고 뒤 돌진, whose implementation memo is
    /// 경고 프레임과 판정 동기 and whose learning element is 회피 타이밍.
    /// <para>
    /// Every test here is about one of the three properties that make the dodge real, and each
    /// one would be easy to lose in a refactor: the wind-up is readable and harmless, the dash
    /// direction is locked once committed, and a charge connects at most once.
    /// </para>
    /// </summary>
    public sealed class EnemyChargerTests
    {
        private const int Player = 10;

        private static EnemyBrainConfig Config()
        {
            EnemyBrainConfig config = EnemyBrainConfig.Charger();
            config.Validate();
            return config;
        }

        private static PerceivedActor At(float x, float z) =>
            new PerceivedActor(Player, new Vector2(x, z), true);

        private static EnemyPerception See(PerceivedActor actor) =>
            new EnemyPerception(Vector2.zero, new[] { actor });

        private static void Run(
            ref EnemyBrainState state,
            in EnemyBrainConfig config,
            float distance,
            int ticks,
            List<EnemyIntent> log = null)
        {
            for (int i = 0; i < ticks; ++i)
            {
                EnemyIntent intent = EnemyBrain.Decide(ref state, See(At(distance, 0f)), config);
                log?.Add(intent);
            }
        }

        // --- the timer ---------------------------------------------------------------------

        [Test]
        public void EachPhaseLastsExactlyAsLongAsItsConfigSays()
        {
            // The warning the player reacts to and the hit that follows are the same timer, so
            // the count has to be exact in both directions: one tick short gives the dash a
            // free frame, one long steals part of the dodge window.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            var log = new List<EnemyIntent>();

            Run(ref state, config, 5f,
                config.ChargeTelegraphTicks + config.ChargeDurationTicks + config.ChargeRecoverTicks, log);

            Assert.AreEqual(
                config.ChargeTelegraphTicks,
                log.FindAll(i => i.ChargePhase == EnemyChargePhase.Telegraph).Count, "wind-up length");
            Assert.AreEqual(
                config.ChargeDurationTicks,
                log.FindAll(i => i.ChargePhase == EnemyChargePhase.Dashing).Count, "dash length");
            Assert.AreEqual(
                config.ChargeRecoverTicks,
                log.FindAll(i => i.ChargePhase == EnemyChargePhase.Recover).Count, "recovery length");
        }

        [Test]
        public void ThePhasesRunInOrderWithoutGaps()
        {
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            var log = new List<EnemyIntent>();

            Run(ref state, config, 5f,
                config.ChargeTelegraphTicks + config.ChargeDurationTicks + config.ChargeRecoverTicks, log);

            for (int i = 0; i < config.ChargeTelegraphTicks; ++i)
                Assert.AreEqual(EnemyChargePhase.Telegraph, log[i].ChargePhase, $"tick {i}");

            for (int i = 0; i < config.ChargeDurationTicks; ++i)
                Assert.AreEqual(
                    EnemyChargePhase.Dashing, log[config.ChargeTelegraphTicks + i].ChargePhase,
                    $"dash tick {i}");

            for (int i = 0; i < config.ChargeRecoverTicks; ++i)
                Assert.AreEqual(
                    EnemyChargePhase.Recover,
                    log[config.ChargeTelegraphTicks + config.ChargeDurationTicks + i].ChargePhase,
                    $"recovery tick {i}");
        }

        // --- the dodge window --------------------------------------------------------------

        [Test]
        public void TheWindUpNeitherMovesNorAttacks()
        {
            // The dodge window only exists if the enemy is readable and harmless during it.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            for (int i = 0; i < config.ChargeTelegraphTicks; ++i)
            {
                EnemyIntent intent = EnemyBrain.Decide(ref state, See(At(2f, 0f)), config);

                Assert.AreEqual(EnemyChargePhase.Telegraph, intent.ChargePhase, $"tick {i}");
                Assert.AreEqual(Vector2.zero, intent.MoveXZ, $"It moved on wind-up tick {i}.");
                Assert.IsFalse(intent.WantsAttack, $"It attacked during its own warning, tick {i}.");
            }
        }

        [Test]
        public void TheDashDirectionIsLockedWhenTheWindUpEnds()
        {
            // The property that makes dodging work at all. An enemy that kept aiming during its
            // dash would follow the player out of the way and the warning would be a lie.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            for (int i = 0; i < config.ChargeTelegraphTicks; ++i)
                EnemyBrain.Decide(ref state, See(At(4f, 0f)), config);

            // The player dodges hard sideways. The dash must keep going the way it committed.
            EnemyIntent first = EnemyBrain.Decide(
                ref state, new EnemyPerception(Vector2.zero, new[] { At(0f, 4f) }), config);

            Assert.AreEqual(EnemyChargePhase.Dashing, first.ChargePhase);
            Assert.AreEqual(1f, first.MoveXZ.normalized.x, 0.01f, "The dash turned to follow the dodge.");
            Assert.AreEqual(0f, first.MoveXZ.normalized.y, 0.01f);
        }

        [Test]
        public void TheFacingIsStillTrackedDuringTheWindUp()
        {
            // Locked at the END of the wind-up, not the start: the player should be able to
            // read where it is about to go right up to the moment it commits.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            EnemyBrain.Decide(ref state, See(At(4f, 0f)), config);
            EnemyIntent later = EnemyBrain.Decide(
                ref state, new EnemyPerception(Vector2.zero, new[] { At(0f, 4f) }), config);

            Assert.AreEqual(EnemyChargePhase.Telegraph, later.ChargePhase);
            Assert.AreEqual(1f, later.FaceXZ.normalized.y, 0.01f, "It kept facing where the player was.");
        }

        [Test]
        public void TheDashIsFasterThanWalking()
        {
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            Run(ref state, config, 5f, config.ChargeTelegraphTicks);

            EnemyIntent dash = EnemyBrain.Decide(ref state, See(At(5f, 0f)), config);

            Assert.AreEqual(EnemyChargePhase.Dashing, dash.ChargePhase);
            Assert.Greater(dash.SpeedMultiplier, 1f);
        }

        // --- the hit -----------------------------------------------------------------------

        [Test]
        public void ADashConnectsAtMostOnce()
        {
            // Otherwise brushing past is one hit per tick of contact.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            var log = new List<EnemyIntent>();

            Run(ref state, config, 1f, config.ChargeTelegraphTicks + config.ChargeDurationTicks, log);

            Assert.AreEqual(1, log.FindAll(i => i.WantsAttack).Count,
                "A single charge landed more than one hit.");
        }

        [Test]
        public void TheHitHappensDuringTheDashAndNeverBefore()
        {
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            var log = new List<EnemyIntent>();

            Run(ref state, config, 1f, config.ChargeTelegraphTicks + config.ChargeDurationTicks, log);

            for (int i = 0; i < log.Count; ++i)
                if (log[i].WantsAttack)
                    Assert.AreEqual(EnemyChargePhase.Dashing, log[i].ChargePhase,
                        $"A hit landed outside the dash, on tick {i}.");
        }

        [Test]
        public void ADashThatNeverReachesTheTargetLandsNothing()
        {
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            var log = new List<EnemyIntent>();

            // Inside the trigger range, never inside attack range.
            Run(ref state, config, config.ChargeTriggerRange - 0.1f,
                config.ChargeTelegraphTicks + config.ChargeDurationTicks, log);

            Assert.IsFalse(state.ChargeHasHit, "A charge that never reached the target still hit it.");
            Assert.IsEmpty(log.FindAll(i => i.WantsAttack));
        }

        // --- pacing and interruption -------------------------------------------------------

        [Test]
        public void ItWaitsOutItsCooldownBeforeWindingUpAgain()
        {
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            Run(ref state, config, 2f,
                config.ChargeTelegraphTicks + config.ChargeDurationTicks + config.ChargeRecoverTicks);

            EnemyIntent next = EnemyBrain.Decide(ref state, See(At(2f, 0f)), config);

            Assert.AreEqual(EnemyChargePhase.None, next.ChargePhase,
                "It started a second charge with no pause between them.");
        }

        [Test]
        public void ItDoesNotWindUpFromBeyondItsTriggerRange()
        {
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(At(config.ChargeTriggerRange + 1f, 0f)), config);

            Assert.AreEqual(EnemyChargePhase.None, intent.ChargePhase);
        }

        [Test]
        public void AStunCancelsAChargeRatherThanFreezingIt()
        {
            // A dash resumed after a stun would continue along a direction the player watched it
            // commit to seconds earlier, with no warning left to read.
            EnemyBrainConfig config = Config();
            var state = EnemyBrainState.Fresh();
            EnemyBrain.Decide(ref state, See(At(3f, 0f)), config);
            Assert.AreEqual(EnemyChargePhase.Telegraph, state.ChargePhase);

            EnemyBrain.Decide(
                ref state,
                new EnemyPerception(Vector2.zero, new[] { At(3f, 0f) }, stunned: true),
                config);

            Assert.AreEqual(EnemyChargePhase.None, state.ChargePhase);
        }

        // --- the data refuses an unfair charge ---------------------------------------------

        [Test]
        public void AChargeWithNoWarningIsRefused()
        {
            // 회피 타이밍 is what docs/01 says this archetype teaches. A charge with no wind-up
            // teaches nothing, and arrives as a balance complaint rather than as a bug report.
            EnemyBrainConfig config = EnemyBrainConfig.Charger();
            config.ChargeTelegraphTicks = 0;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void AChargeSlowerThanWalkingIsRefused()
        {
            EnemyBrainConfig config = EnemyBrainConfig.Charger();
            config.ChargeSpeedMultiplier = 1f;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void AChargeTriggerBeyondAggroIsRefused()
        {
            // It cannot charge what it has not noticed.
            EnemyBrainConfig config = EnemyBrainConfig.Charger();
            config.ChargeTriggerRange = config.AggroRadius + 1f;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void TheChargerArchetypeValidatesAndHitsHarderThanAChaser()
        {
            // It announces itself and can be dodged, so it has to be worth dodging; a charge
            // that hurt no more than a touch would just be a worse chaser.
            Assert.DoesNotThrow(() => EnemyArchetypeCatalog.Charger().Validate());
            Assert.AreEqual("enemy.charger", EnemyArchetypeCatalog.Charger().DocumentId);
            Assert.Greater(
                EnemyArchetypeCatalog.Charger().AdRatio, EnemyArchetypeCatalog.Chaser().AdRatio);
        }

        [Test]
        public void ANonChargingArchetypeIgnoresTheChargeFieldsEntirely()
        {
            // The chaser and the ranged archetype leave every charge field at zero, which would
            // fail the charge validation if it applied to them.
            Assert.DoesNotThrow(() => EnemyBrainConfig.Chaser().Validate());
            Assert.DoesNotThrow(() => EnemyBrainConfig.Ranged().Validate());
            Assert.IsFalse(EnemyBrainConfig.Chaser().UsesCharge);
            Assert.AreEqual(0, EnemyBrainConfig.Chaser().ChargeTelegraphTicks);
        }
    }
}
