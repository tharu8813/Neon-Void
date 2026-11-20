using System;
using System.Collections.Generic;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// R06 보조 탄환 and G13 4중 캐시 as data: the two cards in docs/03's original catalogue
    /// that change the basic attack rather than a stat or a hit.
    /// </summary>
    public sealed class PluginFiringTests
    {
        // --- the documented numbers --------------------------------------------------------

        [Test]
        public void SupportRoundCarriesTheDocumentsNumbers()
        {
            // [원안 수치] docs/03 R06: 발사 탄환 1개 추가, 추가탄 적중 효과 피해 80%.
            PluginDefinition card = PluginCatalog.SupportRound();

            Assert.AreEqual("R06", card.ProposalId);
            Assert.AreEqual(PluginRarity.Rare, card.Rarity);
            Assert.AreEqual(1, card.Firing.ExtraShotsPerVolley);
            Assert.AreEqual(0.80f, card.Firing.ExtraShotOnHitScale, 1e-4f);
            Assert.DoesNotThrow(() => card.Validate());
        }

        [Test]
        public void QuadCacheCarriesTheDocumentsNumbers()
        {
            // [원안 수치] docs/03 G13: 적중 효과 4회 후 탄환 4발 추가 연사, 추가탄 적중 효과
            // 피해 70%.
            PluginDefinition card = PluginCatalog.QuadCache();

            Assert.AreEqual("G13", card.ProposalId);
            Assert.AreEqual(PluginRarity.Common, card.Rarity);
            Assert.AreEqual(4, card.Firing.HitEffectsPerBurst);
            Assert.AreEqual(4, card.Firing.BurstShots);
            Assert.AreEqual(0.70f, card.Firing.BurstOnHitScale, 1e-4f);
            Assert.DoesNotThrow(() => card.Validate());
        }

        [Test]
        public void BothAreInThePrototypeSetSoSomethingActuallyFiresThem()
        {
            // The failure this project keeps finding is a card that exists and nothing grants.
            var ids = new List<string>();
            foreach (PluginDefinition card in PluginCatalog.Prototype())
                ids.Add(card.ProposalId);

            Assert.Contains("R06", ids);
            Assert.Contains("G13", ids);
        }

        [Test]
        public void EveryPrototypeCardStillValidates()
        {
            foreach (PluginDefinition card in PluginCatalog.Prototype())
                Assert.DoesNotThrow(() => card.Validate(), card.ProposalId);
        }

        // --- what the data refuses ---------------------------------------------------------

        [Test]
        public void HalfABurstIsRefused()
        {
            // A counter with no shots, or shots with no counter, is a card that looks like it
            // does something and never will - the silent-no-op shape, refused by the data.
            var counterOnly = new PluginFiring { HitEffectsPerBurst = 4 };
            var shotsOnly = new PluginFiring { BurstShots = 4, BurstOnHitScale = 0.7f };

            Assert.Throws<InvalidOperationException>(() => counterOnly.Validate("X"));
            Assert.Throws<InvalidOperationException>(() => shotsOnly.Validate("X"));
        }

        [Test]
        public void AnEffectScaleOutsideAFractionIsRefused()
        {
            // Zero would be a shot whose effects do nothing, indistinguishable in play from a
            // broken card. Above 1 would make an extra shot stronger than the attack it was
            // added to, which is not what 적중 효과 피해 80% means.
            foreach (float bad in new[] { 0f, -0.5f, 1.5f, float.NaN })
            {
                var firing = new PluginFiring { ExtraShotsPerVolley = 1, ExtraShotOnHitScale = bad };
                Assert.Throws<InvalidOperationException>(() => firing.Validate("X"), $"{bad} was accepted.");
            }
        }

        [Test]
        public void AFiringModifierThatChangesNothingAboutFiringIsRefused()
        {
            var card = new PluginDefinition
            {
                ProposalId = "X",
                Rarity = PluginRarity.Common,
                Trigger = PluginTriggerKind.None,
                Effect = PluginEffectKind.FiringModifier,
                StatusDurationTicksOverride = -1,
            };

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void OnlyAFiringModifierMayChangeTheBasicAttack()
        {
            // A chaining card that quietly also added projectiles would be doing something no
            // reader of its row in docs/03 would expect.
            PluginDefinition chain = PluginCatalog.ArcChain();
            chain.Firing = new PluginFiring { ExtraShotsPerVolley = 1, ExtraShotOnHitScale = 0.8f };

            Assert.Throws<InvalidOperationException>(() => chain.Validate());
        }

        [Test]
        public void AFiringModifierIsNotTriggeredByHits()
        {
            PluginDefinition card = PluginCatalog.SupportRound();
            card.Trigger = PluginTriggerKind.OnProjectileHit;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AFiringModifierNeedsNoChainIdentity()
        {
            // It never reaches the proc queue, so requiring an effect id would make every such
            // card carry a meaningless number that could later collide with a real one.
            PluginDefinition card = PluginCatalog.SupportRound();

            Assert.AreEqual(0, card.EffectId);
            Assert.DoesNotThrow(() => card.Validate());
        }

        // --- the loadout collects them -----------------------------------------------------

        [Test]
        public void TheLoadoutReportsEachCardsShotsWithThatCardsOwnFraction()
        {
            // Summing the counts into one number would force one card's fraction onto the
            // other's shots.
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.SupportRound(), out _);

            PluginDefinition second = PluginCatalog.SupportRound();
            second.ProposalId = "R06b";
            second.Firing = new PluginFiring { ExtraShotsPerVolley = 2, ExtraShotOnHitScale = 0.5f };
            loadout.TryGrant(second, out _);

            var shots = new List<ExtraShotSpec>();
            loadout.CollectExtraShots(shots);

            Assert.AreEqual(2, shots.Count);
            Assert.AreEqual(1, shots[0].Count);
            Assert.AreEqual(0.80f, shots[0].OnHitEffectScale, 1e-4f);
            Assert.AreEqual(2, shots[1].Count);
            Assert.AreEqual(0.50f, shots[1].OnHitEffectScale, 1e-4f);
        }

        [Test]
        public void ACardThatGrantsNoShotsIsNotReported()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);
            loadout.TryGrant(PluginCatalog.QuadCache(), out _);

            var shots = new List<ExtraShotSpec>();
            loadout.CollectExtraShots(shots);

            Assert.IsEmpty(shots, "A burst card was reported as a per-volley extra shot.");
        }

        [Test]
        public void TheLoadoutReportsOnlyTheCardsThatEarnBursts()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.SupportRound(), out _);
            loadout.TryGrant(PluginCatalog.QuadCache(), out _);
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);

            var bursts = new List<PluginDefinition>();
            loadout.CollectBurstPlugins(bursts);

            Assert.AreEqual(1, bursts.Count);
            Assert.AreEqual("G13", bursts[0].ProposalId);
        }

        // --- the spec itself ---------------------------------------------------------------

        [Test]
        public void ASpecClampsNonsenseRatherThanCarryingIt()
        {
            Assert.AreEqual(0, new ExtraShotSpec(-3, 0.8f).Count);
            Assert.AreEqual(1f, new ExtraShotSpec(1, float.NaN).OnHitEffectScale, 1e-4f);
            Assert.AreEqual(1f, new ExtraShotSpec(1, -0.5f).OnHitEffectScale, 1e-4f);
            Assert.IsTrue(new ExtraShotSpec(0, 0.8f).IsEmpty);
        }
    }
}
