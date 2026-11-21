using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// L03 피버타임 in a running scene: the gauge fills from real hits, the buff reaches the
    /// stat snapshot, and it ends.
    /// <para>
    /// The assertions are on the player's own stats, not on the gauge's bookkeeping. A window
    /// that opened and closed correctly while contributing nothing would pass every pure test
    /// and be a legendary that does nothing - which is the shape this project has shipped
    /// before.
    /// </para>
    /// </summary>
    public sealed class FeverTimePlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        /// <summary>Only L03 carried, so nothing else can move a stat.</summary>
        private IEnumerator ArenaWithOnlyFeverTime()
        {
            _arena = PrototypeArena.Build(new Vector3(3f, 0.7f, 0f));
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Loadout.TryGrant(PluginCatalog.FeverTime(), out string why), why);
            _arena.Procs.PushPassivesToOwner();
            yield return _arena.Tick(1);
        }

        /// <summary>Lands one ordinary hit on the enemy, through the real pipeline.</summary>
        private IEnumerator LandAHit(CombatActor enemy)
        {
            _arena.FireAt(enemy, adRatio: 0.2f);
            yield return _arena.Tick(40);
        }

        [UnityTest]
        public IEnumerator TheGaugeFillsFromRealHitsAndNothingElse()
        {
            yield return ArenaWithOnlyFeverTime();
            CombatActor enemy = _arena.Enemies[0];

            Assert.AreEqual(0f, _arena.Procs.FeverCharge("L03"), 0.0001f, "it began charged");

            yield return _arena.Tick(120);
            Assert.AreEqual(
                0f, _arena.Procs.FeverCharge("L03"), 0.0001f,
                "the gauge filled while nothing was happening");

            yield return LandAHit(enemy);
            Assert.Greater(_arena.Procs.FeverCharge("L03"), 0f, "a landed hit charged nothing");
        }

        [UnityTest]
        public IEnumerator AFullGaugeRaisesEveryStatAndThenGivesThemBack()
        {
            yield return ArenaWithOnlyFeverTime();
            CombatActor enemy = _arena.Enemies[0];

            float plainAd = _arena.Player.Stats.AttackDamage;
            float plainAp = _arena.Player.Stats.AbilityPower;
            float plainSpeed = _arena.Player.MoveSpeedMultiplier;

            // Enough landed hits to fill it, whatever the [초안] requirement is.
            int hits = Mathf.CeilToInt(
                PluginCatalog.DraftFeverChargeRequired / PluginCatalog.DraftFeverChargePerHit);

            for (int i = 0; i < hits && _arena.Procs.FeverTicksLeft("L03") == 0; ++i)
            {
                enemy.Configure(PrototypeArena.EnemyTeam, PrototypeArena.EnemyStats());
                yield return LandAHit(enemy);
            }

            Assert.Greater(_arena.Procs.FeverTicksLeft("L03"), 0, "the gauge never set 피버타임 off");
            Assert.AreEqual(
                0f, _arena.Procs.FeverCharge("L03"), 0.0001f,
                "the gauge kept its charge, so a second 피버타임 is already banked");

            Assert.AreEqual(
                plainAd + PluginCatalog.DraftFeverAttackDamage, _arena.Player.Stats.AttackDamage, 0.01f,
                "피버타임 is running and AD did not move");
            Assert.AreEqual(
                plainAp + PluginCatalog.DraftFeverAbilityPower, _arena.Player.Stats.AbilityPower, 0.01f);
            Assert.Greater(_arena.Player.MoveSpeedMultiplier, plainSpeed, "it did not move the player");

            // And it ends.
            yield return _arena.Tick(PluginCatalog.DraftFeverActiveTicks + 2);

            Assert.AreEqual(0, _arena.Procs.FeverTicksLeft("L03"), "피버타임 never ended");
            Assert.AreEqual(
                plainAd, _arena.Player.Stats.AttackDamage, 0.01f,
                "the buff outlived its own window");
            Assert.AreEqual(plainSpeed, _arena.Player.MoveSpeedMultiplier, 0.001f);
        }

        [UnityTest]
        public IEnumerator DroppingTheCardDropsTheBuffEvenMidWindow()
        {
            // The snapshot is recomputed from base, so a card that is gone must leave nothing
            // behind - including one whose window happened to be open.
            yield return ArenaWithOnlyFeverTime();
            CombatActor enemy = _arena.Enemies[0];
            float plainAd = _arena.Player.Stats.AttackDamage;

            int hits = Mathf.CeilToInt(
                PluginCatalog.DraftFeverChargeRequired / PluginCatalog.DraftFeverChargePerHit);

            for (int i = 0; i < hits && _arena.Procs.FeverTicksLeft("L03") == 0; ++i)
            {
                enemy.Configure(PrototypeArena.EnemyTeam, PrototypeArena.EnemyStats());
                yield return LandAHit(enemy);
            }

            Assert.Greater(_arena.Procs.FeverTicksLeft("L03"), 0, "the gauge never set 피버타임 off");
            Assert.Greater(_arena.Player.Stats.AttackDamage, plainAd);

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            yield return _arena.Tick(1);

            Assert.AreEqual(
                plainAd, _arena.Player.Stats.AttackDamage, 0.01f,
                "the buff outlived the card that granted it");
        }
    }
}
