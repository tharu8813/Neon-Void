using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// [확정] D-006 (B. 곡선) and D-007, both approved 2026-11-22.
    /// <para>
    /// The distribution is what decides how a run feels, so it is checked over tens of
    /// thousands of screens rather than by playing until it looks right. The shape rules -
    /// three slots, no duplicates, the fallback, the pity counted per screen - are checked
    /// one at a time.
    /// </para>
    /// </summary>
    public sealed class RewardRollTests
    {
        private const uint Seed = 20_261_122u;

        private static RewardConditions Normal(int purified = 0, int luck = 0) =>
            new RewardConditions(RewardDifficulty.Normal, purified, luck);

        private static RewardRoller Roller() => RewardRoller.Approved();

        /// <summary>Deals many screens and counts what came out.</summary>
        private static int[] Count(
            RewardRoller roller, int screens, RewardConditions conditions, uint seed = Seed)
        {
            var counts = new int[RewardRollTable.Grades];
            for (int screen = 0; screen < screens; ++screen)
            {
                RewardScreen dealt = roller.Deal(1L, screen, conditions, seed);
                for (int i = 0; i < dealt.Cards.Count; ++i)
                {
                    if (!dealt.Cards[i].IsEmpty)
                        ++counts[(int)dealt.Cards[i].Definition.Rarity];
                }
            }

            return counts;
        }

        // --- the shape ----------------------------------------------------------------------

        [Test]
        public void AScreenHasThreeSlots()
        {
            // [확정] docs/03 section 3 item 1.
            RewardScreen screen = Roller().Deal(1L, 0, Normal(), Seed);

            Assert.AreEqual(DesignConstants.RewardCardSlots, screen.Cards.Count);
            Assert.AreEqual(3, screen.Cards.Count, "the constant moved away from docs/03");
        }

        [Test]
        public void NoScreenShowsTheSamePluginTwice()
        {
            // [확정] D-007. Checked over many screens because a duplicate is a collision, and
            // one screen proves nothing about collisions.
            RewardRoller roller = Roller();

            for (int screen = 0; screen < 20_000; ++screen)
            {
                RewardScreen dealt = roller.Deal(1L, screen, Normal(), Seed);
                var seen = new HashSet<string>();

                for (int i = 0; i < dealt.Cards.Count; ++i)
                {
                    if (dealt.Cards[i].IsEmpty)
                        continue;

                    Assert.IsTrue(
                        seen.Add(dealt.Cards[i].Definition.ProposalId),
                        $"screen {screen} showed {dealt.Cards[i].Definition.ProposalId} twice");
                }
            }
        }

        [Test]
        public void AnOwnedCardIsNotOffered()
        {
            // [확정] D-007 names 미보유 후보 for the fallback, and offering something the
            // vaccine already carries is an empty slot wearing a card's face.
            var owned = new HashSet<string>();
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
            {
                if (all[i].Rarity == PluginRarity.Legendary)
                    owned.Add(all[i].ProposalId);
            }

            RewardRoller roller = Roller();
            for (int screen = 0; screen < 5_000; ++screen)
            {
                RewardScreen dealt = roller.Deal(1L, screen, Normal(), Seed, owned);
                for (int i = 0; i < dealt.Cards.Count; ++i)
                {
                    if (dealt.Cards[i].IsEmpty)
                        continue;

                    Assert.AreNotEqual(
                        PluginRarity.Legendary, dealt.Cards[i].Definition.Rarity,
                        "every 전설 was owned, so none may be offered");
                }
            }
        }

        [Test]
        public void AnExhaustedGradeFallsBackToALowerOneRatherThanRepeating()
        {
            // [확정] D-007: 해당 등급의 후보가 부족하면 낮은 등급의 미보유 후보로 대체.
            var onlyOneLegendary = new List<PluginDefinition>();
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            int legendaries = 0;

            for (int i = 0; i < all.Count; ++i)
            {
                if (all[i].Rarity == PluginRarity.Legendary && ++legendaries > 1)
                    continue;

                onlyOneLegendary.Add(all[i]);
            }

            var roller = new RewardRoller(onlyOneLegendary, RewardRollTable.Approved());
            bool sawFallback = false;

            for (int screen = 0; screen < 20_000 && !sawFallback; ++screen)
            {
                RewardScreen dealt = roller.Deal(1L, screen, Normal(purified: 4), Seed);
                for (int i = 0; i < dealt.Cards.Count; ++i)
                {
                    if (dealt.Cards[i].FellBack)
                    {
                        sawFallback = true;
                        Assert.Less(
                            (int)dealt.Cards[i].Definition.Rarity, (int)dealt.Cards[i].RolledGrade,
                            "a fallback went up a grade");
                    }
                }
            }

            Assert.IsTrue(sawFallback, "one 전설 and three slots never once ran the grade out");
        }

        [Test]
        public void AnEmptyPoolOffersNothingRatherThanInventingDuplicates()
        {
            // [확정] D-007: 전체 후보가 부족하면 존재하는 유효 후보만 표시한다.
            var roller = new RewardRoller(new PluginDefinition[0], RewardRollTable.Approved());
            RewardScreen screen = roller.Deal(1L, 0, Normal(), Seed);

            Assert.AreEqual(0, screen.Offered);
            Assert.AreEqual(DesignConstants.RewardCardSlots, screen.Cards.Count, "the slots went away");
        }

        [Test]
        public void TwoCardsAreOfferedWhenOnlyTwoCandidatesExist()
        {
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            var two = new List<PluginDefinition>();
            for (int i = 0; i < all.Count && two.Count < 2; ++i)
            {
                if (all[i].Rarity == PluginRarity.Common)
                    two.Add(all[i]);
            }

            var roller = new RewardRoller(two, RewardRollTable.Approved());
            Assert.AreEqual(2, roller.Deal(1L, 0, Normal(), Seed).Offered);
        }

        // --- the distribution ------------------------------------------------------------------

        [Test]
        public void TheApprovedTableWeighsEightyEighteenTwo()
        {
            // [확정] D-006 B. 곡선's base weights, read off the table rather than off a deal:
            // a deal also carries the pity, and this is the number before any correction.
            RewardRollTable table = RewardRollTable.Approved();
            float common = table.WeightOf(PluginRarity.Common, RewardDifficulty.Normal, 0, 0, 1f);
            float rare = table.WeightOf(PluginRarity.Rare, RewardDifficulty.Normal, 0, 0, 1f);
            float legendary = table.WeightOf(PluginRarity.Legendary, RewardDifficulty.Normal, 0, 0, 1f);
            float total = common + rare + legendary;

            Assert.AreEqual(0.80, common / total, 0.001, "일반");
            Assert.AreEqual(0.18, rare / total, 0.001, "레어");
            Assert.AreEqual(0.02, legendary / total, 0.001, "전설");
        }

        [Test]
        public void ThePityMakesTheDealtRateHigherThanTheBaseWeight()
        {
            // The observed rate is not the base weight, and that is the correction doing its
            // job: a 2% grade leaves most screens empty, the count passes five, and the next
            // screens are helped. The simulation read 전설 at about 1 card in 22 for this
            // reason, not at 1 in 50.
            int[] counts = Count(Roller(), 40_000, Normal());
            int total = counts[0] + counts[1] + counts[2] + counts[3];
            double dealt = counts[2] / (double)total;

            Assert.Greater(dealt, 0.02, "보정이 전혀 걸리지 않았다");
            Assert.Less(dealt, 0.08, "보정이 기본 확률을 압도한다");
        }

        [Test]
        public void NothingMythicIsEverDealt()
        {
            // D-008 has chosen no 신화 card, and a grade with nothing behind it must not roll.
            Assert.AreEqual(0, Count(Roller(), 40_000, Normal(purified: 4, luck: 50))[3]);
        }

        [Test]
        public void PurifyingMoreRaisesTheUpperGrades()
        {
            // [확정] D-006 B. 곡선: this is the half that distinguishes it from the other two
            // candidates, and the reason it was chosen.
            double early = Count(Roller(), 40_000, Normal(purified: 0))[2];
            double late = Count(Roller(), 40_000, Normal(purified: 4))[2];

            Assert.Greater(late, early * 1.5, "정화를 쌓아도 전설이 늘지 않는다");
        }

        [Test]
        public void HardRaisesTheUpperGradesAndEasyLowersThem()
        {
            RewardConditions hard = new RewardConditions(RewardDifficulty.Hard, 0, 0);
            RewardConditions easy = new RewardConditions(RewardDifficulty.Easy, 0, 0);

            Assert.Greater(Count(Roller(), 40_000, hard)[2], Count(Roller(), 40_000, Normal())[2]);
            Assert.Less(Count(Roller(), 40_000, easy)[2], Count(Roller(), 40_000, Normal())[2]);
        }

        [Test]
        public void LuckRaisesTheUpperGrades()
        {
            Assert.Greater(
                Count(Roller(), 40_000, Normal(luck: 25))[2],
                Count(Roller(), 40_000, Normal(luck: 0))[2]);
        }

        // --- 천장 ------------------------------------------------------------------------------

        [Test]
        public void TheCorrectionStartsAfterFiveScreensAndNotBefore()
        {
            // [확정] docs/03 section 3 item 5.
            RewardRollTable table = RewardRollTable.Approved();

            Assert.AreEqual(5, RewardRollTable.PityAfterScreens);
            Assert.AreEqual(1f, table.PityMultiplier(0), 0.0001f);
            Assert.AreEqual(1f, table.PityMultiplier(5), 0.0001f, "보정이 다섯 번째 화면에 이미 걸렸다");
            Assert.Greater(table.PityMultiplier(6), 1f, "여섯 번째 화면에 보정이 없다");
        }

        [Test]
        public void TheCorrectionIsCapped()
        {
            RewardRollTable table = RewardRollTable.Approved();
            Assert.AreEqual(table.PityCap, table.PityMultiplier(1_000), 0.0001f);
        }

        [Test]
        public void ThePityIsCountedPerScreenAndNotPerCard()
        {
            // [확정] docs/03 section 3 item 5: '미등장 5회'는 15장 각 카드를 따로 세는 것이
            // 아니다. One screen with no 전설 adds one, not three.
            var roller = new RewardRoller(CommonOnly(), RewardRollTable.Approved());

            roller.Deal(1L, 0, Normal(), Seed);
            Assert.AreEqual(1, roller.ScreensWithoutHighTier);

            roller.Deal(1L, 1, Normal(), Seed);
            Assert.AreEqual(2, roller.ScreensWithoutHighTier);
        }

        [Test]
        public void TheRerollsDoNotCountTowardsThePityEither()
        {
            // [확정] docs/03 section 3 item 5: 리롤 횟수로도 세지 않는다.
            var roller = new RewardRoller(CommonOnly(), RewardRollTable.Approved());
            RewardScreen screen = roller.Deal(1L, 0, Normal(), Seed);

            Assert.AreEqual(1, roller.ScreensWithoutHighTier);

            for (int reroll = 1; reroll <= 3; ++reroll)
                Assert.IsTrue(roller.Reroll(screen, 0, 1L, 0, reroll, Normal(), Seed));

            Assert.AreEqual(1, roller.ScreensWithoutHighTier, "세 번의 리롤이 천장을 세 칸 올렸다");
        }

        [Test]
        public void AScreenWithNoHighTierAddsOneAndAScreenWithOneResetsIt()
        {
            // [확정] D-006: 화면 어디든, 리롤 포함.
            var dry = new RewardRoller(CommonOnly(), RewardRollTable.Approved());
            for (int screen = 0; screen < 4; ++screen)
                dry.Deal(1L, screen, Normal(), Seed);

            Assert.AreEqual(4, dry.ScreensWithoutHighTier);

            // The real pool, dealt until a 전설 actually appears. Forcing one with a
            // legendary-only pool does not work and should not: a slot that rolled 일반 with
            // no 일반 card falls back downwards and finds nothing, which is D-007 working.
            RewardRoller roller = Roller();
            for (int screen = 0; screen < 2_000; ++screen)
            {
                RewardScreen dealt = roller.Deal(1L, screen, Normal(purified: 4), Seed);
                if (!dealt.ShowsHighTier)
                    continue;

                Assert.AreEqual(0, roller.ScreensWithoutHighTier, "전설이 나왔는데 천장이 안 풀렸다");
                return;
            }

            Assert.Fail("2,000 화면 동안 전설이 한 번도 안 나왔다");
        }

        [Test]
        public void TheCorrectionActuallyEndsADrought()
        {
            // The point of a pity: a player who has seen nothing for a long time sees more.
            RewardRollTable table = RewardRollTable.Approved();

            float cold = table.WeightOf(
                PluginRarity.Legendary, RewardDifficulty.Normal, 0, 0, table.PityMultiplier(0));
            float hot = table.WeightOf(
                PluginRarity.Legendary, RewardDifficulty.Normal, 0, 0, table.PityMultiplier(12));

            Assert.Greater(hot, cold * 3f, "열두 화면을 비워도 전설 가중치가 거의 그대로다");
        }

        // --- 리롤 --------------------------------------------------------------------------------

        [Test]
        public void ARerollChangesOnlyItsOwnSlot()
        {
            // [확정] docs/03 section 3 item 3: 나머지 2장 유지. And the [초안] stream keying is
            // what makes that true rather than a thing the code remembers to do.
            RewardRoller roller = Roller();
            RewardScreen screen = roller.Deal(1L, 0, Normal(), Seed);

            string second = Id(screen, 1);
            string third = Id(screen, 2);

            Assert.IsTrue(roller.Reroll(screen, 0, 1L, 0, 1, Normal(), Seed));

            Assert.AreEqual(second, Id(screen, 1), "슬롯 2가 같이 바뀌었다");
            Assert.AreEqual(third, Id(screen, 2), "슬롯 3이 같이 바뀌었다");
        }

        [Test]
        public void ARerollActuallyRolls()
        {
            // "리롤 눌렀는데 등급은 고정" is named as a bug in docs/03. Over many screens the
            // rerolled slot must differ from what it replaced at least sometimes.
            RewardRoller roller = Roller();
            int changed = 0;

            for (int screen = 0; screen < 500; ++screen)
            {
                RewardScreen dealt = roller.Deal(1L, screen, Normal(), Seed);
                string before = Id(dealt, 0);
                roller.Reroll(dealt, 0, 1L, screen, 1, Normal(), Seed);

                if (Id(dealt, 0) != before)
                    ++changed;
            }

            Assert.Greater(changed, 400, "리롤이 대부분 같은 카드를 돌려줬다");
        }

        [Test]
        public void ARerollCannotLandOnACardAlreadyOnTheScreen()
        {
            // [확정] D-007 holds through a reroll too, which is the case it is easiest to miss.
            RewardRoller roller = Roller();

            for (int screen = 0; screen < 5_000; ++screen)
            {
                RewardScreen dealt = roller.Deal(1L, screen, Normal(), Seed);
                roller.Reroll(dealt, 0, 1L, screen, 1, Normal(), Seed);

                var seen = new HashSet<string>();
                for (int i = 0; i < dealt.Cards.Count; ++i)
                {
                    if (dealt.Cards[i].IsEmpty)
                        continue;

                    Assert.IsTrue(
                        seen.Add(dealt.Cards[i].Definition.ProposalId),
                        $"screen {screen} duplicated after a reroll");
                }
            }
        }

        [Test]
        public void ARerollOutsideTheScreenIsRefused()
        {
            RewardRoller roller = Roller();
            RewardScreen screen = roller.Deal(1L, 0, Normal(), Seed);

            Assert.IsFalse(roller.Reroll(screen, -1, 1L, 0, 1, Normal(), Seed));
            Assert.IsFalse(roller.Reroll(screen, 3, 1L, 0, 1, Normal(), Seed));
            Assert.IsFalse(roller.Reroll(null, 0, 1L, 0, 1, Normal(), Seed));
        }

        // --- 재현성 ------------------------------------------------------------------------------

        [Test]
        public void TheSameKeyDealsTheSameScreen()
        {
            // [초안] docs/03 section 3: seed+runId+rewardScreenId+slotId+rerollIndex. A replay
            // that produced different cards would make the receipt log a lie.
            RewardScreen a = Roller().Deal(7L, 3, Normal(purified: 2, luck: 5), Seed);
            RewardScreen b = Roller().Deal(7L, 3, Normal(purified: 2, luck: 5), Seed);

            Assert.AreEqual(a.ToString(), b.ToString());
        }

        [Test]
        public void DifferentKeysDealDifferentScreens()
        {
            // Two screens of one run, and the same screen of two runs, must not be the same
            // deal - an adder would have made runId 1 + screen 4 collide with runId 2 + screen 3.
            RewardRoller roller = Roller();

            string first = roller.Deal(1L, 4, Normal(), Seed).ToString();
            string second = roller.Deal(2L, 3, Normal(), Seed).ToString();

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void EveryStreamKeyIsItsOwn()
        {
            var seen = new HashSet<uint>();
            int collisions = 0;

            for (int run = 1; run <= 20; ++run)
            {
                for (int screen = 0; screen < 20; ++screen)
                {
                    for (int slot = 0; slot < 3; ++slot)
                    {
                        for (int reroll = 0; reroll < 4; ++reroll)
                        {
                            uint state = RewardRoller
                                .StreamFor(Seed, run, screen, slot, reroll).State;

                            if (!seen.Add(state))
                                ++collisions;
                        }
                    }
                }
            }

            Assert.AreEqual(0, collisions, $"{collisions} of {seen.Count + collisions} keys collided");
        }

        // --- helpers ------------------------------------------------------------------------------

        private static string Id(RewardScreen screen, int slot) =>
            screen.Cards[slot].IsEmpty ? "(없음)" : screen.Cards[slot].Definition.ProposalId;

        private static IReadOnlyList<PluginDefinition> CommonOnly() => OnlyOf(PluginRarity.Common);

        private static IReadOnlyList<PluginDefinition> OnlyOf(PluginRarity rarity)
        {
            var kept = new List<PluginDefinition>();
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
            {
                if (all[i].Rarity == rarity)
                    kept.Add(all[i]);
            }

            return kept;
        }
    }
}
