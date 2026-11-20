using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>G16 대시 폭발 as data, and what the data refuses.</summary>
    public sealed class DashDetonationTests
    {
        [Test]
        public void TheCardIsADashTriggeredSingleTargetBlast()
        {
            PluginDefinition card = PluginCatalog.DashDetonation();

            Assert.AreEqual("G16", card.ProposalId);
            Assert.AreEqual(PluginTriggerKind.OnDashContact, card.Trigger);
            Assert.AreEqual(PluginEffectKind.DamageTarget, card.Effect);
            Assert.Greater(card.RadiusUnits, 0f);
            Assert.DoesNotThrow(() => card.Validate());
        }

        [Test]
        public void TheBlastIsTaggedAsAreaDamage()
        {
            // 폭발 피해: the path is the area, so the 33% area omnivamp efficiency applies.
            // Leaving this off would quietly make a dash through a crowd the best healing in
            // the game.
            Assert.IsTrue(PluginCatalog.DashDetonation().Damage.IsArea);
        }

        [Test]
        public void ADashCardWithNoReachIsRefused()
        {
            // With no reach the sweep finds nobody and the card fires for no enemy ever.
            PluginDefinition card = PluginCatalog.DashDetonation();
            card.RadiusUnits = 0f;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void ADamagingEffectWithNoDamageIsRefused()
        {
            PluginDefinition card = PluginCatalog.DashDetonation();
            card.Damage = new DamageRequest { Type = DamageType.Magic };

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AHitNeverTriggersIt()
        {
            // Nothing has struck anybody when a dash passes through a crowd, and a card that
            // also fired on every hit would be a different card entirely.
            var hit = new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 10f);

            Assert.IsFalse(PluginLoadout.Triggers(PluginTriggerKind.OnDashContact, hit));

            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.DashDetonation(), out _);
            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggered(hit, triggered);

            Assert.IsEmpty(triggered);
        }

        [Test]
        public void AnEffectResolvingNeverTriggersItEither()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.DashDetonation(), out _);

            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggeredByEffectResolution(triggered);

            Assert.IsEmpty(triggered, "A dash card reacted to an on-hit effect.");
        }

        [Test]
        public void TheLoadoutOffersItOnADashContact()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);
            loadout.TryGrant(PluginCatalog.DashDetonation(), out _);

            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggeredByDashContact(triggered);

            Assert.AreEqual(1, triggered.Count);
            Assert.AreEqual("G16", triggered[0].ProposalId);
        }

        [Test]
        public void ItIsInThePrototypeSetAndTheWholeSetStillGrants()
        {
            var ids = new List<string>();
            var loadout = new PluginLoadout();
            var rng = new NeonVoid.Core.DeterministicRng(1u);

            foreach (PluginDefinition card in PluginCatalog.Prototype())
            {
                ids.Add(card.ProposalId);
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, loadout.TryGrant(card, rng, out string reason),
                    $"{card.ProposalId}: {reason}");
            }

            Assert.Contains("G16", ids);
        }
    }
}
