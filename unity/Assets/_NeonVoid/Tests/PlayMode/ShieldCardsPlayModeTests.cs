using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Stats;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The three cards that make 보호막: G05 주기 차폐막, G07 실행 차폐, R05 돌진 차폐.
    /// <para>
    /// The assertion is the shield standing on the player, and then health kept. D-004a
    /// settled which shield a hit eats first; these are about the cards putting shields there
    /// at all, and about two of them standing at once - which is the situation the decision
    /// was about.
    /// </para>
    /// </summary>
    public sealed class ShieldCardsPlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        private IEnumerator ArenaCarrying(params PluginDefinition[] cards)
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            for (int i = 0; i < cards.Length; ++i)
            {
                Assert.AreEqual(
                    PluginGrantVerdict.Granted,
                    _arena.Procs.Loadout.TryGrant(cards[i], out string why), why);
            }

            _arena.Procs.PushPassivesToOwner();
            yield return _arena.Tick(1);
        }

        private static SkillAsset PlainSkill(int id, bool movement) => PrototypeArena.SkillFrom(
            movement ? "skill.test_blink" : "skill.test_bolt",
            new SkillDefinition
            {
                SkillId = id,
                ManaCost = 0f,
                BaseCooldownTicks = 1,
                IsMovementSkill = movement,
                Damage = new DamageRequest { Flat = 1f, Type = DamageType.Magic },
            },
            new SkillShape { Kind = SkillEffectKind.ConeDamage, RadiusUnits = 4f, ConeHalfAngleDegrees = 45f },
            default);

        // --- G05 주기 차폐막 -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePeriodicBarrierArrivesOnItsOwnClock()
        {
            // [확정] docs/02 section 7: 30초 주기. The test drives the card's own constant, so
            // a retune moves one line and this still means what it says.
            yield return ArenaCarrying(PluginCatalog.PeriodicBarrier());

            Assert.IsFalse(_arena.Player.Shields.HasAny, "it shielded before its first period ran");

            yield return _arena.Tick(PluginCatalog.StatedPeriodicShieldTicks + 2);

            Assert.IsTrue(_arena.Player.Shields.HasAny, "30초 주기 came and went with no 보호막");
            Assert.AreEqual(
                PluginCatalog.DraftPeriodicShieldAmount, _arena.Player.Shields.Total, 0.01f);
        }

        [UnityTest]
        public IEnumerator DroppingTheCardStopsItsClock()
        {
            yield return ArenaCarrying(PluginCatalog.PeriodicBarrier());
            yield return _arena.Tick(PluginCatalog.StatedPeriodicShieldTicks / 2);

            Assert.Greater(_arena.Procs.TimerTicks("G05"), 0, "the clock never started");

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            yield return _arena.Tick(2);

            Assert.AreEqual(
                0, _arena.Procs.TimerTicks("G05"),
                "a dropped card kept its count, so re-granting it would shield almost at once");
        }

        // --- G07 실행 차폐 ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator CastingASkillRaisesAShield()
        {
            yield return ArenaCarrying(PluginCatalog.ExecutionBarrier());
            _arena.Caster.Equip(0, PlainSkill(2001, movement: false));

            Assert.IsFalse(_arena.Player.Shields.HasAny);

            SkillCastResult cast = _arena.Caster.CastSlot(0, new Vector2(10f, 0f));
            Assert.IsTrue(cast.Succeeded, cast.Rejection.ToString());
            yield return _arena.Tick(3);

            Assert.AreEqual(
                PluginCatalog.DraftSkillShieldAmount, _arena.Player.Shields.Total, 0.01f,
                "스킬 사용 시 보호막 생성 - the cast went off and no shield appeared");
        }

        [UnityTest]
        public IEnumerator ASkillThatIsRefusedRaisesNothing()
        {
            // 스킬 실패 시 발동하지 않음. An empty slot is the simplest refusal there is.
            yield return ArenaCarrying(PluginCatalog.ExecutionBarrier());

            SkillCastResult cast = _arena.Caster.CastSlot(2, new Vector2(10f, 0f));
            Assert.IsFalse(cast.Succeeded, "this test needs a cast that fails");
            yield return _arena.Tick(3);

            Assert.IsFalse(_arena.Player.Shields.HasAny, "a failed cast still raised a shield");
        }

        // --- R05 돌진 차폐 ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ABlinkRaisesAShieldAndAnOrdinarySkillDoesNot()
        {
            // The 분류 태그 is IsMovementSkill, the same tag G08 이동 버퍼 uses - not the name.
            yield return ArenaCarrying(PluginCatalog.DashBarrier());
            _arena.Caster.Equip(0, PlainSkill(2002, movement: false));
            _arena.Caster.Equip(1, PlainSkill(2003, movement: true));

            Assert.IsTrue(_arena.Caster.CastSlot(0, new Vector2(10f, 0f)).Succeeded);
            yield return _arena.Tick(3);
            Assert.IsFalse(_arena.Player.Shields.HasAny, "an ordinary skill raised 돌진 차폐");

            Assert.IsTrue(_arena.Caster.CastSlot(1, new Vector2(10f, 0f)).Succeeded);
            yield return _arena.Tick(3);

            Assert.AreEqual(
                PluginCatalog.DraftDashShieldAmount, _arena.Player.Shields.Total, 0.01f,
                "돌진 또는 점멸 뒤 보호막 생성 - the blink raised nothing");
        }

        // --- two shields at once, which is what D-004 was about ---------------------------------

        [UnityTest]
        public IEnumerator TwoCardsPutTwoShieldsUpAndTheNewerOneIsEatenFirst()
        {
            // [확정] docs/08 D-004a, approved 2026-11-22: 신규부터. This is the approved rule
            // acting on shields two real cards put there, rather than on a stack built by hand.
            yield return ArenaCarrying(
                PluginCatalog.ExecutionBarrier(), PluginCatalog.DashBarrier());

            _arena.Caster.Equip(0, PlainSkill(2004, movement: false));
            _arena.Caster.Equip(1, PlainSkill(2005, movement: true));

            // G07 first, so it is the older of the two.
            Assert.IsTrue(_arena.Caster.CastSlot(0, new Vector2(10f, 0f)).Succeeded);
            yield return _arena.Tick(5);

            // R05 second. A movement skill also raises G07, so this leaves three layers: the
            // older G07, a newer G07, and R05 - and R05 is the newest of them.
            Assert.IsTrue(_arena.Caster.CastSlot(1, new Vector2(10f, 0f)).Succeeded);
            yield return _arena.Tick(5);

            Assert.GreaterOrEqual(
                _arena.Player.Shields.LayerCount, 2, "two cards raised one shield between them");

            // The layer from the first cast is the oldest; both of the second cast's layers
            // share a tick. Which of THOSE two is eaten is the grant-order tiebreak, covered
            // in ShieldStackTests - what is asserted here is the approved rule itself: a hit
            // goes to a newer layer and leaves the older one alone.
            long oldestTick = _arena.Player.Shields.LayerAt(0).GrantedAtTick;
            float oldestBefore = _arena.Player.Shields.LayerAt(0).Remaining;
            float before = _arena.Player.Shields.Total;
            float full = _arena.Player.Health.Current;

            var blow = new DamageRequest { Flat = 10f, Type = DamageType.True };
            _arena.Player.ReceiveDamage(blow, CombatStats.StartingVaccine());

            Assert.AreEqual(full, _arena.Player.Health.Current, 0.001f, "the shields let it through");
            Assert.AreEqual(before - 10f, _arena.Player.Shields.Total, 0.01f);

            ShieldLayer oldest = _arena.Player.Shields.LayerAt(0);
            Assert.AreEqual(oldestTick, oldest.GrantedAtTick, "the oldest layer was the one consumed");
            Assert.AreEqual(
                oldestBefore, oldest.Remaining, 0.01f,
                "신규부터 - the first cast's shield should not have paid while newer ones stood");
        }
    }
}
