using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Proc;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 적중 효과 피해 - the fraction docs/03 states three times (R06 80%, G13 70%, L01 70%) and
    /// which nothing in the code could express.
    /// </summary>
    public sealed class OnHitEffectScaleTests
    {
        private static DamageRequest Mixed() => new DamageRequest
        {
            Flat = 20f,
            AdRatio = 1.5f,
            ApRatio = 0.4f,
            Type = DamageType.Magic,
            CanCrit = true,
            IsArea = true,
            IsPeriodic = true,
        };

        // --- scaling a request -------------------------------------------------------------

        [Test]
        public void EveryDamageTermScalesIncludingTheFlatOne()
        {
            // Scaling only the stat ratios would make a card whose damage happens to be flat
            // immune to the reduction - a balance hole that depends on how the card was
            // authored rather than on what it does.
            DamageRequest scaled = Mixed().Scaled(0.7f);

            Assert.AreEqual(14f, scaled.Flat, 1e-4f);
            Assert.AreEqual(1.05f, scaled.AdRatio, 1e-4f);
            Assert.AreEqual(0.28f, scaled.ApRatio, 1e-4f);
        }

        [Test]
        public void ScalingDoesNotChangeWhatKindOfHitItIs()
        {
            // A smaller hit is the same kind of hit. Changing these would change which omnivamp
            // efficiency applies and whether the attack may crit.
            DamageRequest original = Mixed();
            DamageRequest scaled = original.Scaled(0.8f);

            Assert.AreEqual(original.Type, scaled.Type);
            Assert.AreEqual(original.CanCrit, scaled.CanCrit);
            Assert.AreEqual(original.IsArea, scaled.IsArea);
            Assert.AreEqual(original.IsPeriodic, scaled.IsPeriodic);
        }

        [Test]
        public void ScalingLeavesTheOriginalAlone()
        {
            // The card's own data is the full-strength version; a mutating scale would make the
            // first reduced shot permanently weaken the card.
            DamageRequest original = Mixed();
            original.Scaled(0.5f);

            Assert.AreEqual(20f, original.Flat, 1e-4f);
            Assert.AreEqual(1.5f, original.AdRatio, 1e-4f);
        }

        [Test]
        public void AScaleOfOneChangesNothing()
        {
            DamageRequest scaled = Mixed().Scaled(1f);

            Assert.AreEqual(20f, scaled.Flat, 1e-4f);
            Assert.AreEqual(1.5f, scaled.AdRatio, 1e-4f);
        }

        [Test]
        public void ANonsenseScaleIsRefusedRatherThanReachingTheDamageFormula()
        {
            Assert.Throws<InvalidOperationException>(() => Mixed().Scaled(-0.5f));
            Assert.Throws<InvalidOperationException>(() => Mixed().Scaled(float.NaN));
            Assert.Throws<InvalidOperationException>(() => Mixed().Scaled(float.PositiveInfinity));
        }

        // --- the hit carries it ------------------------------------------------------------

        [Test]
        public void AnOrdinaryHitCarriesAFullScale()
        {
            var hit = new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f);

            Assert.AreEqual(1f, hit.OnHitEffectScale, 1e-4f);
        }

        [Test]
        public void ANonsenseScaleOnAHitBecomesFullRatherThanLosingTheHit()
        {
            // A hit report is built in the middle of resolving damage, so throwing would lose
            // the hit entirely. Clamping to 1 is the conservative answer: the effect lands at
            // full strength, which is wrong by a fraction rather than wrong by a missing proc.
            foreach (float bad in new[] { -1f, float.NaN, float.NegativeInfinity })
            {
                var hit = new HitReport(
                    1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f,
                    onHitEffectScale: bad);

                Assert.AreEqual(1f, hit.OnHitEffectScale, 1e-4f, $"{bad} was not clamped.");
            }
        }

        [Test]
        public void TheEventCarriesTheHitsScaleRatherThanRecoveringItLater()
        {
            // Same reason the chain path travels on the event: resolution is deferred, and an
            // event waiting behind another branch cannot ask what is firing now.
            var evt = new ProcEvent(
                1L, 1, 0L, 10, 1, 2, 0, DamageType.Physical, false, false, false,
                ProcChainPath.Root, 0, 0.7f);

            Assert.AreEqual(0.7f, evt.OnHitEffectScale, 1e-4f);
        }

        // --- the router inherits it --------------------------------------------------------

        private static ProcTriggerRouter RouterWith(params PluginDefinition[] plugins)
        {
            var guard = new ProcChainGuard();
            var router = new ProcTriggerRouter(new PluginLoadout(), guard, new ProcQueue(guard));
            for (int i = 0; i < plugins.Length; ++i)
                router.TryGrant(plugins[i], out _);

            return router;
        }

        private static HitReport Shot(float scale) => new HitReport(
            1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f, onHitEffectScale: scale);

        [Test]
        public void OutsideADrainTheScaleIsOne()
        {
            Assert.AreEqual(1f, RouterWith().ActiveOnHitEffectScale, 1e-4f);
        }

        [Test]
        public void AnEffectWokenByAReducedShotInheritsTheReduction()
        {
            ProcTriggerRouter router = RouterWith(PluginCatalog.DetonationGlyph());
            router.OnHitLanded(Shot(0.8f), tick: 0L);

            var seen = new List<float>();
            router.Drain((evt, _) => seen.Add(evt.OnHitEffectScale));

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(0.8f, seen[0], 1e-4f);
        }

        [Test]
        public void TheReductionIsInheritedDownTheChainRatherThanCompounded()
        {
            // A 70% shot triggers an effect whose damage lands and triggers a second one. The
            // second gets 70%, not 49%: everything in the chain is an effect of one burst shot,
            // so the document's single number applies once. Compounding would make the damage
            // depend on chain depth, which docs/03 never mentions and nobody could read off a
            // card.
            //
            // Two DIFFERENT effects, because that is how a chain actually continues: the cycle
            // rule refuses an effect that is already in its own ancestry, so one card cannot
            // re-trigger itself this way. 폭발 각인 answers 탄환 적중, 부식 표식 answers 타격 시,
            // and the glyph is granted first so it resolves first.
            ProcTriggerRouter router = RouterWith(
                PluginCatalog.DetonationGlyph(), PluginCatalog.CorrosionMark());
            router.OnHitLanded(Shot(0.7f), tick: 0L);

            var scales = new List<float>();
            int resolved = 0;
            router.Drain((evt, _) =>
            {
                scales.Add(evt.OnHitEffectScale);
                Assert.AreEqual(
                    evt.OnHitEffectScale, router.ActiveOnHitEffectScale, 1e-4f,
                    "A hit arriving during resolution would not inherit this event's fraction.");

                // The effect's own damage landing, which is a hit like any other and carries no
                // fraction of its own - the router is holding the chain's.
                if (++resolved == 1)
                {
                    router.OnHitLanded(
                        new HitReport(1, 9, HitOrigin.Skill, DamageType.Magic, healthRemoved: 5f),
                        tick: 0L);
                }
            });

            Assert.GreaterOrEqual(scales.Count, 3, "The chain did not reach a second generation.");
            for (int i = 0; i < scales.Count; ++i)
                Assert.AreEqual(0.7f, scales[i], 1e-4f, $"event {i} came out at {scales[i]}");
        }

        [Test]
        public void AFullStrengthShotAfterAReducedOneIsStillFullStrength()
        {
            // The ambient scale is restored after each event, so one burst shot does not leave
            // the rest of the tick reduced.
            ProcTriggerRouter router = RouterWith(PluginCatalog.DetonationGlyph());
            router.OnHitLanded(Shot(0.7f), tick: 0L);
            router.Drain((evt, _) => { });

            Assert.AreEqual(1f, router.ActiveOnHitEffectScale, 1e-4f);

            router.OnHitLanded(Shot(1f), tick: 600L);
            var seen = new List<float>();
            router.Drain((evt, _) => seen.Add(evt.OnHitEffectScale));

            Assert.AreEqual(1, seen.Count);
            Assert.AreEqual(1f, seen[0], 1e-4f);
        }

        [Test]
        public void TheScaleIsRestoredEvenWhenAnEffectThrows()
        {
            ProcTriggerRouter router = RouterWith(PluginCatalog.DetonationGlyph());
            router.OnHitLanded(Shot(0.7f), tick: 0L);

            Assert.Throws<InvalidOperationException>(
                () => router.Drain((evt, _) => throw new InvalidOperationException("boom")));
            Assert.AreEqual(1f, router.ActiveOnHitEffectScale, 1e-4f);
        }
    }
}
