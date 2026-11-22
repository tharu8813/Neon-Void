using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Plugins reaching the proc queue: what may be carried, what a hit sets off, and whether
    /// the queue can still see a chain once effects start producing hits of their own.
    /// </summary>
    public sealed class PluginProcTests
    {
        private const int Player = 1;
        private const int EnemyA = 2;
        private const int EnemyB = 3;
        private const int EnemyC = 4;

        private static HitReport Hit(
            int target, HitOrigin origin = HitOrigin.Projectile, float healthRemoved = 5f) =>
            new HitReport(Player, target, origin, DamageType.Physical, healthRemoved: healthRemoved);

        /// <summary>A plugin that both fires on any hit and deals damage, to exercise re-entry.</summary>
        private static PluginDefinition LoopingTestPlugin(int maxOccurrences) => new PluginDefinition
        {
            ProposalId = "TEST-LOOP",
            Rarity = PluginRarity.Common,
            Trigger = PluginTriggerKind.OnAnyHit,
            Effect = PluginEffectKind.ChainToNearbyEnemy,
            Rule = ProcEffectRule.SelfChaining(99, maxOccurrences, internalCooldownTicks: 1),
            Damage = new DamageRequest { Flat = 1f, Type = DamageType.Magic },
            RadiusUnits = 5f,
            AppliedStatus = StatusEffectId.None,
            StatusDurationTicksOverride = -1,
        };

        private static ProcTriggerRouter RouterWith(params PluginDefinition[] plugins)
        {
            var guard = new ProcChainGuard();
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, new ProcQueue(guard));
            for (int i = 0; i < plugins.Length; ++i)
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, router.TryGrant(plugins[i], out string reason), reason);

            return router;
        }

        // --- what may be carried -----------------------------------------------------------

        [Test]
        public void AMythicPluginIsRefusedBecauseNoMythicCardIsApproved()
        {
            // docs/03 section 2 keeps the four mythic ideas as drafts and says they must not
            // appear as real cards before approval; D-008 is the open decision. Refusing at the
            // door into the loadout covers every path in, not just the reward pool.
            var loadout = new PluginLoadout();
            PluginDefinition mythic = PluginCatalog.ArcChain();
            mythic.ProposalId = "M01";
            mythic.Rarity = PluginRarity.Mythic;

            Assert.AreEqual(
                PluginGrantVerdict.RejectedMythicNotApproved, loadout.TryGrant(mythic, out string reason));
            Assert.That(reason, Does.Contain("D-008"));
            Assert.AreEqual(0, loadout.Count);
        }

        [Test]
        public void TheSamePluginLevelsUpRatherThanBeingCarriedTwice()
        {
            // [확정] docs/03 3절 6항: 같은 플러그인 획득 = 레벨 상승. It used to be refused,
            // which is what the same line calls the plan rather than the rule.
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);

            Assert.AreEqual(
                PluginGrantVerdict.LevelledUp, loadout.TryGrant(PluginCatalog.ArcChain(), out _));
            Assert.AreEqual(1, loadout.Count, "a second copy was added instead of a level");
            Assert.AreEqual(2, loadout.LevelOf("G10"));
        }

        [Test]
        public void AChainingPluginWithoutAnInternalCooldownIsRefused()
        {
            // Its chain path cannot save it: every fresh activation is a new chain with a new
            // path, so without a per-target cooldown the same enemy is hit again every tick.
            var loadout = new PluginLoadout();
            PluginDefinition broken = PluginCatalog.ArcChain();
            broken.Rule.InternalCooldownTicks = 0;

            Assert.AreEqual(PluginGrantVerdict.RejectedInvalid, loadout.TryGrant(broken, out string reason));
            Assert.That(reason, Does.Contain("internal cooldown"));
        }

        [Test]
        public void GrantingAPluginRegistersItsChainRuleWithTheGuard()
        {
            var guard = new ProcChainGuard();
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, new ProcQueue(guard));

            router.TryGrant(PluginCatalog.ArcChain(), out _);

            ProcEffectRule registered = guard.RuleFor(10);
            Assert.IsTrue(registered.CanRetriggerItself, "G10 lost its own rule and fell back to the default.");
            Assert.AreEqual(PluginCatalog.StatedInternalCooldownTicks, registered.InternalCooldownTicks);
        }

        // --- what a hit sets off -----------------------------------------------------------

        [Test]
        public void AProjectileTriggerIgnoresSkillAndContactHits()
        {
            Assert.IsTrue(PluginLoadout.Triggers(PluginTriggerKind.OnProjectileHit, Hit(EnemyA)));
            Assert.IsFalse(PluginLoadout.Triggers(
                PluginTriggerKind.OnProjectileHit, Hit(EnemyA, HitOrigin.Skill)));
            Assert.IsFalse(PluginLoadout.Triggers(
                PluginTriggerKind.OnProjectileHit, Hit(EnemyA, HitOrigin.Contact)));
        }

        [Test]
        public void ASkillTriggerIgnoresBasicAttacks()
        {
            Assert.IsTrue(PluginLoadout.Triggers(PluginTriggerKind.OnSkillHit, Hit(EnemyA, HitOrigin.Skill)));
            Assert.IsFalse(PluginLoadout.Triggers(PluginTriggerKind.OnSkillHit, Hit(EnemyA)));
        }

        [Test]
        public void AnyHitMeansAnyKnownOrigin()
        {
            Assert.IsTrue(PluginLoadout.Triggers(PluginTriggerKind.OnAnyHit, Hit(EnemyA)));
            Assert.IsTrue(PluginLoadout.Triggers(PluginTriggerKind.OnAnyHit, Hit(EnemyA, HitOrigin.Skill)));
            Assert.IsTrue(PluginLoadout.Triggers(PluginTriggerKind.OnAnyHit, Hit(EnemyA, HitOrigin.Contact)));
            Assert.IsFalse(
                PluginLoadout.Triggers(PluginTriggerKind.OnAnyHit, Hit(EnemyA, HitOrigin.Unknown)),
                "An unclassified hit should not trigger anything; the origin is the tag triggers read.");
        }

        [Test]
        public void AHitThatRemovedNoHealthTriggersNothing()
        {
            // An attack absorbed by 무적 still "hit". Treating it as a proc source would make an
            // invulnerable target a free generator of chain lightning and explosions.
            HitReport absorbed = Hit(EnemyA, healthRemoved: 0f);

            Assert.IsFalse(absorbed.Landed);
            Assert.IsFalse(PluginLoadout.Triggers(PluginTriggerKind.OnAnyHit, absorbed));
            Assert.IsFalse(PluginLoadout.Triggers(PluginTriggerKind.OnProjectileHit, absorbed));
        }

        [Test]
        public void TriggeredPluginsComeBackInCarryOrder()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);        // G10, projectile
            loadout.TryGrant(PluginCatalog.DetonationGlyph(), out _); // G17, projectile
            loadout.TryGrant(PluginCatalog.CorrosionMark(), out _);   // G12, any hit

            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggered(Hit(EnemyA), triggered);

            Assert.AreEqual(3, triggered.Count);
            Assert.AreEqual("G10", triggered[0].ProposalId);
            Assert.AreEqual("G17", triggered[1].ProposalId);
            Assert.AreEqual("G12", triggered[2].ProposalId);
        }

        [Test]
        public void OnlyTheAnyHitPluginTriggersOnASkillHit()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);
            loadout.TryGrant(PluginCatalog.CorrosionMark(), out _);

            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggered(Hit(EnemyA, HitOrigin.Skill), triggered);

            Assert.AreEqual(1, triggered.Count);
            Assert.AreEqual("G12", triggered[0].ProposalId);
        }

        // --- ancestry through the drain ----------------------------------------------------

        [Test]
        public void AHitCausedByAnEffectInheritsThatEffectsChain()
        {
            // The failure this prevents: an effect deals damage, the damage lands, and the
            // landing is a hit that can trigger plugins again. Enqueued from the root like a
            // basic attack, every one of those looks like an unrelated first-generation event,
            // the cycle and occurrence rules never see a chain, and the effect feeds itself.
            ProcTriggerRouter router = RouterWith(LoopingTestPlugin(maxOccurrences: 2));

            // The basic attack that starts it all.
            router.OnHitLanded(Hit(EnemyA), tick: 0L);

            // Every resolution reports one further hit, on a target the effect has not touched,
            // exactly as the real chain-lightning effect does.
            var struck = new Queue<int>(new[] { EnemyB, EnemyC });
            ProcDrainReport report = router.Drain((evt, plugin) =>
            {
                if (struck.Count == 0)
                    return;
                router.OnHitLanded(Hit(struck.Dequeue(), HitOrigin.Skill), evt.Tick);
            });

            Assert.AreEqual(2, report.Resolved, "The chain did not run to its bound.");
            Assert.AreEqual(1, report.BlockedByOccurrenceLimit,
                "The third link was allowed, so the chain was not being tracked across the drain.");
        }

        [Test]
        public void TheSameHitsWithoutAChainAreNeverBlocked()
        {
            // The control for the test above: identical hits reported outside a drain each
            // start their own chain and are all allowed. Without this, the assertion above
            // could pass for the wrong reason.
            ProcTriggerRouter router = RouterWith(LoopingTestPlugin(maxOccurrences: 2));

            router.OnHitLanded(Hit(EnemyA), tick: 0L);
            router.OnHitLanded(Hit(EnemyB), tick: 0L);
            router.OnHitLanded(Hit(EnemyC), tick: 0L);

            ProcDrainReport report = router.Drain((_, __) => { });

            Assert.AreEqual(3, report.Resolved);
            Assert.AreEqual(0, report.TotalBlocked);
        }

        [Test]
        public void TheActiveChainIsRootOutsideADrain()
        {
            ProcTriggerRouter router = RouterWith(LoopingTestPlugin(maxOccurrences: 2));

            Assert.AreEqual(0, router.ActivePath.Depth);

            router.OnHitLanded(Hit(EnemyA), tick: 0L);
            router.Drain((evt, _) => Assert.AreEqual(1, router.ActivePath.Depth,
                "An event resolving should put its own ancestry in scope."));

            Assert.AreEqual(0, router.ActivePath.Depth, "The ancestry outlived the drain.");
        }

        [Test]
        public void AFailingEffectDoesNotLeaveItsChainInScope()
        {
            // Otherwise one throwing effect would make every later hit in the tick inherit a
            // chain it had nothing to do with, and those hits would be refused for no reason.
            ProcTriggerRouter router = RouterWith(LoopingTestPlugin(maxOccurrences: 2));
            router.OnHitLanded(Hit(EnemyA), tick: 0L);

            Assert.Throws<System.InvalidOperationException>(
                () => router.Drain((_, __) => throw new System.InvalidOperationException("effect failed")));

            Assert.AreEqual(0, router.ActivePath.Depth);
        }

        [Test]
        public void AnEffectWithNoCarriedPluginResolvesToNothing()
        {
            // A queued effect whose plugin is gone must not throw; it simply does nothing.
            ProcTriggerRouter router = RouterWith(LoopingTestPlugin(maxOccurrences: 2));
            router.OnHitLanded(Hit(EnemyA), tick: 0L);
            router.Loadout.Clear();

            int resolverCalls = 0;
            ProcDrainReport report = router.Drain((_, __) => resolverCalls++);

            Assert.AreEqual(1, report.Resolved);
            Assert.AreEqual(0, resolverCalls);
        }

        // --- the prototype catalogue -------------------------------------------------------

        [Test]
        public void EveryPrototypePluginIsCarryable()
        {
            var loadout = new PluginLoadout();
            IReadOnlyList<PluginDefinition> prototype = PluginCatalog.Prototype();

            Assert.AreEqual(PluginCatalog.Prototype().Count, prototype.Count);
            for (int i = 0; i < prototype.Count; ++i)
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, loadout.TryGrant(prototype[i], out string reason),
                    $"{prototype[i].ProposalId}: {reason}");
        }

        [Test]
        public void TheNumbersTheOriginalDesignStatesAreReproducedExactly()
        {
            // docs/03: G10 and G17 both carry 대상별 내부 CD 3초, and G12 lasts 4초. These are
            // the design's own figures, not tuning, so a change here is a change to the design.
            Assert.AreEqual(180, PluginCatalog.StatedInternalCooldownTicks, "3초 at 60 Hz.");
            Assert.AreEqual(180, PluginCatalog.ArcChain().Rule.InternalCooldownTicks);
            Assert.AreEqual(180, PluginCatalog.DetonationGlyph().Rule.InternalCooldownTicks);

            Assert.AreEqual(240, PluginCatalog.StatedCorrosionDurationTicks, "4초 at 60 Hz.");
            Assert.AreEqual(240, PluginCatalog.CorrosionMark().StatusDurationTicksOverride);
        }

        [Test]
        public void TheCorrosionMarkOverridesTheCatalogueDurationOnPurpose()
        {
            // The catalogue gives 방화벽 차단 300 ticks; G12 states 4초. The override is what
            // keeps the plugin honest to its own design line.
            StatusEffectDefinition catalogued = StatusCatalog.Draft()[StatusEffectId.FirewallBreach];

            Assert.AreEqual(300, catalogued.BaseDurationTicks);
            Assert.AreEqual(240, PluginCatalog.CorrosionMark().StatusDurationTicksOverride);
            Assert.AreNotEqual(
                catalogued.BaseDurationTicks, PluginCatalog.CorrosionMark().StatusDurationTicksOverride);
        }

        [Test]
        public void TheExplosionDeclaresItselfAsAreaDamage()
        {
            // The flag feeds omnivamp efficiency. An area effect that forgot it would pay full
            // efficiency on every enemy it caught, which is a sustain change, not a detail -
            // and the document's own exception column asks for this flag explicitly.
            Assert.IsTrue(PluginCatalog.DetonationGlyph().Damage.IsArea);
            Assert.IsFalse(PluginCatalog.ArcChain().Damage.IsArea, "Chain lightning hits one enemy.");
        }

        [Test]
        public void TheOriginalCatalogueCountsAreTheOnesTheDocumentStates()
        {
            Assert.AreEqual(17, PluginRarity.Common.OriginalCatalogCount());
            Assert.AreEqual(7, PluginRarity.Rare.OriginalCatalogCount());
            Assert.AreEqual(3, PluginRarity.Legendary.OriginalCatalogCount());
            Assert.AreEqual(0, PluginRarity.Mythic.OriginalCatalogCount(),
                "27 is not a total that includes 신화; the grade has no approved cards.");
        }
    }
}
