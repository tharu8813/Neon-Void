using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// G09 비상 복구 - 체력 임계 아래에서 최대 체력 50% 회복 또는 방어 효과 + 이동속도, CD 60초.
    /// <para>
    /// <b>Which of the two it gives is [미확정]</b> (docs/03's own column: 회복/실드 여부 먼저
    /// 확인 필요), so these tests hold what is decided - when it fires, how much it is worth,
    /// that it carries 이동속도, and the cooldown - and read the reading from
    /// <see cref="EmergencyRecoveryPolicy"/> rather than asserting one.
    /// </para>
    /// </summary>
    public sealed class EmergencyRecoveryPlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        /// <summary>Only G09 carried, so nothing else can heal or shield the player.</summary>
        private IEnumerator ArenaWithOnlyEmergencyRecovery()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            _arena.Procs.Grant(PluginCatalog.EmergencyRecovery(), out string reason);
            Assert.IsTrue(_arena.Procs.Loadout.Carries("G09"), reason);
        }

        /// <summary>Hurts the player down to a fraction of their maximum.</summary>
        private void HurtTo(float fraction)
        {
            float target = _arena.Player.Health.Max * fraction;
            float remove = _arena.Player.Health.Current - target;
            if (remove <= 0f)
                return;

            _arena.Player.ReceiveDamage(
                new DamageRequest { Flat = remove, Type = DamageType.True },
                CombatStats.StartingVaccine());
        }

        [UnityTest]
        public IEnumerator ItDoesNothingWhileTheCarrierIsHealthy()
        {
            // 위기 is a threshold, not "any damage". A scratch must not spend a 60 second
            // cooldown.
            yield return ArenaWithOnlyEmergencyRecovery();

            HurtTo(0.9f);
            yield return _arena.Tick(2);

            Assert.AreEqual(0, _arena.Player.Shields.LayerCount, "it shielded a healthy carrier");
            Assert.Less(
                _arena.Player.Health.Current, _arena.Player.Health.Max,
                "it healed a carrier that was not in 위기");
        }

        [UnityTest]
        public IEnumerator CrossingTheThresholdFiresItOnce()
        {
            yield return ArenaWithOnlyEmergencyRecovery();

            float max = _arena.Player.Health.Max;
            HurtTo(0.2f);
            yield return _arena.Tick(2);

            if (EmergencyRecoveryPolicy.IsShield)
            {
                Assert.AreEqual(1, _arena.Player.Shields.LayerCount, "no 보호막 was raised");
                Assert.AreEqual(
                    max * EmergencyRecoveryPolicy.DraftMaxHealthFraction,
                    _arena.Player.Shields.Total, 0.01f,
                    "the 보호막 is not 최대 체력 50%");
            }
            else
            {
                // 20% + 50% of maximum, capped at full.
                Assert.Greater(
                    _arena.Player.Health.Current, max * 0.6f,
                    "the 회복 was not 최대 체력 50%");
                Assert.AreEqual(0, _arena.Player.Shields.LayerCount, "the heal reading raised a 보호막");
            }
        }

        [UnityTest]
        public IEnumerator ItCarriesTheMovementSpeedWithIt()
        {
            // docs/03 states 회복 또는 방어 효과 **+ 이동속도**, and the 이동속도 half is not
            // part of the undecided choice.
            yield return ArenaWithOnlyEmergencyRecovery();

            Assert.AreEqual(
                NeonVoid.Combat.Status.StatusEffectId.Haste,
                PluginCatalog.EmergencyRecovery().AppliedStatus);

            HurtTo(0.2f);
            yield return _arena.Tick(2);

            Assert.Greater(
                _arena.Player.MoveSpeedMultiplier, 1f,
                "위기 passed and the carrier did not speed up");
        }

        [UnityTest]
        public IEnumerator ItWillNotFireAgainWithinItsCooldown()
        {
            // [확정] docs/03: CD 60초. A card that fired on every tick below the threshold
            // would make the threshold a permanent heal.
            yield return ArenaWithOnlyEmergencyRecovery();

            HurtTo(0.2f);
            yield return _arena.Tick(2);

            float after = _arena.Player.Health.Current;
            int layers = _arena.Player.Shields.LayerCount;

            // Drop below the threshold again well inside the cooldown.
            HurtTo(0.15f);
            yield return _arena.Tick(2);

            if (EmergencyRecoveryPolicy.IsShield)
                Assert.AreEqual(layers, _arena.Player.Shields.LayerCount, "it fired twice inside its CD");
            else
                Assert.Less(_arena.Player.Health.Current, after, "it healed again inside its CD");

            Assert.AreEqual(
                3600, EmergencyRecoveryPolicy.StatedCooldownTicks, "60초 at 60 Hz");
        }

        [UnityTest]
        public IEnumerator TheAmountFollowsTheCarriersMaximumRatherThanANumber()
        {
            // 최대 체력 50%에 해당하는 - a fraction of whoever carries it, so a grown vaccine
            // gets more. A baked number would be half of whatever the card was written against.
            yield return ArenaWithOnlyEmergencyRecovery();

            CombatStats bigger = PrototypeArena.PlayerStats();
            bigger.MaxHealth = 400f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, bigger);
            yield return _arena.Tick(1);

            HurtTo(0.2f);
            yield return _arena.Tick(2);

            float expected = 400f * EmergencyRecoveryPolicy.DraftMaxHealthFraction;

            if (EmergencyRecoveryPolicy.IsShield)
                Assert.AreEqual(expected, _arena.Player.Shields.Total, 0.01f);
            else
                Assert.AreEqual(400f * 0.2f + expected, _arena.Player.Health.Current, 1f);
        }
    }
}
