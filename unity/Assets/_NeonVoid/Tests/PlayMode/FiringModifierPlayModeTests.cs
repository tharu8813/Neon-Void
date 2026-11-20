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
    /// R06 보조 탄환 and G13 4중 캐시 in a running scene: the extra projectiles actually leave
    /// the weapon, and the 적중 효과 피해 fraction actually reduces what the effects do.
    /// <para>
    /// The second half is the one that matters. Every piece of the fraction - the request
    /// scaling, the hit carrying it, the event carrying it, the router inheriting it - is
    /// covered by EditMode tests, and all of that can be correct while the line that multiplies
    /// in the resolver is missing. This project has shipped that exact shape four times, so the
    /// assertion here is on health removed.
    /// </para>
    /// </summary>
    public sealed class FiringModifierPlayModeTests
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

        // --- the extra shot leaves the weapon ---------------------------------------------

        /// <summary>
        /// Empties the loadout and republishes it, so a test starts from "nothing granted".
        /// The prototype set now carries R06, and the director grants and pushes it on its own
        /// first tick - so clearing before that tick would simply be overwritten.
        /// </summary>
        private IEnumerator WithAnEmptyLoadout()
        {
            yield return _arena.Tick(1);
            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
        }

        [UnityTest]
        public IEnumerator TheBasicAttackFiresOneProjectileWithNothingGranted()
        {
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            _arena.PlayerObject.transform.forward = Vector3.forward;
            yield return WithAnEmptyLoadout();

            Assert.AreEqual(0, _arena.Attacker.ExtraShotsPerVolley);

            _arena.Attacker.enabled = true;   // the arena disables it; these tests are about firing
            yield return _arena.Tick(1);

            Assert.AreEqual(1, LiveProjectiles(), "A bare volley is one shot.");
        }

        [UnityTest]
        public IEnumerator SupportRoundAddsAProjectileToEveryVolley()
        {
            // [원안 수치] docs/03 R06: 발사 탄환 1개 추가.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            _arena.PlayerObject.transform.forward = Vector3.forward;
            yield return WithAnEmptyLoadout();

            _arena.Procs.Grant(PluginCatalog.SupportRound(), out string reason);

            Assert.AreEqual(1, _arena.Attacker.ExtraShotsPerVolley, reason);

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            Assert.AreEqual(2, LiveProjectiles(), "The extra shot never left the weapon.");
        }

        [UnityTest]
        public IEnumerator ExtraShotsArePushedInRatherThanAskedFor()
        {
            // Combat must not reference Plugins, so the loadout is pushed into the attacker.
            // A card granted and never pushed would sit there firing nothing - which is the
            // failure this project keeps finding, so the push is tested rather than assumed.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 6f));
            yield return WithAnEmptyLoadout();

            _arena.Procs.Grant(PluginCatalog.SupportRound(), out string reason);

            Assert.AreEqual(1, _arena.Attacker.ExtraShotsPerVolley, reason);
        }

        [UnityTest]
        public IEnumerator ReplacingTheExtraShotsDoesNotAccumulateThem()
        {
            // Pushed lists replace, like the stat pipeline recomputes from base. A list that
            // added would grow every time the loadout was republished.
            _arena = PrototypeArena.Build();

            for (int i = 0; i < 4; ++i)
                _arena.Attacker.SetExtraShots(new[] { new ExtraShotSpec(1, 0.8f) });
            _arena.Attacker.enabled = true;

            Assert.AreEqual(1, _arena.Attacker.ExtraShotsPerVolley);
            yield return null;
        }

        // --- the burst -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AQueuedBurstFiresItsShotsAFewTicksApart()
        {
            // 연사 is rapid, not simultaneous: four projectiles at once would be a shotgun,
            // which is not what docs/03 G13 describes.
            _arena = PrototypeArena.Build();
            _arena.Attacker.SetExtraShots(null);
            _arena.PlayerObject.transform.forward = Vector3.forward;
            _arena.Attacker.enabled = true;
            _arena.Attacker.QueueBurst(4, 0.7f);

            Assert.AreEqual(4, _arena.Attacker.PendingBurstShots);

            yield return _arena.Tick(1);
            Assert.AreEqual(3, _arena.Attacker.PendingBurstShots, "The whole burst fired at once.");

            yield return _arena.Tick(40);
            Assert.AreEqual(0, _arena.Attacker.PendingBurstShots, "The burst never finished.");
        }

        [UnityTest]
        public IEnumerator AStunHoldsABurstRatherThanDeletingIt()
        {
            // The shots were earned before the stun landed. Discarding them would make 정지
            // silently delete damage the player had already paid for.
            _arena = PrototypeArena.Build();
            _arena.Attacker.enabled = true;
            _arena.Attacker.QueueBurst(4, 0.7f);
            _arena.Player.ApplyStatus(NeonVoid.Combat.Status.StatusEffectId.Stun, 1f);

            yield return _arena.Tick(5);

            Assert.AreEqual(4, _arena.Attacker.PendingBurstShots, "A stun ate the burst.");
        }

        [UnityTest]
        public IEnumerator FourResolvedHitEffectsEarnABurst()
        {
            // [원안 수치] docs/03 G13: 적중 효과 4회 후 탄환 4발. Counted on effects that
            // actually resolved - a refused one did nothing, and charging a burst off it would
            // turn the rules' refusals into damage.
            _arena = PrototypeArena.Build(
                new Vector3(2f, 0.7f, 5f), new Vector3(-2f, 0.7f, 5f), new Vector3(0f, 0.7f, 7f));
            _arena.Attacker.SetExtraShots(null);
            _arena.Procs.Grant(PluginCatalog.QuadCache(), out _);

            Assert.AreEqual(0, _arena.Attacker.PendingBurstShots);

            // 폭발 각인 is already carried by the prototype set, so landing shots produces
            // resolved hit effects to count.
            int fired = 0;
            while (_arena.Procs.HitEffectsTowardsBurst("G13") == 0 && _arena.Attacker.PendingBurstShots == 0
                   && fired < 8)
            {
                _arena.FireAt(_arena.Enemies[fired % _arena.Enemies.Count]);
                fired++;
                yield return _arena.Tick(30);
            }

            Assert.Greater(
                _arena.Procs.HitEffectsTowardsBurst("G13") + _arena.Attacker.PendingBurstShots, 0,
                "No hit effect was ever counted towards the burst.");
        }

        // --- the fraction actually reduces damage ----------------------------------------

        [UnityTest]
        public IEnumerator AReducedShotMakesItsEffectsHitForLess()
        {
            // The whole point, asserted on health removed rather than on the plumbing. Two
            // identical dummies, one shot at full strength and one at G13's 70%: the explosion
            // 폭발 각인 produces must be smaller on the second.
            _arena = PrototypeArena.Build(new Vector3(-6f, 0.7f, 5f), new Vector3(6f, 0.7f, 5f));
            yield return WithAnEmptyLoadout();

            // 폭발 각인 alone, so the health reading below is the explosion and nothing else.
            // With the whole prototype set carried, R04's 해체 adds an UNSCALED damage over
            // time to the same number - the fraction applies to effect damage, not to status
            // magnitudes - and the ratio stops measuring what this test is about. It is better
            // for this test to name the one card it depends on than to loosen its tolerance
            // every time a new card lands.
            _arena.Procs.Grant(PluginCatalog.DetonationGlyph(), out _);

            CombatActor full = _arena.Enemies[0];
            CombatActor reduced = _arena.Enemies[1];

            // A companion beside each target, so 폭발 각인 has something to explode onto whose
            // health can be read. The shot's own damage is identical in both cases, so any
            // difference in the companion's loss is the effect fraction.
            CombatActor fullNeighbour = _arena.SpawnEnemyNear(full, "Full Neighbour");
            CombatActor reducedNeighbour = _arena.SpawnEnemyNear(reduced, "Reduced Neighbour");
            yield return _arena.Tick(1);

            float fullNeighbourBefore = fullNeighbour.Health.Current;
            float reducedNeighbourBefore = reducedNeighbour.Health.Current;

            _arena.FireAt(full, onHitEffectScale: 1f);
            yield return _arena.Tick(60);
            _arena.FireAt(reduced, onHitEffectScale: 0.7f);
            yield return _arena.Tick(60);

            float fullLoss = fullNeighbourBefore - fullNeighbour.Health.Current;
            float reducedLoss = reducedNeighbourBefore - reducedNeighbour.Health.Current;

            Assert.Greater(fullLoss, 0f, "폭발 각인 did not reach the neighbour at all.");
            Assert.Greater(reducedLoss, 0f, "The reduced shot's effect did nothing rather than less.");
            Assert.Less(reducedLoss, fullLoss, "The fraction never reached the damage.");
            Assert.AreEqual(
                0.7f, reducedLoss / fullLoss, 0.08f,
                "The reduction is not the 70% docs/03 states for G13.");
        }
    }
}
