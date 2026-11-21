using System;
using NeonVoid.Combat;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// D-004: 보호막 여러 개의 피해 흡수·만료·흡혈 순서.
    /// <para>
    /// docs/02 section 7 leaves 흡수 순서, 갱신·합산 and 만료 판정 [미확정], and docs/08's safe
    /// handling is an independent, tested strategy with the live connection held until the
    /// decision is approved. So these tests do not assert a rule - they assert that <b>every
    /// reading the document names behaves as that reading says</b>, which is what makes
    /// approving one a constant rather than a rewrite.
    /// </para>
    /// <para>
    /// D-004's approval condition is 2개 이상 보호막 예시 승인, and
    /// <see cref="TheTwoShieldExampleForApproval"/> is that example, written out so the numbers
    /// can be agreed or corrected directly.
    /// </para>
    /// </summary>
    public sealed class ShieldStackTests
    {
        private const string Periodic = "G05";   // 주기 차폐막
        private const string OnSkill = "G07";    // 실행 차폐
        private const string OnDash = "R05";     // 돌진 차폐

        private static ShieldStack With(ShieldAbsorptionOrder order, ShieldRefreshRule refresh = ShieldRefreshRule.Separate)
        {
            ShieldPolicy policy = ShieldPolicy.Draft();
            policy.Order = order;
            policy.Refresh = refresh;
            return new ShieldStack(policy);
        }

        // --- 흡수 순서 -----------------------------------------------------------------------

        [Test]
        public void NewestFirstEatsTheShieldGrantedLast()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
            shields.Grant(Periodic, 30f, nowTick: 0L, lifetimeTicks: 0);
            shields.Grant(OnSkill, 20f, nowTick: 100L, lifetimeTicks: 0);

            ShieldAbsorption hit = shields.Absorb(25f, nowTick: 120L);

            Assert.AreEqual(25f, hit.Absorbed, 0.001f);
            Assert.AreEqual(0f, hit.Remainder, 0.001f);
            Assert.AreEqual(1, hit.LayersBroken, "the newer 20 should have broken and taken 5 off the older");
            Assert.AreEqual(25f, shields.Total, 0.001f);
            Assert.AreEqual(Periodic, shields.LayerAt(0).SourceId);
        }

        [Test]
        public void OldestFirstEatsTheShieldGrantedFirst()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.OldestFirst);
            shields.Grant(Periodic, 30f, nowTick: 0L, lifetimeTicks: 0);
            shields.Grant(OnSkill, 20f, nowTick: 100L, lifetimeTicks: 0);

            ShieldAbsorption hit = shields.Absorb(25f, nowTick: 120L);

            Assert.AreEqual(25f, hit.Absorbed, 0.001f);
            Assert.AreEqual(0, hit.LayersBroken, "the older 30 should have swallowed all 25 by itself");
            Assert.AreEqual(5f, shields.LayerAt(0).Remaining, 0.001f);
            Assert.AreEqual(20f, shields.LayerAt(1).Remaining, 0.001f);
        }

        [Test]
        public void ExpiringSoonestFirstSpendsWhatWouldBeWastedAnyway()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.ExpiringSoonestFirst);
            shields.Grant(Periodic, 30f, nowTick: 0L, lifetimeTicks: 1000);
            shields.Grant(OnSkill, 20f, nowTick: 10L, lifetimeTicks: 50);      // expires at 60

            ShieldAbsorption hit = shields.Absorb(25f, nowTick: 20L);

            Assert.AreEqual(1, hit.LayersBroken, "the one about to expire should have gone first");
            Assert.AreEqual(25f, shields.Total, 0.001f);
            Assert.AreEqual(Periodic, shields.LayerAt(0).SourceId);
        }

        [Test]
        public void TheOrderChangesNothingAboutHowMuchIsAbsorbed()
        {
            // Whatever the decision is, the total swallowed by one hit is the same. If the
            // readings differed here the choice would be a balance change rather than an
            // ordering, and the approval would be about the wrong thing.
            foreach (ShieldAbsorptionOrder order in Enum.GetValues(typeof(ShieldAbsorptionOrder)))
            {
                ShieldStack shields = With(order);
                shields.Grant(Periodic, 30f, 0L, 0);
                shields.Grant(OnSkill, 20f, 100L, 0);

                ShieldAbsorption hit = shields.Absorb(42f, 120L);

                Assert.AreEqual(42f, hit.Absorbed, 0.001f, $"{order} absorbed a different amount");
                Assert.AreEqual(0f, hit.Remainder, 0.001f, $"{order} let damage through");
                Assert.AreEqual(8f, shields.Total, 0.001f, $"{order} left a different amount standing");
            }
        }

        // --- 갱신·합산 -----------------------------------------------------------------------

        [Test]
        public void SeparateLeavesTwoShieldsFromOneSource()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst, ShieldRefreshRule.Separate);
            shields.Grant(Periodic, 30f, 0L, 0);
            shields.Grant(Periodic, 30f, 100L, 0);

            Assert.AreEqual(2, shields.LayerCount);
            Assert.AreEqual(60f, shields.Total, 0.001f);
        }

        [Test]
        public void ReplaceThrowsAwayWhatWasLeftOfTheFirst()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst, ShieldRefreshRule.Replace);
            shields.Grant(Periodic, 30f, 0L, 0);
            shields.Absorb(25f, 10L);                 // 5 left
            shields.Grant(Periodic, 30f, 100L, 0);

            Assert.AreEqual(1, shields.LayerCount);
            Assert.AreEqual(30f, shields.Total, 0.001f, "the leftover 5 should have been discarded, not kept");
        }

        [Test]
        public void MergeAddsToWhatWasLeft()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst, ShieldRefreshRule.Merge);
            shields.Grant(Periodic, 30f, 0L, 0);
            shields.Absorb(25f, 10L);                 // 5 left
            shields.Grant(Periodic, 30f, 100L, 0);

            Assert.AreEqual(1, shields.LayerCount);
            Assert.AreEqual(35f, shields.Total, 0.001f);
        }

        [Test]
        public void MergeKeepsTheOlderPlaceInTheQueue()
        {
            // Otherwise a card that re-shields every few seconds renews its own place at the
            // front of a newest-first queue for free, and the ordering decision stops meaning
            // anything for exactly the cards it is about.
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst, ShieldRefreshRule.Merge);
            shields.Grant(Periodic, 10f, 0L, 0);
            shields.Grant(OnSkill, 10f, 50L, 0);
            shields.Grant(Periodic, 10f, 100L, 0);    // merges into the layer granted at 0

            ShieldAbsorption hit = shields.Absorb(10f, 120L);

            Assert.AreEqual(1, hit.LayersBroken);
            Assert.AreEqual(Periodic, shields.LayerAt(0).SourceId, "the merged layer should still be standing");
            Assert.AreEqual(20f, shields.Total, 0.001f, "the newest layer (G07) should have been eaten");
        }

        [Test]
        public void ADifferentSourceIsNeverARefresh()
        {
            foreach (ShieldRefreshRule rule in Enum.GetValues(typeof(ShieldRefreshRule)))
            {
                ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst, rule);
                shields.Grant(Periodic, 10f, 0L, 0);
                shields.Grant(OnSkill, 10f, 10L, 0);
                shields.Grant(OnDash, 10f, 20L, 0);

                Assert.AreEqual(3, shields.LayerCount, $"{rule} merged shields from different cards");
                Assert.AreEqual(30f, shields.Total, 0.001f);
            }
        }

        // --- 만료 -----------------------------------------------------------------------------

        [Test]
        public void AShieldIsGoneOnTheTickItsTimerSays()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
            shields.Grant(OnSkill, 20f, nowTick: 0L, lifetimeTicks: 60);

            Assert.AreEqual(0, shields.Expire(59L), "it expired a tick early");
            Assert.AreEqual(1, shields.Expire(60L), "it did not expire on its own tick");
            Assert.IsFalse(shields.HasAny);
        }

        [Test]
        public void TheExpiryTickItselfIsAnOpenQuestionAndBothReadingsWork()
        {
            ShieldPolicy inclusive = ShieldPolicy.Draft();
            inclusive.AbsorbsOnTheExpiryTick = true;

            var lenient = new ShieldStack(inclusive);
            lenient.Grant(OnSkill, 20f, 0L, 60);

            Assert.AreEqual(0, lenient.Expire(60L), "the lenient reading dropped it on its last tick");

            // A partial hit, so the shield is still standing to be expired a tick later -
            // spending it entirely here would remove the layer and prove nothing about time.
            Assert.AreEqual(5f, lenient.Absorb(5f, 60L).Absorbed, 0.001f);
            Assert.AreEqual(15f, lenient.Total, 0.001f);

            Assert.AreEqual(1, lenient.Expire(61L), "it outlived the tick after its expiry");
            Assert.IsFalse(lenient.HasAny);
        }

        [Test]
        public void AnExpiredShieldAbsorbsNothingEvenIfNobodyCalledExpire()
        {
            // A shield that was never hit must not outlive its timer just because the expiry
            // pass happened to run after the hit.
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
            shields.Grant(OnSkill, 20f, 0L, 60);

            ShieldAbsorption hit = shields.Absorb(20f, nowTick: 100L);

            Assert.AreEqual(0f, hit.Absorbed, 0.001f, "an expired shield swallowed a hit");
            Assert.AreEqual(20f, hit.Remainder, 0.001f);
        }

        [Test]
        public void AShieldWithNoTimerNeverExpires()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
            shields.Grant(Periodic, 10f, 0L, lifetimeTicks: 0);

            Assert.AreEqual(0, shields.Expire(long.MaxValue - 1));
            Assert.AreEqual(10f, shields.Total, 0.001f);
        }

        // --- the hit itself --------------------------------------------------------------------

        [Test]
        public void DamageBeyondEveryShieldGoesThroughToHealth()
        {
            // docs/02 section 3: actualHealthDamage = max(0, appliedBeforeShield - shieldAbsorbed).
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
            shields.Grant(Periodic, 10f, 0L, 0);
            shields.Grant(OnSkill, 5f, 10L, 0);

            ShieldAbsorption hit = shields.Absorb(40f, 20L);

            Assert.AreEqual(15f, hit.Absorbed, 0.001f);
            Assert.AreEqual(25f, hit.Remainder, 0.001f);
            Assert.AreEqual(2, hit.LayersBroken);
            Assert.IsFalse(shields.HasAny);
        }

        [Test]
        public void NoShieldsMeansTheHitPassesStraightThrough()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
            ShieldAbsorption hit = shields.Absorb(12f, 0L);

            Assert.AreEqual(0f, hit.Absorbed, 0.001f);
            Assert.AreEqual(12f, hit.Remainder, 0.001f);
        }

        [Test]
        public void AHitOfNothingDoesNothing()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
            shields.Grant(Periodic, 10f, 0L, 0);

            Assert.AreEqual(0f, shields.Absorb(0f, 0L).Absorbed, 0.001f);
            Assert.AreEqual(10f, shields.Total, 0.001f, "a hit of nothing ate into a shield");
        }

        [Test]
        public void AShieldOfNothingIsRefused()
        {
            ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);

            Assert.Throws<ArgumentOutOfRangeException>(() => shields.Grant(Periodic, 0f, 0L, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => shields.Grant(Periodic, -5f, 0L, 0));
            Assert.Throws<ArgumentException>(() => shields.Grant("  ", 5f, 0L, 0));
        }

        [Test]
        public void TwoShieldsGrantedOnTheSameTickAreChosenBetweenDeterministically()
        {
            // The tiebreak is the order they were granted in. Anything that could differ on a
            // replay would make the run non-reproducible, which is the same hazard that made
            // AKillInAnOpenedRoomPaysItsMissionXp flaky.
            for (int run = 0; run < 2; ++run)
            {
                ShieldStack shields = With(ShieldAbsorptionOrder.NewestFirst);
                shields.Grant(Periodic, 10f, 50L, 0);
                shields.Grant(OnSkill, 10f, 50L, 0);

                shields.Absorb(10f, 60L);

                Assert.AreEqual(1, shields.LayerCount);
                Assert.AreEqual(
                    OnSkill, shields.LayerAt(0).SourceId,
                    "a tie on the grant tick was not broken by grant order");
            }
        }

        // --- the example D-004 asks to approve -------------------------------------------------

        [Test]
        public void TheTwoShieldExampleForApproval()
        {
            // D-004 승인 조건: 2개 이상 보호막 예시 승인. This is that example under the
            // provisional reading in ShieldPolicy.Draft() - newest first, separate shields,
            // gone on the expiry tick. It is NOT an approved decision; it is written out so
            // the numbers can be agreed or corrected.
            //
            //   t=0    G05 주기 차폐막 grants 30, lasting 300 ticks (expires t=300)
            //   t=120  G07 실행 차폐 grants 20, lasting 120 ticks  (expires t=240)
            //   t=130  a hit of 25 lands
            //   t=240  G07's shield would have expired
            //   t=250  a hit of 40 lands
            var shields = new ShieldStack(ShieldPolicy.Draft());

            shields.Grant(Periodic, 30f, nowTick: 0L, lifetimeTicks: 300);
            shields.Grant(OnSkill, 20f, nowTick: 120L, lifetimeTicks: 120);
            Assert.AreEqual(50f, shields.Total, 0.001f, "two shields stand, 50 between them");

            ShieldAbsorption first = shields.Absorb(25f, nowTick: 130L);
            Assert.AreEqual(25f, first.Absorbed, 0.001f);
            Assert.AreEqual(0f, first.Remainder, 0.001f, "nothing reached health");
            Assert.AreEqual(1, first.LayersBroken, "G07's 20 broke and G05 covered the last 5");
            Assert.AreEqual(25f, shields.Total, 0.001f, "G05 has 25 of its 30 left");

            Assert.AreEqual(0, shields.Expire(240L), "G07 was already spent, so nothing expires");

            ShieldAbsorption second = shields.Absorb(40f, nowTick: 250L);
            Assert.AreEqual(25f, second.Absorbed, 0.001f, "G05's remaining 25");
            Assert.AreEqual(15f, second.Remainder, 0.001f, "15 reaches health");
            Assert.AreEqual(1, second.LayersBroken);
            Assert.IsFalse(shields.HasAny, "both shields are gone");
        }
    }
}
