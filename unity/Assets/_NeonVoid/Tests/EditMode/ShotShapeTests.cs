using System;
using NeonVoid.Combat.Stats;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// G06 스로틀 증폭 and G03 확장 탄두: the two cards that change the shape of the basic
    /// attack rather than a stat.
    /// <para>
    /// They are not stats because docs/02's stat block does not have 공격속도, 탄환속도 or
    /// 탄환 크기 - the attack rate lives on the attacker and the shot's size on the shot. The
    /// scene tests prove the fractions reach the sweep; these hold the arithmetic and the
    /// refusals.
    /// </para>
    /// </summary>
    public sealed class ShotShapeTests
    {
        // --- the cards themselves ------------------------------------------------------------

        [Test]
        public void BothCardsAreUsableAsWritten()
        {
            Assert.DoesNotThrow(() => PluginCatalog.ThrottleAmplifier().Validate());
            Assert.DoesNotThrow(() => PluginCatalog.ExpandedWarhead().Validate());

            Assert.AreEqual(PluginTriggerKind.None, PluginCatalog.ThrottleAmplifier().Trigger);
            Assert.AreEqual(PluginEffectKind.Passive, PluginCatalog.ThrottleAmplifier().Effect);
            Assert.AreEqual(PluginTriggerKind.None, PluginCatalog.ExpandedWarhead().Trigger);
            Assert.AreEqual(PluginEffectKind.Passive, PluginCatalog.ExpandedWarhead().Effect);
        }

        [Test]
        public void ThrottleAmplifierRaisesBothHalvesAndNothingElse()
        {
            // docs/03 G06 is 공격속도 + 탄환속도 증가 and nothing more. A card that also moved a
            // stat would be a different card from the one the document describes.
            PluginPassive passive = PluginCatalog.ThrottleAmplifier().Passive;

            Assert.Greater(passive.AttackSpeedFraction, 0f);
            Assert.Greater(passive.ProjectileSpeedFraction, 0f);
            Assert.AreEqual(0f, passive.ProjectileSizeFraction);
            Assert.AreEqual(0f, passive.MoveSpeedFraction);
            Assert.AreEqual(0, passive.RandomStatCount);
            Assert.IsTrue(
                passive.StatModifiers == null || passive.StatModifiers.Length == 0,
                "G06 contributes a stat the document does not give it");
        }

        [Test]
        public void ExpandedWarheadGrowsTheShotAndNothingElse()
        {
            PluginPassive passive = PluginCatalog.ExpandedWarhead().Passive;

            Assert.Greater(passive.ProjectileSizeFraction, 0f);
            Assert.AreEqual(0f, passive.AttackSpeedFraction);
            Assert.AreEqual(0f, passive.ProjectileSpeedFraction);
            Assert.AreEqual(0f, passive.MoveSpeedFraction);
        }

        [Test]
        public void NeitherCardIsEmpty()
        {
            // IsEmpty decides whether a carried card contributes anything at all, so a new kind
            // of contribution it did not know about would be a card that is silently inert.
            Assert.IsFalse(PluginCatalog.ThrottleAmplifier().Passive.IsEmpty);
            Assert.IsFalse(PluginCatalog.ExpandedWarhead().Passive.IsEmpty);
        }

        // --- the refusals ---------------------------------------------------------------------

        [Test]
        public void AShotMayNotBeShrunk()
        {
            // 확장 탄두 only ever grows. A negative fraction retuned in later would shrink the
            // collision radius towards zero, and a shot of no size passes through everything -
            // which would read as the card doing nothing rather than as a bad number.
            var shrinking = new PluginPassive { ProjectileSizeFraction = -0.5f };
            Assert.Throws<InvalidOperationException>(() => shrinking.Validate());
        }

        [Test]
        public void NoShapeFractionMayBeNonsense()
        {
            Assert.Throws<InvalidOperationException>(
                () => new PluginPassive { AttackSpeedFraction = float.NaN }.Validate());
            Assert.Throws<InvalidOperationException>(
                () => new PluginPassive { ProjectileSpeedFraction = float.PositiveInfinity }.Validate());
            Assert.Throws<InvalidOperationException>(
                () => new PluginPassive { ProjectileSizeFraction = float.NaN }.Validate());
        }

        [Test]
        public void ASlowingFractionIsAllowedBecauseADebuffIsNotABadNumber()
        {
            // Only the size is one-way. A negative attack speed fraction is what a future
            // 기능고장-style card would carry, and refusing it here would decide that no such
            // card may exist.
            Assert.DoesNotThrow(() => new PluginPassive { AttackSpeedFraction = -0.2f }.Validate());
        }

        // --- the loadout ----------------------------------------------------------------------

        [Test]
        public void CarriedCardsAddTheirFractionsUp()
        {
            var loadout = new PluginLoadout();
            Assert.AreEqual(0f, loadout.PassiveAttackSpeedFraction());
            Assert.AreEqual(0f, loadout.PassiveProjectileSizeFraction());

            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.ThrottleAmplifier(), out string why), why);
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.ExpandedWarhead(), out why), why);

            Assert.AreEqual(
                PluginCatalog.DraftThrottleAttackSpeedFraction, loadout.PassiveAttackSpeedFraction(), 0.0001f);
            Assert.AreEqual(
                PluginCatalog.DraftThrottleProjectileSpeedFraction,
                loadout.PassiveProjectileSpeedFraction(), 0.0001f);
            Assert.AreEqual(
                PluginCatalog.DraftWarheadSizeFraction, loadout.PassiveProjectileSizeFraction(), 0.0001f);
        }

        [Test]
        public void ACardThatContributesNoShapeContributesNoShape()
        {
            // 경량화 moves the player, not the weapon. Asserted because the aggregation walks
            // every carried card, so a field left on the wrong one would be invisible.
            var loadout = new PluginLoadout();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.Lightweight(), out string why), why);

            Assert.AreEqual(0f, loadout.PassiveAttackSpeedFraction());
            Assert.AreEqual(0f, loadout.PassiveProjectileSpeedFraction());
            Assert.AreEqual(0f, loadout.PassiveProjectileSizeFraction());
            Assert.Greater(loadout.PassiveMoveSpeedFraction(), 0f);
        }

        // --- the cap --------------------------------------------------------------------------

        [Test]
        public void TheAttackSpeedCapStillBoundsTheFraction()
        {
            // [확정] docs/02 section 3: a cap exists. The fraction is added to the status
            // multiplier rather than compounded with it, so it is a fraction of the base in the
            // same sense every other contribution is - and it still goes through the cap.
            float cap = StatCaps.Draft().MaxAttacksPerSecond;
            const float Base = 2f;

            int uncapped = AttackRateMath.IntervalTicksFor(Base, 1f + 0.15f, cap);
            int plain = AttackRateMath.IntervalTicksFor(Base, 1f, cap);
            Assert.Less(uncapped, plain, "the fraction did not shorten the interval at all");

            int huge = AttackRateMath.IntervalTicksFor(Base, 1f + 100f, cap);
            Assert.AreEqual(
                AttackRateMath.IntervalTicks(cap), huge,
                "a large stack of fractions went past the cap");
            Assert.GreaterOrEqual(huge, 1, "the interval fell below a tick");
        }
    }
}
