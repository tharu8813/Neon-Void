using NeonVoid.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace NeonVoid.Tests
{
    /// <summary>
    /// The keys the game actually ships with, read off the asset the game actually loads.
    /// <para>
    /// The HUD prints Q, E, R and F. Until now those were four strings typed into a serialized
    /// field, and nothing checked that they were the keys the input asset was bound to - so the
    /// HUD could have been teaching the player the wrong key and every test would still pass.
    /// </para>
    /// </summary>
    public sealed class ShippedInputAssetTests
    {
        private const string AssetPath = "Assets/_NeonVoid/Content/Input/NeonVoidControls.inputactions";

        private static KeyBindingTable Sheet()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            Assert.IsNotNull(asset, $"{AssetPath} is not where the scene builder looks for it");

            KeyBindingTable sheet = InputBindingDirector.ReadSheet(asset);
            Assert.IsNotEmpty(sheet.Slots, $"the asset has no '{KeyBindings.MapName}' map");
            return sheet;
        }

        [Test]
        public void TheKeysTheHudPrintsAreTheKeysTheAssetIsBoundTo()
        {
            KeyBindingTable sheet = Sheet();

            Assert.AreEqual("Q", sheet.DisplayKeyFor("Skill1"));
            Assert.AreEqual("E", sheet.DisplayKeyFor("Skill2"));
            Assert.AreEqual("R", sheet.DisplayKeyFor("Skill3"));
            Assert.AreEqual("F", sheet.DisplayKeyFor("Interact"));
            Assert.AreEqual("Space", sheet.DisplayKeyFor("Dash"));
        }

        [Test]
        public void TheShippedDefaultsDoNotPutTwoActionsOnOneKey()
        {
            // docs/06 section 3 asks for conflict detection. The first thing worth detecting a
            // conflict in is the set the player starts with.
            Assert.IsEmpty(Sheet().Conflicts());
        }

        [Test]
        public void NothingIsOverriddenInTheAssetAsItShips()
        {
            // An override committed into the asset would be a key the player never chose and
            // 재설정 could not take them back to.
            Assert.IsEmpty(Sheet().Overrides());
        }

        [Test]
        public void EveryBindingIsEitherAKeyACompositeHeaderOrThePointer()
        {
            KeyBindingTable sheet = Sheet();
            int rebindable = 0;
            int headers = 0;
            int pointers = 0;

            for (int i = 0; i < sheet.Slots.Count; ++i)
            {
                switch (sheet.Slots[i].Kind)
                {
                    case KeyBindingKind.Rebindable:
                        Assert.IsTrue(
                            KeyBindings.IsControlPath(sheet.Slots[i].CurrentPath),
                            $"{sheet.Slots[i].Label} is offered as a key but is not a control");
                        ++rebindable;
                        break;

                    case KeyBindingKind.CompositeHeader:
                        ++headers;
                        break;

                    default:
                        ++pointers;
                        break;
                }
            }

            // WASD and the arrows.
            Assert.AreEqual(2, headers, "the move composites are not two");

            // [확정] docs/02 section 7: 마우스 조준, and nothing else is aimed.
            Assert.AreEqual(1, pointers);
            Assert.Greater(rebindable, 0);
        }

        [Test]
        public void EveryRowHasAName()
        {
            // A settings screen row with no label is a key the player cannot identify.
            KeyBindingTable sheet = Sheet();

            for (int i = 0; i < sheet.Slots.Count; ++i)
                Assert.IsNotEmpty(sheet.Slots[i].Label, sheet.Slots[i].BindingId);
        }

        [Test]
        public void EveryBindingHasAnIdToStoreAnOverrideUnder()
        {
            // The override is keyed on this. A blank one would make two bindings the same key
            // in the settings file.
            KeyBindingTable sheet = Sheet();
            var seen = new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < sheet.Slots.Count; ++i)
            {
                Assert.IsNotEmpty(sheet.Slots[i].BindingId, sheet.Slots[i].Label);
                Assert.IsTrue(seen.Add(sheet.Slots[i].BindingId), $"{sheet.Slots[i].Label} shares an id");
            }
        }

        [Test]
        public void TheMoveKeysAreNamedByDirection()
        {
            // 이동 위 rather than four rows all called 이동.
            KeyBindingTable sheet = Sheet();
            int named = 0;

            for (int i = 0; i < sheet.Slots.Count; ++i)
            {
                if (sheet.Slots[i].ActionName != "Move" || !sheet.Slots[i].IsRebindable)
                    continue;

                StringAssert.StartsWith("이동 ", sheet.Slots[i].Label);
                ++named;
            }

            Assert.AreEqual(8, named, "WASD and the arrow keys are eight directions");
        }
    }
}
