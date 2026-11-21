using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Stats;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// R03 위기 반발: 플레이어 HP 30% 이하일 때 주변 적 넉백.
    /// <para>
    /// The assertion is distance moved. A card that queued the effect, resolved it and shoved
    /// nobody would pass every pure test, and 넉백 is the whole card - it deals no damage at
    /// all, so there is nothing else to measure.
    /// </para>
    /// </summary>
    public sealed class CrisisRepulsePlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        /// <summary>Only R03 carried, with an enemy standing well inside its reach.</summary>
        private IEnumerator ArenaWithOnlyCrisisRepulse()
        {
            _arena = PrototypeArena.Build(new Vector3(2f, 0.7f, 0f));
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Loadout.TryGrant(PluginCatalog.CrisisRepulse(), out string why), why);
            _arena.Procs.PushPassivesToOwner();
            yield return _arena.Tick(1);
        }

        /// <summary>Hurts the player down to a fraction of their maximum, through the pipeline.</summary>
        private void HurtTo(float fraction)
        {
            float target = _arena.Player.Health.Max * fraction;
            float remove = _arena.Player.Health.Current - target;
            if (remove <= 0f)
                return;

            var blow = new DamageRequest { Flat = remove, Type = DamageType.True };
            _arena.Player.ReceiveDamage(blow, CombatStats.StartingVaccine());
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        [UnityTest]
        public IEnumerator BeingHurtWhileLowShovesTheEnemiesBack()
        {
            yield return ArenaWithOnlyCrisisRepulse();
            CombatActor enemy = _arena.Enemies[0];

            Vector3 player = _arena.PlayerObject.transform.position;
            float before = FlatDistance(player, enemy.transform.position);

            HurtTo(0.2f);                       // below the card's 30%
            yield return _arena.Tick(3);

            float after = FlatDistance(_arena.PlayerObject.transform.position, enemy.transform.position);

            Assert.Greater(
                after, before + 1f,
                $"위기 반발 did not shove: {before:0.##}u before, {after:0.##}u after");
        }

        [UnityTest]
        public IEnumerator BeingHurtWhileHealthyShovesNobody()
        {
            // 플레이어 HP 30% 이하일 때. A card that fired on any damage would be a permanent
            // no-contact field rather than a rescue.
            yield return ArenaWithOnlyCrisisRepulse();
            CombatActor enemy = _arena.Enemies[0];

            Vector3 held = enemy.transform.position;

            HurtTo(0.9f);
            yield return _arena.Tick(3);

            Assert.Less(
                FlatDistance(held, enemy.transform.position), 0.5f,
                "it shoved while the player was in no danger at all");
        }

        [UnityTest]
        public IEnumerator ItWillNotFireAgainUntilItsCooldownHasRun()
        {
            // docs/03 leaves 내부 CD 미확정, so the draft cooldown is the card's own constant -
            // but a card with no cooldown at all would fire on every hit taken while low,
            // which is exactly when the player is being hit repeatedly.
            yield return ArenaWithOnlyCrisisRepulse();
            CombatActor enemy = _arena.Enemies[0];

            HurtTo(0.2f);
            yield return _arena.Tick(3);

            Vector3 afterFirst = enemy.transform.position;

            // Hurt again, well inside the cooldown.
            HurtTo(0.1f);
            yield return _arena.Tick(3);

            Assert.Less(
                FlatDistance(afterFirst, enemy.transform.position), 0.5f,
                "it fired a second time inside its own internal cooldown");
        }

        [UnityTest]
        public IEnumerator ABossIsNotMoved()
        {
            // [초안] 보스 저항. docs/01 section 3: 강한 CC는 강인함·저항/회피창 검증 없이는
            // 넣지 않는다, and shoving a boss out of a telegraphed pattern is the strongest
            // form of that.
            yield return ArenaWithOnlyCrisisRepulse();
            CombatActor enemy = _arena.Enemies[0];
            enemy.gameObject.AddComponent<AreaBossBrain>();

            Vector3 held = enemy.transform.position;

            HurtTo(0.2f);
            yield return _arena.Tick(3);

            Assert.Less(
                FlatDistance(held, enemy.transform.position), 0.5f,
                "a boss was shoved out of its own fight");
        }

        [UnityTest]
        public IEnumerator WithoutTheCardNobodyIsShoved()
        {
            yield return ArenaWithOnlyCrisisRepulse();
            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();

            CombatActor enemy = _arena.Enemies[0];
            Vector3 held = enemy.transform.position;

            HurtTo(0.2f);
            yield return _arena.Tick(3);

            Assert.Less(
                FlatDistance(held, enemy.transform.position), 0.5f,
                "something shoved with no card to do it");
        }
    }
}
