using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.Projectiles;
using NeonVoid.Combat.Stats;
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
    
        // --- G06 스로틀 증폭 and G03 확장 탄두 ---------------------------------------------

        /// <summary>The arena's enemies are spheres of scale 1.4, so half a body is 0.7 units.</summary>
        private const float EnemyBodyRadius = 0.7f;

        [UnityTest]
        public IEnumerator ThrottleAmplifierFiresFasterAndSendsTheShotFaster()
        {
            // docs/03 G06 스로틀 증폭: 공격속도 + 탄환속도 증가. Both halves are read off the
            // attacker rather than off a stat block, because docs/02's stat block has neither.
            _arena = PrototypeArena.Build();
            yield return WithAnEmptyLoadout();

            int plainInterval = _arena.Attacker.IntervalTicks;
            float plainSpeed = _arena.Attacker.EffectiveProjectileSpeed;

            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Loadout.TryGrant(PluginCatalog.ThrottleAmplifier(), out string why), why);
            _arena.Procs.PushPassivesToOwner();

            Assert.Less(
                _arena.Attacker.IntervalTicks, plainInterval,
                "G06 did not shorten the interval between basic attacks");
            Assert.Greater(
                _arena.Attacker.EffectiveProjectileSpeed, plainSpeed,
                "G06 did not speed the shot up");
            Assert.AreEqual(
                plainSpeed * (1f + PluginCatalog.DraftThrottleProjectileSpeedFraction),
                _arena.Attacker.EffectiveProjectileSpeed, 0.001f,
                "the projectile speed fraction is not of the base");
        }

        [UnityTest]
        public IEnumerator NoStackOfThrottleAmplifiersBeatsTheAttackSpeedCap()
        {
            // [확정] docs/02 section 3: 공격속도에 상한은 있지만 값 미확정, and docs/03 lists
            // 공격속도 상한 검사 against G06 by name. A rate contribution that went around
            // AttackRateMath would be the one card in the set that could reach one tick.
            _arena = PrototypeArena.Build();
            yield return WithAnEmptyLoadout();

            // One card with an absurd fraction rather than a stack of forty: the loadout keeps
            // one of each card, so granting 스로틀 증폭 forty times grants it once - and the test
            // would then be asserting the cap while nothing was pushing against it.
            PluginDefinition runaway = PluginCatalog.ThrottleAmplifier();
            runaway.Passive.AttackSpeedFraction = 100f;
            Assert.AreEqual(
                PluginGrantVerdict.Granted, _arena.Procs.Loadout.TryGrant(runaway, out string why), why);

            _arena.Procs.PushPassivesToOwner();

            Assert.GreaterOrEqual(
                _arena.Attacker.IntervalTicks, 1,
                "a runaway 스로틀 증폭 drove the interval below a tick");

            int capped = AttackRateMath.IntervalTicks(StatCaps.Draft().MaxAttacksPerSecond);
            Assert.AreEqual(
                capped, _arena.Attacker.IntervalTicks,
                "a runaway 스로틀 증폭 went past the attack speed cap");
        }

        [UnityTest]
        public IEnumerator AWiderShotHitsWhatANarrowOneMisses()
        {
            // The 유효 충돌크기 half of G03. Fired directly at two radii rather than through the
            // card, so the margins are generous and the assertion is about the sweep taking the
            // radius at all - the card's own 40% is [초안] and would make this geometry depend
            // on a tuning number.
            const float Offset = 1.2f;
            _arena = PrototypeArena.Build(new Vector3(Offset, 0.7f, 6f));
            CombatActor enemy = _arena.Enemies[0];

            // Aimed straight down +Z, so the enemy is Offset units off the shot's line.
            CombatActor aimPoint = _arena.SpawnEnemyNear(_arena.Player, "Aim Point", 0f);
            aimPoint.transform.position = new Vector3(0f, 0.7f, 20f);

            float before = enemy.Health.Current;
            _arena.FireAt(aimPoint, adRatio: 20f, radiusUnits: CombatProjectile.BaseRadiusUnits);
            yield return _arena.Tick(40);

            Assert.AreEqual(
                before, enemy.Health.Current, 0.001f,
                $"an ordinary shot reached a body {Offset}u off its line");

            float wide = Offset - EnemyBodyRadius + 0.2f;
            _arena.FireAt(aimPoint, adRatio: 20f, radiusUnits: wide);
            yield return _arena.Tick(40);

            Assert.Less(
                enemy.Health.Current, before,
                $"a shot of radius {wide}u still missed a body {Offset}u off its line");
        }

        [UnityTest]
        public IEnumerator ACarriedSizeCardReachesTheShotsTheAttackerFires()
        {
            // The whole chain: card → loadout → director → attacker → the sweep. A generous
            // fraction rather than G03's own, for the same reason as above.
            const float Offset = 1.2f;
            _arena = PrototypeArena.Build(new Vector3(Offset, 0.7f, 6f));
            _arena.PlayerObject.transform.forward = Vector3.forward;
            CombatActor enemy = _arena.Enemies[0];
            yield return WithAnEmptyLoadout();

            _arena.Attacker.enabled = true;
            float before = enemy.Health.Current;
            yield return _arena.Tick(_arena.Attacker.IntervalTicks + 40);

            Assert.AreEqual(
                before, enemy.Health.Current, 0.001f,
                "the basic attack already reached a body off its line");

            PluginDefinition wide = PluginCatalog.ExpandedWarhead();
            wide.Passive.ProjectileSizeFraction = 6f;   // 0.13 → 0.91, comfortably past the body
            Assert.AreEqual(
                PluginGrantVerdict.Granted, _arena.Procs.Loadout.TryGrant(wide, out string why), why);
            _arena.Procs.PushPassivesToOwner();

            Assert.AreEqual(
                CombatProjectile.BaseRadiusUnits * 7f, _arena.Attacker.EffectiveProjectileRadius, 0.001f,
                "the carried card never reached the attacker");

            yield return _arena.Tick(_arena.Attacker.IntervalTicks + 40);

            Assert.Less(
                enemy.Health.Current, before,
                "a carried 확장 탄두 did not widen the shots the attacker fires");
        }
    
        // --- R07 자동 추적탄 ----------------------------------------------------------------

        /// <summary>Somewhere straight ahead, so a shot aimed at it travels down +Z.</summary>
        private CombatActor AimPointAhead()
        {
            CombatActor aim = _arena.SpawnEnemyNear(_arena.Player, "Aim Point", 0f);
            aim.transform.position = new Vector3(0f, 0.7f, 40f);
            return aim;
        }

        [UnityTest]
        public IEnumerator AGuidedShotCurvesIntoWhatAStraightOneMisses()
        {
            // 유도탄 발사. The assertion is health removed, not the projectile's heading: a shot
            // that turned correctly and still swept the old direction would pass a heading test
            // and hit nothing.
            const float Offset = 5f;
            _arena = PrototypeArena.Build(new Vector3(Offset, 0.7f, 20f));
            CombatActor enemy = _arena.Enemies[0];
            CombatActor aim = AimPointAhead();

            float before = enemy.Health.Current;
            _arena.FireAt(aim, adRatio: 20f);
            yield return _arena.Tick(160);

            Assert.AreEqual(
                before, enemy.Health.Current, 0.001f,
                "a straight shot reached a body it was not aimed at");

            _arena.FireAt(aim, adRatio: 20f, homingTurnDegreesPerTick: 6f);
            yield return _arena.Tick(160);

            Assert.Less(
                enemy.Health.Current, before, "a 유도탄 did not curve into its target");
        }

        [UnityTest]
        public IEnumerator AGuidedShotThatLosesItsTargetFliesOnStraight()
        {
            // [초안] 타깃 소실: it does not despawn, which would delete a shot the player had
            // already earned, and it does not re-acquire, which would make it a weapon that
            // cannot miss - a different and far stronger card than 유도탄 발사.
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 18f));
            CombatActor enemy = _arena.Enemies[0];
            CombatActor aim = AimPointAhead();

            CombatProjectile shot = _arena.FireAt(aim, adRatio: 1f, homingTurnDegreesPerTick: 6f);
            yield return _arena.Tick(10);

            Assert.IsTrue(shot.IsHoming, "the shot was not guided to begin with");
            Assert.AreEqual(enemy, shot.HomingTarget, "it followed something other than the only enemy");

            Vector3 heading = shot.Direction;
            PrototypeArena.Kill(enemy);
            yield return _arena.Tick(5);

            Assert.IsTrue(shot != null, "losing its target deleted the shot");
            Assert.IsFalse(shot.IsHoming, "it went looking for somebody else");
            Assert.AreEqual(
                heading, shot.Direction,
                "it kept turning after it had nothing to turn towards");
        }

        [UnityTest]
        public IEnumerator SeekerRoundGuidesItsOwnShotAndNotTheMainOne()
        {
            // The additive reading: R07 adds a guided shot. Converting the basic attack is what
            // L01 and L02 do, and that is the question D-005 has open.
            _arena = PrototypeArena.Build(new Vector3(0f, 0.7f, 30f));
            _arena.PlayerObject.transform.forward = Vector3.forward;
            yield return WithAnEmptyLoadout();

            Assert.AreEqual(
                PluginGrantVerdict.Granted,
                _arena.Procs.Loadout.TryGrant(PluginCatalog.SeekerRound(), out string why), why);
            _arena.Procs.PushPassivesToOwner();

            Assert.AreEqual(1, _arena.Attacker.ExtraShotsPerVolley, "R07 added no shot");

            _arena.Attacker.enabled = true;
            yield return _arena.Tick(1);

            CombatProjectile[] volley =
                Object.FindObjectsByType<CombatProjectile>(FindObjectsSortMode.None);
            Assert.AreEqual(2, volley.Length, "the volley was not one shot plus R07's");

            int guided = 0;
            for (int i = 0; i < volley.Length; ++i)
            {
                if (volley[i].IsHoming)
                    ++guided;
            }

            Assert.AreEqual(
                1, guided,
                "R07 guided the wrong number of shots - the main attack must stay straight");
        }
    }
}
