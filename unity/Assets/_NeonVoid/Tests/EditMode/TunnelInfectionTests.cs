using System.Collections.Generic;
using NeonVoid.Combat.Stats;
using NeonVoid.Core;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 터널감염.
    /// <para>
    /// [확정] docs/00 section 7: 미학습 소지 플러그인 1~2개 비활성. [확정] docs/01 section 4:
    /// 학습된 플러그인 제외 - the cards the vaccine walked in with came off the 출전 목록 and the
    /// environment may not touch them. That exception is the reason the loadout tracks which
    /// cards were deployed at all.
    /// </para>
    /// </summary>
    public sealed class TunnelInfectionTests
    {
        private const uint Seed = 0xA17C_0FFEu;

        private static PluginLoadout Deployed(params PluginDefinition[] cards)
        {
            var loadout = new PluginLoadout();
            for (int i = 0; i < cards.Length; ++i)
                loadout.TryGrant(cards[i], new DeterministicRng(3u), out _);

            loadout.MarkCarriedAsLearned();
            return loadout;
        }

        private static void FindInRun(PluginLoadout loadout, params PluginDefinition[] cards)
        {
            for (int i = 0; i < cards.Length; ++i)
                loadout.TryGrant(cards[i], new DeterministicRng(5u), out _);
        }

        // --- the count --------------------------------------------------------------------------

        [Test]
        public void ItTakesOneOrTwo()
        {
            for (uint seed = 1u; seed < 300u; ++seed)
            {
                int count = TunnelInfection.CountFor(seed);
                Assert.GreaterOrEqual(count, TunnelInfection.DraftMinInfected);
                Assert.LessOrEqual(count, TunnelInfection.DraftMaxInfected);
            }
        }

        [Test]
        public void TheSameAreaTakesTheSameNumber()
        {
            // An environment that rerolled on re-entry would be a slot machine rather than a
            // condition of the place.
            Assert.AreEqual(TunnelInfection.CountFor(Seed), TunnelInfection.CountFor(Seed));
        }

        // --- the exception ----------------------------------------------------------------------

        [Test]
        public void ADeployedCardIsNeverChosen()
        {
            // [확정] docs/01 section 4: 학습된 플러그인 제외.
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack(), PluginCatalog.Lightweight());

            Assert.IsEmpty(TunnelInfection.Choose(loadout, 2, Seed));
        }

        [Test]
        public void ADeployedCardCannotBeSuppressedEvenIfAskedFor()
        {
            // Enforced by the loadout, not only by the chooser: a caller that got the choice
            // wrong must not be able to take a card off the 출전 목록 anyway.
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());

            Assert.IsFalse(loadout.Suppress("R02"));
            Assert.AreEqual(1, loadout.Count);
            Assert.IsFalse(loadout.IsSuppressed("R02"));
        }

        [Test]
        public void OnlyWhatWasFoundInTheRunIsEligible()
        {
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(loadout, PluginCatalog.Lightweight(), PluginCatalog.ArcChain());

            List<string> chosen = TunnelInfection.Choose(loadout, 2, Seed);

            Assert.AreEqual(2, chosen.Count);
            CollectionAssert.DoesNotContain(chosen, "R02");
            foreach (string id in chosen)
                Assert.IsFalse(loadout.IsLearned(id), id);
        }

        [Test]
        public void CarryingOnlyDeployedCardsIsSurvivingTheEnvironment()
        {
            // Not a failure: docs/01's exception is doing its job, and a player who brought only
            // 출전 cards walks through 터널감염 untouched.
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());

            Assert.IsEmpty(TunnelInfection.Choose(loadout, 2, Seed));
        }

        [Test]
        public void FewerCandidatesThanAskedForIsFine()
        {
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(loadout, PluginCatalog.Lightweight());

            Assert.AreEqual(1, TunnelInfection.Choose(loadout, 2, Seed).Count);
        }

        [Test]
        public void NothingCarriedChoosesNothing()
        {
            Assert.IsEmpty(TunnelInfection.Choose(new PluginLoadout(), 2, Seed));
            Assert.IsEmpty(TunnelInfection.Choose(null, 2, Seed));
            Assert.IsEmpty(TunnelInfection.Choose(Deployed(PluginCatalog.KillStack()), 0, Seed));
        }

        [Test]
        public void TheSameAreaInfectsTheSameCards()
        {
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(loadout, PluginCatalog.Lightweight(), PluginCatalog.ArcChain(), PluginCatalog.CorrosionMark());

            CollectionAssert.AreEqual(
                TunnelInfection.Choose(loadout, 2, Seed),
                TunnelInfection.Choose(loadout, 2, Seed));
        }

        [Test]
        public void ItDoesNotAlwaysStartAtTheOldestCard()
        {
            // A fixed starting point would make the first card the player picked up the one
            // that always goes, which reads as a rule rather than as an infection.
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(
                loadout, PluginCatalog.Lightweight(), PluginCatalog.ArcChain(),
                PluginCatalog.CorrosionMark(), PluginCatalog.QuadCache());

            var firsts = new HashSet<string>();
            for (uint seed = 1u; seed < 400u; ++seed)
            {
                List<string> chosen = TunnelInfection.Choose(loadout, 1, seed);
                if (chosen.Count > 0)
                    firsts.Add(chosen[0]);
            }

            Assert.Greater(firsts.Count, 1, "every seed infected the same card");
        }

        // --- what suppression does to the loadout --------------------------------------------------

        [Test]
        public void ASuppressedCardStopsContributing()
        {
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(loadout, PluginCatalog.Lightweight());

            var before = new List<StatModifier>();
            loadout.CollectPassiveModifiers(before);
            float speedBefore = loadout.PassiveMoveSpeedFraction();

            Assert.IsTrue(loadout.Suppress("G02"), "경량화 was not switched off");

            var after = new List<StatModifier>();
            loadout.CollectPassiveModifiers(after);

            Assert.AreEqual(1, loadout.Count, "the suppressed card is still active");
            Assert.AreEqual(2, loadout.OwnedCount, "the suppressed card stopped being owned");
            Assert.Less(
                loadout.PassiveMoveSpeedFraction(), speedBefore,
                "경량화 was switched off and the move speed did not change");
            Assert.LessOrEqual(after.Count, before.Count);
        }

        [Test]
        public void ASuppressedCardIsStillOwnedAndNotOfferedAgain()
        {
            // Offering it again would turn 터널감염 into a way to duplicate a card.
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(loadout, PluginCatalog.Lightweight());
            loadout.Suppress("G02");

            Assert.IsTrue(loadout.Carries("G02"));
            Assert.IsTrue(loadout.IsSuppressed("G02"));

            // Taking it again raises the one the player owns rather than slipping a second copy
            // past the suppression - which would hand them a working card out of an infected one.
            Assert.AreEqual(
                PluginGrantVerdict.LevelledUp,
                loadout.TryGrant(PluginCatalog.Lightweight(), out string why), why);
            Assert.AreEqual(1, loadout.Count, "a second 경량화 was added while the first was off");
            Assert.AreEqual(2, loadout.LevelOf("G02"));
            Assert.IsTrue(loadout.IsSuppressed("G02"), "the level-up switched it back on");
        }

        [Test]
        public void LeavingTheAreaBringsItBackExactlyAsItWas()
        {
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(loadout, PluginCatalog.Lightweight());

            float before = loadout.PassiveMoveSpeedFraction();
            loadout.Suppress("G02");

            Assert.AreEqual(1, loadout.RestoreSuppressed());
            Assert.AreEqual(2, loadout.Count);
            Assert.AreEqual(
                before, loadout.PassiveMoveSpeedFraction(), 0.0001f,
                "the card came back with a different roll");
            Assert.IsFalse(loadout.IsSuppressed("G02"));
        }

        [Test]
        public void RestoringWithNothingSuppressedIsZeroRatherThanAnError()
        {
            Assert.AreEqual(0, new PluginLoadout().RestoreSuppressed());
        }

        [Test]
        public void ClearingTheLoadoutForgetsTheSuppressionAndTheTags()
        {
            // A new 의뢰 starts from the 출전 목록; a card left suppressed, or a stale 학습 tag,
            // would follow the player into it.
            PluginLoadout loadout = Deployed(PluginCatalog.KillStack());
            FindInRun(loadout, PluginCatalog.Lightweight());
            loadout.Suppress("G02");

            loadout.Clear();

            Assert.AreEqual(0, loadout.OwnedCount);
            Assert.IsFalse(loadout.IsLearned("R02"));
            Assert.IsFalse(loadout.Carries("G02"));
        }
    }
}
