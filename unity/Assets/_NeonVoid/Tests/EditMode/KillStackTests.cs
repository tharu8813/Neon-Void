using System;
using System.Collections.Generic;
using NeonVoid.Combat;
using NeonVoid.Combat.Stats;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>R02 킬 스택 as data, and the shape of its growth.</summary>
    public sealed class KillStackTests
    {
        [Test]
        public void TheCardGrowsAttackDamageOnAKill()
        {
            PluginDefinition card = PluginCatalog.KillStack();

            Assert.AreEqual("R02", card.ProposalId);
            Assert.AreEqual(PluginRarity.Rare, card.Rarity);
            Assert.AreEqual(PluginTriggerKind.OnKill, card.Trigger);
            Assert.AreEqual(PluginEffectKind.GrowingPassive, card.Effect);
            Assert.AreEqual(ModifiedStat.AttackDamage, card.Growth.Stat);
            Assert.DoesNotThrow(() => card.Validate());
        }

        [Test]
        public void ItNeverReachesTheProcQueue()
        {
            // It is a term in the stat snapshot, not an effect that resolves - so it carries no
            // chain rule, and the guard must never be handed an empty one. Adding the firing
            // modifiers without this broke 38 PlayMode tests once already.
            PluginDefinition card = PluginCatalog.KillStack();

            Assert.IsFalse(card.ReachesProcQueue);
            Assert.AreEqual(0, card.EffectId);
        }

        [Test]
        public void TheContributionGrowsWithTheStacksAndStopsAtTheBound()
        {
            PluginGrowth growth = PluginCatalog.KillStack().Growth;

            Assert.AreEqual(0f, growth.ModifierAt(0).Flat, 1e-4f);
            Assert.AreEqual(
                growth.PerStack * 3f, growth.ModifierAt(3).Flat, 1e-4f, "Three kills, three times.");
            Assert.AreEqual(
                growth.ModifierAt(growth.MaxStacks).Flat,
                growth.ModifierAt(growth.MaxStacks + 50).Flat, 1e-4f,
                "Past the bound the contribution kept growing.");
        }

        [Test]
        public void AGrowthThatAccumulatesNothingIsRefused()
        {
            // A card that counts kills and does not reward them.
            foreach (float bad in new[] { 0f, -1f, float.NaN })
            {
                var growth = new PluginGrowth
                {
                    Stat = ModifiedStat.AttackDamage, PerStack = bad, MaxStacks = 5,
                };
                Assert.Throws<InvalidOperationException>(() => growth.Validate("X"), $"{bad} was accepted.");
            }
        }

        [Test]
        public void ABoundBelowOneIsRefused()
        {
            // The VALUE is undecided (D-019); its existence is not optional, because a bound of
            // zero clamps every stack away and the card silently does nothing.
            var growth = new PluginGrowth
            {
                Stat = ModifiedStat.AttackDamage, PerStack = 0.5f, MaxStacks = 0,
            };

            Assert.Throws<InvalidOperationException>(() => growth.Validate("X"));
        }

        [Test]
        public void OnlyAGrowingPassiveMayGrowAStat()
        {
            PluginDefinition chain = PluginCatalog.ArcChain();
            chain.Growth = new PluginGrowth
            {
                Stat = ModifiedStat.AttackDamage, PerStack = 0.5f, MaxStacks = 5,
            };

            Assert.Throws<InvalidOperationException>(() => chain.Validate());
        }

        [Test]
        public void AnOrdinaryHitDoesNotCountAsAKill()
        {
            var survived = new HitReport(1, 2, HitOrigin.Projectile, DamageType.Physical, healthRemoved: 5f);
            var killed = new HitReport(
                1, 2, HitOrigin.Projectile, DamageType.Physical, killed: true, healthRemoved: 5f);

            Assert.IsFalse(survived.Killed);
            Assert.IsTrue(killed.Killed);
        }

        [Test]
        public void AKillNeverEnqueuesIt()
        {
            // The kill is read where the stat snapshot is rebuilt, not in the trigger table.
            var killed = new HitReport(
                1, 2, HitOrigin.Projectile, DamageType.Physical, killed: true, healthRemoved: 5f);

            Assert.IsFalse(PluginLoadout.Triggers(PluginTriggerKind.OnKill, killed));

            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.KillStack(), out _);
            var triggered = new List<PluginDefinition>();
            loadout.CollectTriggered(killed, triggered);

            Assert.IsEmpty(triggered);
        }

        [Test]
        public void TheLoadoutReportsItAmongTheKillGrowthCards()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.ArcChain(), out _);
            loadout.TryGrant(PluginCatalog.KillStack(), out _);

            var growth = new List<PluginDefinition>();
            loadout.CollectKillGrowthPlugins(growth);

            Assert.AreEqual(1, growth.Count);
            Assert.AreEqual("R02", growth[0].ProposalId);
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

            Assert.Contains("R02", ids);
        }
    }
}
