using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.Stats;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// G11 무피격 가속: 이동속도 100% 증가, 피해를 받으면 비활성화.
    /// <para>
    /// The assertion that matters is the movement multiplier, not the gate's own bookkeeping.
    /// A condition that was evaluated correctly and never reached the stat snapshot would be a
    /// card that is permanently off, and every pure test of the gate would still pass - which
    /// is the shape this project has shipped before.
    /// </para>
    /// </summary>
    public sealed class UntouchedAccelerationPlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        /// <summary>Only G11 carried, so nothing else can touch the movement multiplier.</summary>
        private IEnumerator ArenaWithOnlyUntouchedAcceleration()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Loadout.TryGrant(PluginCatalog.UntouchedAcceleration(), out string why), why);
            _arena.Procs.PushPassivesToOwner();
            yield return _arena.Tick(1);
        }

        /// <summary>A hit the player survives, through the normal damage entry point.</summary>
        private void Graze()
        {
            var scratch = new DamageRequest { Flat = 1f, Type = DamageType.True };
            _arena.Player.ReceiveDamage(scratch, CombatStats.StartingVaccine());
        }

        [UnityTest]
        public IEnumerator AnUnhurtVaccineMovesAtTheCardsOwnNumber()
        {
            // [원안 수치] docs/03 G11: 이동속도 100% 증가. And it is live from the start: a
            // carrier who has never been hurt has gone an unbounded time unhurt.
            yield return ArenaWithOnlyUntouchedAcceleration();

            Assert.AreEqual(
                1f + PluginCatalog.StatedUntouchedMoveSpeedFraction,
                _arena.Player.MoveSpeedMultiplier, 0.001f,
                "무피격 가속 was carried and the player moved at the same speed");
        }

        [UnityTest]
        public IEnumerator TakingDamageTurnsItOffOnTheNextTick()
        {
            yield return ArenaWithOnlyUntouchedAcceleration();
            float fast = _arena.Player.MoveSpeedMultiplier;

            Graze();
            yield return _arena.Tick(1);

            Assert.AreEqual(
                1f, _arena.Player.MoveSpeedMultiplier, 0.001f,
                "피해를 받으면 비활성화 - the player kept the speed after being hit");
            Assert.Less(_arena.Player.MoveSpeedMultiplier, fast);
        }

        [UnityTest]
        public IEnumerator ItComesBackOnceTheVaccineHasBeenLeftAlone()
        {
            // The recovery time is [초안] - docs/03 leaves 재활성화 시점 미확정 - so this asserts
            // the card's own number rather than a number of its own.
            yield return ArenaWithOnlyUntouchedAcceleration();

            Graze();
            yield return _arena.Tick(1);
            Assert.AreEqual(1f, _arena.Player.MoveSpeedMultiplier, 0.001f);

            // One tick short of the recovery, counted from the tick the damage landed.
            yield return _arena.Tick(PluginCatalog.DraftUntouchedRecoveryTicks - 2);
            Assert.AreEqual(
                1f, _arena.Player.MoveSpeedMultiplier, 0.001f,
                "it came back before the recovery had run");

            yield return _arena.Tick(2);
            Assert.AreEqual(
                1f + PluginCatalog.StatedUntouchedMoveSpeedFraction,
                _arena.Player.MoveSpeedMultiplier, 0.001f,
                "it never came back");
        }

        [UnityTest]
        public IEnumerator BeingHitAgainStartsTheWaitOver()
        {
            // The reading that matches 피해를 받으면 비활성화: being hurt while waiting is being
            // hurt, not a second strike against a timer that keeps running.
            yield return ArenaWithOnlyUntouchedAcceleration();

            Graze();
            yield return _arena.Tick(PluginCatalog.DraftUntouchedRecoveryTicks - 10);

            Graze();
            yield return _arena.Tick(10);

            Assert.AreEqual(
                1f, _arena.Player.MoveSpeedMultiplier, 0.001f,
                "the second hit did not restart the wait");

            yield return _arena.Tick(PluginCatalog.DraftUntouchedRecoveryTicks);
            Assert.AreEqual(
                1f + PluginCatalog.StatedUntouchedMoveSpeedFraction,
                _arena.Player.MoveSpeedMultiplier, 0.001f);
        }

        [UnityTest]
        public IEnumerator DroppingTheCardDropsTheSpeedEvenWhileTheGateIsOpen()
        {
            // The snapshot is recomputed from base, so a card that is no longer carried must
            // leave no residue - including one whose gate happened to be open when it went.
            yield return ArenaWithOnlyUntouchedAcceleration();
            Assert.Greater(_arena.Player.MoveSpeedMultiplier, 1f);

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            yield return _arena.Tick(1);

            Assert.AreEqual(
                1f, _arena.Player.MoveSpeedMultiplier, 0.001f,
                "the speed outlived the card that granted it");
        }
    }
}
