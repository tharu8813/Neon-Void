using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.Stats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// 보호막 in the real damage pipeline: a shielded actor keeps health a bare one would lose.
    /// <para>
    /// The ordering questions are D-004's and live in ShieldStackTests, where every reading is
    /// covered. What only a running scene shows is that the stack is actually consulted
    /// between the resolve and the health - a shield that absorbed perfectly in isolation and
    /// was never asked would pass every pure test and protect nobody.
    /// </para>
    /// </summary>
    public sealed class ShieldPlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        private void Hit(CombatActor target, float amount)
        {
            var blow = new DamageRequest { Flat = amount, Type = DamageType.True };
            target.ReceiveDamage(blow, CombatStats.StartingVaccine());
        }

        [UnityTest]
        public IEnumerator AShieldedActorKeepsHealthABareOneLoses()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            float full = _arena.Player.Health.Current;

            _arena.Player.Shields.Grant("test", 20f, nowTick: 0L, lifetimeTicks: 0);
            Assert.AreEqual(20f, _arena.Player.Shields.Total, 0.001f);

            Hit(_arena.Player, 12f);

            Assert.AreEqual(full, _arena.Player.Health.Current, 0.001f, "the shield let damage through");
            Assert.AreEqual(8f, _arena.Player.Shields.Total, 0.001f, "the shield did not pay for it");
        }

        [UnityTest]
        public IEnumerator DamageBeyondTheShieldStillReachesHealth()
        {
            // docs/02 section 3: actualHealthDamage = max(0, appliedBeforeShield - absorbed).
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            float full = _arena.Player.Health.Current;
            _arena.Player.Shields.Grant("test", 10f, 0L, 0);

            Hit(_arena.Player, 30f);

            Assert.AreEqual(full - 20f, _arena.Player.Health.Current, 0.001f);
            Assert.IsFalse(_arena.Player.Shields.HasAny, "the shield survived being overrun");
        }

        [UnityTest]
        public IEnumerator AnUnshieldedActorIsUnchanged()
        {
            // The null case. Shields were added to a pipeline every other test depends on, so
            // "nothing changed for anybody without one" is worth asserting rather than assuming.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            float full = _arena.Player.Health.Current;
            Assert.IsFalse(_arena.Player.Shields.HasAny);

            Hit(_arena.Player, 15f);

            Assert.AreEqual(full - 15f, _arena.Player.Health.Current, 0.001f);
        }

        [UnityTest]
        public IEnumerator AShieldStopsProtectingOnceItsTimeIsUp()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(1);

            long granted = _arena.Runner.TickIndex;
            _arena.Player.Shields.Grant("test", 50f, granted, lifetimeTicks: 30);

            yield return _arena.Tick(40);
            float full = _arena.Player.Health.Current;

            Hit(_arena.Player, 15f);

            Assert.AreEqual(
                full - 15f, _arena.Player.Health.Current, 0.001f,
                "an expired shield was still absorbing");
        }

        [UnityTest]
        public IEnumerator AShieldDoesNotHealTheAttackerUnderTheConservativeReading()
        {
            // [미확정] docs/02 section 4: whether damage a shield swallowed counts as damage
            // dealt for 흡혈. LifestealPolicy.Draft says no, and this checks the two drafts
            // agree with each other now that shields exist to swallow anything at all.
            _arena = PrototypeArena.Build(new Vector3(2f, 0.7f, 0f));
            yield return _arena.Tick(1);

            CombatStats thirsty = PrototypeArena.PlayerStats();
            thirsty.OmnivampPercent = 50f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, thirsty);
            _arena.Player.Health.Remove(30f);
            float hurt = _arena.Player.Health.Current;

            CombatActor enemy = _arena.Enemies[0];
            enemy.Shields.Grant("test", 100f, 0L, 0);

            var blow = new DamageRequest { Flat = 20f, Type = DamageType.True };
            enemy.ReceiveDamage(blow, _arena.Player.Stats);

            Assert.AreEqual(
                80f, enemy.Shields.Total, 0.001f, "the shield did not take the hit");
            Assert.AreEqual(
                hurt, _arena.Player.Health.Current, 0.001f,
                "the attacker healed for damage a shield swallowed");
        }
    }
}
