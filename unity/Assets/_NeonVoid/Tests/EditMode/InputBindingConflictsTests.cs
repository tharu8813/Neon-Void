using System.Collections.Generic;
using NeonVoid.Player;
using NUnit.Framework;

namespace NeonVoid.Tests
{
    /// <summary>
    /// docs/06 section 3 requires key conflict checking alongside rebinding.
    /// </summary>
    public sealed class InputBindingConflictsTests
    {
        private static KeyValuePair<string, string> Bind(string action, string path) =>
            new KeyValuePair<string, string>(action, path);

        [Test]
        public void TheShippedDraftBindingsDoNotCollide()
        {
            // Mirrors Content/Input/NeonVoidControls.inputactions. Escape is on Menu only;
            // Cancel sits on Backspace precisely so the two do not fight.
            var bindings = new[]
            {
                Bind("Move", "<Keyboard>/w"), Bind("Move", "<Keyboard>/a"),
                Bind("Move", "<Keyboard>/s"), Bind("Move", "<Keyboard>/d"),
                Bind("Move", "<Keyboard>/upArrow"), Bind("Move", "<Keyboard>/downArrow"),
                Bind("Move", "<Keyboard>/leftArrow"), Bind("Move", "<Keyboard>/rightArrow"),
                Bind("Aim", "<Mouse>/position"),
                Bind("Dash", "<Keyboard>/space"),
                Bind("Skill1", "<Keyboard>/q"),
                Bind("Skill2", "<Keyboard>/e"),
                Bind("Skill3", "<Keyboard>/r"),
                Bind("Interact", "<Keyboard>/f"),
                Bind("Inspect", "<Keyboard>/tab"),
                Bind("Menu", "<Keyboard>/escape"),
                Bind("Confirm", "<Keyboard>/enter"),
                Bind("Cancel", "<Keyboard>/backspace"),
            };

            var conflicts = InputBindingConflicts.Find(bindings);

            Assert.IsEmpty(conflicts, "Draft bindings collide: " + string.Join("; ", conflicts));
        }

        [Test]
        public void TwoActionsOnOneKeyAreReportedWithBothOwners()
        {
            var conflicts = InputBindingConflicts.Find(new[]
            {
                Bind("Menu", "<Keyboard>/escape"),
                Bind("Cancel", "<Keyboard>/escape"),
            });

            Assert.AreEqual(1, conflicts.Count);
            Assert.AreEqual("Menu", conflicts[0].FirstAction);
            Assert.AreEqual("Cancel", conflicts[0].SecondAction);
        }

        [Test]
        public void ControlPathCaseDoesNotHideAConflict()
        {
            var conflicts = InputBindingConflicts.Find(new[]
            {
                Bind("Dash", "<Keyboard>/Space"),
                Bind("Confirm", "<keyboard>/space"),
            });

            Assert.AreEqual(1, conflicts.Count, "Case differences let a real key clash through.");
        }

        [Test]
        public void OneActionBoundTwiceToTheSameKeyIsNotAConflict()
        {
            // Redundant, but it cannot produce ambiguous behaviour, so it must not block
            // the player in the rebinding UI.
            var conflicts = InputBindingConflicts.Find(new[]
            {
                Bind("Move", "<Keyboard>/w"),
                Bind("Move", "<Keyboard>/w"),
            });

            Assert.IsEmpty(conflicts);
        }

        [Test]
        public void ThreeWayCollisionReportsEveryClashingPair()
        {
            var conflicts = InputBindingConflicts.Find(new[]
            {
                Bind("Dash", "<Keyboard>/space"),
                Bind("Confirm", "<Keyboard>/space"),
                Bind("Interact", "<Keyboard>/space"),
            });

            Assert.AreEqual(2, conflicts.Count, "A third claim on the same key went unreported.");
        }

        [Test]
        public void EmptyAndNullEntriesAreIgnoredRatherThanCrashing()
        {
            var conflicts = InputBindingConflicts.Find(new[]
            {
                Bind("Dash", null),
                Bind(null, "<Keyboard>/space"),
                Bind("", "  "),
                Bind("Interact", "<Keyboard>/f"),
            });

            Assert.IsEmpty(conflicts);
        }

        [Test]
        public void NullInputReturnsNoConflictsInsteadOfThrowing()
        {
            Assert.IsEmpty(InputBindingConflicts.Find(null));
        }
    }
}
