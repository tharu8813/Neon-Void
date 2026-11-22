using System.Collections.Generic;
using NeonVoid.Plugins;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// 출전 소지: turning what the save holds into the cards a 의뢰 starts with.
    /// <para>
    /// [확정] docs/00 section 5: 출전 소지 플러그인은 사망 시 소실되므로 '들고 간 영구 자산'도
    /// 위험 자산이다. That only means anything if the 출전 list is what the player walks in with.
    /// </para>
    /// </summary>
    public sealed class DeployedPluginsTests
    {
        private static string Instance(string proposalId, string unique) =>
            PluginInstanceId.Mint(PluginDocumentKeys.KeyFor(proposalId), unique).Value;

        [Test]
        public void EachIdBecomesTheCardItNames()
        {
            DeployedPlugins.Resolution resolved = DeployedPlugins.Resolve(
                new[] { Instance("R02", "a"), Instance("G02", "b"), Instance("L01", "c") });

            Assert.AreEqual(3, resolved.Cards.Count);
            Assert.AreEqual("R02", resolved.Cards[0].ProposalId);
            Assert.AreEqual("G02", resolved.Cards[1].ProposalId);
            Assert.AreEqual("L01", resolved.Cards[2].ProposalId);
            Assert.AreEqual(0, resolved.Missing);
        }

        [Test]
        public void TheOrderIsTheOrderTheSaveListedThem()
        {
            // A replay that deployed the same cards in a different order would roll their
            // passives differently - the rolls come off one stream in grant order.
            DeployedPlugins.Resolution first = DeployedPlugins.Resolve(
                new[] { Instance("G02", "a"), Instance("R02", "b") });
            DeployedPlugins.Resolution second = DeployedPlugins.Resolve(
                new[] { Instance("R02", "b"), Instance("G02", "a") });

            Assert.AreEqual("G02", first.Cards[0].ProposalId);
            Assert.AreEqual("R02", second.Cards[0].ProposalId);
        }

        [Test]
        public void AnIdFromBeforeTheKeysIsCountedRatherThanDropped()
        {
            // The player owns it; it just will not be in their hands this run. A loadout
            // silently shorter than the 보관 DB terminal listed gets blamed on the terminal.
            DeployedPlugins.Resolution resolved = DeployedPlugins.Resolve(
                new[] { "8f14e45fceea167a5a36dedd4bea2543", Instance("R02", "a") });

            Assert.AreEqual(1, resolved.Cards.Count);
            Assert.AreEqual(1, resolved.Unnamed);
            Assert.AreEqual(0, resolved.Unknown);
            Assert.AreEqual(1, resolved.Missing);
        }

        [Test]
        public void AKeyFromALaterBuildIsCountedToo()
        {
            DeployedPlugins.Resolution resolved = DeployedPlugins.Resolve(
                new[] { "plugin.something_new#abc", Instance("G02", "a") });

            Assert.AreEqual(1, resolved.Cards.Count);
            Assert.AreEqual(0, resolved.Unnamed);
            Assert.AreEqual(1, resolved.Unknown);
        }

        [Test]
        public void NothingDeployedIsAnEmptyLoadoutAndNotAnError()
        {
            // [확정] docs/00: a fresh vaccine owns nothing. Its plugins come from the 보상
            // screens inside the run, so starting empty is the design and not a failure.
            DeployedPlugins.Resolution none = DeployedPlugins.Resolve(new string[0]);
            Assert.IsEmpty(none.Cards);
            Assert.AreEqual(0, none.Missing);

            DeployedPlugins.Resolution nothing = DeployedPlugins.Resolve(null);
            Assert.IsEmpty(nothing.Cards);
            Assert.AreEqual(0, nothing.Missing);
        }

        [Test]
        public void TheDefaultResolutionAnswersWithoutThrowing()
        {
            DeployedPlugins.Resolution blank = default;

            Assert.IsEmpty(blank.Cards);
            Assert.AreEqual(0, blank.Missing);
            Assert.IsNotEmpty(blank.ToString());
        }

        [Test]
        public void NoLimitIsAppliedToHowManyMayBeDeployed()
        {
            // [확정] docs/04 3.3: 출전 수 제한 없음.
            var ids = new List<string>();
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
                ids.Add(Instance(all[i].ProposalId, "u" + i));

            DeployedPlugins.Resolution resolved = DeployedPlugins.Resolve(ids);

            Assert.AreEqual(all.Count, resolved.Cards.Count);
            Assert.AreEqual(0, resolved.Missing);
        }

        [Test]
        public void ItInventsNothing()
        {
            // Every card it returns is one the catalogue authored. A resolution that built a
            // definition from a key would put a plugin in the game that nobody wrote.
            DeployedPlugins.Resolution resolved = DeployedPlugins.Resolve(
                new[] { Instance("G09", "a"), "plugin.invented#b", "nonsense" });

            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < resolved.Cards.Count; ++i)
            {
                bool authored = false;
                for (int c = 0; c < all.Count; ++c)
                    authored |= all[c].ProposalId == resolved.Cards[i].ProposalId;

                Assert.IsTrue(authored, resolved.Cards[i].ProposalId);
            }

            Assert.AreEqual(1, resolved.Cards.Count);
            Assert.AreEqual(2, resolved.Missing);
        }

        [Test]
        public void ItSaysWhatItDidInOneLine()
        {
            // For the log a player's bug report will contain.
            string clean = DeployedPlugins.Resolve(new[] { Instance("R02", "a") }).ToString();
            string lossy = DeployedPlugins.Resolve(new[] { "old-id" }).ToString();

            Assert.IsNotEmpty(clean);
            Assert.AreNotEqual(clean, lossy);
            StringAssert.Contains("알지 못함", lossy);
        }
    }
}
