using System;
using System.Collections.Generic;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Core;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// L01 레이저 코어 and L02 부메랑 프로토콜: the two 전설 cards that replace the basic attack.
    /// <para>
    /// Each card alone is fully stated by docs/03 and is held here. Their <b>combination</b> is
    /// <b>[미확정] D-005</b> - docs/03's exception columns say 동시 장착 정책 미확정 for L01 and
    /// L01과 동시 적용 시 여러 발사체 정의 for L02 - so these tests never assert one reading. They
    /// read <see cref="ShotConversionPolicy"/> and hold that the code does what the reading in
    /// force says, which is what keeps approving a different reading a one-constant change.
    /// </para>
    /// </summary>
    public sealed class ShotConversionTests
    {
        private static DeterministicRng Rng() => new DeterministicRng(20_261_123u);

        private static List<MainShotSpec> FormsOf(params PluginDefinition[] cards)
        {
            var loadout = new PluginLoadout();
            for (int i = 0; i < cards.Length; ++i)
                Assert.AreEqual(PluginGrantVerdict.Granted, loadout.TryGrant(cards[i], Rng(), out string why), why);

            var forms = new List<MainShotSpec>();
            loadout.CollectMainShots(forms);
            return forms;
        }

        private static MainShotSpec Only(List<MainShotSpec> forms, MainShotMode mode)
        {
            for (int i = 0; i < forms.Count; ++i)
            {
                if (forms[i].Mode == mode)
                    return forms[i];
            }

            Assert.Fail($"no {mode} form was resolved");
            return MainShotSpec.Ordinary;
        }

        // --- the cards themselves ------------------------------------------------------------

        [Test]
        public void BothCardsAreValidData()
        {
            Assert.DoesNotThrow(() => PluginCatalog.LaserCore().Validate());
            Assert.DoesNotThrow(() => PluginCatalog.BoomerangProtocol().Validate());
        }

        [Test]
        public void BothAreLegendaryAndInTheCatalogue()
        {
            Assert.AreEqual(PluginRarity.Legendary, PluginCatalog.LaserCore().Rarity);
            Assert.AreEqual(PluginRarity.Legendary, PluginCatalog.BoomerangProtocol().Rarity);

            bool laser = false;
            bool boomerang = false;
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
            {
                laser |= all[i].ProposalId == "L01";
                boomerang |= all[i].ProposalId == "L02";
            }

            Assert.IsTrue(laser, "L01 cannot be rolled");
            Assert.IsTrue(boomerang, "L02 cannot be rolled");
        }

        [Test]
        public void TheCardsHaveWordsRatherThanTheirIds()
        {
            // They read (미구현) until this change, which is a reward screen telling the player a
            // 전설 does nothing.
            Assert.IsTrue(PluginCardTexts.Knows("L01"));
            Assert.IsTrue(PluginCardTexts.Knows("L02"));
            Assert.IsFalse(PluginCardTexts.For("L01").Effect.Contains("미구현"));
            Assert.IsFalse(PluginCardTexts.For("L02").Effect.Contains("미구현"));
        }

        [Test]
        public void TheLaserKeepsTheStatedEffectFraction()
        {
            // [원안 수치] docs/03 L01: 적중 효과 효율 70%. The one number the document gives.
            Assert.AreEqual(0.70f, PluginCatalog.StatedLaserOnHitScale, 0.0001f);
            Assert.AreEqual(
                0.70f, PluginCatalog.LaserCore().Firing.MainShotOnHitScale, 0.0001f);
        }

        [Test]
        public void TheBoomerangThrowsOneUntilLevelsExist()
        {
            // docs/03 says 레벨 상승 시 부메랑 수 증가, and plugin levels are not built (docs/03
            // section 3 item 6; the loadout still refuses a duplicate). One is what the card is
            // worth today and the number is data, so the level change is a data change.
            Assert.AreEqual(1, PluginCatalog.StatedBoomerangCountAtFirstLevel);
            Assert.AreEqual(1, PluginCatalog.BoomerangProtocol().Firing.MainShotCount);

            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.BoomerangProtocol(), Rng(), out _);
            Assert.AreEqual(
                PluginGrantVerdict.RejectedAlreadyCarried,
                loadout.TryGrant(PluginCatalog.BoomerangProtocol(), Rng(), out _),
                "a duplicate was accepted, so the count above is not the whole story");
        }

        // --- what the loadout resolves to ----------------------------------------------------

        [Test]
        public void CarryingNeitherLeavesTheOrdinaryShot()
        {
            Assert.IsEmpty(FormsOf(PluginCatalog.Lightweight()));
            Assert.IsEmpty(FormsOf());
        }

        [Test]
        public void TheLaserAloneConvertsTheShot()
        {
            List<MainShotSpec> forms = FormsOf(PluginCatalog.LaserCore());

            Assert.AreEqual(1, forms.Count);
            MainShotSpec laser = Only(forms, MainShotMode.Laser);
            Assert.AreEqual(0.70f, laser.OnHitEffectScale, 0.0001f);
            Assert.Greater(laser.RangeUnits, 0f, "a beam that reaches nothing");
            Assert.IsTrue(laser.ReplacesTheProjectile);
            Assert.IsFalse(laser.HoldsTheAttackUntilItReturns, "a laser does not come back");
        }

        [Test]
        public void TheBoomerangAloneReplacesTheShotAndHoldsTheAttack()
        {
            List<MainShotSpec> forms = FormsOf(PluginCatalog.BoomerangProtocol());

            Assert.AreEqual(1, forms.Count);
            MainShotSpec disc = Only(forms, MainShotMode.Boomerang);
            Assert.AreEqual(1, disc.Count);
            Assert.Greater(disc.AreaRadiusUnits, 0f, "범위 피해 with no reach");
            Assert.GreaterOrEqual(disc.OutboundTicks, 1);
            Assert.IsTrue(disc.HoldsTheAttackUntilItReturns, "귀환 전 추가 기본공격 불가 is the card's cost");
        }

        [Test]
        public void TheLaserCarriesTheUndecidedCombinationFlags()
        {
            // [미확정] D-005: whether R06's extra 탄환 is converted too, and whether the beam
            // ignores 관통. Both are read from the policy, never decided in the card.
            MainShotSpec laser = Only(FormsOf(PluginCatalog.LaserCore()), MainShotMode.Laser);

            Assert.AreEqual(ShotConversionPolicy.DraftLaserConvertsExtraShots, laser.ConvertsExtraShots);
            Assert.AreEqual(ShotConversionPolicy.DraftLaserPiercesEverything, laser.PiercesEverything);
        }

        [Test]
        public void CarryingBothFollowsTheReadingInForce()
        {
            List<MainShotSpec> forms =
                FormsOf(PluginCatalog.LaserCore(), PluginCatalog.BoomerangProtocol());

            bool hasLaser = false;
            bool hasBoomerang = false;
            for (int i = 0; i < forms.Count; ++i)
            {
                hasLaser |= forms[i].Mode == MainShotMode.Laser;
                hasBoomerang |= forms[i].Mode == MainShotMode.Boomerang;
            }

            switch (ShotConversionPolicy.Draft)
            {
                case ShotConversionPolicy.BothCarried.BothForms:
                    Assert.AreEqual(2, forms.Count, "both cards are carried and only one form fires");
                    Assert.IsTrue(hasLaser && hasBoomerang);
                    break;

                case ShotConversionPolicy.BothCarried.LaserReplacesIt:
                    Assert.AreEqual(1, forms.Count);
                    Assert.IsTrue(hasLaser);
                    Assert.IsFalse(hasBoomerang);
                    break;

                case ShotConversionPolicy.BothCarried.BoomerangReplacesIt:
                    Assert.AreEqual(1, forms.Count);
                    Assert.IsFalse(hasLaser);
                    Assert.IsTrue(hasBoomerang);
                    break;

                default:
                    Assert.Fail("an unimplemented reading of D-005 is selected");
                    break;
            }
        }

        [Test]
        public void ThePolicyAndTheLoadoutNeverDisagree()
        {
            // The loadout applies the policy; if either grew its own copy of the rule, the two
            // would drift and the card the player picked up would quietly stop working.
            Assert.IsTrue(ShotConversionPolicy.LaserApplies(true, false));
            Assert.IsFalse(ShotConversionPolicy.LaserApplies(false, true));
            Assert.IsTrue(ShotConversionPolicy.BoomerangApplies(false, true));
            Assert.IsFalse(ShotConversionPolicy.BoomerangApplies(true, false));
            Assert.IsFalse(ShotConversionPolicy.LaserApplies(false, false));
            Assert.IsFalse(ShotConversionPolicy.BoomerangApplies(false, false));
        }

        [Test]
        public void TheUndecidedQuestionIsStatedWhereItCanBeRead()
        {
            // A [미확정] rule nobody can see in a log is a rule that gets approved by accident.
            string summary = ShotConversionPolicy.UndecidedSummary;
            Assert.IsNotEmpty(summary);
            StringAssert.Contains("D-005", summary);
        }

        [Test]
        public void TheLoadoutSaysWhetherAnythingConvertsTheShot()
        {
            var loadout = new PluginLoadout();
            Assert.IsFalse(loadout.ConvertsTheMainShot());

            loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out _);
            Assert.IsFalse(loadout.ConvertsTheMainShot());

            loadout.TryGrant(PluginCatalog.LaserCore(), Rng(), out _);
            Assert.IsTrue(loadout.ConvertsTheMainShot());
        }

        // --- the data refuses a half-written conversion ---------------------------------------

        [Test]
        public void ALaserWithNoRangeIsRefused()
        {
            var firing = new PluginFiring
            {
                ShotConversion = PluginShotConversion.Laser,
                MainShotOnHitScale = 0.7f,
                MainShotCount = 1,
            };

            Assert.Throws<InvalidOperationException>(() => firing.Validate("X01"));
        }

        [Test]
        public void ABoomerangWithNoReachIsRefused()
        {
            var firing = new PluginFiring
            {
                ShotConversion = PluginShotConversion.Boomerang,
                MainShotOnHitScale = 1f,
                MainShotCount = 1,
                MainShotOutboundTicks = 30,
            };

            Assert.Throws<InvalidOperationException>(() => firing.Validate("X02"));
        }

        [Test]
        public void ABoomerangThatNeverTravelsIsRefused()
        {
            var firing = new PluginFiring
            {
                ShotConversion = PluginShotConversion.Boomerang,
                MainShotOnHitScale = 1f,
                MainShotCount = 1,
                MainShotAreaRadiusUnits = 1.5f,
                MainShotOutboundTicks = 0,
            };

            Assert.Throws<InvalidOperationException>(() => firing.Validate("X03"));
        }

        [Test]
        public void ConversionNumbersWithNoConversionAreRefused()
        {
            // Numbers nothing reads are how a card ends up looking configured and doing nothing.
            var firing = new PluginFiring { MainShotRangeUnits = 20f };

            Assert.Throws<InvalidOperationException>(() => firing.Validate("X04"));
        }

        [Test]
        public void AConvertedShotFiredZeroTimesIsRefused()
        {
            var firing = new PluginFiring
            {
                ShotConversion = PluginShotConversion.Laser,
                MainShotOnHitScale = 0.7f,
                MainShotRangeUnits = 20f,
                MainShotCount = 0,
            };

            Assert.Throws<InvalidOperationException>(() => firing.Validate("X05"));
        }

        [Test]
        public void AConversionIsNotAnEmptyFiringBlock()
        {
            // PluginDefinition only allows a FiringModifier to carry one of these, and an empty
            // block would make L01 a card that declares nothing.
            Assert.IsFalse(PluginCatalog.LaserCore().Firing.IsEmpty);
            Assert.IsFalse(PluginCatalog.BoomerangProtocol().Firing.IsEmpty);
            Assert.IsTrue(PluginFiring.None.IsEmpty);
        }

        // --- the spec itself -------------------------------------------------------------------

        [Test]
        public void AnOrdinarySpecReplacesNothing()
        {
            Assert.IsFalse(MainShotSpec.Ordinary.ReplacesTheProjectile);
            Assert.AreEqual(1f, MainShotSpec.Ordinary.OnHitEffectScale, 0.0001f);
            Assert.AreEqual(MainShotMode.Projectile, MainShotSpec.Ordinary.Mode);
        }

        [Test]
        public void ASpecWithNonsenseNumbersFallsBackRatherThanBreakingTheShot()
        {
            // A NaN reaching a projectile is a shot that flies nowhere and damages nobody - a
            // silent no-op in the one system the player feels every second.
            MainShotSpec laser = MainShotSpec.Laser(float.NaN, float.NaN, true, false);
            Assert.AreEqual(1f, laser.OnHitEffectScale, 0.0001f);
            Assert.AreEqual(0f, laser.RangeUnits, 0.0001f);

            MainShotSpec disc = MainShotSpec.Boomerang(-4, float.NegativeInfinity, -9, 2f);
            Assert.AreEqual(1, disc.Count, "a volley that fires a negative number of shots");
            Assert.AreEqual(0f, disc.AreaRadiusUnits, 0.0001f);
            Assert.AreEqual(0, disc.OutboundTicks);
            Assert.AreEqual(1f, disc.OnHitEffectScale, 0.0001f, "a fraction above 1 was kept");
        }
    }
}
