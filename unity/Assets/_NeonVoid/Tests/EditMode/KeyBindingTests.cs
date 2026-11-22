using System.Collections.Generic;
using NeonVoid.Player;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// Which key belongs to which action, and what happens when two actions want the same one.
    /// <para>
    /// docs/06 section 3 asks for conflict detection and docs/05 section 2 puts 키 on the
    /// 설정 screen. Both are rules about a table, and a rule that can only be exercised by
    /// pressing a physical key on a real device is a rule nothing checks - so the table is a
    /// plain type with no Unity input in it.
    /// </para>
    /// </summary>
    public sealed class KeyBindingTests
    {
        private const string DashId = "dash-0";
        private const string Skill1Id = "skill1-0";
        private const string UpId = "move-up";
        private const string ArrowUpId = "move-up-arrow";
        private const string CompositeId = "move-wasd";
        private const string AimId = "aim-0";

        private static KeyBindingTable Sheet() => new KeyBindingTable(new List<KeyBindingSlot>
        {
            new KeyBindingSlot(DashId, "Dash", "대시", "<Keyboard>/space", "<Keyboard>/space", KeyBindingKind.Rebindable),
            new KeyBindingSlot(Skill1Id, "Skill1", "스킬 1", "<Keyboard>/q", "<Keyboard>/q", KeyBindingKind.Rebindable),
            new KeyBindingSlot(CompositeId, "Move", "이동 (WASD)", "2DVector", "2DVector", KeyBindingKind.CompositeHeader),
            new KeyBindingSlot(UpId, "Move", "이동 위", "<Keyboard>/w", "<Keyboard>/w", KeyBindingKind.Rebindable),
            new KeyBindingSlot(ArrowUpId, "Move", "이동 위", "<Keyboard>/upArrow", "<Keyboard>/upArrow", KeyBindingKind.Rebindable),
            new KeyBindingSlot(AimId, "Aim", "조준", "<Mouse>/position", "<Mouse>/position", KeyBindingKind.Pointer),
        });

        // --- what a key reads as ---------------------------------------------------------

        [Test]
        public void AKeyReadsAsItsCap()
        {
            Assert.AreEqual("Q", KeyBindings.DisplayKey("<Keyboard>/q"));
            Assert.AreEqual("Space", KeyBindings.DisplayKey("<Keyboard>/space"));
            Assert.AreEqual("↑", KeyBindings.DisplayKey("<Keyboard>/upArrow"));
            Assert.AreEqual("Esc", KeyBindings.DisplayKey("<Keyboard>/escape"));
        }

        [Test]
        public void AKeyThisTableHasNeverHeardOfStillPrintsSomething()
        {
            // Better a path's last segment than an empty key cap the player cannot match to
            // anything on their keyboard.
            Assert.AreEqual("f13", KeyBindings.DisplayKey("<Keyboard>/f13"));
            Assert.AreEqual(string.Empty, KeyBindings.DisplayKey(null));
        }

        [Test]
        public void OnlyAControlPathIsAcceptedAsOne()
        {
            Assert.IsTrue(KeyBindings.IsControlPath("<Keyboard>/q"));
            Assert.IsFalse(KeyBindings.IsControlPath("q"));
            Assert.IsFalse(KeyBindings.IsControlPath("2DVector"), "a composite is not a control");
            Assert.IsFalse(KeyBindings.IsControlPath(string.Empty));
        }

        // --- what can be moved -------------------------------------------------------------

        [Test]
        public void AnOrdinaryKeyMoves()
        {
            RebindResult result = Sheet().Rebind(DashId, "<Keyboard>/leftShift");

            Assert.IsTrue(result.Ok, result.Detail);
            Assert.AreEqual("<Keyboard>/leftShift", result.Table.Find(DashId).CurrentPath);
            Assert.AreEqual("L-Shift", result.Table.Find(DashId).DisplayKey);
            Assert.IsTrue(result.Table.Find(DashId).IsOverridden);
        }

        [Test]
        public void TheCompositesOwnRowIsNotAKey()
        {
            // 2DVector is the four directions as one thing. Moving it would mean nothing.
            RebindResult result = Sheet().Rebind(CompositeId, "<Keyboard>/j");

            Assert.AreEqual(RebindVerdict.NotRebindable, result.Verdict);
            Assert.IsNotEmpty(result.Detail);
        }

        [Test]
        public void ThePointerIsNotAKeyEither()
        {
            // [확정] docs/02 section 7: 마우스 조준. There is no key to move it to.
            Assert.AreEqual(RebindVerdict.NotRebindable, Sheet().Rebind(AimId, "<Keyboard>/j").Verdict);
        }

        [Test]
        public void SomethingThatIsNotAControlIsRefused()
        {
            Assert.AreEqual(RebindVerdict.NotAControl, Sheet().Rebind(DashId, "q").Verdict);
            Assert.AreEqual(RebindVerdict.UnknownBinding, Sheet().Rebind("nothing", "<Keyboard>/j").Verdict);
        }

        [Test]
        public void RebindingToTheKeyItAlreadyHasChangesNothing()
        {
            Assert.AreEqual(RebindVerdict.Unchanged, Sheet().Rebind(DashId, "<Keyboard>/space").Verdict);
        }

        // --- conflicts -----------------------------------------------------------------------

        [Test]
        public void AKeyAnotherActionHoldsIsRefusedAndTheHolderIsNamed()
        {
            // Refused, not resolved: which action keeps the key is the player's call, and
            // silently unbinding the other is how someone loses their movement keys.
            RebindResult result = Sheet().Rebind(Skill1Id, "<Keyboard>/space");

            Assert.AreEqual(RebindVerdict.Conflict, result.Verdict);
            Assert.AreEqual("대시", result.HeldBy);
            StringAssert.Contains("Space", result.Detail);
            Assert.AreEqual("<Keyboard>/q", result.Table.Find(Skill1Id).CurrentPath, "the sheet changed anyway");
        }

        [Test]
        public void TheSameActionOnTwoKeysIsAnAlternateBindingAndNotAClash()
        {
            // WASD and the arrow keys both move. Refusing that would make the asset's own
            // defaults illegal.
            KeyBindingTable sheet = Sheet();

            Assert.IsEmpty(sheet.Conflicts());

            RebindResult result = sheet.Rebind(ArrowUpId, "<Keyboard>/w");
            Assert.IsTrue(result.Ok, result.Detail);
            Assert.IsEmpty(result.Table.Conflicts());
        }

        [Test]
        public void ASheetWithTwoActionsOnOneKeyReportsIt()
        {
            var clashing = new KeyBindingTable(new List<KeyBindingSlot>
            {
                new KeyBindingSlot(DashId, "Dash", "대시", "<Keyboard>/space", "<Keyboard>/f", KeyBindingKind.Rebindable),
                new KeyBindingSlot(Skill1Id, "Skill1", "스킬 1", "<Keyboard>/q", "<Keyboard>/f", KeyBindingKind.Rebindable),
            });

            IReadOnlyList<InputBindingConflict> conflicts = clashing.Conflicts();
            Assert.AreEqual(1, conflicts.Count);
            StringAssert.Contains("Dash", conflicts[0].ToString());
            StringAssert.Contains("Skill1", conflicts[0].ToString());
        }

        // --- what is written down ---------------------------------------------------------

        [Test]
        public void OnlyWhatThePlayerChangedIsStored()
        {
            // A file that listed every default would turn the asset's keys into data that goes
            // stale the moment a default changes.
            Assert.IsEmpty(Sheet().Overrides());

            KeyBindingTable moved = Sheet().Rebind(DashId, "<Keyboard>/leftShift").Table;
            IReadOnlyList<KeyBindingSlot> overrides = moved.Overrides();

            Assert.AreEqual(1, overrides.Count);
            Assert.AreEqual(DashId, overrides[0].BindingId);
        }

        [Test]
        public void ResettingOneKeyPutsItBackAndLeavesTheRest()
        {
            KeyBindingTable moved = Sheet()
                .Rebind(DashId, "<Keyboard>/leftShift").Table
                .Rebind(Skill1Id, "<Keyboard>/z").Table;

            KeyBindingTable reset = moved.Reset(DashId);

            Assert.AreEqual("<Keyboard>/space", reset.Find(DashId).CurrentPath);
            Assert.AreEqual("<Keyboard>/z", reset.Find(Skill1Id).CurrentPath);
            Assert.AreEqual(1, reset.Overrides().Count);
        }

        [Test]
        public void ResettingEverythingLeavesNothingStored()
        {
            // [확정] docs/05 section 2's 재설정.
            KeyBindingTable moved = Sheet().Rebind(DashId, "<Keyboard>/leftShift").Table;

            Assert.IsEmpty(moved.ResetAll().Overrides());
            Assert.AreEqual("<Keyboard>/space", moved.ResetAll().Find(DashId).CurrentPath);
        }

        // --- reading a stored file back ----------------------------------------------------

        [Test]
        public void StoredOverridesComeBackOntoAFreshSheet()
        {
            var stored = new[] { new KeyValuePair<string, string>(DashId, "<Keyboard>/leftShift") };

            KeyBindingTable loaded = Sheet().WithOverrides(stored);

            Assert.AreEqual("<Keyboard>/leftShift", loaded.Find(DashId).CurrentPath);
            Assert.AreEqual("<Keyboard>/space", loaded.Find(DashId).DefaultPath, "the default was overwritten");
        }

        [Test]
        public void AnOverrideForABindingThisBuildNoLongerHasIsDropped()
        {
            // The settings file outlives any one version of the action asset.
            var stored = new[]
            {
                new KeyValuePair<string, string>("a-binding-that-was-removed", "<Keyboard>/j"),
                new KeyValuePair<string, string>(DashId, "<Keyboard>/leftShift"),
            };

            KeyBindingTable loaded = Sheet().WithOverrides(stored);

            Assert.AreEqual(1, loaded.Overrides().Count);
            Assert.AreEqual("<Keyboard>/leftShift", loaded.Find(DashId).CurrentPath);
        }

        [Test]
        public void AnOverrideThatIsNotAControlPathIsIgnoredRatherThanApplied()
        {
            var stored = new[] { new KeyValuePair<string, string>(DashId, "쓰레기") };

            Assert.AreEqual("<Keyboard>/space", Sheet().WithOverrides(stored).Find(DashId).CurrentPath);
        }

        // --- what a HUD asks it ---------------------------------------------------------------

        [Test]
        public void TheKeyCapForAnActionFollowsTheRebind()
        {
            // The reason this exists: the HUD printed a hardcoded Q.
            KeyBindingTable sheet = Sheet();
            Assert.AreEqual("Q", sheet.DisplayKeyFor("Skill1"));

            KeyBindingTable moved = sheet.Rebind(Skill1Id, "<Keyboard>/z").Table;
            Assert.AreEqual("Z", moved.DisplayKeyFor("Skill1"));
        }

        [Test]
        public void AnActionWithNoKeyAnswersWithNothingRatherThanAWrongOne()
        {
            Assert.AreEqual(string.Empty, Sheet().DisplayKeyFor("Skill9"));
            Assert.AreEqual(string.Empty, Sheet().DisplayKeyFor("Aim"), "the pointer is not a key cap");
        }
    }
}
