using System;
using System.Collections.Generic;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// R07 자동 추적탄 as data. The steering itself is a scene behaviour and is tested there;
    /// what is here is the card, the refusals, and the fact that the turn rate survives the
    /// trip from the card to the shot specification.
    /// </summary>
    public sealed class SeekerRoundTests
    {
        [Test]
        public void SeekerRoundAddsOneGuidedShot()
        {
            PluginDefinition card = PluginCatalog.SeekerRound();

            Assert.DoesNotThrow(() => card.Validate());
            Assert.AreEqual("R07", card.ProposalId);
            Assert.AreEqual(PluginRarity.Rare, card.Rarity);
            Assert.AreEqual(PluginEffectKind.FiringModifier, card.Effect);
            Assert.AreEqual(PluginTriggerKind.None, card.Trigger);

            Assert.AreEqual(1, card.Firing.ExtraShotsPerVolley, "유도탄 발사 fires nothing");
            Assert.Greater(
                card.Firing.ExtraShotHomingTurnDegreesPerTick, 0f,
                "the shot it adds does not steer, so it is 보조 탄환 under another name");
        }

        [Test]
        public void ItAddsAShotRatherThanRedefiningEveryShot()
        {
            // The additive reading is what keeps this card out of D-005: 여러 탄환 형태의 합성
            // is the open question L01 레이저 코어 and L02 부메랑 프로토콜 are blocked on, and a
            // card that redefined the basic attack would have to answer it.
            PluginDefinition card = PluginCatalog.SeekerRound();

            Assert.IsTrue(card.Firing.AddsExtraShots);
            Assert.IsFalse(card.Firing.FiresBursts, "R07 is not a burst card");
            Assert.IsTrue(card.Passive.IsEmpty, "R07 contributes no stat");
        }

        [Test]
        public void NoOtherCardSteersItsShots()
        {
            // R06 보조 탄환 is the card R07 would otherwise be a duplicate of, and G13's burst
            // is 추가 연사. Guiding either would hand R07's behaviour to a card without it.
            Assert.AreEqual(
                0f, PluginCatalog.SupportRound().Firing.ExtraShotHomingTurnDegreesPerTick,
                "R06 보조 탄환 guides its shot");
            Assert.AreEqual(
                0f, PluginCatalog.QuadCache().Firing.ExtraShotHomingTurnDegreesPerTick,
                "G13 4중 캐시 guides its burst");
        }

        // --- the refusals ---------------------------------------------------------------------

        [Test]
        public void ATurnRateWithNoShotsToGuideIsRefused()
        {
            // The silent-no-op shape: a card that reads as 유도탄 발사 and fires nothing.
            var hollow = new PluginFiring { ExtraShotHomingTurnDegreesPerTick = 4f };
            Assert.Throws<InvalidOperationException>(() => hollow.Validate("TEST"));
        }

        [Test]
        public void ANonsenseTurnRateIsRefused()
        {
            PluginDefinition card = PluginCatalog.SeekerRound();

            card.Firing.ExtraShotHomingTurnDegreesPerTick = -1f;
            Assert.Throws<InvalidOperationException>(() => card.Validate());

            card.Firing.ExtraShotHomingTurnDegreesPerTick = float.NaN;
            Assert.Throws<InvalidOperationException>(() => card.Validate());
        }

        // --- the trip to the shot ---------------------------------------------------------------

        [Test]
        public void TheTurnRateSurvivesTheTripFromCardToShotSpec()
        {
            // The gap this closes: every piece can be right while the rate is dropped between
            // the loadout and the attacker, and the card then fires an ordinary extra shot that
            // nothing distinguishes from R06's.
            var loadout = new PluginLoadout();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.SeekerRound(), out string why), why);

            var shots = new List<ExtraShotSpec>();
            loadout.CollectExtraShots(shots);

            Assert.AreEqual(1, shots.Count);
            Assert.IsTrue(shots[0].IsHoming, "the extra shot arrived without its turn rate");
            Assert.AreEqual(
                PluginCatalog.DraftSeekerTurnDegreesPerTick,
                shots[0].HomingTurnDegreesPerTick, 0.0001f);
        }

        [Test]
        public void AnOrdinaryExtraShotIsNotGuided()
        {
            var loadout = new PluginLoadout();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.SupportRound(), out string why), why);

            var shots = new List<ExtraShotSpec>();
            loadout.CollectExtraShots(shots);

            Assert.AreEqual(1, shots.Count);
            Assert.IsFalse(shots[0].IsHoming);
        }

        [Test]
        public void AShotSpecRefusesANonsenseTurnRate()
        {
            // The spec is built at runtime from card data, so it sanitises rather than throwing:
            // a NaN reaching the steering would turn the shot into a direction of NaN and it
            // would stop hitting anything at all, silently.
            Assert.AreEqual(0f, new ExtraShotSpec(1, 1f, float.NaN).HomingTurnDegreesPerTick);
            Assert.AreEqual(0f, new ExtraShotSpec(1, 1f, -3f).HomingTurnDegreesPerTick);
            Assert.AreEqual(
                0f, new ExtraShotSpec(1, 1f, float.PositiveInfinity).HomingTurnDegreesPerTick);
        }
    }
}
