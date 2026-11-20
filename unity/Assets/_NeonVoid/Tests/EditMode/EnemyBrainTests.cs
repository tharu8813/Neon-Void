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
        public void ATargetThatDiesIsReleasedRatherThanAttackedWhereItFell()
        {
            // Acquired at 3 units, which is outside attack range, so no attack has been spent
            // and the cooldown is still clear. The corpse is then at 1.5, inside attack range:
            // a brain that kept hold of a dead target would hit it on this very tick.
            var config = Config();     // attack range 2.4, preferred range 1.8
            var state = EnemyBrainState.Fresh();
            EnemyBrain.Decide(ref state, See(Vector2.zero, Alive(Player, 3f, 0f)), config);

            EnemyIntent intent = EnemyBrain.Decide(
                ref state,
                See(Vector2.zero, new PerceivedActor(Player, new Vector2(1.5f, 0f), false)),
                config);

            Assert.IsFalse(intent.HasTarget);
            Assert.IsFalse(intent.WantsAttack, "The enemy kept attacking a dead target.");
            Assert.AreEqual(Vector2.zero, intent.MoveXZ, "The enemy kept manoeuvring around a corpse.");
        }


        // --- line of sight -----------------------------------------------------------------

        [Test]
        public void ARangedEnemyWithNoLineOfSightDoesNotShoot()
        {
            // docs/01 lists LOS as what `enemy.ranged` must implement. Without this an enemy
            // shoots the player through a wall, which makes cover decorative.
            EnemyBrainConfig config = EnemyBrainConfig.Ranged();
            config.Validate();
            var state = EnemyBrainState.Fresh();
            var hidden = new PerceivedActor(Player, new Vector2(8f, 0f), true, hasLineOfSight: false);

            EnemyIntent intent = EnemyBrain.Decide(ref state, See(Vector2.zero, hidden), config);

            Assert.IsTrue(intent.HasTarget, "It should still know where the player went.");
            Assert.IsFalse(intent.WantsAttack, "It fired through cover.");
        }

        [Test]
        public void ARangedEnemyWithNoLineOfSightClosesInToGetOne()
        {
            // Standing behind cover forever is not a tactic. Moving to regain a line is how
            // peeking out emerges without a special case for it.
            EnemyBrainConfig config = EnemyBrainConfig.Ranged();
            var state = EnemyBrainState.Fresh();

            // Inside the band, where a visible target would make it hold position.
            var hidden = new PerceivedActor(Player, new Vector2(8f, 0f), true, hasLineOfSight: false);
            EnemyIntent blocked = EnemyBrain.Decide(ref state, See(Vector2.zero, hidden), config);

            var stateVisible = EnemyBrainState.Fresh();
            EnemyIntent clear = EnemyBrain.Decide(
                ref stateVisible, See(Vector2.zero, Alive(Player, 8f, 0f)), config);

            Assert.Greater(blocked.MoveXZ.magnitude, 0.5f, "A blocked enemy stood still.");
            Assert.AreEqual(Vector2.zero, clear.MoveXZ, "A clear line inside the band means hold.");
        }

        [Test]
        public void ARangedEnemyWithALineFiresFromItsPreferredRange()
        {
            EnemyBrainConfig config = EnemyBrainConfig.Ranged();
            var state = EnemyBrainState.Fresh();

            EnemyIntent intent = EnemyBrain.Decide(
                ref state, See(Vector2.zero, Alive(Player, 8f, 0f)), config);

            Assert.IsTrue(intent.WantsAttack, $"It held its fire at {config.PreferredRange} units.");
        }

        [Test]
        public void AContactArchetypeIgnoresLineOfSightEntirely()
        {
            // A chaser is already limited by having to reach its target; casting a ray for it
            // would be waste, and the config says so rather than the agent guessing.
            EnemyBrainConfig config = Config();     // chaser
            Assert.IsFalse(config.RequiresLineOfSight);

            var state = EnemyBrainState.Fresh();
            var hidden = new PerceivedActor(Player, new Vector2(1f, 0f), true, hasLineOfSight: false);

            EnemyIntent intent = EnemyBrain.Decide(ref state, See(Vector2.zero, hidden), config);

            Assert.IsTrue(intent.WantsAttack, "A contact attacker was blocked by a line it does not need.");
        }

        // --- archetypes as data ------------------------------------------------------------

        [Test]
        public void BothImplementedArchetypesValidate()
        {
            Assert.DoesNotThrow(() => EnemyArchetypeCatalog.Chaser().Validate());
            Assert.DoesNotThrow(() => EnemyArchetypeCatalog.Ranged().Validate());
            Assert.AreEqual("enemy.chaser", EnemyArchetypeCatalog.Chaser().DocumentId);
            Assert.AreEqual("enemy.ranged", EnemyArchetypeCatalog.Ranged().DocumentId);
        }

        [Test]
        public void AFiringArchetypeMustRequireLineOfSight()
        {
            EnemyArchetype cheating = EnemyArchetypeCatalog.Ranged();
            cheating.Brain.RequiresLineOfSight = false;

            Assert.Throws<InvalidOperationException>(() => cheating.Validate());
        }

        [Test]
        public void AProjectileThatExpiresBeforeItsOwnAttackRangeIsRefused()
        {
            // The failure it prevents is quiet: the enemy fires, the shot dies in mid-air, and
            // the archetype looks like it is missing on purpose.
            EnemyArchetype shortLived = EnemyArchetypeCatalog.Ranged();
            shortLived.ProjectileLifetimeTicks = 10;    // ~1.8 units at 11 units/second

            Assert.Throws<InvalidOperationException>(() => shortLived.Validate());
        }

        [Test]
        public void TheRangedArchetypeIsFrailerThanTheChaser()
        {
            // Not balance for its own sake: a thing that hits from 8 units away and is as tough
            // as a thing that must walk up to you is simply the better enemy, and the arena
            // would have no reason to contain the other one.
            EnemyArchetype chaser = EnemyArchetypeCatalog.Chaser();
            EnemyArchetype ranged = EnemyArchetypeCatalog.Ranged();

            Assert.Less(ranged.MaxHealth, chaser.MaxHealth);
            Assert.Greater(ranged.Brain.PreferredRange, chaser.Brain.PreferredRange);
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
