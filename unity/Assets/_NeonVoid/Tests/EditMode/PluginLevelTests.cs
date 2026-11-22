using System.Collections.Generic;
using NeonVoid.Combat.Stats;
using NeonVoid.Core;
using NeonVoid.Missions;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 플러그인 레벨.
    /// <para>
    /// [확정] docs/03 section 3 item 6: 같은 플러그인 획득 = 레벨 상승, 현재 최대로 강화된
    /// 플러그인은 후보에서 제거, 개별 maxLevel·다음 단계 효과는 데이터로 정의. [확정] docs/04
    /// section 3.1: 학습할 때 레벨을 유지한다.
    /// </para>
    /// </summary>
    public sealed class PluginLevelTests
    {
        private static DeterministicRng Rng() => new DeterministicRng(11u);

        private static PluginLoadout Carrying(params PluginDefinition[] cards)
        {
            var loadout = new PluginLoadout();
            for (int i = 0; i < cards.Length; ++i)
                loadout.TryGrant(cards[i], Rng(), out _);

            return loadout;
        }

        // --- the three rules the document states --------------------------------------------------

        [Test]
        public void TakingACardAgainRaisesIt()
        {
            PluginLoadout loadout = Carrying(PluginCatalog.Lightweight());

            Assert.AreEqual(1, loadout.LevelOf("G02"));
            Assert.AreEqual(
                PluginGrantVerdict.LevelledUp,
                loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out string why), why);
            Assert.AreEqual(2, loadout.LevelOf("G02"));
            Assert.AreEqual(1, loadout.Count, "a second copy was added");
        }

        [Test]
        public void ACardStopsAtItsCeiling()
        {
            PluginLoadout loadout = Carrying(PluginCatalog.Lightweight());
            int max = PluginLevels.MaxLevelFor("G02");

            for (int i = 1; i < max; ++i)
                Assert.AreEqual(
                    PluginGrantVerdict.LevelledUp,
                    loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out _), $"step {i}");

            Assert.AreEqual(max, loadout.LevelOf("G02"));
            Assert.AreEqual(
                PluginGrantVerdict.RejectedAlreadyCarried,
                loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out _),
                "it went past its ceiling");
        }

        [Test]
        public void OnlyACardAtItsCeilingLeavesTheRewardPool()
        {
            // [확정] docs/03 3절 6항: 현재 최대로 강화된 플러그인은 후보에서 제거 - those and no
            // others. A card the player owns but can still raise has to stay offered, because
            // taking it again is the only way it goes up.
            PluginLoadout loadout = Carrying(PluginCatalog.Lightweight());

            Assert.IsFalse(loadout.IsAtMaxLevel("G02"), "a Lv.1 card was treated as finished");

            while (PluginLevels.CanLevel("G02", loadout.LevelOf("G02")))
                loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out _);

            Assert.IsTrue(loadout.IsAtMaxLevel("G02"));
        }

        [Test]
        public void TheCeilingAndTheStepAreData()
        {
            // 개별 maxLevel·다음 단계 효과는 데이터로 정의. No card states either yet, so every
            // one takes the drafted default - and that default lives in one place.
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
            {
                Assert.GreaterOrEqual(
                    PluginLevels.MaxLevelFor(all[i].ProposalId), PluginLevels.FirstLevel,
                    all[i].ProposalId);
            }

            Assert.AreEqual(PluginLevels.DraftMaxLevel, PluginLevels.MaxLevelFor("G02"));
        }

        // --- what a level is worth -------------------------------------------------------------------

        [Test]
        public void ALevelMultipliesWhatTheCardIsWorth()
        {
            PluginDefinition one = PluginCatalog.Lightweight();
            PluginDefinition two = PluginLevels.LevelledUp(one);

            Assert.AreEqual(2, two.Level);
            Assert.Greater(two.Passive.MoveSpeedFraction, one.Passive.MoveSpeedFraction);
            Assert.AreEqual(
                one.Passive.MoveSpeedFraction * (1f + PluginLevels.DraftStepFraction),
                two.Passive.MoveSpeedFraction, 0.0001f);
        }

        [Test]
        public void ALevelDoesNotChangeDurationsOrCooldowns()
        {
            // A level makes a card worth more, not different. docs/03 says 다음 단계 효과는
            // 데이터로 정의 rather than giving a rule to infer one from, so nothing that changes
            // what a card DOES is touched.
            PluginDefinition one = PluginCatalog.EmergencyRecovery();
            PluginDefinition three = PluginLevels.At(one, 3);

            Assert.AreEqual(one.StatusDurationTicksOverride, three.StatusDurationTicksOverride);
            Assert.AreEqual(one.Rule.InternalCooldownTicks, three.Rule.InternalCooldownTicks);
            Assert.AreEqual(one.RadiusUnits, three.RadiusUnits, 0.0001f);
            Assert.AreEqual(one.Trigger, three.Trigger);
            Assert.AreEqual(one.Effect, three.Effect);
        }

        [Test]
        public void RaisingACardDoesNotRerollWhatItRolled()
        {
            // G01 랜덤 패치 rolls three stats when it is granted. A level-up that rebuilt it from
            // the catalogue would hand the player three different stats - a different card, not
            // a stronger one.
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.RandomPatch(), Rng(), out _);

            var before = new List<StatModifier>();
            loadout.CollectPassiveModifiers(before);

            loadout.TryGrant(PluginCatalog.RandomPatch(), Rng(), out _);

            var after = new List<StatModifier>();
            loadout.CollectPassiveModifiers(after);

            Assert.AreEqual(before.Count, after.Count, "the number of rolled stats changed");
            for (int i = 0; i < before.Count; ++i)
                Assert.AreEqual(before[i].Stat, after[i].Stat, $"stat {i} was rerolled");
        }

        [Test]
        public void ScalingTwiceIsNotCompounded()
        {
            // Lv.3 is worth the same whether it got there in one step or two.
            PluginDefinition straight = PluginLevels.At(PluginCatalog.Lightweight(), 3);
            PluginDefinition stepped =
                PluginLevels.LevelledUp(PluginLevels.LevelledUp(PluginCatalog.Lightweight()));

            Assert.AreEqual(
                straight.Passive.MoveSpeedFraction, stepped.Passive.MoveSpeedFraction, 0.0001f);
        }

        [Test]
        public void ALevelBelowTheFirstReadsAsTheFirst()
        {
            // A card authored without a level, and every definition in the catalogue, is Lv.1.
            Assert.AreEqual(0, PluginCatalog.Lightweight().Level, "the catalogue authored a level");
            Assert.AreEqual(PluginLevels.FirstLevel, PluginLevels.Clamp("G02", 0));
            Assert.AreEqual(PluginLevels.FirstLevel, PluginLevels.Clamp("G02", -4));
            Assert.AreEqual(1f, PluginLevels.MultiplierFor("G02", 0), 0.0001f);
        }

        // --- 레벨 보존 across a save ------------------------------------------------------------------

        [Test]
        public void AnInstanceIdCarriesTheLevel()
        {
            // [확정] docs/04 3.1: 학습할 때 레벨을 유지한다.
            PluginInstanceId id = PluginInstanceId.Mint("plugin.kill_stack", "abc", 3);

            Assert.AreEqual(3, id.Level);
            Assert.AreEqual("plugin.kill_stack", id.DocumentKey);
            Assert.AreEqual("abc", id.Unique);
            Assert.AreEqual(3, PluginInstanceId.Parse(id.Value).Level);
        }

        [Test]
        public void ALevelOneInstanceIsWrittenExactlyAsItAlwaysWas()
        {
            // No marker at the first level, so every id this project has already written into a
            // player's file is still exactly what it was and nothing has to be migrated.
            Assert.AreEqual(
                "plugin.kill_stack#abc", PluginInstanceId.Mint("plugin.kill_stack", "abc", 1).Value);
            Assert.AreEqual(
                "plugin.kill_stack#abc", PluginInstanceId.Mint("plugin.kill_stack", "abc").Value);
        }

        [Test]
        public void AnIdFromBeforeLevelsIsALevelOneCard()
        {
            Assert.AreEqual(1, PluginInstanceId.Parse("plugin.kill_stack#abc").Level);
            Assert.AreEqual(1, PluginInstanceId.Parse("8f14e45fceea167a5a36dedd4bea2543").Level);
        }

        [Test]
        public void DeployingACardBringsItBackAtItsLevel()
        {
            string id = PluginInstanceId.Mint(PluginDocumentKeys.KeyFor("G02"), "abc", 3).Value;

            DeployedPlugins.Resolution resolved = DeployedPlugins.Resolve(new[] { id });

            Assert.AreEqual(1, resolved.Cards.Count);
            Assert.AreEqual(3, resolved.Cards[0].Level);
            Assert.Greater(
                resolved.Cards[0].Passive.MoveSpeedFraction,
                PluginCatalog.Lightweight().Passive.MoveSpeedFraction,
                "it came back at full size but with Lv.1 numbers");
        }

        [Test]
        public void LearningACardArchivesTheLevelItWasAt()
        {
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out _);
            loadout.TryGrant(PluginCatalog.Lightweight(), Rng(), out _);
            Assert.AreEqual(2, loadout.LevelOf("G02"));

            var learning = new ExtractionLearning(() => "u1");
            learning.Offer(loadout);
            Assert.AreEqual(1, learning.Candidates.Count);
            Assert.AreEqual(2, learning.Candidates[0].Level, "the candidate forgot its level");

            learning.Toggle(learning.Candidates[0].DocumentKey);
            string[] ids = learning.MintChosenInstanceIds();

            Assert.AreEqual(1, ids.Length);
            Assert.AreEqual(2, PluginInstanceId.Parse(ids[0]).Level);
        }

        [Test]
        public void ALevelSurvivesARoundTripThroughTheArchive()
        {
            // The whole point: raise it in a run, learn it, take it out of the 보관 DB, walk in
            // with it. [확정] R-015 레벨 보존.
            var loadout = new PluginLoadout();
            loadout.TryGrant(PluginCatalog.KillStack(), Rng(), out _);
            loadout.TryGrant(PluginCatalog.KillStack(), Rng(), out _);

            var learning = new ExtractionLearning(() => "trip");
            learning.Offer(loadout);
            learning.Toggle(PluginDocumentKeys.KeyFor("R02"));
            string[] archived = learning.MintChosenInstanceIds();

            DeployedPlugins.Resolution deployed = DeployedPlugins.Resolve(archived);

            Assert.AreEqual(1, deployed.Cards.Count);
            Assert.AreEqual("R02", deployed.Cards[0].ProposalId);
            Assert.AreEqual(2, deployed.Cards[0].Level, "the level was lost in the 보관 DB");
        }

        // --- the one stated per-level effect ------------------------------------------------------------

        [Test]
        public void TheBoomerangGainsOnePerLevel()
        {
            // [확정] docs/03 L02: 레벨 상승 시 부메랑 수 증가.
            Assert.AreEqual(1, PluginCatalog.BoomerangProtocol().Firing.MainShotCount);
            Assert.AreEqual(2, PluginLevels.At(PluginCatalog.BoomerangProtocol(), 2).Firing.MainShotCount);
            Assert.AreEqual(3, PluginLevels.At(PluginCatalog.BoomerangProtocol(), 3).Firing.MainShotCount);
        }

        [Test]
        public void NoOtherCardsCountsMove()
        {
            // The boomerang is the exception because docs/03 states it. Nothing else infers one.
            PluginDefinition support = PluginLevels.At(PluginCatalog.SupportRound(), 3);
            Assert.AreEqual(
                PluginCatalog.SupportRound().Firing.ExtraShotsPerVolley,
                support.Firing.ExtraShotsPerVolley);

            PluginDefinition cache = PluginLevels.At(PluginCatalog.QuadCache(), 3);
            Assert.AreEqual(PluginCatalog.QuadCache().Firing.BurstShots, cache.Firing.BurstShots);
        }

        [Test]
        public void EveryCardStillValidatesAtEveryLevel()
        {
            // A level that produced an invalid card would be a reward the game refuses to hand
            // out, after telling the player they had earned it.
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
            {
                for (int level = 1; level <= PluginLevels.MaxLevelFor(all[i].ProposalId); ++level)
                {
                    PluginDefinition at = PluginLevels.At(all[i], level);
                    Assert.DoesNotThrow(() => at.Validate(), $"{all[i].ProposalId} Lv.{level}");
                }
            }
        }
    }
}
