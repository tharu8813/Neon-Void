using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/02 section 9 item 4: a legal A-&gt;B-&gt;C chain must pass, only an A-&gt;B-&gt;A
    /// cycle is blocked, and independent routes keep firing.
    /// <para>
    /// The over-blocking failure is the dangerous one here. A guard that simply refused any
    /// repeated effect id would pass a naive "no infinite loop" check while quietly deleting
    /// chain lightning from the game, and nothing in a damage number would show it.
    /// </para>
    /// </summary>
    public sealed class ProcChainTests
    {
        private const int ChainLightning = 1;
        private const int Explosion = 2;
        private const int DamageOverTime = 3;
        private const int Dismantle = 4;

        private const int EnemyA = 10;
        private const int EnemyB = 11;
        private const int EnemyC = 12;

        private static ProcChainGuard Guard(int maxDepth = ProcChainGuard.DefaultMaxChainDepth)
        {
            var guard = new ProcChainGuard(maxDepth);
            guard.Register(ProcEffectRule.SelfChaining(ChainLightning, maxOccurrences: 3));
            guard.Register(ProcEffectRule.SingleUse(Explosion));
            guard.Register(ProcEffectRule.SingleUse(DamageOverTime));
            return guard;
        }

        // --- the legal chain --------------------------------------------------------------

        [Test]
        public void LegalChain_HitThenChainLightningThenExplosionThenDotAllResolve()
        {
            var guard = Guard();
            var queue = new ProcQueue(guard);
            var resolvedEffects = new List<int>();

            queue.TryEnqueue(ChainLightning, sourceActorId: 1, targetActorId: EnemyA, ProcChainPath.Root, tick: 0);

            ProcDrainReport report = queue.Drain((evt, q) =>
            {
                resolvedEffects.Add(evt.EffectId);
                switch (evt.EffectId)
                {
                    case ChainLightning:
                        q.TryEnqueue(Explosion, 1, evt.TargetActorId, evt.ChildPath, evt.Tick);
                        break;
                    case Explosion:
                        q.TryEnqueue(DamageOverTime, 1, evt.TargetActorId, evt.ChildPath, evt.Tick);
                        break;
                }
            });

            CollectionAssert.AreEqual(
                new[] { ChainLightning, Explosion, DamageOverTime },
                resolvedEffects,
                "A legal chain was cut short.");
            Assert.AreEqual(3, report.Resolved);
            Assert.AreEqual(0, report.TotalBlocked);
            Assert.IsTrue(report.CompletedWithinBudget);
        }

        [Test]
        public void LegalChain_ChainLightningHopsToNewEnemies()
        {
            var guard = Guard();
            var queue = new ProcQueue(guard);
            var struck = new List<int>();
            var hopOrder = new Queue<int>(new[] { EnemyB, EnemyC });

            queue.TryEnqueue(ChainLightning, 1, EnemyA, ProcChainPath.Root, 0);

            ProcDrainReport report = queue.Drain((evt, q) =>
            {
                struck.Add(evt.TargetActorId);
                if (evt.EffectId == ChainLightning && hopOrder.Count > 0)
                    q.TryEnqueue(ChainLightning, 1, hopOrder.Dequeue(), evt.ChildPath, evt.Tick);
            });

            CollectionAssert.AreEqual(new[] { EnemyA, EnemyB, EnemyC }, struck,
                "Chain lightning was blocked from hopping to new targets.");
            Assert.AreEqual(0, report.BlockedByCycle);
        }

        // --- the cycle --------------------------------------------------------------------

        [Test]
        public void Cycle_AThenBThenAIsRefused()
        {
            var guard = Guard();
            var path = ProcChainPath.Root
                .Append(Explosion, EnemyA)          // A fired
                .Append(DamageOverTime, EnemyA);    // B fired

            ProcDecision decision = guard.Evaluate(Explosion, EnemyA, path, tick: 0);

            Assert.AreEqual(ProcVerdict.BlockedByCycle, decision.Verdict);
            Assert.IsFalse(decision.IsBudgetRefusal, "A rule refusal must not be reported as a budget failure.");
        }

        [Test]
        public void Cycle_IsRefusedEvenWhenTheEffectComesBackOnADifferentTarget()
        {
            var guard = Guard();
            var path = ProcChainPath.Root
                .Append(Explosion, EnemyA)
                .Append(DamageOverTime, EnemyB);

            Assert.AreEqual(
                ProcVerdict.BlockedByCycle,
                guard.Evaluate(Explosion, EnemyC, path, 0).Verdict,
                "A single-use effect re-entered its own chain by changing target.");
        }

        [Test]
        public void Cycle_SelfChainingEffectStillCannotBounceBackOntoATargetItAlreadyStruck()
        {
            var guard = Guard();
            var path = ProcChainPath.Root
                .Append(ChainLightning, EnemyA)
                .Append(ChainLightning, EnemyB);

            Assert.AreEqual(
                ProcVerdict.BlockedByRepeatOnSameTarget,
                guard.Evaluate(ChainLightning, EnemyA, path, 0).Verdict);

            Assert.AreEqual(
                ProcVerdict.Allowed,
                guard.Evaluate(ChainLightning, EnemyC, path, 0).Verdict,
                "Hopping onward to an untouched enemy must still be legal.");
        }

        [Test]
        public void Cycle_AnInfiniteSelfTriggerTerminatesInsteadOfHangingTheTick()
        {
            var guard = Guard();
            var queue = new ProcQueue(guard, maxEventsPerDrain: 1000);
            int resolved = 0;

            queue.TryEnqueue(Explosion, 1, EnemyA, ProcChainPath.Root, 0);

            // An effect that tries to retrigger itself forever.
            ProcDrainReport report = queue.Drain((evt, q) =>
            {
                resolved++;
                q.TryEnqueue(Explosion, 1, evt.TargetActorId, evt.ChildPath, evt.Tick);
            });

            Assert.AreEqual(1, resolved, "The self-trigger was not stopped.");
            Assert.AreEqual(1, report.BlockedByCycle);
            Assert.IsFalse(report.BudgetExhausted, "Termination came from the budget, not the cycle rule.");
        }

        // --- independence -----------------------------------------------------------------

        [Test]
        public void IndependentBranchesFromOneHitDoNotBlockEachOther()
        {
            var guard = Guard();
            var queue = new ProcQueue(guard);
            var resolved = new List<string>();

            // One hit spawns two separate branches, each of which triggers the same follow-up.
            queue.TryEnqueue(Explosion, 1, EnemyA, ProcChainPath.Root, 0);
            queue.TryEnqueue(ChainLightning, 1, EnemyB, ProcChainPath.Root, 0);

            ProcDrainReport report = queue.Drain((evt, q) =>
            {
                resolved.Add($"{evt.EffectId}@{evt.TargetActorId}");
                if (evt.EffectId != DamageOverTime)
                    q.TryEnqueue(DamageOverTime, 1, evt.TargetActorId, evt.ChildPath, evt.Tick);
            });

            CollectionAssert.AreEqual(
                new[] { "2@10", "1@11", "3@10", "3@11" },
                resolved,
                "A sibling branch made the other look like a cycle.");
            Assert.AreEqual(0, report.TotalBlocked);
        }

        [Test]
        public void AFreshAttackStartsANewChainSoTheSameEffectFiresAgain()
        {
            var guard = Guard();
            var queue = new ProcQueue(guard);

            Assert.IsTrue(queue.TryEnqueue(Explosion, 1, EnemyA, ProcChainPath.Root, 0).IsAllowed);
            queue.Drain((e, q) => { });

            Assert.IsTrue(
                queue.TryEnqueue(Explosion, 1, EnemyA, ProcChainPath.Root, 1).IsAllowed,
                "The next attack must not inherit the previous attack's ancestry.");
        }

        [Test]
        public void AppendingToAPathLeavesTheParentPathUntouched()
        {
            var parent = ProcChainPath.Root.Append(Explosion, EnemyA);
            ProcChainPath child = parent.Append(DamageOverTime, EnemyB);

            Assert.AreEqual(1, parent.Depth, "Append mutated the shared parent path.");
            Assert.AreEqual(2, child.Depth);
            Assert.IsFalse(parent.ContainsEffect(DamageOverTime));
        }

        // --- limits and budgets -------------------------------------------------------------

        [Test]
        public void SelfChainingStopsAtItsOccurrenceBound()
        {
            var guard = Guard();   // chain lightning max 3
            var path = ProcChainPath.Root
                .Append(ChainLightning, EnemyA)
                .Append(ChainLightning, EnemyB)
                .Append(ChainLightning, EnemyC);

            Assert.AreEqual(
                ProcVerdict.BlockedByOccurrenceLimit,
                guard.Evaluate(ChainLightning, 13, path, 0).Verdict);
        }

        [Test]
        public void DepthBudgetRefusalIsReportedAsAResourceFailureNotAsARuleOutcome()
        {
            var guard = new ProcChainGuard(maxChainDepth: 2);
            ProcChainPath path = ProcChainPath.Root
                .Append(Explosion, EnemyA)
                .Append(DamageOverTime, EnemyB);

            ProcDecision decision = guard.Evaluate(Dismantle, EnemyC, path, 0);

            Assert.AreEqual(ProcVerdict.BlockedByDepthBudget, decision.Verdict);
            Assert.IsTrue(decision.IsBudgetRefusal,
                "The simulation gave up on a rule it wanted to apply; that has to be distinguishable.");
            Assert.IsNotEmpty(decision.ChainPath, "A budget refusal must carry the chain that caused it.");
        }

        [Test]
        public void EventBudgetExhaustionKeepsTheAbandonedWorkVisible()
        {
            var guard = new ProcChainGuard();
            // Everything is self-chaining with a high bound, so the budget is what stops it.
            guard.Register(ProcEffectRule.SelfChaining(ChainLightning, maxOccurrences: 10_000));
            var queue = new ProcQueue(guard, maxEventsPerDrain: 4);

            queue.TryEnqueue(ChainLightning, 1, EnemyA, ProcChainPath.Root, 0);

            int nextTarget = 100;
            ProcDrainReport report = queue.Drain((evt, q) =>
            {
                // Two follow-ups per event, so the queue outgrows the budget.
                q.TryEnqueue(ChainLightning, 1, nextTarget++, evt.ChildPath, evt.Tick);
                q.TryEnqueue(ChainLightning, 1, nextTarget++, evt.ChildPath, evt.Tick);
            });

            Assert.AreEqual(4, report.Resolved);
            Assert.IsTrue(report.BudgetExhausted);
            Assert.IsNotEmpty(report.Abandoned, "Abandoned events were discarded silently.");
            Assert.IsFalse(report.CompletedWithinBudget);
        }

        [Test]
        public void InternalCooldownStopsRepeatsAcrossSeparateChainsWhichNoPathCheckCanSee()
        {
            var guard = new ProcChainGuard();
            guard.Register(new ProcEffectRule
            {
                EffectId = Dismantle,
                CanRetriggerItself = false,
                MaxOccurrencesPerChain = 1,
                InternalCooldownTicks = 30,
            });

            Assert.AreEqual(ProcVerdict.Allowed, guard.Evaluate(Dismantle, EnemyA, ProcChainPath.Root, 0).Verdict);
            guard.NoteFired(Dismantle, EnemyA, 0);

            Assert.AreEqual(ProcVerdict.BlockedByInternalCooldown,
                guard.Evaluate(Dismantle, EnemyA, ProcChainPath.Root, 10).Verdict);

            Assert.AreEqual(ProcVerdict.Allowed,
                guard.Evaluate(Dismantle, EnemyB, ProcChainPath.Root, 10).Verdict,
                "The cooldown is per target; a different enemy must not be gated by it.");

            Assert.AreEqual(ProcVerdict.Allowed,
                guard.Evaluate(Dismantle, EnemyA, ProcChainPath.Root, 30).Verdict);
        }

        // --- safety defaults ----------------------------------------------------------------

        [Test]
        public void AnUnregisteredEffectDefaultsToTheSafeSingleUseRule()
        {
            var guard = new ProcChainGuard();
            const int unknown = 9999;

            ProcChainPath path = ProcChainPath.Root.Append(unknown, EnemyA);

            Assert.AreEqual(ProcVerdict.BlockedByCycle, guard.Evaluate(unknown, EnemyB, path, 0).Verdict,
                "A catalogue typo must not silently enable an unbounded loop.");
        }

        [Test]
        public void ASelfChainingRuleWithoutARealBoundIsRejected()
        {
            var guard = new ProcChainGuard();

            Assert.Throws<System.InvalidOperationException>(() =>
                guard.Register(new ProcEffectRule
                {
                    EffectId = ChainLightning,
                    CanRetriggerItself = true,
                    MaxOccurrencesPerChain = 1,
                }));

            Assert.Throws<System.InvalidOperationException>(() =>
                guard.Register(new ProcEffectRule { EffectId = Explosion, MaxOccurrencesPerChain = 0 }));
        }

        // --- determinism ---------------------------------------------------------------------

        [Test]
        public void TheSameEnqueueOrderAlwaysResolvesInTheSameOrder()
        {
            List<string> RunOnce()
            {
                var guard = Guard();
                var queue = new ProcQueue(guard);
                var order = new List<string>();

                queue.TryEnqueue(Explosion, 1, EnemyA, ProcChainPath.Root, 0);
                queue.TryEnqueue(ChainLightning, 1, EnemyB, ProcChainPath.Root, 0);
                queue.TryEnqueue(DamageOverTime, 1, EnemyC, ProcChainPath.Root, 0);

                queue.Drain((evt, q) =>
                {
                    order.Add($"#{evt.EventId} {evt.EffectId}@{evt.TargetActorId}");
                    if (evt.EffectId == ChainLightning)
                        q.TryEnqueue(Explosion, 1, evt.TargetActorId, evt.ChildPath, evt.Tick);
                });

                return order;
            }

            CollectionAssert.AreEqual(RunOnce(), RunOnce());
        }

        [Test]
        public void FollowUpsResolveAfterEverythingAlreadyQueuedNotImmediately()
        {
            // Depth-first resolution would let one branch finish before a sibling starts, which
            // changes which effects see which world state. FIFO keeps that ordering fixed.
            var guard = Guard();
            var queue = new ProcQueue(guard);
            var order = new List<int>();

            queue.TryEnqueue(ChainLightning, 1, EnemyA, ProcChainPath.Root, 0);
            queue.TryEnqueue(DamageOverTime, 1, EnemyB, ProcChainPath.Root, 0);

            queue.Drain((evt, q) =>
            {
                order.Add(evt.EffectId);
                if (evt.EffectId == ChainLightning)
                    q.TryEnqueue(Explosion, 1, evt.TargetActorId, evt.ChildPath, evt.Tick);
            });

            CollectionAssert.AreEqual(
                new[] { ChainLightning, DamageOverTime, Explosion },
                order,
                "A follow-up jumped ahead of work that was already queued.");
        }

        [Test]
        public void EveryEventCarriesTheMetadataTheContractRequires()
        {
            var guard = Guard();
            var queue = new ProcQueue(guard);
            ProcEvent captured = default;

            queue.TryEnqueue(
                Explosion, sourceActorId: 1, targetActorId: EnemyA, ProcChainPath.Root, tick: 42,
                runId: 7, attackId: 99, damageType: DamageType.Magic,
                isArea: true, isPeriodic: true, isCritical: true, randomStreamId: 2);

            queue.Drain((evt, q) => captured = evt);

            Assert.AreEqual(7, captured.RunId);
            Assert.AreEqual(42L, captured.Tick);
            Assert.AreEqual(99, captured.AttackId);
            Assert.AreEqual(DamageType.Magic, captured.DamageType);
            Assert.IsTrue(captured.IsArea && captured.IsPeriodic && captured.IsCritical);
            Assert.AreEqual(2, captured.RandomStreamId);
            Assert.Greater(captured.EventId, 0L);
            Assert.AreEqual(1, captured.ChildPath.Depth);
        }
    }
}
