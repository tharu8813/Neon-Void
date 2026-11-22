using System;
using System.Collections;
using System.IO;
using NeonVoid.Player;
using NeonVoid.Progression.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// A rebind that reaches the input system, the settings file and the HUD.
    /// <para>
    /// The table is tested on its own in EditMode; what needs a running scene is the three
    /// places a key has to arrive at. [확정] docs/04 section 1 keeps 키 바인딩 in 전역 설정, so
    /// the store here is injected over a temporary directory - docs/04 section 4 fails a test
    /// that reaches the player's own.
    /// </para>
    /// </summary>
    public sealed class KeyBindingPlayModeTests
    {
        private string _root;
        private GameObject _bindingObject;
        private GameObject _hudObject;
        private InputActionAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "neonvoid-keys-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudObject != null)
                UnityEngine.Object.Destroy(_hudObject);
            if (_bindingObject != null)
                UnityEngine.Object.Destroy(_bindingObject);
            if (_asset != null)
                UnityEngine.Object.Destroy(_asset);

            _hudObject = null;
            _bindingObject = null;
            _asset = null;

            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        /// <summary>
        /// A map with the same shape as the shipped one: three skills, 상호작용, 대시, a move
        /// composite and the pointer. Built here rather than loaded so the test says what it is
        /// testing; <c>ShippedInputAssetTests</c> is what checks the real asset.
        /// </summary>
        private InputActionAsset BuildAsset()
        {
            // Once per test. A binding's id is what an override is stored under, and the real
            // asset's ids are serialized in the file - rebuilding the asset between two
            // launches would hand the second one a set of ids the first had never seen, which
            // is a different thing from relaunching the game.
            if (_asset != null)
                return _asset;

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = _asset.AddActionMap(KeyBindings.MapName);

            map.AddAction("Dash", InputActionType.Button, "<Keyboard>/space");
            map.AddAction("Skill1", InputActionType.Button, "<Keyboard>/q");
            map.AddAction("Skill2", InputActionType.Button, "<Keyboard>/e");
            map.AddAction("Interact", InputActionType.Button, "<Keyboard>/f");

            InputAction aim = map.AddAction("Aim", InputActionType.Value, "<Mouse>/position");
            aim.expectedControlType = "Vector2";

            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.expectedControlType = "Vector2";
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            return _asset;
        }

        private GlobalSettingsStore NewSettings()
        {
            var store = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            store.Open();
            return store;
        }

        private InputBindingDirector Director(GlobalSettingsStore settings)
        {
            _bindingObject = new GameObject("Input Binding Director");
            _bindingObject.SetActive(false);
            var director = _bindingObject.AddComponent<InputBindingDirector>();
            director.Configure(BuildAsset());
            director.UseSettings(settings);
            _bindingObject.SetActive(true);
            return director;
        }

        private static string IdOf(InputBindingDirector director, string actionName)
        {
            foreach (KeyBindingSlot slot in director.Table.Slots)
            {
                if (slot.IsRebindable && slot.ActionName == actionName)
                    return slot.BindingId;
            }

            Assert.Fail($"{actionName} has no rebindable binding");
            return string.Empty;
        }

        // --- the three places a key has to arrive ---------------------------------------------

        [UnityTest]
        public IEnumerator ARebindReachesTheInputSystemAndTheSettingsFile()
        {
            InputBindingDirector director = Director(NewSettings());
            string skill1 = IdOf(director, "Skill1");

            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"));

            RebindResult result = director.Rebind(skill1, "<Keyboard>/z");
            Assert.IsTrue(result.Ok, result.Detail);
            yield return null;

            // The input system, which is what actually reads the keyboard.
            InputAction action = _asset.FindActionMap(KeyBindings.MapName).FindAction("Skill1");
            Assert.AreEqual("<Keyboard>/z", action.bindings[0].effectivePath);
            Assert.AreEqual(
                "<Keyboard>/q", action.bindings[0].path,
                "the default was overwritten, so 재설정 has nothing to go back to");

            // And the file, read by a second store so this is what is on disk.
            var reopened = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            Assert.AreEqual(GlobalSettingsVerdict.Loaded, reopened.Open());
            Assert.AreEqual(1, reopened.Document.Overrides().Count);
            Assert.AreEqual("<Keyboard>/z", reopened.Document.Overrides()[0].Value);
        }

        [UnityTest]
        public IEnumerator AStoredKeyIsOnTheActionBeforeAnythingReadsIt()
        {
            // The whole point of writing it down: the next launch starts with the player's key,
            // not the asset's.
            GlobalSettingsStore first = NewSettings();
            InputBindingDirector director = Director(first);
            string dash = IdOf(director, "Dash");
            Assert.IsTrue(director.Rebind(dash, "<Keyboard>/leftShift").Ok);
            yield return null;

            // A relaunch: the component is gone and the asset is back to what it ships with.
            UnityEngine.Object.Destroy(_bindingObject);
            _asset.RemoveAllBindingOverrides();
            yield return null;

            Assert.AreEqual(
                "<Keyboard>/space",
                _asset.FindActionMap(KeyBindings.MapName).FindAction("Dash").bindings[0].effectivePath,
                "the asset was not actually back to its default, so this would prove nothing");

            InputBindingDirector relaunched = Director(NewSettings());

            Assert.AreEqual("L-Shift", relaunched.DisplayKeyFor("Dash"));
            Assert.AreEqual(
                "<Keyboard>/leftShift",
                _asset.FindActionMap(KeyBindings.MapName).FindAction("Dash").bindings[0].effectivePath);
        }

        [UnityTest]
        public IEnumerator AKeyAnotherActionHoldsIsRefusedAndNothingMoves()
        {
            InputBindingDirector director = Director(NewSettings());
            string skill1 = IdOf(director, "Skill1");

            RebindResult result = director.Rebind(skill1, "<Keyboard>/f");
            yield return null;

            Assert.AreEqual(RebindVerdict.Conflict, result.Verdict);
            Assert.AreEqual("상호작용", result.HeldBy);
            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"), "the sheet moved anyway");
            Assert.AreEqual(
                "<Keyboard>/q",
                _asset.FindActionMap(KeyBindings.MapName).FindAction("Skill1").bindings[0].effectivePath);

            var reopened = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            reopened.Open();
            Assert.IsEmpty(reopened.Document.Overrides(), "a refused rebind was written down");
        }

        [UnityTest]
        public IEnumerator ResettingEverythingPutsTheAssetsOwnKeysBack()
        {
            // [확정] docs/05 section 2's 재설정.
            InputBindingDirector director = Director(NewSettings());
            Assert.IsTrue(director.Rebind(IdOf(director, "Skill1"), "<Keyboard>/z").Ok);
            yield return null;

            Assert.IsTrue(director.ResetAll(), director.LastRefusal);
            yield return null;

            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"));
            Assert.AreEqual(
                "<Keyboard>/q",
                _asset.FindActionMap(KeyBindings.MapName).FindAction("Skill1").bindings[0].effectivePath);

            var reopened = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            reopened.Open();
            Assert.IsEmpty(reopened.Document.Overrides());
        }

        // --- what the player is shown ------------------------------------------------------

        [UnityTest]
        public IEnumerator TheHudPrintsTheKeyThePlayerActuallyHas()
        {
            // The reason this increment exists: the HUD's Q/E/R and its F were four strings in
            // a serialized field, and a player who had moved 스킬 1 would be told the wrong key.
            InputBindingDirector director = Director(NewSettings());

            _hudObject = new GameObject("HUD Model");
            _hudObject.SetActive(false);
            _hudObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var hud = _hudObject.AddComponent<NeonVoid.UI.HudController>();
            hud.Configure(null, bindingDirector: director);

            Assert.AreEqual("Q", hud.KeyFor("Skill1", "ignored"));
            Assert.AreEqual("F", hud.KeyFor("Interact", "ignored"));

            Assert.IsTrue(director.Rebind(IdOf(director, "Skill1"), "<Keyboard>/z").Ok);
            yield return null;

            Assert.AreEqual("Z", hud.KeyFor("Skill1", "ignored"));
        }

        [UnityTest]
        public IEnumerator AHudWithNoBindingsFallsBackRatherThanGoingBlank()
        {
            _hudObject = new GameObject("HUD Model");
            _hudObject.SetActive(false);
            _hudObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var hud = _hudObject.AddComponent<NeonVoid.UI.HudController>();
            hud.Configure(null);

            Assert.AreEqual("Q", hud.KeyFor("Skill1", "Q"));
            yield return null;
        }

        // --- a settings file that would break the game --------------------------------------

        [UnityTest]
        public IEnumerator SavedKeysThatConflictAreNotAppliedAndNothingIsOverwritten()
        {
            // It can only get this way by hand editing or by another build. Applying it would
            // give one key two meanings with no way to tell which fired.
            GlobalSettingsStore settings = NewSettings();
            InputBindingDirector probe = Director(settings);
            string skill1 = IdOf(probe, "Skill1");
            string skill2 = IdOf(probe, "Skill2");
            UnityEngine.Object.Destroy(_bindingObject);
            _asset.RemoveAllBindingOverrides();
            yield return null;

            GlobalSettingsStore clashing = NewSettings();
            clashing.Document.SetOverrides(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>(skill1, "<Keyboard>/j"),
                new System.Collections.Generic.KeyValuePair<string, string>(skill2, "<Keyboard>/j"),
            });
            Assert.IsTrue(clashing.Save(out string detail), detail);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("conflict"));
            InputBindingDirector director = Director(NewSettings());

            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"), "a conflicting file was applied");
            Assert.AreEqual("E", director.DisplayKeyFor("Skill2"));

            var reopened = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            reopened.Open();
            Assert.AreEqual(2, reopened.Document.Overrides().Count, "the file was overwritten on sight");
        }
    
        // --- the 설정 screen ----------------------------------------------------------------

        private NeonVoid.UI.HudController Hud(InputBindingDirector director)
        {
            _hudObject = new GameObject("HUD Model");
            _hudObject.SetActive(false);
            _hudObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            var hud = _hudObject.AddComponent<NeonVoid.UI.HudController>();
            hud.Configure(null, bindingDirector: director);
            return hud;
        }

        [UnityTest]
        public IEnumerator TheSettingsScreenOpensAndClosesAndStartsClosed()
        {
            InputBindingDirector director = Director(NewSettings());
            NeonVoid.UI.HudController hud = Hud(director);

            Assert.IsFalse(hud.SettingsOpen);

            hud.ToggleSettings();
            Assert.IsTrue(hud.SettingsOpen);

            hud.ToggleSettings();
            Assert.IsFalse(hud.SettingsOpen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AKeyPressedToRebindItDoesNotAlsoPlay()
        {
            // docs/05 section 7: 전투에서 UI 보기 전후. A player holding the screen open and
            // pressing Space to bind 대시 must not dash while they do it.
            var routerObject = new GameObject("Router");
            routerObject.SetActive(false);
            var router = routerObject.AddComponent<PlayerInputRouter>();

            // The router has no asset here and says so. What is under test is the suspension
            // flag, which is set from outside it.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("InputActionAsset"));
            routerObject.SetActive(true);

            try
            {
                InputBindingDirector director = Director(NewSettings());
                NeonVoid.UI.HudController hud = Hud(director);

                Assert.IsFalse(router.CombatInputSuspended);

                hud.OpenSettings();
                yield return null;
                Assert.IsTrue(router.CombatInputSuspended, "combat kept reading the keyboard");

                hud.CloseSettings();
                yield return null;
                Assert.IsFalse(router.CombatInputSuspended, "combat never came back");
            }
            finally
            {
                UnityEngine.Object.Destroy(routerObject);
            }
        }

        [UnityTest]
        public IEnumerator AKeyPressedWhileWaitingIsTakenAndReachesTheFile()
        {
            InputBindingDirector director = Director(NewSettings());
            NeonVoid.UI.HudController hud = Hud(director);
            hud.OpenSettings();

            string skill1 = IdOf(director, "Skill1");
            hud.BeginCapture(skill1);

            Assert.IsTrue(hud.Capture.IsListening);
            StringAssert.Contains("스킬 1", hud.Capture.Message);

            Assert.IsTrue(hud.OfferCapturedKey("<Keyboard>/z"), "the press was not consumed");
            yield return null;

            Assert.IsFalse(hud.Capture.IsListening);
            Assert.IsFalse(hud.Capture.IsFailure, hud.Capture.Message);
            Assert.AreEqual("Z", director.DisplayKeyFor("Skill1"));

            var reopened = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            reopened.Open();
            Assert.AreEqual(1, reopened.Document.Overrides().Count);
        }

        [UnityTest]
        public IEnumerator EscapeCancelsTheCaptureInsteadOfBindingEscape()
        {
            // The press that gets a player out of a prompt they opened by mistake.
            InputBindingDirector director = Director(NewSettings());
            NeonVoid.UI.HudController hud = Hud(director);
            hud.OpenSettings();
            hud.BeginCapture(IdOf(director, "Skill1"));

            Assert.IsTrue(hud.OfferCapturedKey("<Keyboard>/escape"));
            yield return null;

            Assert.IsFalse(hud.Capture.IsListening);
            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"), "Escape was bound to 스킬 1");
            Assert.IsTrue(hud.SettingsOpen, "cancelling a capture closed the whole screen");
        }

        [UnityTest]
        public IEnumerator AConflictIsRefusedOnScreenAndNamesTheHolder()
        {
            InputBindingDirector director = Director(NewSettings());
            NeonVoid.UI.HudController hud = Hud(director);
            hud.OpenSettings();
            hud.BeginCapture(IdOf(director, "Skill1"));

            Assert.IsTrue(hud.OfferCapturedKey("<Keyboard>/f"));
            yield return null;

            Assert.IsTrue(hud.Capture.IsFailure);
            StringAssert.Contains("상호작용", hud.Capture.Message);
            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"));
        }

        [UnityTest]
        public IEnumerator APressWhenNothingIsWaitingIsNotConsumed()
        {
            // Otherwise the screen would swallow every keystroke the moment it was open.
            InputBindingDirector director = Director(NewSettings());
            NeonVoid.UI.HudController hud = Hud(director);
            hud.OpenSettings();

            Assert.IsFalse(hud.OfferCapturedKey("<Keyboard>/z"));
            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThePointerAndTheCompositeHeaderRefuseToBeCaptured()
        {
            InputBindingDirector director = Director(NewSettings());
            NeonVoid.UI.HudController hud = Hud(director);
            hud.OpenSettings();

            foreach (KeyBindingSlot slot in director.Table.Slots)
            {
                if (slot.IsRebindable)
                    continue;

                hud.BeginCapture(slot.BindingId);
                Assert.IsFalse(hud.Capture.IsListening, slot.Label);
                Assert.IsTrue(hud.Capture.IsFailure, slot.Label);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ResettingFromTheScreenPutsEveryKeyBack()
        {
            InputBindingDirector director = Director(NewSettings());
            NeonVoid.UI.HudController hud = Hud(director);
            hud.OpenSettings();
            hud.BeginCapture(IdOf(director, "Skill1"));
            hud.OfferCapturedKey("<Keyboard>/z");
            yield return null;

            hud.ResetAllKeys();
            yield return null;

            Assert.AreEqual("Q", director.DisplayKeyFor("Skill1"));
            Assert.IsFalse(hud.Capture.IsFailure, hud.Capture.Message);

            var reopened = new GlobalSettingsStore(
                new DiskSaveFileSystem(), GlobalSettingsLayout.Under(_root));
            reopened.Open();
            Assert.IsEmpty(reopened.Document.Overrides());
        }
    }
}
