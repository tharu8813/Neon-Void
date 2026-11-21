#if UNITY_EDITOR
using System.Collections;
using NeonVoid.Combat;
using NeonVoid.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The HUD drawing the real panel from the real simulation.
    /// <para>
    /// HudSnapshotTests covers what the numbers mean; this covers the half that type cannot:
    /// that the values are actually written onto the elements of NeonVoidHud.uxml, on the
    /// simulation tick, without anybody pressing anything.
    /// </para>
    /// <para>
    /// Editor-only because it loads the project's own UXML and PanelSettings through
    /// AssetDatabase. Building the panel in code instead would test a panel this game does not
    /// ship - and the thing most likely to break is a renamed element in the UXML, which only
    /// the real file can catch.
    /// </para>
    /// </summary>
    public sealed class HudPlayModeTests
    {
        private const string UxmlPath = "Assets/_NeonVoid/Runtime/UI/NeonVoidHud.uxml";
        private const string PanelPath = "Assets/_NeonVoid/UI/NeonVoidPanelSettings.asset";

        private PrototypeArena _arena;
        private GameObject _hudObject;
        private HudController _hud;

        [TearDown]
        public void TearDown()
        {
            if (_hudObject != null)
                Object.Destroy(_hudObject);
            _hudObject = null;
            _hud = null;

            _arena?.Dispose();
            _arena = null;
        }

        private IEnumerator ArenaWithTheHud()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            Assert.IsNotNull(uxml, $"the HUD document is missing at {UxmlPath}");
            Assert.IsNotNull(panel, $"the HUD panel settings are missing at {PanelPath}");

            _hudObject = new GameObject("HUD");
            _hudObject.SetActive(false);

            UIDocument document = _hudObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = uxml;

            _hud = _hudObject.AddComponent<HudController>();
            _hud.Configure(_arena.Player);
            _hudObject.SetActive(true);

            yield return _arena.Tick(2);
        }

        private VisualElement Root => _hudObject.GetComponent<UIDocument>().rootVisualElement;

        private T Element<T>(string name) where T : VisualElement
        {
            var found = Root.Q<T>(name);
            Assert.IsNotNull(found, $"the HUD has no {typeof(T).Name} named '{name}'");
            return found;
        }

        [UnityTest]
        public IEnumerator TheHealthBarShowsTheVaccinesOwnHealth()
        {
            yield return ArenaWithTheHud();

            ProgressBar health = Element<ProgressBar>("healthBar");
            Assert.AreEqual(1f, health.value, 0.001f, "a full vaccine did not read as full");

            _arena.Player.ReceiveDamage(
                new DamageRequest { Flat = _arena.Player.Health.Max * 0.4f, Type = DamageType.True },
                CombatStats.StartingVaccine());
            yield return _arena.Tick(2);

            Assert.AreEqual(0.6f, health.value, 0.01f, "the bar did not follow the health down");
            Assert.AreEqual(
                HudSnapshot.Pool(_arena.Player.Health.Current, _arena.Player.Health.Max), health.title,
                "the bar's own label disagrees with the health it is drawing");
        }

        [UnityTest]
        public IEnumerator TheShieldPipAppearsOnlyWhileThereIsAShield()
        {
            yield return ArenaWithTheHud();

            VisualElement row = Element<VisualElement>("shieldRow");
            Assert.IsTrue(row.ClassListContains("hidden"), "an empty 보호막 was on screen");

            _arena.Player.Shields.Grant("test", 40f, 0L, 0);
            yield return _arena.Tick(2);

            Assert.IsFalse(row.ClassListContains("hidden"), "a 보호막 was raised and nothing showed");
            Assert.AreEqual("40", Element<Label>("shieldValue").text);

            _arena.Player.Shields.Clear();
            yield return _arena.Tick(2);

            Assert.IsTrue(row.ClassListContains("hidden"), "the pip outlived the 보호막");
        }

        [UnityTest]
        public IEnumerator TheAreaPanelIsHiddenOutsideAnArea()
        {
            // The arena is not a hardware area, so everything about one must stay off screen -
            // a panel reading "0 / 0" in the hub would be worse than no panel.
            yield return ArenaWithTheHud();

            Assert.IsTrue(Element<VisualElement>("areaPanel").ClassListContains("hidden"));
            Assert.IsTrue(Element<VisualElement>("bossPanel").ClassListContains("hidden"));
            Assert.IsFalse(_hud.Last.InArea);
        }

        [UnityTest]
        public IEnumerator TheHudRedrawsOnTheTickWithNobodyAskingIt()
        {
            // It is a tick receiver, not something a caller remembers to refresh. A HUD that
            // only updated when something happened to call it would be right in every test
            // that called it and stale in the game.
            yield return ArenaWithTheHud();

            float before = _hud.Last.Health;
            Assert.Greater(before, 0f, "the snapshot was never taken at all");

            _arena.Player.ReceiveDamage(
                new DamageRequest { Flat = 10f, Type = DamageType.True },
                CombatStats.StartingVaccine());

            // Nothing in this test calls Redraw. Only the ticks do.
            yield return _arena.Tick(2);

            Assert.Less(_hud.Last.Health, before, "the snapshot never refreshed on a tick");
            Assert.AreEqual(
                _hud.Last.HealthFraction, Element<ProgressBar>("healthBar").value, 0.001f,
                "the snapshot refreshed and the panel did not");
        }

        [UnityTest]
        public IEnumerator EverySkillSlotTheDocumentPromisesIsThere()
        {
            // [확정] docs/03 section 6: 3슬롯. A renamed or missing element in the UXML is the
            // most likely way this HUD breaks, and nothing else would catch it.
            yield return ArenaWithTheHud();

            for (int i = 0; i < HudSnapshot.SkillSlots; ++i)
            {
                Assert.IsNotNull(Root.Q<VisualElement>($"skillSlot{i}"), $"slot {i} is missing");
                Assert.IsNotNull(Root.Q<ProgressBar>($"skillBar{i}"), $"slot {i} has no bar");
                Assert.IsTrue(
                    Root.Q<VisualElement>($"skillSlot{i}").ClassListContains("skill--empty"),
                    $"slot {i} holds no skill and did not read as empty");
            }
        }
    }
}
#endif
