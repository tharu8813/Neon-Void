using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The 보상 화면 as a thing a player acts on: three rerolls a mission, one card taken, and
    /// what happens when the loadout says no.
    /// <para>
    /// The distribution is held in <c>RewardRollTests</c>. This is the session on top of it.
    /// </para>
    /// </summary>
    public sealed class RewardSessionTests
    {
        private const uint Seed = 20_261_122u;
        private const long Run = 1L;

        private static RewardConditions Normal(int purified = 1) =>
            new RewardConditions(RewardDifficulty.Normal, purified, 0);

        private static RewardSession Session(out PluginLoadout loadout)
        {
            loadout = new PluginLoadout();
            return new RewardSession(RewardRoller.Approved(), loadout);
        }

        // --- opening and closing ---------------------------------------------------------------

        [Test]
        public void NothingIsOpenUntilAnAreaIsPurified()
        {
            RewardSession session = Session(out _);

            Assert.IsFalse(session.IsOpen);
            Assert.IsNull(session.Screen);
        }

        [Test]
        public void OpeningDealsThreeSlots()
        {
            RewardSession session = Session(out _);
            RewardScreen screen = session.Open(Run, Normal(), Seed);

            Assert.IsTrue(session.IsOpen);
            Assert.AreEqual(DesignConstants.RewardCardSlots, screen.Cards.Count);
            Assert.AreEqual(1, session.ScreensThisMission);
        }

        [Test]
        public void TakingACardClosesTheScreenAndGrantsIt()
        {
            RewardSession session = Session(out PluginLoadout loadout);
            RewardScreen screen = session.Open(Run, Normal(), Seed);
            string taken = screen.Cards[0].Definition.ProposalId;

            RewardResult result = session.Pick(0);

            Assert.IsTrue(result.Ok, result.Detail);
            Assert.IsFalse(session.IsOpen, "the screen stayed open after a pick");
            Assert.IsTrue(loadout.Carries(taken), "the card never reached the loadout");
            Assert.AreEqual(1, loadout.Count);
        }

        [Test]
        public void OnlyOneCardOfThreeIsTaken()
        {
            RewardSession session = Session(out PluginLoadout loadout);
            session.Open(Run, Normal(), Seed);

            Assert.IsTrue(session.Pick(1).Ok);
            Assert.AreEqual(1, loadout.Count, "more than one card was granted");
        }

        [Test]
        public void PickingWithNothingOpenIsRefused()
        {
            RewardSession session = Session(out _);
            RewardResult result = session.Pick(0);

            Assert.AreEqual(RewardVerdict.NoScreenOpen, result.Verdict);
            Assert.IsNotEmpty(result.Detail);
        }

        [Test]
        public void PickingASlotThatIsNotThereIsRefused()
        {
            RewardSession session = Session(out _);
            session.Open(Run, Normal(), Seed);

            Assert.AreEqual(RewardVerdict.NoSuchSlot, session.Pick(-1).Verdict);
            Assert.AreEqual(RewardVerdict.NoSuchSlot, session.Pick(3).Verdict);
            Assert.IsTrue(session.IsOpen, "a refused pick closed the screen");
        }

        [Test]
        public void ARefusedGrantLeavesTheScreenOpen()
        {
            // The player must not lose the reward and the other two choices at once because the
            // loadout said no to one of them.
            var loadout = new PluginLoadout();
            var session = new RewardSession(RewardRoller.Approved(), loadout);
            RewardScreen screen = session.Open(Run, Normal(), Seed);

            // Carry the first card already, so granting it again is refused as a duplicate.
            loadout.TryGrant(screen.Cards[0].Definition, out _);

            RewardResult result = session.Pick(0);

            Assert.AreEqual(RewardVerdict.LoadoutRefused, result.Verdict);
            Assert.IsTrue(session.IsOpen, "a refused grant threw the screen away");
            Assert.IsNotEmpty(session.LastRefusal);
        }

        [Test]
        public void ASessionWithNoLoadoutRefusesInsteadOfSwallowingTheCard()
        {
            // Found by playing it: the director was built before the component that owns the
            // loadout, captured null, and then every pick closed the screen and granted
            // nothing. The player watched the reward go and got nothing.
            var session = new RewardSession(RewardRoller.Approved(), (PluginLoadout)null);
            session.Open(Run, Normal(), Seed);

            RewardResult result = session.Pick(0);

            Assert.AreEqual(RewardVerdict.LoadoutRefused, result.Verdict);
            Assert.IsTrue(session.IsOpen, "the screen closed over a card nobody received");
            Assert.IsNotEmpty(session.LastRefusal);
        }

        [Test]
        public void TheLoadoutIsAskedForEachTimeRatherThanHeld()
        {
            // A new vaccine replaces the loadout, and a session holding the old one would grant
            // into something nobody is carrying.
            PluginLoadout current = null;
            var session = new RewardSession(RewardRoller.Approved(), () => current);
            session.Open(Run, Normal(), Seed);

            Assert.AreEqual(RewardVerdict.LoadoutRefused, session.Pick(0).Verdict);

            current = new PluginLoadout();
            Assert.IsTrue(session.Pick(0).Ok, session.LastRefusal);
            Assert.AreEqual(1, current.Count);
        }

        [Test]
        public void DismissingTakesNothing()
        {
            RewardSession session = Session(out PluginLoadout loadout);
            session.Open(Run, Normal(), Seed);

            session.Dismiss();

            Assert.IsFalse(session.IsOpen);
            Assert.AreEqual(0, loadout.Count);
        }

        // --- 리롤 --------------------------------------------------------------------------------

        [Test]
        public void AMissionGivesThreeRerolls()
        {
            // [확정] docs/03 section 3 item 4.
            RewardSession session = Session(out _);

            Assert.AreEqual(DesignConstants.InitialRewardRerolls, session.RerollsLeft);
            Assert.AreEqual(3, session.RerollsLeft, "the constant moved away from docs/03");
        }

        [Test]
        public void ARerollSpendsOneAndChangesOnlyItsSlot()
        {
            RewardSession session = Session(out _);
            RewardScreen screen = session.Open(Run, Normal(), Seed);

            string second = screen.Cards[1].ToString();
            string third = screen.Cards[2].ToString();

            Assert.IsTrue(session.Reroll(0).Ok);

            Assert.AreEqual(2, session.RerollsLeft);
            Assert.AreEqual(second, screen.Cards[1].ToString(), "슬롯 2가 같이 바뀌었다");
            Assert.AreEqual(third, screen.Cards[2].ToString(), "슬롯 3이 같이 바뀌었다");
        }

        [Test]
        public void TheRerollsRunOut()
        {
            RewardSession session = Session(out _);
            session.Open(Run, Normal(), Seed);

            for (int i = 0; i < DesignConstants.InitialRewardRerolls; ++i)
                Assert.IsTrue(session.Reroll(0).Ok, $"reroll {i} was refused");

            RewardResult spent = session.Reroll(0);
            Assert.AreEqual(RewardVerdict.NoRerollsLeft, spent.Verdict);
            Assert.AreEqual(0, session.RerollsLeft);
        }

        [Test]
        public void TheRerollsAreSharedAcrossAMissionsScreens()
        {
            // [확정] docs/03 section 3 item 4: 의뢰당 3회, not per screen. A player who purifies
            // three areas spends the same three across all of them.
            RewardSession session = Session(out _);

            session.Open(Run, Normal(1), Seed);
            Assert.IsTrue(session.Reroll(0).Ok);
            Assert.IsTrue(session.Reroll(1).Ok);
            session.Dismiss();

            session.Open(Run, Normal(2), Seed);
            Assert.AreEqual(1, session.RerollsLeft, "the second screen refilled the rerolls");

            Assert.IsTrue(session.Reroll(0).Ok);
            Assert.AreEqual(RewardVerdict.NoRerollsLeft, session.Reroll(1).Verdict);
        }

        [Test]
        public void ANewMissionRefillsThem()
        {
            RewardSession session = Session(out _);
            session.Open(Run, Normal(), Seed);
            session.Reroll(0);
            session.Reroll(0);

            session.BeginMission();

            Assert.AreEqual(DesignConstants.InitialRewardRerolls, session.RerollsLeft);
            Assert.AreEqual(0, session.ScreensThisMission);
            Assert.IsFalse(session.IsOpen, "a new mission began with the last one's screen up");
        }

        // --- 중복 ---------------------------------------------------------------------------------

        [Test]
        public void ACardTheVaccineAlreadyCarriesIsNotOffered()
        {
            // [확정] D-007 names 미보유 후보. Offering something already carried is a slot the
            // player cannot use.
            var loadout = new PluginLoadout();
            var session = new RewardSession(RewardRoller.Approved(), loadout);

            // Take a card from each of several screens, then check it never comes back.
            var taken = new HashSet<string>();
            for (int screen = 0; screen < 8; ++screen)
            {
                RewardScreen dealt = session.Open(Run, Normal(screen), Seed);
                for (int i = 0; i < dealt.Cards.Count; ++i)
                {
                    if (dealt.Cards[i].IsEmpty)
                        continue;

                    Assert.IsFalse(
                        taken.Contains(dealt.Cards[i].Definition.ProposalId),
                        $"{dealt.Cards[i].Definition.ProposalId} was offered after being taken");
                }

                if (session.Pick(0).Ok)
                    taken.Add(dealt.Cards[0].Definition.ProposalId);
                else
                    session.Dismiss();
            }

            Assert.Greater(taken.Count, 1, "nothing was ever taken, so this proves nothing");
        }

        // --- the words on the card -------------------------------------------------------------

        [Test]
        public void EveryCardTheGameCanDealHasWordsForIt()
        {
            // A reward screen showing "G10" is unreadable. Every card in the pool needs a name
            // and a line of what it does.
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();

            for (int i = 0; i < all.Count; ++i)
            {
                Assert.IsTrue(
                    PluginCardTexts.Knows(all[i].ProposalId),
                    $"{all[i].ProposalId} has no name on the 보상 화면");

                PluginCardText text = PluginCardTexts.For(all[i].ProposalId);
                Assert.IsNotEmpty(text.Name, all[i].ProposalId);
                Assert.IsNotEmpty(text.Effect, all[i].ProposalId);
            }
        }

        [Test]
        public void TheTextTableCoversAllTwentySevenOfTheDocumentsCards()
        {
            // [확정] docs/03: 일반 17 + 레어 7 + 전설 3. Four of them are not written in code
            // yet, and the words for them are here so adding the card is adding the card.
            Assert.AreEqual(27, PluginCardTexts.Count);
        }

        [Test]
        public void ACardWithNoEntryReadsAsItsIdRatherThanAsNothing()
        {
            PluginCardText unknown = PluginCardTexts.For("Z99");

            Assert.AreEqual("Z99", unknown.Name);
            Assert.IsFalse(PluginCardTexts.Knows("Z99"));
        }

        [Test]
        public void EveryGradeHasALabel()
        {
            foreach (PluginRarity rarity in System.Enum.GetValues(typeof(PluginRarity)))
                Assert.IsNotEmpty(PluginCardTexts.LabelOf(rarity), rarity.ToString());
        }
    }
}
