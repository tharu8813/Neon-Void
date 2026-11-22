using System.Collections.Generic;
using NeonVoid.Missions;
using NeonVoid.Plugins;
using NeonVoid.Progression.Save;
using NeonVoid.UI;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The 플러그인 데이터베이스 터미널's content.
    /// <para>
    /// [확정] docs/04 section 3.3-3.4: 보관 DB 용량 무제한, 출전 개수 제한 없음, and DB에서
    /// 꺼내면 DB에서 사라진다 - 복사가 아니라 동일 소유 인스턴스의 이동.
    /// </para>
    /// </summary>
    public sealed class PluginDatabaseSheetTests
    {
        private static VaccineSaveState Owning(string[] archive, string[] loadout)
        {
            var vaccine = new VaccineSaveState();
            vaccine.archivePluginInstanceIds = new List<string>(archive ?? new string[0]);
            vaccine.carriedPluginInstanceIds = new List<string>(loadout ?? new string[0]);
            return vaccine;
        }

        private static string Instance(string proposalId, string unique) =>
            PluginInstanceId.Mint(PluginDocumentKeys.KeyFor(proposalId), unique).Value;

        // --- the terminal is connected now ------------------------------------------------------

        [Test]
        public void AllFourHubTerminalsDoSomething()
        {
            // The 플러그인 데이터베이스 was the last one with nothing behind it, and a hub with a
            // post the player gets no answer from reads as broken rather than unfinished.
            for (int i = 0; i < HubTerminals.All.Length; ++i)
            {
                HubTerminalKind kind = HubTerminals.All[i];
                Assert.IsTrue(HubTerminals.IsConnected(kind), HubTerminals.NameOf(kind));
                Assert.IsEmpty(HubTerminals.PendingLabel(kind));
                Assert.IsEmpty(HubTerminals.WhyNotConnected(kind));
            }

            Assert.AreEqual(HubTerminals.Count, HubTerminals.All.Length);
        }

        // --- what it lists ------------------------------------------------------------------------

        [Test]
        public void ItListsBothSidesAndSaysWhichIsWhich()
        {
            PluginDatabaseSheet sheet = PluginDatabaseSheet.Build(
                Owning(new[] { Instance("R02", "a") }, new[] { Instance("G02", "b") }),
                canMove: true, unavailable: string.Empty);

            Assert.IsTrue(sheet.Shown);
            Assert.AreEqual(2, sheet.Rows.Count);
            Assert.AreEqual(1, sheet.ArchiveCount);
            Assert.AreEqual(1, sheet.LoadoutCount);

            Assert.AreEqual(PluginHolding.Archive, sheet.Rows[0].Holding);
            Assert.AreEqual("R02", sheet.Rows[0].ProposalId);
            Assert.AreEqual(PluginHolding.Loadout, sheet.Rows[1].Holding);
            Assert.AreEqual("G02", sheet.Rows[1].ProposalId);
        }

        [Test]
        public void ARowKnowsWhereItWouldGo()
        {
            // [확정] docs/04 3.4: it is a move, so every row has exactly one destination and the
            // screen never has to work it out.
            PluginDatabaseSheet sheet = PluginDatabaseSheet.Build(
                Owning(new[] { Instance("R02", "a") }, new[] { Instance("G02", "b") }),
                canMove: true, unavailable: string.Empty);

            Assert.AreEqual(PluginHolding.Loadout, sheet.Rows[0].MovesTo);
            Assert.AreEqual(PluginHolding.Archive, sheet.Rows[1].MovesTo);
        }

        [Test]
        public void ARowCarriesWhatThePlayerReads()
        {
            PluginDatabaseSheet sheet = PluginDatabaseSheet.Build(
                Owning(new[] { Instance("R02", "a") }, null), canMove: true, unavailable: string.Empty);

            PluginDatabaseRow row = sheet.Rows[0];
            Assert.AreEqual(PluginCardTexts.For("R02").Name, row.Name);
            Assert.AreEqual(PluginCardTexts.For("R02").Effect, row.Effect);
            Assert.AreEqual(PluginRarity.Rare, row.Rarity);
            Assert.IsTrue(row.IsKnownHere);
            Assert.AreEqual(PluginDocumentKeys.KeyFor("R02"), row.DocumentKey);
        }

        [Test]
        public void AnInstanceThisBuildCannotNameIsStillListed()
        {
            // It is the player's - written before the document keys, or by a later build.
            // Hiding it would make their own property vanish from the only screen that lists it.
            PluginDatabaseSheet sheet = PluginDatabaseSheet.Build(
                Owning(new[] { "8f14e45fceea167a5a36dedd4bea2543" }, null),
                canMove: true, unavailable: string.Empty);

            Assert.AreEqual(1, sheet.Rows.Count);
            Assert.IsFalse(sheet.Rows[0].IsKnownHere);
            Assert.IsNotEmpty(sheet.Rows[0].Name, "an unnamed row would read as a bug");
            Assert.AreEqual("8f14e45fceea167a5a36dedd4bea2543", sheet.Rows[0].InstanceId);
        }

        [Test]
        public void AnEmptyDatabaseIsStillAScreen()
        {
            PluginDatabaseSheet sheet = PluginDatabaseSheet.Build(
                Owning(null, null), canMove: true, unavailable: string.Empty);

            Assert.IsTrue(sheet.Shown);
            Assert.IsTrue(sheet.IsEmpty);
            Assert.AreEqual(0, sheet.ArchiveCount);
            Assert.AreEqual(0, sheet.LoadoutCount);
        }

        [Test]
        public void NoSaveMeansNothingToDraw()
        {
            Assert.IsFalse(PluginDatabaseSheet.Build(null, true, string.Empty).Shown);
            Assert.IsFalse(PluginDatabaseSheet.Closed.Shown);
            Assert.IsEmpty(PluginDatabaseSheet.Closed.Rows);
            Assert.IsEmpty(PluginDatabaseSheet.Closed.Unavailable);
        }

        [Test]
        public void TheDefaultSheetAnswersWithoutThrowing()
        {
            // default(PluginDatabaseSheet) never runs the constructor, and a screen that asked
            // it anything would NRE. This project has shipped that shape three times.
            PluginDatabaseSheet blank = default;

            Assert.IsEmpty(blank.Rows);
            Assert.IsEmpty(blank.Unavailable);
            Assert.IsFalse(blank.Shown);
            Assert.IsTrue(blank.IsEmpty);
            Assert.AreEqual(0, blank.ArchiveCount);
        }

        [Test]
        public void ASheetThatCannotMoveSaysWhy()
        {
            // A button that silently does nothing reads as broken; the actual reason is almost
            // always that the save is not committing.
            PluginDatabaseSheet sheet = PluginDatabaseSheet.Build(
                Owning(new[] { Instance("R02", "a") }, null),
                canMove: false, unavailable: "저장이 연결되지 않아 이동이 기록되지 않습니다");

            Assert.IsFalse(sheet.CanMove);
            Assert.IsNotEmpty(sheet.Unavailable);
        }

        [Test]
        public void NoLimitIsEnforcedOnEitherSide()
        {
            // [확정] docs/04 3.3: 보관 DB 용량 무제한, 출전 개수 제한 없음. The counts are numbers
            // to show, never a cap.
            var many = new List<string>();
            IReadOnlyList<PluginDefinition> all = PluginCatalog.All();
            for (int i = 0; i < all.Count; ++i)
                many.Add(Instance(all[i].ProposalId, "u" + i));

            PluginDatabaseSheet sheet = PluginDatabaseSheet.Build(
                Owning(many.ToArray(), null), canMove: true, unavailable: string.Empty);

            Assert.AreEqual(all.Count, sheet.ArchiveCount);
            Assert.IsTrue(sheet.CanMove);
        }

        [Test]
        public void BothSidesAreNamedInTheGamesOwnWords()
        {
            Assert.AreEqual("보관 DB", PluginDatabaseSheet.LabelOf(PluginHolding.Archive));
            Assert.AreEqual("출전", PluginDatabaseSheet.LabelOf(PluginHolding.Loadout));
        }
    }
}
