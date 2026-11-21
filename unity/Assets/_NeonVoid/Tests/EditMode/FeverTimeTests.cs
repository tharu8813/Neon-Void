using System;
using System.Collections.Generic;
using NeonVoid.Combat.Stats;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// L03 피버타임 as data: the gauge arithmetic and the refusals. Whether the buff reaches
    /// the stat snapshot is a scene behaviour and is tested there.
    /// <para>
    /// Every number this card has is [초안] - docs/03 lists 버프 스택·충전 단위·계수 미확정 by
    /// name - so these tests assert the card's own constants rather than figures of their own.
    /// </para>
    /// </summary>
    public sealed class FeverTimeTests
    {
        // --- the gauge -------------------------------------------------------------------------

        [Test]
        public void AKillIsWorthItsHitAsWell()
        {
            // docs/03 says 적중 또는 처치. A kill that paid only the kill's share would be worth
            // less than a graze whenever PerKill was the smaller number, which is the kind of
            // inversion nobody notices until a retune makes it true.
            var gauge = new PluginCharge { PerHit = 1f, PerKill = 4f, Required = 30f, ActiveTicks = 60 };

            Assert.AreEqual(1f, gauge.Add(0f, killed: false), 0.0001f);
            Assert.AreEqual(5f, gauge.Add(0f, killed: true), 0.0001f);
        }

        [Test]
        public void ItIsFullOnlyWhenItReachesWhatItAsksFor()
        {
            var gauge = new PluginCharge { PerHit = 1f, PerKill = 0f, Required = 10f, ActiveTicks = 60 };

            Assert.IsFalse(gauge.IsFull(9.99f));
            Assert.IsTrue(gauge.IsFull(10f), "a gauge at exactly its requirement is full");
            Assert.IsTrue(gauge.IsFull(40f));
        }

        [Test]
        public void TheFractionNeverReadsPastFull()
        {
            // It is a bar on a HUD; a bar that reads 300% is a bar nobody can draw.
            var gauge = new PluginCharge { PerHit = 1f, Required = 10f, ActiveTicks = 60 };

            Assert.AreEqual(0f, gauge.Fraction(0f), 0.0001f);
            Assert.AreEqual(0.5f, gauge.Fraction(5f), 0.0001f);
            Assert.AreEqual(1f, gauge.Fraction(10f), 0.0001f);
            Assert.AreEqual(1f, gauge.Fraction(1000f), 0.0001f);
        }

        // --- the card -------------------------------------------------------------------------

        [Test]
        public void FeverTimeIsALegendaryThatChargesAndRuns()
        {
            PluginDefinition card = PluginCatalog.FeverTime();

            Assert.DoesNotThrow(() => card.Validate());
            Assert.AreEqual("L03", card.ProposalId);
            Assert.AreEqual(PluginRarity.Legendary, card.Rarity);
            Assert.AreEqual(PluginEffectKind.ChargedPassive, card.Effect);
            Assert.AreEqual(PluginTriggerKind.None, card.Trigger);

            Assert.Greater(card.Charge.Required, 0f);
            Assert.Greater(card.Charge.ActiveTicks, 0);
            Assert.IsFalse(card.Passive.IsEmpty, "the gauge fills and buys nothing");
        }

        [Test]
        public void ItRaisesEveryStatThisGameHas()
        {
            // 전체 능력치 증가, spelled out as the stats CombatStats actually defines rather
            // than as one multiplier on a notion of "all stats" that does not exist here. The
            // set is asserted so that adding a stat to the game and forgetting this card shows
            // up as a failure rather than as a legendary that quietly covers less than it says.
            PluginDefinition card = PluginCatalog.FeverTime();

            var raised = new HashSet<ModifiedStat>();
            for (int i = 0; i < card.Passive.StatModifiers.Length; ++i)
                raised.Add(card.Passive.StatModifiers[i].Stat);

            foreach (ModifiedStat stat in PluginPassive.RandomCandidates)
            {
                Assert.IsTrue(
                    raised.Contains(stat),
                    $"피버타임 leaves {stat} alone, so 전체 능력치 증가 is not every stat");
            }

            Assert.Greater(card.Passive.MoveSpeedFraction, 0f, "it does not move the player any faster");
        }

        [Test]
        public void ItIsNotCountedAmongTheUngatedContributions()
        {
            // Same hazard the conditional passives had: counted in both places, the card is a
            // permanent buff plus a window that adds it again, and the window still appears to
            // work because the numbers do change when it opens.
            var loadout = new PluginLoadout();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.FeverTime(), out string why), why);

            Assert.AreEqual(0f, loadout.PassiveMoveSpeedFraction(), "a charged card leaked while idle");

            var ungated = new List<StatModifier>();
            loadout.CollectPassiveModifiers(ungated);
            Assert.IsEmpty(ungated, "a charged card's stats were published while its gauge was empty");

            var charged = new List<PluginDefinition>();
            loadout.CollectChargedPassives(charged);
            Assert.AreEqual(1, charged.Count);
            Assert.AreEqual("L03", charged[0].ProposalId);
        }

        // --- the refusals ------------------------------------------------------------------------

        [Test]
        public void AGaugeThatIsAlreadyFullIsRefused()
        {
            // A requirement of zero is a permanent passive wearing a gauge's name.
            PluginDefinition card = PluginCatalog.FeverTime();
            card.Charge.Required = 0f;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AGaugeNothingFillsIsRefused()
        {
            PluginDefinition card = PluginCatalog.FeverTime();
            card.Charge.PerHit = 0f;
            card.Charge.PerKill = 0f;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AWindowOfNoTimeIsRefused()
        {
            PluginDefinition card = PluginCatalog.FeverTime();
            card.Charge.ActiveTicks = 0;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AChargedPassiveThatBuysNothingIsRefused()
        {
            PluginDefinition card = PluginCatalog.FeverTime();
            card.Passive = PluginPassive.None;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AnotherKindMayNotCarryAGauge()
        {
            PluginDefinition lightweight = PluginCatalog.Lightweight();
            lightweight.Charge = new PluginCharge
            {
                PerHit = 1f, Required = 10f, ActiveTicks = 60,
            };

            Assert.Throws<InvalidOperationException>(() => lightweight.Validate());
        }

        [Test]
        public void AGaugeDoesNotDrainOnAHit()
        {
            PluginDefinition card = PluginCatalog.FeverTime();
            card.Charge.PerHit = -1f;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }
    }
}
