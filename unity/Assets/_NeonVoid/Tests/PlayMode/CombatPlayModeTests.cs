using System.Collections;
using NeonVoid.Combat;
using NeonVoid.Combat.AI;
using NeonVoid.Combat.Skills;
using NeonVoid.Combat.Status;
using NeonVoid.Plugins;
using NeonVoid.Missions;
using NeonVoid.Progression;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeonVoid.Tests.PlayModeTests
{
    /// <summary>
    /// The checks that used to be a list handed to a person: enemies closing distance, hits
    /// landing through real colliders, a corpse that stops being a wall, plugin effects firing,
    /// a mark that actually reduces armour, and a death that registers exactly once.
    /// <para>
    /// None of this needed a human. It needed the game loop, which is what a PlayMode test
    /// runs: real physics, the real SimulationRunner, one tick per frame. What still needs
    /// eyes is what the screen looks like - that a thing is drawn, legible and in the right
    /// place - and how it feels to play. Everything above is behaviour, and behaviour can be
    /// asserted.
    /// </para>
    /// </summary>
    public sealed class CombatPlayModeTests
    {
        private PrototypeArena _arena;

        [TearDown]
        public void TearDown()
        {
            _arena?.Dispose();
            _arena = null;
        }

        // --- enemy AI ----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AnEnemyClosesDistanceOnThePlayer()
        {
            _arena = PrototypeArena.Build(new Vector3(8f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeChaser(enemy);

            float before = PrototypeArena.DistanceXZ(enemy, _arena.Player);
            yield return _arena.Tick(120);
            float after = PrototypeArena.DistanceXZ(enemy, _arena.Player);

            Assert.Less(after, before - 1f, $"The enemy did not approach: {before:0.00} -> {after:0.00}");
        }

        [UnityTest]
        public IEnumerator AnEnemyHoldsItsPreferredRangeInsteadOfOscillating()
        {
            // The hysteresis band, in the scene rather than in a pure function: with floating
            // point and a CharacterController in the way, a band that is too narrow shows up
            // as an enemy twitching forever on the spot.
            _arena = PrototypeArena.Build(new Vector3(6f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeChaser(enemy);

            yield return _arena.Tick(240);
            Vector3 settled = enemy.transform.position;

            yield return _arena.Tick(60);
            float drift = Vector3.Distance(settled, enemy.transform.position);

            Assert.Less(drift, 0.35f, $"The enemy kept shuffling after settling: drifted {drift:0.000}");
        }

        [UnityTest]
        public IEnumerator AnEnemyDamagesThePlayerThroughTheSharedEntryPoint()
        {
            _arena = PrototypeArena.Build(new Vector3(3f, 0.7f, 0f));
            PrototypeArena.MakeChaser(_arena.Enemies[0]);

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.Less(_arena.Player.Health.Current, before, "The player was never hit.");
        }

        [UnityTest]
        public IEnumerator AnEnemyGivesUpBeyondItsLeashRange()
        {
            _arena = PrototypeArena.Build(new Vector3(30f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            EnemyAgent agent = PrototypeArena.MakeChaser(enemy);

            yield return _arena.Tick(60);

            Assert.IsFalse(agent.LastIntent.HasTarget, "An enemy 30 units away acquired the player.");
            Assert.AreEqual(Vector2.zero, agent.LastIntent.MoveXZ);
        }

        // --- death -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ACorpseStopsBeingAWall()
        {
            // The regression this covers shipped once: a dead enemy kept its
            // CharacterController and stayed a solid, invisible obstacle that could corner the
            // player and stop their shots.
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            PrototypeArena.Kill(enemy);
            yield return _arena.Tick(2);

            Assert.IsFalse(enemy.IsAlive);
            Assert.IsFalse(enemy.GetComponent<CharacterController>().enabled,
                "The corpse kept its controller, so it is still solid.");

            Collider[] colliders = enemy.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
                Assert.IsFalse(colliders[i].enabled, "A corpse collider is still enabled.");
        }

        [UnityTest]
        public IEnumerator APlayerDeathRegistersExactlyOnceAndReleasesNothing()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            RunDeathLedger ledger = _arena.DeathDirector.Ledger;
            Assert.IsTrue(ledger.HasRegisteredDeath, "The death was not recorded.");
            Assert.AreEqual(RunDeathPhase.AwaitingCommit, ledger.Phase);
            Assert.IsFalse(ledger.MayReleaseResults,
                "Results were released although no save service ever acknowledged anything.");
        }

        [UnityTest]
        public IEnumerator ADeadPlayerStopsActing()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            PrototypeArena.Kill(_arena.Player);
            Vector3 restingPlace = _arena.PlayerObject.transform.position;
            yield return _arena.Tick(60);

            Assert.AreEqual(
                restingPlace.x, _arena.PlayerObject.transform.position.x, 0.01f, "The corpse moved.");
            Assert.AreEqual(
                restingPlace.z, _arena.PlayerObject.transform.position.z, 0.01f, "The corpse moved.");
        }

        // --- projectiles -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AShotDamagesTheEnemyItReaches()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            float before = enemy.Health.Current;
            _arena.FireAt(enemy);
            yield return _arena.Tick(60);

            Assert.Less(enemy.Health.Current, before, "The shot never landed.");
        }

        [UnityTest]
        public IEnumerator AShotWithNoPierceStopsAtTheFirstEnemy()
        {
            // Two enemies in a line, the second directly behind the first.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(7f, 0.7f, 0f));
            // Without the plugins: an explosion reaching the enemy behind is correct behaviour
            // and would make this assertion about pierce meaningless.
            _arena.WithoutPlugins();
            CombatActor first = _arena.Enemies[0];
            CombatActor second = _arena.Enemies[1];
            yield return _arena.Tick(2);

            float secondBefore = second.Health.Current;
            _arena.FireAt(first);
            yield return _arena.Tick(60);

            Assert.Less(first.Health.Current, first.Health.Max, "The shot never landed.");
            Assert.AreEqual(secondBefore, second.Health.Current, 1e-3f,
                "A shot with one target passed through into the enemy behind.");
        }

        // --- plugins -----------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ThePrototypePluginsAreCarriedAndFireOnAHit()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(6.2f, 0.7f, 1.2f));
            yield return _arena.Tick(2);

            Assert.AreEqual(
                NeonVoid.Plugins.PluginCatalog.Prototype().Count, _arena.Procs.Loadout.Count,
                "The prototype plugins were not carried.");

            _arena.FireAt(_arena.Enemies[0]);
            yield return _arena.Tick(60);

            Assert.Greater(_arena.Procs.ResolvedCountOf(12), 0, "G12 부식 표식 never fired.");
            Assert.Greater(_arena.Procs.ResolvedCountOf(17), 0, "G17 폭발 각인 never fired.");
            Assert.Greater(_arena.Procs.ResolvedCountOf(10), 0, "G10 전위 연쇄 never fired.");
        }

        [UnityTest]
        public IEnumerator TheCorrosionMarkActuallyReducesArmour()
        {
            // The bug this exists for: the mark was applied, stacked and displayed while
            // reducing nothing, because the reduction was a fraction of a base armour of zero.
            // Nothing in the game said so and no test caught it, because the unit test used a
            // base with armour the real enemies do not have.
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            float armourBefore = enemy.Stats.PhysicalArmor;
            _arena.FireAt(enemy);
            yield return _arena.Tick(60);

            Assert.IsTrue(enemy.Status.Has(StatusEffectId.FirewallBreach), "The mark was not applied.");
            Assert.Less(enemy.Stats.PhysicalArmor, armourBefore,
                "The mark is applied and reduces nothing, which is exactly the bug.");
            Assert.Less(enemy.Stats.MagicResistance, enemy.BaseStats.MagicResistance,
                "docs/03 G12 reduces both resistances.");
        }

        [UnityTest]
        public IEnumerator ChainLightningReachesAnEnemyNobodyShot()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(6.5f, 0.7f, 1.5f));
            CombatActor shot = _arena.Enemies[0];
            CombatActor bystander = _arena.Enemies[1];
            yield return _arena.Tick(2);

            float bystanderBefore = bystander.Health.Current;
            _arena.FireAt(shot);
            yield return _arena.Tick(60);

            Assert.Less(bystander.Health.Current, bystanderBefore,
                "An enemy standing next to the target took no damage, so nothing chained or exploded.");
        }

        // --- skills ------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ArcPulseDamagesAndSlowsWhatIsInFrontOfTheCursor()
        {
            _arena = PrototypeArena.Build(new Vector3(3f, 0.7f, 0f));
            _arena.WithoutPlugins();
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            SkillAsset arcPulse = PrototypeArena.SkillFrom(
                "skill.arc_pulse",
                new SkillDefinition
                {
                    SkillId = 1001,
                    ManaCost = 25f,
                    BaseCooldownTicks = 300,
                    Damage = new DamageRequest
                    {
                        Flat = 10f, ApRatio = 0.8f, Type = DamageType.Magic, IsArea = true,
                    },
                },
                new SkillShape
                {
                    Kind = SkillEffectKind.ConeDamage, RadiusUnits = 5f, ConeHalfAngleDegrees = 45f,
                },
                new SkillStatusEffect
                {
                    Id = StatusEffectId.Slow, Magnitude = 0.3f, DurationTicksOverride = -1,
                });
            _arena.Caster.Equip(0, arcPulse);

            float before = enemy.Health.Current;
            SkillCastResult result = _arena.Caster.CastSlot(0, new Vector2(10f, 0f));
            yield return _arena.Tick(2);

            Assert.IsTrue(result.Succeeded, $"The cast was refused: {result.Rejection}");
            Assert.Less(enemy.Health.Current, before, "The cone hit nothing.");
            Assert.IsTrue(enemy.Status.Has(StatusEffectId.Slow), "The slow was not applied.");
            Assert.Less(_arena.Player.Mana.Current, _arena.Player.Mana.Max, "No mana was spent.");
        }

        [UnityTest]
        public IEnumerator ArcPulseLeavesWhatIsBehindTheCasterAlone()
        {
            _arena = PrototypeArena.Build(new Vector3(-3f, 0.7f, 0f));
            _arena.WithoutPlugins();
            CombatActor behind = _arena.Enemies[0];
            yield return _arena.Tick(2);

            _arena.Caster.Equip(0, PrototypeArena.SkillFrom(
                "skill.arc_pulse",
                new SkillDefinition
                {
                    SkillId = 1001,
                    BaseCooldownTicks = 300,
                    Damage = new DamageRequest { Flat = 10f, Type = DamageType.Magic, IsArea = true },
                },
                new SkillShape
                {
                    Kind = SkillEffectKind.ConeDamage, RadiusUnits = 5f, ConeHalfAngleDegrees = 45f,
                },
                SkillStatusEffect.None));

            float before = behind.Health.Current;
            _arena.Caster.CastSlot(0, new Vector2(10f, 0f));   // aimed the other way
            yield return _arena.Tick(2);

            Assert.AreEqual(before, behind.Health.Current, 1e-3f, "The cone hit behind the caster.");
        }

        [UnityTest]
        public IEnumerator AFieldBurnsEverythingStandingInItOverTime()
        {
            _arena = PrototypeArena.Build(new Vector3(1.5f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            yield return _arena.Tick(2);

            _arena.Caster.Equip(1, PrototypeArena.SkillFrom(
                "skill.firewall_field",
                new SkillDefinition
                {
                    SkillId = 1002,
                    BaseCooldownTicks = 480,
                    Damage = new DamageRequest
                    {
                        Flat = 4f, Type = DamageType.Magic, IsArea = true, IsPeriodic = true,
                    },
                },
                new SkillShape
                {
                    Kind = SkillEffectKind.PersistentField,
                    RadiusUnits = 3f,
                    Field = FieldSchedule.Of(300, 30),
                },
                SkillStatusEffect.None));

            _arena.Caster.CastSlot(1, new Vector2(0f, 1f));
            yield return _arena.Tick(5);
            float afterFirstTick = enemy.Health.Current;

            yield return _arena.Tick(60);
            Assert.Less(enemy.Health.Current, afterFirstTick, "The field burned once and stopped.");

            yield return _arena.Tick(300);
            Assert.IsNull(
                Object.FindFirstObjectByType<PersistentDamageField>(), "The field outlived its duration.");
        }

        [UnityTest]
        public IEnumerator ABlinkMovesThePlayerAndRefusesToEnterAWall()
        {
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            _arena.Caster.Equip(2, PrototypeArena.SkillFrom(
                "skill.packet_blink",
                new SkillDefinition
                {
                    SkillId = 1003,
                    BaseCooldownTicks = 1,
                    IsMovementSkill = true,
                },
                new SkillShape { Kind = SkillEffectKind.Teleport, TeleportMaxDistanceUnits = 6f },
                SkillStatusEffect.None));

            Vector3 start = _arena.PlayerObject.transform.position;
            SkillCastResult moved = _arena.Caster.CastSlot(2, new Vector2(5f, 0f));
            yield return _arena.Tick(2);

            Assert.IsTrue(moved.Succeeded, $"The blink was refused: {moved.Rejection}");
            Assert.Greater(
                Vector3.Distance(start, _arena.PlayerObject.transform.position), 3f, "The blink did not move.");

            // A wall right on top of the caster leaves nowhere to land.
            GameObject wall = _arena.Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = _arena.PlayerObject.transform.position + new Vector3(3f, 0f, 0f);
            wall.transform.localScale = new Vector3(14f, 4f, 14f);
            yield return _arena.Tick(2);

            Vector3 blocked = _arena.PlayerObject.transform.position;
            float manaBefore = _arena.Player.Mana.Current;
            SkillCastResult refused = _arena.Caster.CastSlot(2, new Vector2(blocked.x + 5f, blocked.z));
            yield return _arena.Tick(2);

            Assert.IsFalse(refused.Succeeded, "The blink went into a wall.");
            Assert.AreEqual(SkillCastRejection.NoValidDestination, refused.Rejection);
            Assert.AreEqual(manaBefore, _arena.Player.Mana.Current, 1e-3f,
                "A blink with nowhere to land still charged the player.");
        }






        // --- the run identity --------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheMissionBeginsWithASeedThatCanBeReadBack()
        {
            // A deterministic generator whose seed nobody can recover is not reproducible in
            // practice, whatever it is in principle.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            Assert.IsTrue(_arena.Mission.Identity.IsValid, "No run was started.");
            Assert.IsTrue(_arena.Mission.State.IsRunning);
            Assert.AreNotEqual(
                _arena.Mission.Identity.SeedFor(RandomStream.Reward),
                _arena.Mission.Identity.SeedFor(RandomStream.Combat),
                "Two streams share a seed, so one would shift the other.");
        }

        [UnityTest]
        public IEnumerator ADeathReceiptIsKeyedOnTheRunThatWasActuallyPlayed()
        {
            // It used to be keyed on a hardcoded 1, so every run the player ever played would
            // have minted the same receipt id - and telling one death from another is the whole
            // job of that key.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            long runId = _arena.Mission.Identity.RunId;
            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(runId, _arena.DeathDirector.Ledger.RegisteredDeathId.RunId);
        }

        [UnityTest]
        public IEnumerator APlayerDeathFailsTheMission()
        {
            // [확정] docs/00: 사망은 해당 의뢰 실패. In memory only; what it costs is still the
            // save service's call, and there is no save service.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);
            Assert.IsTrue(_arena.Mission.State.IsRunning);

            PrototypeArena.Kill(_arena.Player);
            yield return _arena.Tick(2);

            Assert.AreEqual(MissionPhase.Failed, _arena.Mission.State.Phase);
            Assert.IsFalse(_arena.Mission.State.IsRunning);
        }

        // --- the support -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ASupportRaisesItsAlliesDamageAndNotItsOwn()
        {
            // 우선 처치 only means something if its help is real. This is also the first time
            // anything other than the player applies a status to an actor.
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f), new Vector3(6f, 0.7f, 2f));
            CombatActor ally = _arena.Enemies[0];
            CombatActor support = _arena.Enemies[1];
            PrototypeArena.MakeChaser(ally);
            PrototypeArena.MakeSupport(support);
            yield return _arena.Tick(5);

            float allyBase = ally.BaseStats.AttackDamage;

            Assert.IsTrue(ally.Status.Has(StatusEffectId.Empower), "The ally was never buffed.");
            Assert.Greater(ally.Stats.AttackDamage, allyBase,
                "The buff was applied and changed nothing, which is the bug this archetype would hide.");
            Assert.IsFalse(support.Status.Has(StatusEffectId.Empower),
                "It buffed itself, which makes it a tougher enemy rather than a support.");
        }

        [UnityTest]
        public IEnumerator ASupportDoesNotBuffThePlayer()
        {
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f));
            PrototypeArena.MakeSupport(_arena.Enemies[0]);
            yield return _arena.Tick(5);

            Assert.IsFalse(_arena.Player.Status.Has(StatusEffectId.Empower),
                "It handed the player its own buff.");
        }

        [UnityTest]
        public IEnumerator KillingTheSupportStopsTheBuffComingBack()
        {
            // The whole tactical point: the help has to stop arriving.
            _arena = PrototypeArena.Build(new Vector3(4f, 0.7f, 0f), new Vector3(6f, 0.7f, 2f));
            CombatActor ally = _arena.Enemies[0];
            CombatActor support = _arena.Enemies[1];
            PrototypeArena.MakeChaser(ally);
            EnemyAgent supportAgent = PrototypeArena.MakeSupport(support);
            yield return _arena.Tick(5);
            Assert.IsTrue(ally.Status.Has(StatusEffectId.Empower));

            PrototypeArena.Kill(support);
            int duration = ally.Status.RemainingTicksOf(StatusEffectId.Empower);
            yield return _arena.Tick(duration + 2);

            Assert.IsFalse(ally.Status.Has(StatusEffectId.Empower),
                "The buff was refreshed by a corpse.");
            Assert.AreEqual(ally.BaseStats.AttackDamage, ally.Stats.AttackDamage, 1e-3f,
                "The ally kept its raised damage after the support died.");
        }

        // --- the charger -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AChargerStandsStillToWarnAndThenCoversGround()
        {
            // 경고 프레임과 판정 동기, in the scene rather than in the pure function: the
            // wind-up has to be visibly motionless, and the dash has to move further than
            // walking could, or neither reads as a charge.
            //
            // Measured by watching the phase rather than by counting ticks. The first version
            // counted them from the moment it noticed the wind-up, which was already two ticks
            // in, so its last "wind-up" tick was really the first dash tick - and the test
            // failed on a charge that was behaving correctly.
            _arena = PrototypeArena.Build(new Vector3(7f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            EnemyAgent agent = PrototypeArena.MakeCharger(enemy);
            yield return _arena.Tick(2);

            int guard = 0;
            while (agent.LastIntent.ChargePhase != EnemyChargePhase.Telegraph && guard++ < 900)
                yield return _arena.Tick(1);
            Assert.AreEqual(EnemyChargePhase.Telegraph, agent.LastIntent.ChargePhase, "It never wound up.");

            Vector3 windUpStart = enemy.transform.position;
            int windUpTicks = 0;
            while (agent.LastIntent.ChargePhase == EnemyChargePhase.Telegraph && windUpTicks++ < 900)
            {
                Assert.Less(
                    Vector3.Distance(windUpStart, enemy.transform.position), 0.05f,
                    $"It moved during its own warning, on wind-up tick {windUpTicks}.");
                yield return _arena.Tick(1);
            }

            Assert.AreEqual(EnemyChargePhase.Dashing, agent.LastIntent.ChargePhase,
                "The wind-up did not lead into a dash.");

            Vector3 dashStart = enemy.transform.position;
            int dashTicks = 0;
            while (agent.LastIntent.ChargePhase == EnemyChargePhase.Dashing && dashTicks++ < 900)
                yield return _arena.Tick(1);

            float dashed = Vector3.Distance(dashStart, enemy.transform.position);
            float walkable = agent.Archetype.MoveSpeedUnitsPerSecond * dashTicks * PrototypeArena.TickSeconds;

            Assert.Greater(dashed, walkable * 1.5f,
                $"The dash covered {dashed:0.00} units in {dashTicks} ticks, barely more than walking ({walkable:0.00}).");
        }

        [UnityTest]
        public IEnumerator AChargerHitsThePlayerOnceWhenItConnects()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            PrototypeArena.MakeCharger(_arena.Enemies[0]);

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.Less(_arena.Player.Health.Current, before, "The charge never connected at all.");
        }

        // --- the ranged archetype ----------------------------------------------------------

        [UnityTest]
        public IEnumerator ARangedEnemyHoldsItsDistanceInsteadOfWalkingIn()
        {
            _arena = PrototypeArena.Build(new Vector3(14f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeRanged(enemy);
            yield return _arena.Tick(420);

            float distance = PrototypeArena.DistanceXZ(enemy, _arena.Player);
            EnemyArchetype archetype = enemy.GetComponent<EnemyAgent>().Archetype;
            float band = archetype.Brain.RangeToleranceUnits + 0.6f;

            Assert.AreEqual(archetype.Brain.PreferredRange, distance, band,
                $"It settled at {distance:0.0} rather than near {archetype.Brain.PreferredRange}.");
        }

        [UnityTest]
        public IEnumerator ARangedEnemyShootsThePlayerFromAcrossTheArena()
        {
            _arena = PrototypeArena.Build(new Vector3(9f, 0.7f, 0f));
            PrototypeArena.MakeRanged(_arena.Enemies[0]);

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.Less(_arena.Player.Health.Current, before, "Nothing ever reached the player.");
            Assert.Greater(
                PrototypeArena.DistanceXZ(_arena.Enemies[0], _arena.Player), 4f,
                "It hit the player by walking into them, which is not what a ranged enemy does.");
        }

        [UnityTest]
        public IEnumerator AWallStopsARangedEnemyFromShootingThroughIt()
        {
            // docs/01 lists LOS as what this archetype must implement. Without it cover is
            // decorative and the arena's two blocks mean nothing.
            _arena = PrototypeArena.Build(new Vector3(12f, 0.7f, 0f));
            PrototypeArena.MakeRanged(_arena.Enemies[0]);
            _arena.Wall(new Vector3(6f, 1f, 0f), new Vector3(1f, 4f, 24f));

            float before = _arena.Player.Health.Current;
            yield return _arena.Tick(300);

            Assert.AreEqual(before, _arena.Player.Health.Current, 1e-3f,
                "The player was shot through a wall.");
        }

        [UnityTest]
        public IEnumerator ABlockedRangedEnemyRepositionsRatherThanStandingStill()
        {
            _arena = PrototypeArena.Build(new Vector3(12f, 0.7f, 0f));
            CombatActor enemy = _arena.Enemies[0];
            PrototypeArena.MakeRanged(enemy);
            _arena.Wall(new Vector3(6f, 1f, 0f), new Vector3(1f, 4f, 24f));

            Vector3 start = enemy.transform.position;
            yield return _arena.Tick(180);

            Assert.Greater(
                Vector3.Distance(start, enemy.transform.position), 0.5f,
                "It stood behind cover doing nothing, which is not a tactic.");
        }

        // --- plugin passives ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator PassivePluginsReachThePlayersStatSnapshot()
        {
            // Granted in Start and pushed to the actor there. A plugin that was carried but
            // never pushed would sit in the list contributing nothing, which is the failure
            // this project has already shipped in two other shapes.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);

            CombatStats resolved = _arena.Player.Stats;

            Assert.Greater(resolved.PhysicalPenPercent, 0f, "G04 취약점 분석 contributed nothing.");
            Assert.Greater(resolved.MagicPenPercent, 0f, "G04 취약점 분석 contributed nothing.");
            Assert.AreEqual(
                NeonVoid.Plugins.PluginCatalog.StatedExecutionBonusPercent,
                resolved.LowHealthDamageBonusPercent, 1e-3f, "R01 처형 알고리즘 contributed nothing.");
            Assert.Greater(
                _arena.Player.MoveSpeedMultiplier, 1f, "G02 경량화 did not raise movement speed.");
        }

        [UnityTest]
        public IEnumerator TheExecutionBonusHitsHarderOnAWoundedEnemy()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f), new Vector3(5f, 0.7f, 6f));
            CombatActor healthy = _arena.Enemies[0];
            CombatActor wounded = _arena.Enemies[1];
            yield return _arena.Tick(2);

            // Taken down to 40%, under R01's 50% mark, without the plugins interfering.
            _arena.WithoutPlugins();
            wounded.ReceiveDamage(
                new DamageRequest { Flat = wounded.Health.Max * 0.6f, Type = DamageType.True },
                CombatStats.StartingVaccine());
            yield return _arena.Tick(2);

            float healthyBefore = healthy.Health.Current;
            float woundedBefore = wounded.Health.Current;

            var hit = new DamageRequest { Flat = 5f, Type = DamageType.True };
            CombatStats attacker = _arena.Player.Stats;
            healthy.ReceiveDamage(hit, attacker);
            wounded.ReceiveDamage(hit, attacker);

            float healthyLost = healthyBefore - healthy.Health.Current;
            float woundedLost = woundedBefore - wounded.Health.Current;

            Assert.Greater(woundedLost, healthyLost,
                "A wounded enemy took no more than a healthy one, so R01 never applied.");
        }

        [UnityTest]
        public IEnumerator AMovementPassiveCannotWalkOutOfARoot()
        {
            // 속박 is a stop. A movement plugin added on top of it would let a build ignore the
            // effect that is holding the player still.
            _arena = PrototypeArena.Build();
            yield return _arena.Tick(2);
            Assert.Greater(_arena.Player.MoveSpeedMultiplier, 1f);

            _arena.Player.ApplyStatus(NeonVoid.Combat.Status.StatusEffectId.Root, 1f);
            yield return _arena.Tick(2);

            Assert.AreEqual(0f, _arena.Player.MoveSpeedMultiplier, 1e-4f);
        }

        // --- the clock ---------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheSimulationRunsOneTickPerFrameAndDropsNothing()
        {
            _arena = PrototypeArena.Build(new Vector3(5f, 0.7f, 0f));
            PrototypeArena.MakeChaser(_arena.Enemies[0]);

            long before = _arena.Runner.TickIndex;
            yield return _arena.Tick(120);

            Assert.AreEqual(120L, _arena.Runner.TickIndex - before,
                "The clock did not advance one tick per frame, so timing assertions here mean nothing.");
            Assert.AreEqual(0L, _arena.Runner.DroppedTicks, "The simulation abandoned ticks.");
        }
    }
}
