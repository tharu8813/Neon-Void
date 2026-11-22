using System;
using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Missions;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 철수 학습: which cards a successful extraction may keep, and the ids they are kept under.
    /// <para>
    /// [확정] R-015 / docs/04 section 3.1: 일반·레어 0~2개, 레벨 보존. [확정] docs/03 section 4:
    /// 전설·신화는 학습 카드에 절대 노출되지 않음. [미확정] D-002 (기존 학습 출전분 회수와 2개
    /// 한도의 관계) is deliberately not answered - the test that says so is at the bottom.
    /// </para>
    /// </summary>
    public sealed class ExtractionLearningTests
    {
        private static int _mint;

        private static ExtractionLearning Fresh()
        {
            _mint = 0;
            return new ExtractionLearning(() => "u" + (++_mint));
        }

        private static PluginLoadout Carrying(params PluginDefinition[] cards)
        {
            var loadout = new PluginLoadout();
            for (int i = 0; i < cards.Length; ++i)
            {
                Assert.AreEqual(
                    PluginGrantVerdict.Granted,
                    loadout.TryGrant(cards[i], new DeterministicRng(7u), out string why), why);
            }

            return loadout;
        }

        // --- the document keys ------------------------------------------------------------------

        [Test]
        public void EveryCardInTheCatalogueHasAStableKey()
        {
            // A card with no key can never be archived, so it would be a reward that cannot be
            // kept - and nothing would say why.
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
            {
                Assert.IsTrue(
                    PluginDocumentKeys.HasKey(all[i].ProposalId),
                    $"{all[i].ProposalId} has no document key");
            }

            Assert.AreEqual(27, PluginDocumentKeys.Count, "원안 27종");
        }

        [Test]
        public void AKeyIsNotTheProposalId()
        {
            // The whole reason the keys exist: docs/03's ids are 제안 ID on a document whose card
            // names are [초안], and docs/04 section 5 forbids making one permanent by accident.
            foreach (KeyValuePair<string, string> pair in PluginDocumentKeys.All)
            {
                StringAssert.StartsWith(PluginDocumentKeys.Prefix, pair.Value);
                Assert.AreNotEqual(pair.Key, pair.Value);
                StringAssert.DoesNotContain(pair.Key, pair.Value, "a 제안 ID leaked into a save key");
            }
        }

        [Test]
        public void KeysRoundTripBothWays()
        {
            foreach (KeyValuePair<string, string> pair in PluginDocumentKeys.All)
            {
                Assert.AreEqual(pair.Value, PluginDocumentKeys.KeyFor(pair.Key));
                Assert.AreEqual(pair.Key, PluginDocumentKeys.ProposalIdFor(pair.Value));
                Assert.IsTrue(PluginDocumentKeys.Knows(pair.Value));
            }
        }

        [Test]
        public void AKeyFromALaterBuildIsSurvivable()
        {
            // The archive is the player's. A key this build does not know is a plugin it has not
            // got yet, not a corrupt file.
            Assert.IsEmpty(PluginDocumentKeys.ProposalIdFor("plugin.something_new"));
            Assert.IsFalse(PluginDocumentKeys.Knows("plugin.something_new"));
            Assert.IsEmpty(PluginDocumentKeys.KeyFor("Z99"));
        }

        [Test]
        public void TheKeysArePinnedSoARenameIsDeliberate()
        {
            // A key written into a player's archive and later changed turns their learned
            // plugin into one the game cannot name. Changing one of these should break a test.
            Assert.AreEqual("plugin.lightweight", PluginDocumentKeys.KeyFor("G02"));
            Assert.AreEqual("plugin.emergency_recovery", PluginDocumentKeys.KeyFor("G09"));
            Assert.AreEqual("plugin.rare_acquisition", PluginDocumentKeys.KeyFor("G15"));
            Assert.AreEqual("plugin.support_round", PluginDocumentKeys.KeyFor("R06"));
            Assert.AreEqual("plugin.kill_stack", PluginDocumentKeys.KeyFor("R02"));
            Assert.AreEqual("plugin.laser_core", PluginDocumentKeys.KeyFor("L01"));
            Assert.AreEqual("plugin.boomerang_protocol", PluginDocumentKeys.KeyFor("L02"));
        }

        // --- the instance id --------------------------------------------------------------------

        [Test]
        public void AnInstanceIdSaysWhatItIsAndWhichOne()
        {
            PluginInstanceId id = PluginInstanceId.Mint("plugin.kill_stack", "abc");

            Assert.AreEqual("plugin.kill_stack#abc", id.Value);
            Assert.AreEqual("plugin.kill_stack", id.DocumentKey);
            Assert.AreEqual("abc", id.Unique);
            Assert.IsTrue(id.NamesADefinition);
            Assert.IsTrue(id.IsKnownHere);
            Assert.AreEqual("R02", id.ProposalId);
        }

        [Test]
        public void TwoInstancesOfOneCardAreDifferentIds()
        {
            // Whether a vaccine MAY hold two is [미확정] D-002/D-013. The format can express
            // either answer, which is the point; it decides neither.
            Assert.AreNotEqual(
                PluginInstanceId.Mint("plugin.kill_stack").Value,
                PluginInstanceId.Mint("plugin.kill_stack").Value);
        }

        [Test]
        public void AnIdFromBeforeTheKeysStillParses()
        {
            // Saves written before the key existed hold bare GUIDs. An archive that threw on one
            // would lock the player out of their own file.
            PluginInstanceId old = PluginInstanceId.Parse("8f14e45fceea167a5a36dedd4bea2543");

            Assert.IsFalse(old.NamesADefinition);
            Assert.IsFalse(old.IsKnownHere);
            Assert.IsEmpty(old.ProposalId);
            Assert.AreEqual("8f14e45fceea167a5a36dedd4bea2543", old.Value);
        }

        [Test]
        public void AHalfWrittenIdIsNotMistakenForAKey()
        {
            Assert.IsFalse(PluginInstanceId.Parse("#abc").NamesADefinition);
            Assert.IsFalse(PluginInstanceId.Parse("plugin.kill_stack#").NamesADefinition);
            Assert.IsFalse(PluginInstanceId.Parse(string.Empty).NamesADefinition);
            Assert.IsFalse(PluginInstanceId.Parse(null).NamesADefinition);
        }

        [Test]
        public void AnIdCannotBeMintedWithoutBothHalves()
        {
            Assert.Throws<ArgumentException>(() => PluginInstanceId.Mint(null, "abc"));
            Assert.Throws<ArgumentException>(() => PluginInstanceId.Mint("plugin.x", "  "));
            Assert.Throws<ArgumentException>(() => PluginInstanceId.Mint("plugin#x", "abc"));
            Assert.Throws<ArgumentException>(() => PluginInstanceId.Mint("plugin.x", "a#b"));
        }

        // --- what is offered ----------------------------------------------------------------------

        [Test]
        public void OnlyCommonAndRareAreOffered()
        {
            // [확정] docs/03 section 4: 전설/신화는 성공 철수 학습 카드에 절대 노출되지 않음.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(
                PluginCatalog.Lightweight(),
                PluginCatalog.KillStack(),
                PluginCatalog.LaserCore()));

            Assert.AreEqual(2, learning.Candidates.Count);
            for (int i = 0; i < learning.Candidates.Count; ++i)
                Assert.IsTrue(ExtractionLearning.IsLearnable(learning.Candidates[i].Rarity));

            Assert.AreEqual(1, learning.UnlearnableCarried, "the 전설 was not counted as left out");
        }

        [Test]
        public void TheRuleAboutWhatMayBeLearnedIsInOnePlace()
        {
            Assert.IsTrue(ExtractionLearning.IsLearnable(PluginRarity.Common));
            Assert.IsTrue(ExtractionLearning.IsLearnable(PluginRarity.Rare));
            Assert.IsFalse(ExtractionLearning.IsLearnable(PluginRarity.Legendary));
            Assert.IsFalse(ExtractionLearning.IsLearnable(PluginRarity.Mythic));
        }

        [Test]
        public void CarryingNothingOffersNothingAndIsNotAnError()
        {
            ExtractionLearning learning = Fresh();
            learning.Offer(null);
            Assert.IsEmpty(learning.Candidates);

            learning.Offer(new PluginLoadout());
            Assert.IsEmpty(learning.Candidates);
            Assert.IsEmpty(learning.MintChosenInstanceIds());
        }

        // --- the choice ----------------------------------------------------------------------------

        [Test]
        public void TwoMayBeChosen()
        {
            // [확정] R-015: 최대 2개.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(
                PluginCatalog.Lightweight(), PluginCatalog.KillStack(), PluginCatalog.ArcChain()));

            Assert.IsTrue(learning.Toggle(PluginDocumentKeys.KeyFor("G02")));
            Assert.IsTrue(learning.Toggle(PluginDocumentKeys.KeyFor("R02")));
            Assert.AreEqual(2, learning.ChosenCount);
            Assert.IsFalse(learning.HasRoom);
        }

        [Test]
        public void AThirdIsRefusedRatherThanReplacingOne()
        {
            // Trimming would pick which of the player's chosen cards to lose, silently - the
            // same reason ExtractRunCommand refuses three instead of taking the first two.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(
                PluginCatalog.Lightweight(), PluginCatalog.KillStack(), PluginCatalog.ArcChain()));

            learning.Toggle(PluginDocumentKeys.KeyFor("G02"));
            learning.Toggle(PluginDocumentKeys.KeyFor("R02"));

            Assert.IsFalse(learning.Toggle(PluginDocumentKeys.KeyFor("G10")));
            Assert.AreEqual(2, learning.ChosenCount);
            Assert.IsNotEmpty(learning.LastRefusal);
            Assert.AreEqual(PluginDocumentKeys.KeyFor("G02"), learning.ChosenKeys[0], "the first choice was dropped");
        }

        [Test]
        public void AChosenCardCanAlwaysBeUnchosenEvenWhenFull()
        {
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(PluginCatalog.Lightweight(), PluginCatalog.KillStack()));

            learning.Toggle(PluginDocumentKeys.KeyFor("G02"));
            learning.Toggle(PluginDocumentKeys.KeyFor("R02"));
            Assert.IsFalse(learning.HasRoom);

            Assert.IsFalse(learning.Toggle(PluginDocumentKeys.KeyFor("G02")));
            Assert.AreEqual(1, learning.ChosenCount);
            Assert.IsTrue(learning.HasRoom);
        }

        [Test]
        public void ACardThatWasNotOfferedCannotBeChosen()
        {
            // The selection decides a permanent archive write; a key arriving from anywhere but
            // the offer is refused rather than trusted.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(PluginCatalog.Lightweight()));

            Assert.IsFalse(learning.Toggle(PluginDocumentKeys.KeyFor("L01")));
            Assert.IsFalse(learning.Toggle("plugin.not_a_card"));
            Assert.AreEqual(0, learning.ChosenCount);
        }

        [Test]
        public void ChoosingNothingIsACompleteAnswer()
        {
            // [확정] R-015 allows zero. An extraction that banks XP and learns nothing is a
            // correct extraction, not a cancelled one.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(PluginCatalog.Lightweight()));
            learning.Toggle(PluginDocumentKeys.KeyFor("G02"));

            learning.ChooseNothing();

            Assert.AreEqual(0, learning.ChosenCount);
            Assert.IsEmpty(learning.MintChosenInstanceIds());
        }

        [Test]
        public void ANewOfferClearsTheOldChoice()
        {
            // A choice made against a different list is not a choice.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(PluginCatalog.Lightweight()));
            learning.Toggle(PluginDocumentKeys.KeyFor("G02"));
            Assert.AreEqual(1, learning.ChosenCount);

            learning.Offer(Carrying(PluginCatalog.KillStack()));
            Assert.AreEqual(0, learning.ChosenCount);
        }

        // --- what is handed to the save -------------------------------------------------------------

        [Test]
        public void TheMintedIdsNameTheChosenCards()
        {
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(PluginCatalog.Lightweight(), PluginCatalog.KillStack()));
            learning.Toggle(PluginDocumentKeys.KeyFor("R02"));

            string[] ids = learning.MintChosenInstanceIds();

            Assert.AreEqual(1, ids.Length);
            Assert.AreEqual("R02", PluginInstanceId.Parse(ids[0]).ProposalId);
        }

        [Test]
        public void NeverMoreIdsThanTheLimit()
        {
            // What this hands over goes straight into ExtractRunCommand, which throws on more
            // than two. The screen must not be able to build that call at all.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(
                PluginCatalog.Lightweight(), PluginCatalog.KillStack(),
                PluginCatalog.ArcChain(), PluginCatalog.CorrosionMark()));

            for (int i = 0; i < learning.Candidates.Count; ++i)
                learning.Toggle(learning.Candidates[i].DocumentKey);

            Assert.LessOrEqual(
                learning.MintChosenInstanceIds().Length,
                NeonVoid.Core.DesignConstants.LearnablePluginsPerSuccessfulRun);
        }

        [Test]
        public void MintingTwiceGivesDifferentInstancesOfTheSameCard()
        {
            // Minted at commit, not when a card is ticked: ticking, unticking and ticking again
            // must not leave abandoned ids behind.
            ExtractionLearning learning = Fresh();
            learning.Offer(Carrying(PluginCatalog.Lightweight()));
            learning.Toggle(PluginDocumentKeys.KeyFor("G02"));

            string first = learning.MintChosenInstanceIds()[0];
            string second = learning.MintChosenInstanceIds()[0];

            Assert.AreNotEqual(first, second);
            Assert.AreEqual(
                PluginInstanceId.Parse(first).DocumentKey,
                PluginInstanceId.Parse(second).DocumentKey);
        }

        // --- [미확정] D-002 is left alone --------------------------------------------------------

        [Test]
        public void ACardTheVaccineAlreadyLearnedIsNotOfferedAgain()
        {
            // [미확정] D-002 and docs/04 section 3's 동일 정의 인스턴스 복수 허용 여부. Offering it
            // would answer that silently, in the direction that writes more to the player's
            // file. It is left out and counted, so a screen can say why.
            ExtractionLearning learning = Fresh();
            learning.Offer(
                Carrying(PluginCatalog.Lightweight(), PluginCatalog.KillStack()),
                new[] { PluginDocumentKeys.KeyFor("G02") });

            Assert.AreEqual(1, learning.Candidates.Count);
            Assert.AreEqual("R02", learning.Candidates[0].ProposalId);
            Assert.AreEqual(1, learning.CarriedInFromBefore);
        }

        [Test]
        public void NothingHereTouchesWhatTheVaccineCarried()
        {
            // [확정] docs/00: 성공 귀환해도 자동 DB 보관 없음. The selection only ever names cards
            // picked up in this run; re-archiving a carried one is D-002's question.
            ExtractionLearning learning = Fresh();
            PluginLoadout loadout = Carrying(PluginCatalog.Lightweight());
            int before = loadout.Count;

            learning.Offer(loadout);
            learning.Toggle(PluginDocumentKeys.KeyFor("G02"));
            learning.MintChosenInstanceIds();

            Assert.AreEqual(before, loadout.Count, "the selection changed the loadout");
        }
    }
}
