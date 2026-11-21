using System;
using System.Collections.Generic;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// R03 위기 반발 as data. The shove itself is a scene behaviour and is tested there; here
    /// are the card, the threshold and the refusals.
    /// </summary>
    public sealed class CrisisRepulseTests
    {
        [Test]
        public void CrisisRepulseFiresAtTheDocumentsOwnThreshold()
        {
            PluginDefinition card = PluginCatalog.CrisisRepulse();

            Assert.DoesNotThrow(() => card.Validate());
            Assert.AreEqual("R03", card.ProposalId);
            Assert.AreEqual(PluginRarity.Rare, card.Rarity);
            Assert.AreEqual(PluginTriggerKind.OnOwnerLowHealth, card.Trigger);
            Assert.AreEqual(PluginEffectKind.KnockbackAroundOwner, card.Effect);

            // [원안 수치] docs/03 R03: 플레이어 HP 30% 이하.
            Assert.AreEqual(0.30f, card.OwnerHealthThresholdFraction, 0.0001f);
        }

        [Test]
        public void ItShovesAndDealsNothing()
        {
            // 주변 적 넉백 and nothing else. A knockback that also hurt would be a rare that
            // does two things, one of which nobody wrote down.
            PluginDefinition card = PluginCatalog.CrisisRepulse();

            Assert.Greater(card.KnockbackUnits, 0f);
            Assert.Greater(card.RadiusUnits, 0f, "주변 적 needs a reach");
            Assert.AreEqual(0f, card.Damage.Flat, "it deals flat damage");
            Assert.AreEqual(0f, card.Damage.AdRatio, "it scales off AD");
            Assert.AreEqual(0f, card.Damage.ApRatio, "it scales off AP");
        }

        [Test]
        public void ItCarriesAnInternalCooldownEvenThoughTheDocumentLeavesItOpen()
        {
            // 내부 CD is [미확정]. A card with none would fire on every hit taken while low,
            // which is precisely the situation it exists for - so the draft is a cooldown
            // rather than no cooldown, and it is one constant to change.
            Assert.Greater(
                PluginCatalog.CrisisRepulse().Rule.InternalCooldownTicks, 0,
                "위기 반발 has no internal cooldown, so being cornered is a permanent shove field");
        }

        [Test]
        public void TheLoadoutListsItAmongTheLowHealthCards()
        {
            var loadout = new PluginLoadout();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.CrisisRepulse(), out string why), why);

            var lowHealth = new List<PluginDefinition>();
            loadout.CollectTriggeredByOwnerLowHealth(lowHealth);

            Assert.AreEqual(1, lowHealth.Count);
            Assert.AreEqual("R03", lowHealth[0].ProposalId);
        }

        [Test]
        public void NoOtherCardFiresOnTheCarriersHealth()
        {
            var loadout = new PluginLoadout();
            PluginDefinition[] others =
            {
                PluginCatalog.ArcChain(),
                PluginCatalog.DetonationGlyph(),
                PluginCatalog.CorrosionMark(),
                PluginCatalog.ExecutionAlgorithm(),
                PluginCatalog.KillStack(),
                PluginCatalog.SkillResponse(),
            };

            for (int i = 0; i < others.Length; ++i)
                loadout.TryGrant(others[i], out _);

            var lowHealth = new List<PluginDefinition>();
            loadout.CollectTriggeredByOwnerLowHealth(lowHealth);

            Assert.IsEmpty(lowHealth, "a card other than R03 fires on the carrier's own health");
        }

        // --- the refusals ---------------------------------------------------------------------

        [Test]
        public void AThresholdThatIsNotAFractionIsRefused()
        {
            // Above 1 fires always, at or below 0 fires never. Both are cards that look like
            // 위기 반발 and are not.
            PluginDefinition card = PluginCatalog.CrisisRepulse();

            card.OwnerHealthThresholdFraction = 0f;
            Assert.Throws<InvalidOperationException>(() => card.Validate());

            card.OwnerHealthThresholdFraction = 1.5f;
            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AnotherTriggerMayNotCarryAThreshold()
        {
            PluginDefinition chain = PluginCatalog.ArcChain();
            chain.OwnerHealthThresholdFraction = 0.3f;

            Assert.Throws<InvalidOperationException>(() => chain.Validate());
        }

        [Test]
        public void AShoveOfNoDistanceIsRefused()
        {
            // The silent no-op: the effect resolves, the queue counts it, nobody moves.
            PluginDefinition card = PluginCatalog.CrisisRepulse();
            card.KnockbackUnits = 0f;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AShoveWithNoReachIsRefused()
        {
            PluginDefinition card = PluginCatalog.CrisisRepulse();
            card.RadiusUnits = 0f;

            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        [Test]
        public void AnotherEffectMayNotShove()
        {
            PluginDefinition mark = PluginCatalog.CorrosionMark();
            mark.KnockbackUnits = 3f;

            Assert.Throws<InvalidOperationException>(() => mark.Validate());
        }
    }
}
