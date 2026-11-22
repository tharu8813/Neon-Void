using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// L01 레이저 코어 and L02 부메랑 프로토콜 fired in a real scene.
    /// <para>
    /// What each card does alone is stated by docs/03. Their combination is
    /// <b>[확정] D-005 (2026-11-23)</b>: 둘 다 발사, 추가탄도 변환, 관통 상한 준수 - asserted
    /// here as behaviour in a real scene rather than as constants, which EditMode already holds.
    /// </para>
    /// </summary>
    public sealed class ShotConversionPlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        private static int LiveProjectiles() =>
            Object.FindObjectsByType<CombatProjectile>(FindObjectsSortMode.None).Length;

        private static int LiveBoomerangs() =>
            Object.FindObjectsByType<BoomerangProjectile>(FindObjectsSortMode.None).Length;

        /// <summary>
        /// The prototype set carries R06 and the director grants it on its own first tick, so a
        /// test that cleared before that tick would simply be overwritten.
        /// </summary>
        private IEnumerator WithOnly(PluginDefinition card)
        {
            yield return _arena.Tick(1);
            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();

            Assert.AreEqual(
                PluginGrantVerdict.Granted, _arena.Procs.Grant(card, out string reason), reason);
            _arena.PlayerObject.transform.forward = Vector3.forward;
        }

        /// <summary>
        /// Enough health that repeated hits cannot end the test. A dummy that died halfway
        /// through a boomerang's round trip would make the return leg untestable.
        /// </summary>
        private static void MakeTough(CombatActor enemy)
        {
            CombatStats tough = PrototypeArena.EnemyStats();
            tough.MaxHealth = 100_000f;
            enemy.Configure(PrototypeArena.EnemyTeam, tough);
        }

        // --- L01 레이저 코어 -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheLaserDamagesInTheSameTickItFires()
        {
            // This is what makes it a 레이저 rather than a very fast 탄환: an ordinary shot needs
            // about twenty ticks to cross six units at the drafted speed, so damage on the tick
            // of the shot can only be hitscan.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            yield return WithOnly(PluginCatalog.LaserCore());

            CombatActor enemy = _arena.Enemies[0];
            MakeTough(enemy);
            float before = enemy.Health.Current;

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            Assert.Less(enemy.Health.Current, before, "the beam did no damage at all");
            Assert.AreEqual(0, LiveProjectiles(), "it fired a 탄환 as well as the beam");
        }

        [UnityTest]
        public IEnumerator TheLaserReplacesTheProjectileRatherThanAddingToIt()
        {
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            yield return WithOnly(PluginCatalog.LaserCore());

            Assert.AreEqual(1, _arena.Attacker.MainShots.Count);
            Assert.AreEqual(MainShotMode.Laser, _arena.Attacker.MainShots[0].Mode);

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(3);

            Assert.AreEqual(0, LiveProjectiles(), "탄환을 레이저로 변환 left the 탄환 behind");
        }

        [UnityTest]
        public IEnumerator AWallStopsTheBeam()
        {
            // A laser passes through no more wall than a 탄환 does. Without this the card would
            // be a weapon that shoots through the level geometry.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            yield return WithOnly(PluginCatalog.LaserCore());

            _arena.Wall(new Vector3(0f, 1f, 3f), new Vector3(6f, 3f, 0.5f));

            CombatActor enemy = _arena.Enemies[0];
            MakeTough(enemy);
            float before = enemy.Health.Current;

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(3);

            Assert.AreEqual(before, enemy.Health.Current, 0.001f, "the beam went through the wall");
        }

        [UnityTest]
        public IEnumerator TheBeamRespectsThePierceBudget()
        {
            // [확정] D-005: the beam respects 관통 exactly as a 탄환 does, so with no 관통 it
            // damages the first enemy on the line and stops.
            _arena = PrototypeArena.Build(
                new Vector3(0f, 0.7f, 4f), new Vector3(0f, 0.7f, 7f));
            yield return WithOnly(PluginCatalog.LaserCore());

            CombatActor near = _arena.Enemies[0];
            CombatActor far = _arena.Enemies[1];
            MakeTough(near);
            MakeTough(far);
            float nearBefore = near.Health.Current;
            float farBefore = far.Health.Current;

            Assert.AreEqual(1, _arena.Attacker.EffectiveMaxTargets, "the dummy arena has no 관통");

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            Assert.Less(near.Health.Current, nearBefore, "the nearest enemy was not hit");
            Assert.AreEqual(
                farBefore, far.Health.Current, 0.001f,
                "the beam went through the 관통 budget D-005 approved");
        }

        [UnityTest]
        public IEnumerator AConvertedShotStillTriggersTheBasicAttackCards()
        {
            // L01 changes what the basic attack looks like, not what triggers off it. G10 전위
            // 연쇄 and G17 폭발 각인 are written against 탄환 적중, and a card that silently
            // stopped triggering them would be a nerf nobody wrote down. G12 부식 표식 is the
            // cheapest witness: it marks whatever the attack hits.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            yield return WithOnly(PluginCatalog.LaserCore());

            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Grant(PluginCatalog.CorrosionMark(), out string reason), reason);

            CombatActor enemy = _arena.Enemies[0];
            MakeTough(enemy);

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(2);

            Assert.IsTrue(
                enemy.Status.Has(NeonVoid.Combat.Status.StatusEffectId.FirewallBreach),
                "the beam landed and no 적중 effect fired");
        }

        // --- L02 부메랑 프로토콜 ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheBoomerangIsThrownInsteadOfAProjectile()
        {
            _arena = PrototypeArena.Build();
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            Assert.AreEqual(1, _arena.Attacker.MainShots.Count);
            Assert.AreEqual(MainShotMode.Boomerang, _arena.Attacker.MainShots[0].Mode);

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            Assert.AreEqual(1, LiveBoomerangs(), "no 부메랑 left the weapon");
            Assert.AreEqual(0, LiveProjectiles(), "it threw a 부메랑 and fired a 탄환 too");
            Assert.AreEqual(1, _arena.Attacker.BoomerangsInFlight);
        }

        [UnityTest]
        public IEnumerator NoFurtherBasicAttackWhileItIsOut()
        {
            // [확정] docs/03 L02: 귀환 전 추가 기본공격 불가. The attack interval is 30 ticks at
            // the drafted rate, so by tick 40 an unheld weapon would have fired again.
            _arena = PrototypeArena.Build();
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);
            Assert.AreEqual(1, _arena.Attacker.BoomerangsInFlight);

            yield return _arena.Tick(39);

            Assert.IsTrue(_arena.Attacker.WaitingForBoomerang, "it came back far too early");
            Assert.AreEqual(
                1, LiveBoomerangs(), "a second 부메랑 was thrown before the first returned");
            Assert.AreEqual(0, LiveProjectiles(), "it fell back to firing 탄환 while holding");
        }

        [UnityTest]
        public IEnumerator ItDamagesOnTheWayOutAndAgainOnTheWayBack()
        {
            // 범위 피해 + 귀환. A return leg that passed harmlessly through everything it had
            // already caught would be a boomerang that comes back for decoration, and the
            // ledger is cleared at the turn precisely so it does not.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 3f));
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            CombatActor enemy = _arena.Enemies[0];
            MakeTough(enemy);
            float full = enemy.Health.Current;

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            // Only one throw: the attacker is switched off so a second volley cannot be what
            // lands the second hit.
            _arena.Attacker.enabled = false;
            yield return _arena.Tick(20);

            float afterOutbound = enemy.Health.Current;
            Assert.Less(afterOutbound, full, "the outbound leg hit nobody");

            yield return _arena.Tick(60);

            Assert.Less(enemy.Health.Current, afterOutbound, "the 귀환 leg did no damage");
        }

        [UnityTest]
        public IEnumerator ItCatchesEverythingWithinItsReach()
        {
            // 범위 피해: an area, not a single target. Two dummies standing together are both
            // caught by one 부메랑.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 3f));
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            CombatActor first = _arena.Enemies[0];
            CombatActor second = _arena.SpawnEnemyNear(first, "Beside", 1.1f);
            MakeTough(first);
            MakeTough(second);
            float firstFull = first.Health.Current;
            float secondFull = second.Health.Current;

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);
            _arena.Attacker.enabled = false;
            yield return _arena.Tick(25);

            Assert.Less(first.Health.Current, firstFull);
            Assert.Less(second.Health.Current, secondFull, "the 범위 피해 only hit one of them");
        }

        [UnityTest]
        public IEnumerator TheHoldIsLiftedWhenItReturns()
        {
            _arena = PrototypeArena.Build();
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);
            _arena.Attacker.enabled = false;

            yield return _arena.Tick(180);

            Assert.AreEqual(0, _arena.Attacker.BoomerangsInFlight, "it never came home");
            Assert.IsFalse(_arena.Attacker.WaitingForBoomerang);
            Assert.AreEqual(0, LiveBoomerangs(), "it is still in the scene");
        }

        [UnityTest]
        public IEnumerator FiringResumesAfterTheReturn()
        {
            // The hold is a pause, not an end: a card that stopped the basic attack for good
            // would read in play exactly like the weapon breaking.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 3f));
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            CombatActor enemy = _arena.Enemies[0];
            MakeTough(enemy);

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(100);
            float afterOneTrip = enemy.Health.Current;

            yield return _arena.Tick(120);

            Assert.Less(
                enemy.Health.Current, afterOneTrip,
                "nothing was thrown after the first 부메랑 came back");
        }

        [UnityTest]
        public IEnumerator ABoomerangDestroyedInFlightStillLiftsTheHold()
        {
            // The hold is a count on the attacker, so a 부메랑 that disappeared without
            // releasing - teardown, a despawn, anything - would stop the player attacking for
            // the rest of the mission. That is a deadlock, not a bug in a number.
            _arena = PrototypeArena.Build();
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);
            Assert.AreEqual(1, _arena.Attacker.BoomerangsInFlight);

            _arena.Attacker.enabled = false;
            foreach (BoomerangProjectile disc in
                     Object.FindObjectsByType<BoomerangProjectile>(FindObjectsSortMode.None))
            {
                Object.Destroy(disc.gameObject);
            }

            yield return _arena.Tick(2);

            Assert.AreEqual(
                0, _arena.Attacker.BoomerangsInFlight,
                "the attack is held for a 귀환 that can no longer happen");
        }

        [UnityTest]
        public IEnumerator ADeadThrowerDropsItRatherThanLeavingItFlying()
        {
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 3f));
            yield return WithOnly(PluginCatalog.BoomerangProtocol());

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);
            Assert.AreEqual(1, LiveBoomerangs());

            _arena.Attacker.enabled = false;
            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(0, LiveBoomerangs(), "it is still flying for a thrower who is gone");
        }

        // --- the two together: [미확정] D-005 -------------------------------------------------

        [UnityTest]
        public IEnumerator CarryingBothFiresBothForms()
        {
            // [확정] D-005: 둘 다 발사.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            yield return WithOnly(PluginCatalog.LaserCore());

            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Grant(PluginCatalog.BoomerangProtocol(), out string reason), reason);
            Assert.AreEqual(
                2, _arena.Attacker.MainShots.Count, ShotConversionPolicy.ApprovedSummary);

            CombatActor enemy = _arena.Enemies[0];
            MakeTough(enemy);
            float before = enemy.Health.Current;

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            // The beam is hitscan, so its damage has already landed on the throw tick while the
            // 부메랑 is still on its way out. Both forms left the weapon in the same volley.
            Assert.Less(enemy.Health.Current, before, "the 레이저 half of the pair did nothing");
            Assert.AreEqual(1, LiveBoomerangs(), "the 부메랑 half of the pair did nothing");
            Assert.AreEqual(
                0, LiveProjectiles(), "two conversions are carried and it still fired a 탄환");
        }

        [UnityTest]
        public IEnumerator TheBoomerangStillHoldsTheAttackWhenBothAreCarried()
        {
            // 둘 다 발사 means the 부메랑's cost applies to the pair: the beam does not get a
            // free volley while the disc is out, or the hold would be worth nothing on the one
            // build that can afford both cards.
            _arena = PrototypeArena.Build();
            yield return WithOnly(PluginCatalog.LaserCore());
            _arena.Procs.Grant(PluginCatalog.BoomerangProtocol(), out _);

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);
            yield return _arena.Tick(39);

            Assert.IsTrue(_arena.Attacker.WaitingForBoomerang);
            Assert.AreEqual(1, LiveBoomerangs(), "a second 부메랑 was thrown while one was out");
        }

        [UnityTest]
        public IEnumerator TheExtraShotIsConvertedToo()
        {
            // [확정] D-005: L01 converts R06's extra 탄환 as well, so a volley leaves no 탄환
            // in the scene at all.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            yield return WithOnly(PluginCatalog.LaserCore());

            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Grant(PluginCatalog.SupportRound(), out string reason), reason);
            Assert.AreEqual(1, _arena.Attacker.ExtraShotsPerVolley, reason);

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            Assert.AreEqual(
                0, LiveProjectiles(),
                "R06's extra 탄환 stayed a 탄환: " + ShotConversionPolicy.ApprovedSummary);
        }

        // --- the combination docs/03 names as a verification point -----------------------------

        [UnityTest]
        public IEnumerator EverythingThatChangesTheShotAtOnceStaysFinite()
        {
            // docs/03 section 4: L01 + L02 + R06 + G03/G04/G10 동시 보유에도 CPU 정지·무한
            // VFX·효과 강제 삭제 없음. Now that D-005 says both 전설 forms fire, this stack is
            // reachable in play, so it is run rather than reasoned about.
            _arena = PrototypeArena.Build(
                new Vector3(0f, 0.7f, 5f), new Vector3(1.4f, 0.7f, 6f),
                new Vector3(-1.4f, 0.7f, 6f));
            yield return WithOnly(PluginCatalog.LaserCore());

            foreach (PluginDefinition card in new[]
                     {
                         PluginCatalog.BoomerangProtocol(),
                         PluginCatalog.SupportRound(),
                         PluginCatalog.ExpandedWarhead(),
                         PluginCatalog.WeaknessAnalysis(),
                         PluginCatalog.ArcChain(),
                     })
            {
                Assert.AreEqual(
                    PluginGrantVerdict.Granted, _arena.Procs.Grant(card, out string why),
                    card.ProposalId + ": " + why);
            }

            for (int i = 0; i < _arena.Enemies.Count; ++i)
                MakeTough(_arena.Enemies[i]);

            long before = _arena.Runner.TickIndex;
            _arena.Attacker.enabled = true;
            yield return _arena.Tick(600);

            // The simulation is still advancing: a stall is the failure docs/03 names first.
            // A floor rather than an exact count, because this test is about the simulation
            // still running, not about how many ticks a frame is worth.
            Assert.GreaterOrEqual(
                _arena.Runner.TickIndex, before + 500,
                "the simulation stalled under the full shot stack");

            // And it has not filled the scene: the 부메랑 hold bounds the volleys, and every
            // beam is gone within a few ticks of being fired.
            Assert.LessOrEqual(
                LiveBoomerangs(), 1, "부메랑s accumulated despite 귀환 전 추가 기본공격 불가");
            Assert.LessOrEqual(
                Object.FindObjectsByType<LaserBeam>(FindObjectsSortMode.None).Length, 4,
                "beam visuals are piling up instead of expiring");

            // Something was actually happening for those ten seconds, so this is not a test
            // that passes because nothing fired.
            bool anyHurt = false;
            for (int i = 0; i < _arena.Enemies.Count; ++i)
                anyHurt |= _arena.Enemies[i].Health.Current < _arena.Enemies[i].Health.Max;
            Assert.IsTrue(anyHurt, "the full stack ran for 600 ticks and damaged nobody");
        }
    }
}
