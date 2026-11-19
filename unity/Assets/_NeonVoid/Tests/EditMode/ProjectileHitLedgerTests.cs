using System;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Core;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/02 section 9 item 8: one projectile may not damage the same enemy twice, and the
    /// rule has to hold while a target disappears or a boss changes phase.
    /// </summary>
    public sealed class ProjectileHitLedgerTests
    {
        private const int EnemyA = 1;
        private const int EnemyB = 2;
        private const int Boss = 3;

        [Test]
        public void ARepeatedContactWithTheSameEnemyIsRefused()
        {
            // The physics case this exists for: a projectile overlapping a large collider
            // reports a trigger every step, so without the ledger the target takes damage
            // once per physics step and dies at a rate that depends on frame rate.
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 1);

            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(EnemyA));

            for (int step = 0; step < 10; ++step)
                Assert.AreEqual(ProjectileHitVerdict.AlreadyHit, ledger.TryRegisterHit(EnemyA),
                    "The projectile damaged the same enemy again on contact step " + step);

            Assert.AreEqual(1, ledger.DamagedCount);
        }

        [Test]
        public void APiercingProjectilePassesThroughSeveralDifferentEnemies()
        {
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 3);

            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(EnemyA));
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(EnemyB));
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(Boss));

            Assert.IsTrue(ledger.IsSpent);
            Assert.AreEqual(ProjectileHitVerdict.PierceExhausted, ledger.TryRegisterHit(4));
        }

        [Test]
        public void PierceBudgetAndRepeatRuleAreReportedAsDifferentReasons
            ()
        {
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 1);
            ledger.TryRegisterHit(EnemyA);

            // Same enemy: already hit, not "out of pierce". The distinction matters for the
            // despawn decision - a spent projectile should disappear, a re-contact should not
            // make one that still has budget disappear.
            Assert.AreEqual(ProjectileHitVerdict.AlreadyHit, ledger.TryRegisterHit(EnemyA));
            Assert.AreEqual(ProjectileHitVerdict.PierceExhausted, ledger.TryRegisterHit(EnemyB));
        }

        [Test]
        public void InfinitePierceKeepsGoingWithoutEverBecomingSpent()
        {
            var ledger = new ProjectileHitLedger(ProjectileHitLedger.UnlimitedTargets);

            for (int actorId = 1; actorId <= 500; ++actorId)
                Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(actorId));

            Assert.IsFalse(ledger.IsSpent);
            Assert.AreEqual(500, ledger.DamagedCount);
            Assert.AreEqual(ProjectileHitVerdict.AlreadyHit, ledger.TryRegisterHit(250),
                "Infinite pierce still must not hit one enemy twice.");
        }

        /// <summary>
        /// The boss phase case docs/02 section 9 item 8 names. A phase change swaps visuals,
        /// colliders and behaviour, but it is the same enemy, so the id does not change and the
        /// projectile gets no second free hit.
        /// </summary>
        [Test]
        public void ABossPhaseChangeDoesNotHandTheProjectileASecondHit()
        {
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 1);
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(Boss));

            // Phase 2 begins: new colliders, same actor.
            Assert.AreEqual(ProjectileHitVerdict.AlreadyHit, ledger.TryRegisterHit(Boss));
        }

        [Test]
        public void ATargetThatDisappearsLeavesTheLedgerUsableForTheRest()
        {
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 3);
            ledger.TryRegisterHit(EnemyA);

            // EnemyA despawns mid-flight; the projectile carries on.
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(EnemyB));
            Assert.AreEqual(2, ledger.DamagedCount);
        }

        [Test]
        public void AContactWithNoRealActorIsDiscardedAndCostsNoPierce()
        {
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 2);

            Assert.AreEqual(ProjectileHitVerdict.InvalidTarget, ledger.TryRegisterHit(ActorIdAllocator.NoActor));

            Assert.AreEqual(0, ledger.DamagedCount, "A wall or a null target consumed a pierce charge.");
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(EnemyA));
        }

        [Test]
        public void EvaluateDoesNotRecordAnything()
        {
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 1);

            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.Evaluate(EnemyA));
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.Evaluate(EnemyA));
            Assert.AreEqual(0, ledger.DamagedCount);
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(EnemyA));
        }

        [Test]
        public void AProjectileThatCouldDamageNobodyIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileHitLedger(0));
        }

        [Test]
        public void APooledProjectileStartsWithAnEmptyLedger()
        {
            var ledger = new ProjectileHitLedger(maxDistinctTargets: 2);
            ledger.TryRegisterHit(EnemyA);

            ledger.ResetForReuse();

            Assert.AreEqual(0, ledger.DamagedCount);
            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(EnemyA),
                "A recycled projectile inherited the previous shot's hit list.");
        }

        // --- the pooling hazard the ledger depends on ---------------------------------------

        [Test]
        public void ActorIdsAreNeverReusedSoARecycledEnemyIsNotMistakenForAnOldOne()
        {
            // Without this, a projectile in flight would decide it had "already hit" a freshly
            // spawned enemy that happened to take a dead one's id, and pass straight through.
            // It looks exactly like a missed collision and depends on pool timing.
            var allocator = new ActorIdAllocator();

            int first = allocator.Next();
            int second = allocator.Next();   // first enemy dies here
            int third = allocator.Next();    // its GameObject is recycled into this one

            Assert.AreNotEqual(first, third);
            Assert.AreNotEqual(second, third);
            Assert.AreEqual(3, allocator.IssuedCount);

            var ledger = new ProjectileHitLedger(maxDistinctTargets: 2);
            ledger.TryRegisterHit(first);

            Assert.AreEqual(ProjectileHitVerdict.Allowed, ledger.TryRegisterHit(third),
                "The recycled enemy was skipped because it inherited a dead actor's id.");
        }

        [Test]
        public void TheReservedNoActorValueIsNeverIssued()
        {
            var allocator = new ActorIdAllocator();

            for (int i = 0; i < 100; ++i)
                Assert.AreNotEqual(ActorIdAllocator.NoActor, allocator.Next());

            Assert.IsFalse(ActorIdAllocator.IsValid(ActorIdAllocator.NoActor));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActorIdAllocator(firstId: 0));
        }
    }
}
