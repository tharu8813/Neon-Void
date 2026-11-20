using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The 코어 워처 fought in a running scene.
    /// <para>
    /// <see cref="BossPatternTests"/> holds docs/01 section 3's contract on the data and the
    /// clock. What only a scene can show is the part the contract is actually for: that the
    /// telegraph is <b>dodgeable</b>, and that what it drew is what took the hit. An attack that
    /// re-aimed on the tick it resolved would pass every pure test and be unavoidable in play.
    /// </para>
    /// </summary>
    public sealed class BossFightPlayModeTests
    {
        private PrototypeArena _arena;
        private GameObject _bossObject;
        private CombatActor _boss;
        private AreaBossBrain _brain;

        [TearDown]
        public void TearDown()
        {
            if (_bossObject != null)
                UnityEngine.Object.Destroy(_bossObject);
            _arena?.Dispose();
            _arena = null;
            _bossObject = null;
            _boss = null;
            _brain = null;
        }

        /// <summary>
        /// A 코어 워처 four units in front of the player, with enough health to outlast the test
        /// and a player who cannot be killed by it - these tests are about where the attacks land,
        /// not about who wins.
        /// </summary>
        private IEnumerator Arena()
        {
            _arena = PrototypeArena.Build();

            CombatStats playerStats = PrototypeArena.PlayerStats();
            playerStats.MaxHealth = 1_000_000f;
            _arena.Player.Configure(PrototypeArena.PlayerTeam, playerStats);

            _bossObject = _arena.Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
            _bossObject.name = "코어 워처";
            _bossObject.transform.position = _arena.PlayerObject.transform.position + new Vector3(0f, 0f, 4f);

            CombatStats bossStats = CombatStats.StartingVaccine();
            bossStats.MaxHealth = 500_000f;
            _boss = _bossObject.AddComponent<CombatActor>();
            _boss.Configure(PrototypeArena.EnemyTeam, bossStats);

            _brain = _bossObject.AddComponent<AreaBossBrain>();
            _brain.Configure(_boss, _arena.Player);
            _brain.UseCycle(BossCatalog.CoreWatcher());
            _brain.RefreshHostiles();

            yield return _arena.Tick(1);
        }

        /// <summary>Ticks until the boss has an attack on screen, or fails.</summary>
        private IEnumerator TickToTelegraph(int limit = 400)
        {
            for (int i = 0; i < limit; ++i)
            {
                if (_brain.TryGetTelegraph(out _))
                    yield break;

                yield return _arena.Tick(1);
            }

            Assert.Fail($"no telegraph in {limit} ticks; the boss is at {_brain.Cycle}");
        }

        /// <summary>Ticks until the attack now showing lands, or fails.</summary>
        private IEnumerator TickToResolve(int limit = 200)
        {
            for (int i = 0; i < limit; ++i)
            {
                yield return _arena.Tick(1);
                if (!_brain.TryGetTelegraph(out _))
                    yield break;
            }

            Assert.Fail($"the telegraph never resolved in {limit} ticks; the boss is at {_brain.Cycle}");
        }

        private Vector2 PlayerXZ
        {
            get
            {
                Vector3 at = _arena.PlayerObject.transform.position;
                return new Vector2(at.x, at.z);
            }
        }

        private void TeleportPlayer(Vector3 to)
        {
            Transform player = _arena.PlayerObject.transform;
            player.position = new Vector3(player.position.x, to.y, player.position.z);
            _arena.Driver.TeleportTo(new Vector2(to.x, to.z));
        }

        // --- the telegraph ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator NothingLandsBeforeTheWarningIsUp()
        {
            // docs/01's 입장 예고: the boss is announced before it can hurt anybody.
            yield return Arena();
            float full = _arena.Player.Health.Current;

            yield return _arena.Tick(BossPatternCycle.DraftEntranceTicks - 2);

            Assert.AreEqual(full, _arena.Player.Health.Current, "the boss hit during its own entrance");
            Assert.AreEqual(BossPhase.Entering, _brain.Cycle.Phase);
        }

        [UnityTest]
        public IEnumerator TheAttackLandsOnThePlayerWhoStoodStill()
        {
            yield return Arena();
            yield return TickToTelegraph();

            Assert.IsTrue(_brain.TryGetTelegraph(out BossTelegraph telegraph));
            Assert.IsTrue(telegraph.Covers(PlayerXZ), "the first pattern was not aimed at the player");

            float before = _arena.Player.Health.Current;
            yield return TickToResolve();

            Assert.Less(_arena.Player.Health.Current, before, "the telegraphed attack never landed");
        }

        [UnityTest]
        public IEnumerator SteppingOutOfTheTelegraphDodgesIt()
        {
            // The point of the whole contract. If this fails, every telegraph in the game is
            // decoration.
            yield return Arena();
            yield return TickToTelegraph();

            Assert.IsTrue(_brain.TryGetTelegraph(out BossTelegraph telegraph));
            Assert.IsTrue(telegraph.Covers(PlayerXZ), "the attack was not aimed at the player to begin with");

            // Well outside the fan's 9 units, which is what 0.8~1.5초 is for.
            TeleportPlayer(_bossObject.transform.position + new Vector3(40f, 1.1f, 0f));
            yield return _arena.Tick(1);

            Assert.IsFalse(telegraph.Covers(PlayerXZ), "the player did not actually leave the shape");

            float before = _arena.Player.Health.Current;
            yield return TickToResolve();

            Assert.AreEqual(
                before, _arena.Player.Health.Current,
                "the attack followed the player, so its warning meant nothing");
        }

        [UnityTest]
        public IEnumerator WhatTheTelegraphDrewIsWhatTookTheHit()
        {
            // docs/01: 전조 시각 표시와 실제 충돌 볼륨이 일치. Asserted as an agreement between the
            // stored telegraph and the health bar, over several patterns - a single pattern could
            // agree by luck.
            yield return Arena();

            for (int attack = 0; attack < 4; ++attack)
            {
                yield return TickToTelegraph();
                Assert.IsTrue(_brain.TryGetTelegraph(out BossTelegraph telegraph));

                // Alternate standing in it and stepping out of it, so both answers are exercised.
                if (attack % 2 == 1)
                    TeleportPlayer(_bossObject.transform.position + new Vector3(40f, 1.1f, 0f));
                else
                    TeleportPlayer(_bossObject.transform.position + new Vector3(0f, 1.1f, 2f));

                yield return _arena.Tick(1);
                bool covered = telegraph.Covers(PlayerXZ);

                float before = _arena.Player.Health.Current;
                yield return TickToResolve();
                bool hurt = _arena.Player.Health.Current < before;

                Assert.AreEqual(
                    covered, hurt,
                    $"{telegraph.Pattern.Name}: the telegraph said {covered} and the health bar said {hurt}");
            }
        }

        [UnityTest]
        public IEnumerator ARingIsAimedAtTheBossAndNotAtThePlayer()
        {
            // A ring has no facing, and the brain gives it none. If it were aimed, a bug in the
            // aiming could move a ring's coverage with nothing looking wrong.
            yield return Arena();

            for (int i = 0; i < 1200; ++i)
            {
                if (_brain.TryGetTelegraph(out BossTelegraph telegraph)
                    && telegraph.Pattern.Shape == BossPatternShape.Ring)
                {
                    Vector3 at = _bossObject.transform.position;
                    Assert.AreEqual(at.x, telegraph.OriginXZ.x, 0.01f, "the ring is not centred on the boss");
                    Assert.AreEqual(at.z, telegraph.OriginXZ.y, 0.01f);
                    Assert.AreEqual(Vector2.zero, telegraph.FacingXZ, "a ring was given a facing");
                    yield break;
                }

                yield return _arena.Tick(1);
            }

            Assert.Fail($"no ring pattern in 1200 ticks; the boss is at {_brain.Cycle}");
        }

        // --- 지역 기믹 --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheOverheatAnnouncesItselfAndSparesTheMiddleOfTheRoom()
        {
            // 주기적 과열, from the boss's own position, with its safe spot beside it - so the
            // arena's hazard pushes the player toward the thing trying to kill them.
            yield return Arena();

            BossTelegraph gimmick = default;
            bool found = false;
            for (int i = 0; i < BossCatalog.DraftCoreWatcherOverheatIntervalTicks + 200 && !found; ++i)
            {
                if (_brain.TryGetGimmickTelegraph(out gimmick))
                    found = true;
                else
                    yield return _arena.Tick(1);
            }

            Assert.IsTrue(found, $"the 기믹 never announced itself; the boss is at {_brain.Cycle}");
            Assert.IsTrue(gimmick.IsGimmick);
            Assert.AreEqual("주기적 과열", gimmick.Pattern.Name);

            Vector3 bossAt = _bossObject.transform.position;
            Assert.AreEqual(bossAt.x, gimmick.OriginXZ.x, 0.01f);
            Assert.IsFalse(
                gimmick.Covers(new Vector2(bossAt.x + 2f, bossAt.z)), "the safe middle is not safe");
            Assert.IsTrue(
                gimmick.Covers(new Vector2(bossAt.x + 15f, bossAt.z)), "out in the room is not dangerous");
        }

        // --- the end ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ADeadBossStopsFighting()
        {
            yield return Arena();
            yield return TickToTelegraph();

            PrototypeArena.Kill(_boss);
            yield return _arena.Tick(2);

            Assert.IsFalse(_boss.IsAlive);
            Assert.AreEqual(BossActivity.Finished, _brain.Cycle.Activity);
            Assert.IsFalse(_brain.TryGetTelegraph(out _), "a dead boss still has an attack on screen");

            float after = _arena.Player.Health.Current;
            yield return _arena.Tick(240);

            Assert.AreEqual(after, _arena.Player.Health.Current, "a dead boss kept attacking");
        }
    }
}
