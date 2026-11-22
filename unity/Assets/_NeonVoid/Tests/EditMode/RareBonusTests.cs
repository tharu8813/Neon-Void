using System.Collections.Generic;
using NeonVoid.Core;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// G15 희귀 획득. [확정] D-003 (2026-11-22): 추가 지급량은 1개, 유효 후보가 없으면 추가
    /// 지급하지 않으며, 동일 영수증으로 중복 지급할 수 없다.
    /// </summary>
    public sealed class RareBonusTests
    {
        private static DeterministicRng Rng(uint seed = 20_261_123u) => new DeterministicRng(seed);

        private static IReadOnlyList<PluginDefinition> Rares()
        {
            var rares = new List<PluginDefinition>();
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();

            for (int i = 0; i < all.Count; ++i)
            {
                if (all[i].Rarity == PluginRarity.Rare)
                    rares.Add(all[i]);
            }

            return rares;
        }

        // --- the pick -----------------------------------------------------------------------

        [Test]
        public void ItPicksARare()
        {
            PluginDefinition bonus = RareBonus.Pick(PluginCatalog.All(), new PluginDefinition[0], Rng());

            Assert.AreEqual(PluginRarity.Rare, bonus.Rarity);
            Assert.IsNotEmpty(bonus.ProposalId);
        }

        [Test]
        public void ItNeverPicksSomethingAlreadyCarried()
        {
            // Offering a card the vaccine has is a bonus that gives nothing while looking like
            // it gave something.
            var owned = new List<PluginDefinition>(Rares());
            owned.RemoveAt(owned.Count - 1);

            PluginDefinition bonus = RareBonus.Pick(PluginCatalog.All(), owned, Rng());

            Assert.AreEqual(Rares()[Rares().Count - 1].ProposalId, bonus.ProposalId);
        }

        [Test]
        public void EveryRareCarriedMeansNoBonus()
        {
            // [확정] D-003: 유효 후보가 없으면 추가 지급하지 않는다. Not an error - docs/03's own
            // exception column names 후보 고갈 as a case.
            PluginDefinition bonus = RareBonus.Pick(PluginCatalog.All(), Rares(), Rng());

            Assert.IsEmpty(bonus.ProposalId ?? string.Empty);
            Assert.AreEqual(0, RareBonus.Count(PluginCatalog.All(), Rares()));
        }

        [Test]
        public void AnEmptyPoolGivesNothingRatherThanThrowing()
        {
            Assert.IsEmpty(
                RareBonus.Pick(new PluginDefinition[0], new PluginDefinition[0], Rng()).ProposalId
                ?? string.Empty);
            Assert.AreEqual(0, RareBonus.Count(null, null));
        }

        [Test]
        public void ItNeverPicksAnotherGrantingCard()
        {
            // A bonus that granted another granting card would make one pick resolve a chain of
            // them with no bound on its length.
            var pool = new List<PluginDefinition>
            {
                new PluginDefinition
                {
                    ProposalId = "X01",
                    Rarity = PluginRarity.Rare,
                    Effect = PluginEffectKind.GrantRandomRare,
                    StatusDurationTicksOverride = -1,
                },
            };

            Assert.AreEqual(0, RareBonus.Count(pool, new PluginDefinition[0]));
        }

        [Test]
        public void TheSameSeedPicksTheSameCard()
        {
            // A replay that gave a different bonus would make the receipt a lie.
            string first = RareBonus.Pick(PluginCatalog.All(), new PluginDefinition[0], Rng()).ProposalId;
            string second = RareBonus.Pick(PluginCatalog.All(), new PluginDefinition[0], Rng()).ProposalId;

            Assert.AreEqual(first, second);
        }

        [Test]
        public void NoStreamStillGivesACardRatherThanNone()
        {
            // A bonus that worked in the game and not in a test, or the other way round, is
            // worse than a deterministic one.
            Assert.IsNotEmpty(
                RareBonus.Pick(PluginCatalog.All(), new PluginDefinition[0], null).ProposalId);
        }

        // --- through the loadout ---------------------------------------------------------------

        [Test]
        public void CarryingG15AlsoCarriesARare()
        {
            var loadout = new PluginLoadout();

            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.RareAcquisition(), Rng(), out string why), why);

            Assert.AreEqual(2, loadout.Count, "G15 gave nothing");
            Assert.IsTrue(loadout.Carries("G15"));
            Assert.IsNotEmpty(loadout.LastRareBonus);
            Assert.IsTrue(loadout.Carries(loadout.LastRareBonus));
            Assert.AreEqual(PluginRarity.Rare, Find(loadout, loadout.LastRareBonus).Rarity);
        }

        [Test]
        public void ItGivesExactlyOne()
        {
            // [확정] D-003: 추가 지급량은 1개.
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.RareAcquisition(), Rng(), out _);

            Assert.AreEqual(2, loadout.Count);
        }

        [Test]
        public void ItCannotBeCollectedTwice()
        {
            // [확정] D-003: 동일 영수증으로 중복 지급할 수 없다. The loadout already refuses a
            // duplicate card, so collecting twice is impossible rather than guarded against.
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.RareAcquisition(), Rng(), out _);
            int after = loadout.Count;

            Assert.AreEqual(
                PluginGrantVerdict.RejectedAlreadyCarried,
                loadout.TryGrant(PluginCatalog.RareAcquisition(), Rng(), out _));
            Assert.AreEqual(after, loadout.Count, "a refused grant still handed out a bonus");
        }

        [Test]
        public void WithEveryRareCarriedItGivesNothingAndIsStillTaken()
        {
            // The card is still worth taking as itself; it just has nothing left to give.
            var loadout = new PluginLoadout();
            IReadOnlyList<PluginDefinition> rares = Rares();

            for (int i = 0; i < rares.Count; ++i)
                Assert.AreEqual(PluginGrantVerdict.Granted, loadout.TryGrant(rares[i], out string why), why);

            int before = loadout.Count;
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                loadout.TryGrant(PluginCatalog.RareAcquisition(), Rng(), out _));

            Assert.AreEqual(before + 1, loadout.Count, "it gave a bonus with no candidates left");
            Assert.IsEmpty(loadout.LastRareBonus);
        }

        [Test]
        public void AnOrdinaryCardLeavesTheBonusEmpty()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.RareAcquisition(), Rng(), out _);
            Assert.IsNotEmpty(loadout.LastRareBonus);

            loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out _);
            Assert.IsEmpty(loadout.LastRareBonus, "the last bonus was reported again for another card");
        }

        private static PluginDefinition Find(PluginLoadout loadout, string proposalId)
        {
            IReadOnlyList<PluginDefinition> carried = loadout.Carried;
            for (int i = 0; i < carried.Count; ++i)
            {
                if (carried[i].ProposalId == proposalId)
                    return carried[i];
            }

            return default;
        }
    }
}
