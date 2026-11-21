using System;
using NeonVoid.UI;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// What the HUD means by the numbers it draws, without a panel.
    /// <para>
    /// The arithmetic is separated from the writing so that both can fail independently. A
    /// bar computed right and never written, or written and never computed, are the two ways
    /// a HUD lies - and a test of "the HUD exists" catches neither.
    /// </para>
    /// </summary>
    public sealed class HudSnapshotTests
    {
        private static HudSnapshot Vitals(float health, float healthMax, float mana, float manaMax) =>
            new HudSnapshot(
                health, healthMax, mana, manaMax, 0f, 0,
                default, default, default,
                false, string.Empty, 0, 0,
                false, string.Empty, 0f, 0f,
                0f, 0f, false);

        // --- pools ---------------------------------------------------------------------------

        [Test]
        public void APoolReadsAsItsFraction()
        {
            HudSnapshot snapshot = Vitals(75f, 100f, 30f, 120f);

            Assert.AreEqual(0.75f, snapshot.HealthFraction, 0.0001f);
            Assert.AreEqual(0.25f, snapshot.ManaFraction, 0.0001f);
        }

        [Test]
        public void AnEmptyMaximumReadsAsEmptyAndNotAsFull()
        {
            // The most misleading thing a HUD could show: a dead or unconfigured actor at 100%.
            HudSnapshot snapshot = Vitals(0f, 0f, 0f, 0f);

            Assert.AreEqual(0f, snapshot.HealthFraction);
            Assert.AreEqual(0f, snapshot.ManaFraction);
        }

        [Test]
        public void AFractionNeverLeavesItsBar()
        {
            Assert.AreEqual(1f, Vitals(500f, 100f, 0f, 1f).HealthFraction, 0.0001f);
            Assert.AreEqual(0f, Vitals(-20f, 100f, 0f, 1f).HealthFraction, 0.0001f);
            Assert.AreEqual(0f, Vitals(float.NaN, 100f, 0f, 1f).HealthFraction, 0.0001f);
        }

        [Test]
        public void APoolsLabelIsRounded()
        {
            Assert.AreEqual("75 / 100", HudSnapshot.Pool(74.6f, 100f));
            Assert.AreEqual("0 / 100", HudSnapshot.Pool(-3f, 100f));
        }

        // --- 보호막 ----------------------------------------------------------------------------

        [Test]
        public void OneShieldShowsItsAmountAndSeveralShowTheCount()
        {
            // The layer count is only worth the space when it tells the player something they
            // cannot already see, which is when there is more than one.
            var one = new HudSnapshot(
                1f, 1f, 1f, 1f, 40f, 1, default, default, default,
                false, string.Empty, 0, 0, false, string.Empty, 0f, 0f, 0f, 0f, false);
            var two = new HudSnapshot(
                1f, 1f, 1f, 1f, 75f, 2, default, default, default,
                false, string.Empty, 0, 0, false, string.Empty, 0f, 0f, 0f, 0f, false);

            Assert.AreEqual("40", one.ShieldLine);
            Assert.AreEqual("75 (2)", two.ShieldLine);
            Assert.IsTrue(one.HasShield);
        }

        [Test]
        public void NoShieldIsNotDrawn()
        {
            Assert.IsFalse(Vitals(1f, 1f, 1f, 1f).HasShield);
        }

        // --- skills --------------------------------------------------------------------------

        [Test]
        public void ASkillBarFillsBackUpAsItsCooldownRunsDown()
        {
            // The direction a player reads as "becoming usable". A bar that emptied would say
            // the opposite with the same numbers.
            var justCast = new HudSkillSlot(true, 300, 300);
            var halfway = new HudSkillSlot(true, 150, 300);
            var ready = new HudSkillSlot(true, 0, 300);

            Assert.AreEqual(0f, justCast.ReadyFraction, 0.0001f);
            Assert.AreEqual(0.5f, halfway.ReadyFraction, 0.0001f);
            Assert.AreEqual(1f, ready.ReadyFraction, 0.0001f);
            Assert.IsTrue(ready.IsReady);
            Assert.IsFalse(halfway.IsReady);
        }

        [Test]
        public void AnEmptySlotIsNeitherReadyNorFull()
        {
            var empty = new HudSkillSlot(false, 0, 0);

            Assert.IsFalse(empty.IsReady);
            Assert.AreEqual(0f, empty.ReadyFraction, 0.0001f, "an empty slot read as a ready one");
        }

        [Test]
        public void TheSecondsLeftRoundUpSoAlmostReadyStillReadsAsOne()
        {
            Assert.AreEqual(0, new HudSkillSlot(true, 0, 300).SecondsLeft);
            Assert.AreEqual(1, new HudSkillSlot(true, 1, 300).SecondsLeft);
            Assert.AreEqual(1, new HudSkillSlot(true, 60, 300).SecondsLeft);
            Assert.AreEqual(2, new HudSkillSlot(true, 61, 300).SecondsLeft);
        }

        [Test]
        public void ASkillWithNoStatedCooldownIsSimplyReady()
        {
            Assert.AreEqual(1f, new HudSkillSlot(true, 0, 0).ReadyFraction, 0.0001f);
        }

        // --- the area -------------------------------------------------------------------------

        [Test]
        public void TheObjectiveLineShowsTheTotalEvenAtZero()
        {
            var snapshot = new HudSnapshot(
                1f, 1f, 1f, 1f, 0f, 0, default, default, default,
                true, "Cpu", 0, 3, false, string.Empty, 0f, 0f, 0f, 0f, false);

            Assert.AreEqual("0 / 3", snapshot.ObjectiveLine);
            Assert.IsTrue(snapshot.InArea);
        }

        [Test]
        public void TheGaugesAreClampedAndTheBossFractionBehavesLikeAPool()
        {
            var snapshot = new HudSnapshot(
                1f, 1f, 1f, 1f, 0f, 0, default, default, default,
                true, "Hdd", 1, 3,
                true, "섹터 콜로서스", 250f, 500f,
                1.4f, -0.2f, true);

            Assert.AreEqual(0.5f, snapshot.BossHealthFraction, 0.0001f);
            Assert.AreEqual(1f, snapshot.PurificationProgress, 0.0001f);
            Assert.AreEqual(0f, snapshot.ExtractionProgress, 0.0001f);
            Assert.IsTrue(snapshot.ExtractionArmed);
        }

        [Test]
        public void ThereAreExactlyThreeSkillSlots()
        {
            // [확정] docs/03 section 6: 별도 3슬롯 액티브 스킬.
            Assert.AreEqual(3, HudSnapshot.SkillSlots);

            HudSnapshot snapshot = Vitals(1f, 1f, 1f, 1f);
            Assert.DoesNotThrow(() => snapshot.SkillAt(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.SkillAt(3));
        }
    }
}
