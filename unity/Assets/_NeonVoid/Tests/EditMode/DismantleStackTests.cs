using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// R04 해체 프로토콜 and the 해체 status it stacks.
    /// <para>
    /// docs/03's exception column for this card is unusually specific: 시간·성능은 집약형
    /// 데이터, 스택 캡 임의 추가 금지. And section 7 names the requirement outright - 무제한
    /// 중첩이 하나의 집계 상태를 사용하여 스택 수가 많아져도 선형/안정된 비용으로 처리. Those
    /// two sentences are what most of this file is about.
    /// </para>
    /// </summary>
    public sealed class DismantleStackTests
    {
        private static StatusContainer Container() => new StatusContainer(StatusCatalog.Draft());

        private static StatusEffectDefinition Definition(StatusEffectId id) => StatusCatalog.Draft()[id];

        private static List<StatusPeriodicEvent> Run(StatusContainer status, int ticks)
        {
            var events = new List<StatusPeriodicEvent>();
            for (int i = 0; i < ticks; ++i)
                status.Tick(events);

            return events;
        }

        // --- the status data ---------------------------------------------------------------

        [Test]
        public void DismantleIsTheUnboundedAggregatedPolicyDocsTwoNamesAfterIt()
        {
            StatusEffectDefinition definition = Definition(StatusEffectId.Dismantle);

            Assert.AreEqual(StatusStackPolicy.UnboundedAggregated, definition.StackPolicy);
            Assert.Greater(definition.PeriodicIntervalTicks, 0, "해체 is a damage over time effect.");
            Assert.IsFalse(definition.IsBuff);
            Assert.IsFalse(
                definition.IsControl,
                "해체 is a DoT, and tenacity must not shorten it - docs/02 warns against exactly that.");
        }

        [Test]
        public void IntegrityAttackIsLeftAloneBecauseItIsADifferentEffect()
        {
            // 무결성 공격 is the generic DoT and uses IndependentStacks - one timer per
            // application. Repurposing it for R04 would have changed every other DoT in the
            // game, including the skill field's, to suit one card.
            Assert.AreEqual(
                StatusStackPolicy.IndependentStacks,
                Definition(StatusEffectId.IntegrityAttack).StackPolicy);
        }

        // --- unbounded, and aggregated -----------------------------------------------------

        [Test]
        public void StacksGrowWithoutABound()
        {
            // docs/03: 스택 캡 임의 추가 금지. A cap would be a game rule changed to save frame
            // time, which docs/02 section 6 forbids.
            StatusContainer status = Container();
            for (int i = 0; i < 500; ++i)
                status.Apply(StatusEffectId.Dismantle, 2f);

            Assert.AreEqual(500, status.StacksOf(StatusEffectId.Dismantle));
        }

        [Test]
        public void AThousandStacksAreOneInstanceAndOneEventPerInterval()
        {
            // The 집약형 requirement, stated as an assertion. One aggregate state means the
            // per-tick cost does not grow with the count - which is the whole reason docs/03
            // section 7 singles this card out against thousands of enemies.
            StatusContainer status = Container();
            for (int i = 0; i < 1000; ++i)
                status.Apply(StatusEffectId.Dismantle, 2f);

            Assert.AreEqual(1, status.Instances.Count, "1000 stacks became 1000 instances.");

            List<StatusPeriodicEvent> events = Run(
                status, Definition(StatusEffectId.Dismantle).PeriodicIntervalTicks);

            Assert.AreEqual(1, events.Count, "A thousand stacks fired a thousand events.");
            Assert.AreEqual(StatusEffectId.Dismantle, events[0].Id);
        }

        [Test]
        public void TheEventCarriesTheWholeStacksWorthOfDamage()
        {
            // One event per interval is only correct if that event is worth all the stacks.
            // Otherwise aggregating would be a silent damage cut rather than an optimisation.
            StatusContainer status = Container();
            for (int i = 0; i < 7; ++i)
                status.Apply(StatusEffectId.Dismantle, 2f);

            List<StatusPeriodicEvent> events = Run(
                status, Definition(StatusEffectId.Dismantle).PeriodicIntervalTicks);

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(14f, events[0].Magnitude, 1e-3f, "7 stacks of 2 is 14.");
        }

        [Test]
        public void RestackingKeepsTheEffectAliveWithoutMovingItsNextEvent()
        {
            // Both halves matter. A restack that reset the countdown would let an effect
            // restacked every tick never fire at all; one that zeroed it would make the same
            // restacking fire every tick. The duration does refresh, so a target under
            // continuous pressure keeps its stacks.
            StatusEffectDefinition definition = Definition(StatusEffectId.Dismantle);
            StatusContainer restacked = Container();
            StatusContainer untouched = Container();
            restacked.Apply(StatusEffectId.Dismantle, 2f);
            untouched.Apply(StatusEffectId.Dismantle, 2f);

            var restackedEvents = new List<StatusPeriodicEvent>();
            var untouchedEvents = new List<StatusPeriodicEvent>();
            for (int i = 0; i < definition.PeriodicIntervalTicks * 2; ++i)
            {
                restacked.Apply(StatusEffectId.Dismantle, 2f);
                restacked.Tick(restackedEvents);
                untouched.Tick(untouchedEvents);
            }

            Assert.AreEqual(2, untouchedEvents.Count, "The untouched effect fired at the wrong rate.");
            Assert.AreEqual(
                untouchedEvents.Count, restackedEvents.Count, "Restacking moved the periodic countdown.");
            Assert.Greater(
                restackedEvents[restackedEvents.Count - 1].Magnitude, restackedEvents[0].Magnitude,
                "The stacks were not accumulating into the damage.");
        }

        [Test]
        public void ItStillExpiresWhenNobodyKeepsStacking()
        {
            StatusEffectDefinition definition = Definition(StatusEffectId.Dismantle);
            StatusContainer status = Container();
            status.Apply(StatusEffectId.Dismantle, 2f);

            Run(status, definition.BaseDurationTicks + 1);

            Assert.IsFalse(status.Has(StatusEffectId.Dismantle), "해체 outlived its duration.");
        }

        // --- the card ---------------------------------------------------------------------

        [Test]
        public void TheCardAppliesDismantleAndNothingElse()
        {
            PluginDefinition card = PluginCatalog.DismantleProtocol();

            Assert.AreEqual("R04", card.ProposalId);
            Assert.AreEqual(PluginRarity.Rare, card.Rarity);
            Assert.AreEqual(StatusEffectId.Dismantle, card.AppliedStatus);
            Assert.AreEqual(PluginTriggerKind.OnHitEffectResolved, card.Trigger);
            Assert.DoesNotThrow(() => card.Validate());
        }

        [Test]
        public void TheCardHasNoInternalCooldownBecauseItsTextIsEveryTime()
        {
            // 적중 효과 발생 때마다. A cooldown would silently turn "every time" into
            // "sometimes", which is a different card.
            Assert.AreEqual(0, PluginCatalog.DismantleProtocol().Rule.InternalCooldownTicks);
        }

        [Test]
        public void AHitNeverTriggersItDirectly()
        {
            // 적중 효과 발생 is not 적중. A card that also fired on every hit would stack far
            // faster than its text allows.
            var hit = new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f);

            Assert.IsFalse(PluginLoadout.Triggers(PluginTriggerKind.OnHitEffectResolved, hit));

            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.DismantleProtocol(), out _);
            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggered(hit, triggered);

            Assert.IsEmpty(triggered, "A plain hit set off 해체 프로토콜.");
        }

        [Test]
        public void TheLoadoutOffersItWhenAnEffectResolves()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);
            loadout.TryGrant(PluginCatalog.DismantleProtocol(), out _);

            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggeredByEffectResolution(triggered);

            Assert.AreEqual(1, triggered.Count);
            Assert.AreEqual("R04", triggered[0].ProposalId);
        }

        // --- the queue refuses it feeding itself -----------------------------------------

        [Test]
        public void ItDoesNotStackOffItsOwnResolution()
        {
            // [확정] docs/03 section 2: 효과 재진입 경로 추적, 원형 참조 금지. Taken as "per
            // effect resolution", R04's own resolution is a 적중 효과 like any other and the
            // card would feed itself with no hit involved. The chain rule is what refuses it.
            var guard = new ProcChainGuard();
            var queue = new ProcQueue(guard);
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, queue);
            router.TryGrant(PluginCatalog.DetonationGlyph(), out _);
            router.TryGrant(PluginCatalog.DismantleProtocol(), out _);

            router.OnHitLanded(
                new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f),
                tick: 0L);

            var resolved = new List<int>();
            router.Drain((evt, _) =>
            {
                // No manual offer: Drain makes it, and refuses to make it for a reactor's
                // own resolution. That gate is the thing under test.
                resolved.Add(evt.EffectId);
            });

            int dismantles = 0;
            for (int i = 0; i < resolved.Count; ++i)
                if (resolved[i] == PluginCatalog.DismantleProtocol().EffectId)
                    dismantles++;

            Assert.AreEqual(1, dismantles, "해체 stacked off its own resolution.");
        }

        [Test]
        public void OneEffectResolvingGivesOneStack()
        {
            var guard = new ProcChainGuard();
            var queue = new ProcQueue(guard);
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, queue);
            router.TryGrant(PluginCatalog.DismantleProtocol(), out _);

            router.OnHitEffectResolved(sourceActorId: 1, targetActorId: 2, tick: 0L);

            var seen = new List<ProcEvent>();
            router.Drain((evt, _) => seen.Add(evt));

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(2, seen[0].TargetActorId, "The stack went to the wrong actor.");
        }

        [Test]
        public void TwoEffectsResolvingOnOneEnemyGiveTwoStacks()
        {
            // [D-018, approved] 적중 효과 발생 때마다, taken literally. Inside one lineage an
            // explosion resolves on A and the hit it lands marks A, which is two 적중 효과 on
            // the same enemy. The same-target rule used to refuse the second and quietly make
            // this card "once per target per branch".
            var guard = new ProcChainGuard();
            var queue = new ProcQueue(guard);
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, queue);
            router.TryGrant(PluginCatalog.DetonationGlyph(), out _);
            router.TryGrant(PluginCatalog.CorrosionMark(), out _);
            router.TryGrant(PluginCatalog.DismantleProtocol(), out _);

            router.OnHitLanded(
                new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f),
                tick: 0L);

            int dismantleEffectId = PluginCatalog.DismantleProtocol().EffectId;
            int stacks = 0;
            router.Drain((evt, plugin) =>
            {
                if (evt.EffectId == dismantleEffectId)
                {
                    stacks++;
                    return;
                }

                // The effect's own damage landing on the same enemy, which is what carries the
                // lineage onward.
                router.OnHitLanded(
                    new HitReport(1, 2, HitOrigin.Skill, DamageType.Magic, healthRemoved: 5f),
                    tick: 0L);
            });

            Assert.GreaterOrEqual(stacks, 2, "Only one stack landed on the enemy; 때마다 was lost.");
        }

        [Test]
        public void ADefaultRuleStillRefusesARepeatOnTheSameTarget()
        {
            // The opt-out is opt-in. Chain lightning bouncing back onto an enemy it just struck
            // is still the A->B->A loop docs/02 section 6 blocks.
            var guard = new ProcChainGuard();
            guard.Register(ProcEffectRule.SelfChaining(10, maxOccurrences: 3));

            ProcChainPath path = ProcChainPath.Root.Append(10, targetActorId: 7);
            ProcDecision decision = guard.Evaluate(10, targetActorId: 7, path, tick: 0L);

            Assert.AreEqual(ProcVerdict.BlockedByRepeatOnSameTarget, decision.Verdict);
        }

        [Test]
        public void TheOptInLetsTheSameEffectReachOneTargetAgain()
        {
            var guard = new ProcChainGuard();
            guard.Register(PluginCatalog.DismantleProtocol().Rule);

            int effectId = PluginCatalog.DismantleProtocol().EffectId;
            ProcChainPath path = ProcChainPath.Root.Append(effectId, targetActorId: 7);

            Assert.AreEqual(
                ProcVerdict.Allowed, guard.Evaluate(effectId, 7, path, tick: 0L).Verdict);
        }

        [Test]
        public void TheOccurrenceBoundNeverBindsBeforeTheChainEnds()
        {
            // It is the chain depth limit, not a number chosen for this card. A smaller one
            // would be the 스택 캡 임의 추가 docs/03 forbids.
            Assert.AreEqual(
                ProcChainGuard.DefaultMaxChainDepth,
                PluginCatalog.DismantleProtocol().Rule.MaxOccurrencesPerChain);
        }

        [Test]
        public void TheDocumentsRequiredChainIsNotBlocked()
        {
            // docs/03 section 7, first required integration: G10 번개 → G17 폭발 → R04 해체
            // DoT의 정상 연쇄가 차단되지 않음. The third link did not exist until now, so this
            // is the first time the combination could be run at all.
            var guard = new ProcChainGuard();
            var queue = new ProcQueue(guard);
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, queue);
            router.TryGrant(PluginCatalog.ArcChain(), out _);
            router.TryGrant(PluginCatalog.DetonationGlyph(), out _);
            router.TryGrant(PluginCatalog.DismantleProtocol(), out _);

            router.OnHitLanded(
                new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f),
                tick: 0L);

            var fired = new HashSet<string>();
            ProcDrainReport report = router.Drain((evt, plugin) =>
            {
                fired.Add(plugin.ProposalId);
            });

            Assert.Contains("G10", new List<string>(fired));
            Assert.Contains("G17", new List<string>(fired));
            Assert.Contains("R04", new List<string>(fired), "해체 never reached the chain.");
            Assert.IsTrue(
                report.CompletedWithinBudget,
                $"The documented combination hit a resource limit: {report}");
        }

        [Test]
        public void TheWholePrototypeSetStillGrants()
        {
            var loadout = new PluginLoadout();
            var rng = new NeonVoid.Core.DeterministicRng(1u);
            foreach (PluginDefinition card in PluginCatalog.Prototype())
            {
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, loadout.TryGrant(card, rng, out string reason),
                    $"{card.ProposalId}: {reason}");
            }
        }
    }
}
