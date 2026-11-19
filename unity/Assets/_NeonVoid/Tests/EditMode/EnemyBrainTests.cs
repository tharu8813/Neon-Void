using System;
using NeonVoid.Combat.AI;
using NUnit.Framework;
using UnityEngine;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/02 section 8: the brain selects targets, moves and forms attack intent, and never
    /// touches health. The last part is structural - EnemyIntent has no field for it - so these
    /// tests cover the parts that can go wrong: target hysteresis, the approach band, the
    /// control effects, and determinism.
    /// </summary>
    public sealed class EnemyBrainTests
    {
        private const int Player = 10;
        private const int OtherPlayer = 11;

        private static EnemyBrainConfig Config()
        {
            var config = EnemyBrainConfig.Chaser();
            config.Validate();
            return config;
        }

        private static EnemyPerception See(
            Vector2 self, params PerceivedActor[] candidates) =>
            new EnemyPerception(self, candidates);

        private static PerceivedActor Alive(int id, float x, float z) =>
            new PerceivedActor(id, new Vector2(x, z), true);

        // --- targeting ---------------------------------------------------------------------

        [Test]
        public void NoCandidateInAggroRangeMeansIdle()
        {
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 100f, 0f)), Config());

            Assert.IsFalse(intent.HasTarget);
            Assert.IsFalse(intent.WantsAttack);
            Assert.AreEqual(Vector2.zero, intent.MoveXZ);
        }

        [Test]
        public void TheNearestLivingCandidateInsideAggroIsChosen()
        {
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state,
                See(Vector2.zero, Alive(Player, 9f, 0f), Alive(OtherPlayer, 4f, 0f)),
                Config());

            Assert.AreEqual(OtherPlayer, intent.TargetActorId);
        }

        [Test]
        public void ADeadCandidateIsNotChosen()
        {
            var state = EnemyBrainState.Fresh();
            var corpse = new PerceivedActor(OtherPlayer, new Vector2(1f, 0f), false);

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, corpse, Alive(Player, 5f, 0f)), Config());

            Assert.AreEqual(Player, intent.TargetActorId);
        }

        [Test]
        public void TheCurrentTargetIsKeptBetweenAggroAndLeashInsteadOfFlickering()
        {
            // The failure this prevents: with leash equal to aggro, an enemy standing just
            // outside aggro drops and re-acquires on alternating ticks and never commits.
            var config = Config();     // aggro 12, leash 18
            var state = EnemyBrainState.Fresh();

            EnemyBrain.Decide(ref state, See(Vector2.zero, Alive(Player, 10f, 0f)), config);
            Assert.AreEqual(Player, state.TargetActorId);

            // Now at 15: beyond aggro, inside leash.
            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 15f, 0f)), config);

            Assert.AreEqual(Player, intent.TargetActorId, "The enemy gave up while still inside leash range.");
        }

        [Test]
        public void TheTargetIsDroppedBeyondLeashRange()
        {
            var config = Config();
            var state = EnemyBrainState.Fresh();
            EnemyBrain.Decide(ref state, See(Vector2.zero, Alive(Player, 10f, 0f)), config);

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 25f, 0f)), config);

            Assert.IsFalse(intent.HasTarget);
        }

        [Test]
        public void ATargetThatDiesIsReleased()
        {
            var config = Config();
            var state = EnemyBrainState.Fresh();
            EnemyBrain.Decide(ref state, See(Vector2.zero, Alive(Player, 3f, 0f)), config);

            EnemyIntent intent = EnemyBrain.Decide(
                ref state,
                See(Vector2.zero, new PerceivedActor(Player, new Vector2(3f, 0f), false)),
                config);

            Assert.IsFalse(intent.HasTarget);
        }

        [Test]
        public void EquallyDistantCandidatesAreBrokenByIdSoAReplayAgrees()
        {
            var state = EnemyBrainState.Fresh();
            var forward = See(Vector2.zero, Alive(OtherPlayer, 5f, 0f), Alive(Player, -5f, 0f));

            var stateReversed = EnemyBrainState.Fresh();
            var reversed = See(Vector2.zero, Alive(Player, -5f, 0f), Alive(OtherPlayer, 5f, 0f));

            Assert.AreEqual(
                EnemyBrain.Decide(ref state, forward, Config()).TargetActorId,
                EnemyBrain.Decide(ref stateReversed, reversed, Config()).TargetActorId,
                "Array order decided the target, so two runs could chase different players.");
        }

        // --- approach ----------------------------------------------------------------------

        [Test]
        public void ItClosesInWhenFurtherThanItsPreferredRange()
        {
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 8f, 0f)), Config());

            Assert.Greater(intent.MoveXZ.x, 0f, "It did not advance toward a distant target.");
        }

        [Test]
        public void ItBacksOffWhenTooClose()
        {
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 0.5f, 0f)), Config());

            Assert.Less(intent.MoveXZ.x, 0f, "It did not retreat from a target inside its range.");
        }

        /// <summary>
        /// Without the tolerance band an enemy steps back and forth across its preferred range
        /// forever. It reads as broken AI and never shows up in a single-tick assertion.
        /// </summary>
        [Test]
        public void ItHoldsStillInsideTheToleranceBandInsteadOfOscillating()
        {
            var config = Config();     // preferred 1.8, tolerance 0.4
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 1.8f, 0f)), config);

            Assert.AreEqual(Vector2.zero, intent.MoveXZ);

            // And it stays still over many ticks at that distance.
            for (int tick = 0; tick < 20; ++tick)
            {
                intent = EnemyBrain.Decide(ref state, See(Vector2.zero, Alive(Player, 1.95f, 0f)), config);
                Assert.AreEqual(Vector2.zero, intent.MoveXZ, "The enemy jittered at tick " + tick);
            }
        }

        [Test]
        public void ItFacesItsTargetEvenWhileHoldingPosition()
        {
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 0f, 1.8f)), Config());

            Assert.AreEqual(Vector2.zero, intent.MoveXZ);
            Assert.AreEqual(1f, intent.FaceXZ.y, 1e-4f);
        }

        // --- attacking ----------------------------------------------------------------------

        [Test]
        public void ItAttacksInRangeAndThenWaitsOutItsInterval()
        {
            var config = Config();     // interval 60, range 2.4
            var state = EnemyBrainState.Fresh();
            EnemyPerception inRange = See(Vector2.zero, Alive(Player, 2f, 0f));

            Assert.IsTrue(EnemyBrain.Decide(ref state, inRange, config).WantsAttack);

            for (int tick = 1; tick < config.AttackIntervalTicks; ++tick)
                Assert.IsFalse(EnemyBrain.Decide(ref state, inRange, config).WantsAttack,
                    "A second attack slipped through on tick " + tick);

            Assert.IsTrue(EnemyBrain.Decide(ref state, inRange, config).WantsAttack,
                "The enemy never attacked again.");
        }

        [Test]
        public void ItDoesNotAttackOutOfRange()
        {
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 8f, 0f)), Config());

            Assert.IsFalse(intent.WantsAttack);
            Assert.IsTrue(intent.HasTarget, "It should still be chasing.");
        }

        // --- control effects ------------------------------------------------------------------

        [Test]
        public void StunDeniesBothMovementAndAttack()
        {
            var state = EnemyBrainState.Fresh();
            var stunned = new EnemyPerception(
                Vector2.zero, new[] { Alive(Player, 2f, 0f) }, stunned: true);

            EnemyIntent intent = EnemyBrain.Decide(ref state, stunned, Config());

            Assert.AreEqual(Vector2.zero, intent.MoveXZ);
            Assert.IsFalse(intent.WantsAttack);
        }

        [Test]
        public void RootDeniesMovementButNotAttacking()
        {
            // docs/02 section 5 separates these: a rooted enemy must still be dangerous.
            var state = EnemyBrainState.Fresh();
            var rooted = new EnemyPerception(
                Vector2.zero, new[] { Alive(Player, 2f, 0f) }, rooted: true);

            EnemyIntent intent = EnemyBrain.Decide(ref state, rooted, Config());

            Assert.AreEqual(Vector2.zero, intent.MoveXZ);
            Assert.IsTrue(intent.WantsAttack, "A rooted enemy stopped attacking as well.");
        }

        [Test]
        public void RootDoesNotStopItChasingOnceTheRootEnds()
        {
            var config = Config();
            var state = EnemyBrainState.Fresh();
            EnemyBrain.Decide(
                ref state,
                new EnemyPerception(Vector2.zero, new[] { Alive(Player, 8f, 0f) }, rooted: true),
                config);

            EnemyIntent afterRoot = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 8f, 0f)), config);

            Assert.Greater(afterRoot.MoveXZ.x, 0f);
        }

        [Test]
        public void SlowIsReportedAsASpeedMultiplierRatherThanDenyingMovement()
        {
            var state = EnemyBrainState.Fresh();
            var slowed = new EnemyPerception(
                Vector2.zero, new[] { Alive(Player, 8f, 0f) }, speedMultiplier: 0.4f);

            EnemyIntent intent = EnemyBrain.Decide(ref state, slowed, Config());

            Assert.AreEqual(0.4f, intent.SpeedMultiplier, 1e-4f);
            Assert.Greater(intent.MoveXZ.x, 0f, "A slow stopped the enemy entirely instead of slowing it.");
        }

        [Test]
        public void TheAttackCooldownKeepsRunningWhileStunnedSoNoAttackIsBanked()
        {
            var config = Config();
            var state = EnemyBrainState.Fresh();
            EnemyPerception inRange = See(Vector2.zero, Alive(Player, 2f, 0f));
            var stunned = new EnemyPerception(Vector2.zero, new[] { Alive(Player, 2f, 0f) }, stunned: true);

            EnemyBrain.Decide(ref state, inRange, config);          // attacks, cooldown set

            // Stunned for the whole interval. The cooldown must run down underneath it, so
            // the enemy emerges ready rather than with an attack banked for later.
            for (int tick = 1; tick <= config.AttackIntervalTicks; ++tick)
            {
                EnemyIntent whileStunned = EnemyBrain.Decide(ref state, stunned, config);
                Assert.IsFalse(whileStunned.WantsAttack, "A stunned enemy attacked on tick " + tick);
            }

            Assert.AreEqual(0, state.AttackCooldownTicks,
                "The cooldown froze during the stun, so the enemy would hold a banked attack.");
            Assert.IsTrue(EnemyBrain.Decide(ref state, inRange, config).WantsAttack,
                "It should be ready the moment the stun ends, not later.");
        }

        // --- determinism and validation ----------------------------------------------------------

        [Test]
        public void TheSamePerceptionSequenceAlwaysProducesTheSameDecisions()
        {
            string Run()
            {
                var state = EnemyBrainState.Fresh();
                var log = new System.Text.StringBuilder();
                for (int tick = 0; tick < 200; ++tick)
                {
                    float x = 10f - tick * 0.05f;
                    EnemyIntent intent = EnemyBrain.Decide(
                        ref state, See(Vector2.zero, Alive(Player, x, 0f)), Config());
                    log.Append(intent.WantsAttack ? 'A' : intent.MoveXZ == Vector2.zero ? '.' : 'M');
                }
                return log.ToString();
            }

            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void ALeashThatDoesNotExceedAggroIsRejected()
        {
            var config = EnemyBrainConfig.Chaser();
            config.LeashRadius = config.AggroRadius;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void AZeroToleranceBandIsRejected()
        {
            var config = EnemyBrainConfig.Chaser();
            config.RangeToleranceUnits = 0f;

            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }
    }
}
