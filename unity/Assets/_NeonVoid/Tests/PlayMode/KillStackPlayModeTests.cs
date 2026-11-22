using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Missions;
using NeonVoid.Plugins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// R02 킬 스택: 적 처치 시 AD 소폭 증가.
    /// <para>
    /// The assertion is the attack damage in the stat snapshot, not the stack counter. A
    /// counter that goes up while the snapshot does not is exactly the failure this project
    /// keeps finding - and here the two are separated by a push that has to happen.
    /// </para>
    /// </summary>
    public sealed class KillStackPlayModeTests
    {
        private PrototypeArena _arena;
        private GameObject _missionObject;
        private MissionRunDirector _mission;

        [TearDown]
        public void TearDown()
        {
            if (_missionObject != null)
                UnityEngine.Object.Destroy(_missionObject);
            _missionObject = null;
            _mission = null;

            _arena?.Dispose();
            _arena = null;
        }

        /// <summary>Only R02 carried, so no other card can move the player's AD.</summary>
        private IEnumerator ArenaWithOnlyKillStack(params Vector3[] enemies)
        {
            _arena = PrototypeArena.Build(enemies);
            yield return _arena.Tick(1);

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            _arena.Procs.Grant(PluginCatalog.KillStack(), out string reason);
            Assert.IsTrue(_arena.Procs.Loadout.Carries("R02"), reason);
        }

        /// <summary>
        /// Puts a dummy back on its feet. Configure refills the health pool, which is the only
        /// revive this project has - there is no respawn yet, and these tests need more kills
        /// than the arena has bodies.
        /// </summary>
        private static void PutBackOnItsFeet(CombatActor enemy) =>
            enemy.Configure(PrototypeArena.EnemyTeam, PrototypeArena.EnemyStats());

        /// <summary>
        /// Kills an enemy with a hit from the player, so the kill is reported through the
        /// normal pipeline rather than by taking its health away behind the engine's back.
        /// </summary>
        private IEnumerator PlayerKills(CombatActor enemy)
        {
            var finisher = new DamageRequest
            {
                Flat = enemy.Health.Max * 4f,
                Type = DamageType.True,
                CanCrit = false,
            };

            DamageOutcome outcome = enemy.ReceiveDamage(finisher, _arena.Player.Stats, critRolled: false);
            _arena.Player.NotifyHitLanded(enemy, finisher, outcome, HitOrigin.Projectile);
            Assert.IsTrue(outcome.Killed, "the finisher did not kill");

            yield return _arena.Tick(1);
        }

        [UnityTest]
        public IEnumerator AKillRaisesTheAttackDamageInTheSnapshot()
        {
            yield return ArenaWithOnlyKillStack(new Vector3(0f, 0.7f, 5f));
            float before = _arena.Player.Stats.AttackDamage;

            yield return PlayerKills(_arena.Enemies[0]);

            Assert.AreEqual(1, _arena.Procs.GrowthStacks("R02"), "The kill was not counted.");
            Assert.Greater(
                _arena.Player.Stats.AttackDamage, before,
                "The stack was counted and the attack damage did not move.");
            Assert.AreEqual(
                before + PluginCatalog.DraftKillStackAttackDamage,
                _arena.Player.Stats.AttackDamage, 1e-3f);
        }

        [UnityTest]
        public IEnumerator EachKillAddsAnotherStack()
        {
            yield return ArenaWithOnlyKillStack(
                new Vector3(0f, 0.7f, 5f), new Vector3(2f, 0.7f, 5f), new Vector3(-2f, 0.7f, 5f));
            float before = _arena.Player.Stats.AttackDamage;

            for (int i = 0; i < 3; ++i)
                yield return PlayerKills(_arena.Enemies[i]);

            Assert.AreEqual(3, _arena.Procs.GrowthStacks("R02"));
            Assert.AreEqual(
                before + (PluginCatalog.DraftKillStackAttackDamage * 3f),
                _arena.Player.Stats.AttackDamage, 1e-3f);
        }

        [UnityTest]
        public IEnumerator AHitThatDoesNotKillCountsNothing()
        {
            yield return ArenaWithOnlyKillStack(new Vector3(0f, 0.7f, 5f));
            CombatActor enemy = _arena.Enemies[0];
            float before = _arena.Player.Stats.AttackDamage;

            _arena.FireAt(enemy);
            yield return _arena.Tick(45);

            Assert.Less(enemy.Health.Current, enemy.Health.Max, "the shot never landed");
            Assert.IsTrue(enemy.IsAlive, "the shot killed it, so this proves nothing");
            Assert.AreEqual(0, _arena.Procs.GrowthStacks("R02"));
            Assert.AreEqual(before, _arena.Player.Stats.AttackDamage, 1e-4f);
        }

        [UnityTest]
        public IEnumerator WithoutTheCardAKillChangesNothing()
        {
            yield return ArenaWithOnlyKillStack(new Vector3(0f, 0.7f, 5f));
            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();
            float before = _arena.Player.Stats.AttackDamage;

            yield return PlayerKills(_arena.Enemies[0]);

            Assert.AreEqual(0, _arena.Procs.GrowthStacks("R02"));
            Assert.AreEqual(before, _arena.Player.Stats.AttackDamage, 1e-4f);
        }

        [UnityTest]
        public IEnumerator DroppingTheCardTakesItsStacksWithIt()
        {
            // A card dropped and granted again must not come back with the kills it had. The
            // stat pipeline recomputes from base, so the residue would be in the counter rather
            // than in the snapshot - which is the harder kind to notice.
            yield return ArenaWithOnlyKillStack(new Vector3(0f, 0.7f, 5f));
            float baseline = _arena.Player.Stats.AttackDamage;

            yield return PlayerKills(_arena.Enemies[0]);
            Assert.AreEqual(1, _arena.Procs.GrowthStacks("R02"));

            _arena.Procs.Loadout.Clear();
            _arena.Procs.PushPassivesToOwner();

            Assert.AreEqual(0, _arena.Procs.GrowthStacks("R02"), "The counter outlived the card.");
            Assert.AreEqual(baseline, _arena.Player.Stats.AttackDamage, 1e-4f);

            _arena.Procs.Grant(PluginCatalog.KillStack(), out _);
            Assert.AreEqual(
                baseline, _arena.Player.Stats.AttackDamage, 1e-4f,
                "Re-granting the card restored stacks it should not have remembered.");
        }

        [UnityTest]
        public IEnumerator TheStacksStopAtTheirBound()
        {
            // [초안, D-019] the bound is undecided; that it is OBSERVED is not.
            yield return ArenaWithOnlyKillStack(new Vector3(0f, 0.7f, 5f));
            CombatActor enemy = _arena.Enemies[0];
            float before = _arena.Player.Stats.AttackDamage;

            // More kills than the bound allows, by reviving the same dummy between them.
            for (int i = 0; i < PluginCatalog.DraftKillStackMaxStacks + 5; ++i)
            {
                PutBackOnItsFeet(enemy);
                yield return PlayerKills(enemy);
            }

            Assert.AreEqual(PluginCatalog.DraftKillStackMaxStacks, _arena.Procs.GrowthStacks("R02"));
            Assert.AreEqual(
                before + (PluginCatalog.DraftKillStackAttackDamage * PluginCatalog.DraftKillStackMaxStacks),
                _arena.Player.Stats.AttackDamage, 1e-2f);
        }

        [UnityTest]
        public IEnumerator TheHigherAttackDamageActuallyHitsHarder()
        {
            // The snapshot is only worth anything if the damage pipeline reads it. A stat that
            // rises while the damage does not is the same failure one layer along.
            yield return ArenaWithOnlyKillStack(
                new Vector3(0f, 0.7f, 5f), new Vector3(2f, 0.7f, 5f), new Vector3(-2f, 0.7f, 5f));

            var attack = new DamageRequest { AdRatio = 1f, Type = DamageType.Physical, CanCrit = false };

            CombatActor first = _arena.Enemies[1];
            float firstBefore = first.Health.Current;
            first.ReceiveDamage(attack, _arena.Player.Stats, critRolled: false);
            float weak = firstBefore - first.Health.Current;

            for (int i = 0; i < 5; ++i)
            {
                PutBackOnItsFeet(_arena.Enemies[0]);
                yield return PlayerKills(_arena.Enemies[0]);
            }

            CombatActor second = _arena.Enemies[2];
            float secondBefore = second.Health.Current;
            second.ReceiveDamage(attack, _arena.Player.Stats, critRolled: false);
            float strong = secondBefore - second.Health.Current;

            Assert.Greater(weak, 0f, "the unbuffed attack did nothing");
            Assert.Greater(strong, weak, "Five kills' worth of AD did not reach the damage.");
        }
    
        // --- [확정] D-019: 의뢰 종료 시 전부 소멸 ----------------------------------------------

        /// <summary>
        /// A mission this test drives, pointed at the proc director. Pushed in rather than
        /// found, because the director resolves it in its own Awake and that has already run.
        /// </summary>
        private MissionFlow DriveAMission()
        {
            _missionObject = new GameObject("Mission Run Director");
            _missionObject.SetActive(false);
            _mission = _missionObject.AddComponent<MissionRunDirector>();
            _mission.ConfigureStart(beginOnAwake: false);
            _missionObject.SetActive(true);

            _arena.Procs.enabled = false;
            _arena.Procs.UseMissionDirector(_mission);
            _arena.Procs.enabled = true;

            return _mission.Flow;
        }

        [UnityTest]
        public IEnumerator AMissionThatEndsTakesTheKillStacksWithIt()
        {
            // [확정] D-019, approved 2026-11-22: 의뢰 종료 시 전부 소멸. Until this, the stacks
            // only went away when the card stopped being carried.
            yield return ArenaWithOnlyKillStack(new Vector3(3f, 0f, 0f));
            MissionFlow flow = DriveAMission();

            Assert.IsTrue(flow.TryBeginMission(new RunIdentity(1L, 11u), 0L), flow.LastRefusal);

            CombatActor dummy = _arena.Enemies[0];
            for (int i = 0; i < 3; ++i)
            {
                PutBackOnItsFeet(dummy);
                yield return PlayerKills(dummy);
            }

            Assert.AreEqual(3, _arena.Procs.GrowthStacks("R02"), "the kills were not counted");

            Assert.IsTrue(flow.TryFail(), flow.LastRefusal);
            yield return _arena.Tick(1);

            Assert.AreEqual(
                0, _arena.Procs.GrowthStacks("R02"),
                "the stacks survived the mission that earned them");
        }

        [UnityTest]
        public IEnumerator TheNextMissionStartsWithoutTheLastOnesAttackDamage()
        {
            // The failure this rule exists to stop: a second mission beginning with the first
            // one's attack damage already on the player, for no reason they could name.
            yield return ArenaWithOnlyKillStack(new Vector3(3f, 0f, 0f));
            MissionFlow flow = DriveAMission();

            float clean = _arena.Player.Stats.AttackDamage;

            Assert.IsTrue(flow.TryBeginMission(new RunIdentity(1L, 11u), 0L), flow.LastRefusal);

            CombatActor dummy = _arena.Enemies[0];
            for (int i = 0; i < 2; ++i)
            {
                PutBackOnItsFeet(dummy);
                yield return PlayerKills(dummy);
            }

            Assert.Greater(_arena.Player.Stats.AttackDamage, clean, "the kills never reached the player");

            Assert.IsTrue(flow.TryFail(), flow.LastRefusal);
            Assert.IsTrue(flow.TryReturnToHub(), flow.LastRefusal);
            yield return _arena.Tick(1);

            Assert.AreEqual(
                clean, _arena.Player.Stats.AttackDamage, 0.001f,
                "the player walked back into the hub with the mission's attack damage");

            Assert.IsTrue(flow.TryBeginMission(new RunIdentity(2L, 12u), 100L), flow.LastRefusal);
            yield return _arena.Tick(1);

            Assert.AreEqual(0, _arena.Procs.GrowthStacks("R02"));
            Assert.AreEqual(clean, _arena.Player.Stats.AttackDamage, 0.001f);
        }

        [UnityTest]
        public IEnumerator AMissionEndingWithNoStacksChangesNothing()
        {
            // The no-op path, because EndMission republishes the stats and a republish that
            // happened for nothing would be a cost paid on every mission.
            yield return ArenaWithOnlyKillStack(new Vector3(3f, 0f, 0f));
            MissionFlow flow = DriveAMission();

            float before = _arena.Player.Stats.AttackDamage;
            Assert.IsTrue(flow.TryBeginMission(new RunIdentity(1L, 11u), 0L), flow.LastRefusal);
            Assert.IsTrue(flow.TryFail(), flow.LastRefusal);
            yield return _arena.Tick(1);

            Assert.AreEqual(0, _arena.Procs.GrowthStacks("R02"));
            Assert.AreEqual(before, _arena.Player.Stats.AttackDamage, 0.001f);
        }
    }
}
